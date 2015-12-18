using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;


namespace Spiked3
{
    // this is intended to be THE library driver for pilot, Mono, native, serial or MQTT
    // .net serial handling sucks

    public class Pilot
    {
        MqttClient Mq;
        PilotSerial pilotSerial;
        float X, Y, H;
        TaskCompletionSource<bool> pilotTcs;
        Task serialTask;

        public string CommStatus { get; internal set; }

        public delegate void ReceiveHandler(dynamic json);
        public event ReceiveHandler OnPilotReceive;

        private Pilot() { }

        public static Pilot Factory(string t)
        {
            Pilot _theInstance = new Pilot();

            if (t.Contains("com") || t.Contains("USB"))
            {
                _theInstance.SerialOpen(t);  // todo verify serial port is a pilot
                _theInstance.CommStatus = string.Format("{0} opened", _theInstance.pilotSerial.PortName);
            }
            else
            {
                _theInstance.MqttOpen(t);
                _theInstance.CommStatus = string.Format("Mqtt ({0}) connected", t);
            }

            return _theInstance;
        }

        void MqttOpen(string c)
        {
            Mq = new MqttClient(c);
            Mq.MqttMsgPublishReceived += MqttMsgPublishReceived;
            try
            {                
                Mq.Connect("mmKernel");
            }
            catch (Exception)
            {
#if !__MonoCS__
                Debugger.Break();
#endif
                throw;
            }
            Trace.WriteLine(string.Format("Connected to MQTT @ {0}", c));
            Mq.Subscribe(new string[] { "robot1/#" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
        }

        private void MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            dynamic json;
            string j = Encoding.UTF8.GetString(e.Message);
            switch (e.Topic)
            {
                case "robot1":

                    try
                    {
                        json = JsonConvert.DeserializeObject(j);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine(string.Format("json deserialize threw exception {0}", ex.Message));
                        return;
                    }

                    if (OnPilotReceive != null)
                        OnPilotReceive(json);
                    break;
            }
        }

        void MqttClose()
        {
            Mq.Disconnect();
            Trace.WriteLine("MQTT disconnected", "3");
        }

        private void SerialClose()
        {
            if (pilotSerial != null && pilotSerial.IsOpen)
                pilotSerial.Close();
            Trace.WriteLine("Serial closed", "3");
        }

        private void SerialOpen(string c)
        {
            pilotSerial = new PilotSerial(c, 115200);
            pilotSerial.OnReceive += Internal_OnPilotReceive;
            try
            {
                pilotSerial.Open();
                pilotSerial.WriteTimeout = 50;
                serialTask = new Task(pilotSerial.TaskStart, TaskCreationOptions.LongRunning);
                serialTask.Start();
            }
            catch (Exception ex)
            {
#if !__MonoCS__

                Debugger.Break();
#endif
                Trace.WriteLine(ex.Message);
            }
            Trace.WriteLine(string.Format("Serial opened({0}) on {1}", pilotSerial.IsOpen, pilotSerial.PortName));
        }

        void SerialSend(string t)
        {
            if (pilotSerial != null && pilotSerial.IsOpen)
            {
                Trace.WriteLine("com<-" + t);
                pilotSerial.WriteLine(t);
            }
        }

        public void Send(dynamic j)
        {
            string jsn = JsonConvert.SerializeObject(j);
#if false
            Trace.WriteLine("j->" + jsn);
            return;
#endif
            if (pilotSerial != null && pilotSerial.IsOpen)
                SerialSend(jsn);
            if (Mq != null && Mq.IsConnected)
                Mq.Publish("robot1/Cmd", UTF8Encoding.ASCII.GetBytes(jsn));
        }

        public void Close()
        {
            try
            {
                pilotTcs.SetResult(true);
                serialTask.Wait();  // wait for thread to end
            }
            catch (Exception ex)
            {
            }

            if (pilotSerial != null && pilotSerial.IsOpen)
                pilotSerial.Close();
            if (pilotSerial != null && Mq.IsConnected)
                MqttClose();
        }

        void Internal_OnPilotReceive(dynamic j)
        {
            switch ((string)(j.T))
            {
                case "Pose":
                    X = j.X;
                    Y = j.Y;
                    H = j.H;
                    break;
                case "Bumper":
                case "Move":
                case "Rotate":
                    if (pilotTcs != null)
                        pilotTcs.SetResult(true);
                    break;
            }

            // propagate
            if (OnPilotReceive != null)
                OnPilotReceive(j);
        }

        internal void FakeOnReceive(dynamic j)
        {
            Internal_OnPilotReceive(j);
        }

        public bool waitForEvent(uint timeOut = 2000)
        {
            pilotTcs = new TaskCompletionSource<bool>();
            System.Timers.Timer timer = new System.Timers.Timer { AutoReset = false, Interval = timeOut };
            timer.Elapsed += (obj, args) => { pilotTcs.TrySetResult(false); };
            timer.Start();
            try
            {
                pilotTcs.Task.Wait();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Pilot WaitForEvent exception " + ex.Message);
                return false;
            }
            finally
            {
                pilotTcs = null;
            }
        }
    }

    public class PilotSerial : SerialPort
    {
        public delegate void ReceiveHandler(dynamic json);
        public event ReceiveHandler OnReceive;

        int recvIdx = 0;
        byte[] recvbuf = new byte[4096];

        public PilotSerial(string portName, int baudRate) : base(portName, baudRate) { }

        public void TaskStart()
        {
            byte[] buffer = new byte[1024];
            Action kickoffRead = null;

            kickoffRead = delegate
            {
                try
                {
                    BaseStream.BeginRead(buffer, 0, buffer.Length, delegate (IAsyncResult ar)
                    {
                        try
                        {
                            int actualLength = BaseStream.EndRead(ar);
                            byte[] received = new byte[actualLength];
                            Buffer.BlockCopy(buffer, 0, received, 0, actualLength);
                            AppSerialDataEvent(received);
                        }
                        catch (Exception ex)
                        {
                            //System.Diagnostics.Debugger.Break();
                            Trace.WriteLine(ex.Message);
                        }
                        if (IsOpen)
                            kickoffRead();  // re-trigger
                    }, null);
                }
                catch (Exception ex)
                {
                    if (ex is TaskCanceledException)
                        return;
                    throw;
                }
            };

            kickoffRead();
        }

        public void Serial_OnReceive(dynamic json)
        {
            if (OnReceive != null)
                OnReceive(json);
        }

        void AppSerialDataEvent(byte[] received)
        {
            foreach (var b in received)
            {
                if (b == '\n')
                {
                    recvbuf[recvIdx] = 0;
                    string line = Encoding.UTF8.GetString(recvbuf, 0, recvIdx).Trim(new char[] { '\r', '\n' });
                    if (line.StartsWith("//"))
                    {
                        Trace.WriteLine(line);
                        recvIdx = 0; //Trace.WriteLine("com->" + line,"+");
                    }
                    else
                    {
                        try
                        {
                            if (OnReceive != null)
                                OnReceive(JsonConvert.DeserializeObject(line));
                        }
                        catch (Exception)
                        {
                            //System.Diagnostics.Debugger.Break();
                            //throw;
                        }
                    }
                    recvIdx = 0;
                }
                else
                    recvbuf[recvIdx++] = (byte)b;
#if !__MonoCS__

                if (recvIdx >= recvbuf.Length)
                    System.Diagnostics.Debugger.Break();    // overflow
#endif
            }
        }
    }
}
