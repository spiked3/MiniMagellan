using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;


namespace Spiked3
{
    //+ this is intended to be THE library driver for pilot, Mono, native, serial or MQTT

    public class Pilot
    {
         MqttClient Mq;
         PilotSerial Serial;
         float X, Y, H;
         Thread serialThread;
         TimeSpan defaultWaitTimeOut = new TimeSpan(0, 0, 0, 45);		// time out in seconds
         bool simpleEventFlag;

        public string CommStatus { get; internal set; }

        public delegate void ReceiveHandler(dynamic json);
        public event ReceiveHandler OnPilotReceive;

        public static Pilot Factory(string t)
        {
            Pilot _theInstance = new Pilot();

            if (t.Contains("com") || t.Contains("USB"))
            {
                _theInstance.SerialOpen(t);  // todo verify serial port is a pilot
                _theInstance.CommStatus = string.Format("{0} opened", _theInstance.Serial.PortName);
            }
            else
            {
                _theInstance.MqttOpen(t);
                _theInstance.CommStatus = string.Format("Mqtt ({0}) connected", t);
            }

            _theInstance.OnPilotReceive += _theInstance.Internal_OnPilotReceive;

            return _theInstance;
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
                    //if (j.V == 1)
                    //{
                    //    Debugger.Break();
                    //}
                    //goto case "Move";   // c# fallthrough </RollingEyes>
                case "Move":
                case "Rotate":
                    simpleEventFlag = true;
                    break;
            }
        }

        void MqttOpen(string c)
        {
            Mq = new MqttClient(c);
            Mq.MqttMsgPublishReceived += MqttMsgPublishReceived;
            try
            {                
                Mq.Connect("pTest");    // todo come up with a better clientID
            }
            catch (Exception)
            {
                Debugger.Break();
                throw;
            }
            Trace.WriteLine(string.Format("Connected to MQTT @ {0}", c));
            Mq.Subscribe(new string[] { "robot1/#" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
        }

        private void MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            switch (e.Topic)
            {
                case "robot1":
                    string j = Encoding.UTF8.GetString(e.Message);
                    if (OnPilotReceive != null)
                        OnPilotReceive(JsonConvert.DeserializeObject(j));
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
            if (Serial != null && Serial.IsOpen)
                Serial.Close();
            Trace.WriteLine("Serial closed", "3");
        }

        private void SerialOpen(string c)
        {
            Serial = new PilotSerial(c, 115200);
            try
            {
                Serial.Open();
                Serial.WriteTimeout = 50;
                Serial.OnReceive += Serial_OnReceive;
                serialThread = new Thread(Serial.Start);
                serialThread.Start();
            }
            catch (Exception ex)
            {
                Debugger.Break();
                Trace.WriteLine(ex.Message);
            }
            Trace.WriteLine(string.Format("Serial opened({0}) on {1}", Serial.IsOpen, Serial.PortName));
        }

        public void Serial_OnReceive(dynamic json)
        {
            if (OnPilotReceive != null)
                OnPilotReceive(json);
        }

        void SerialSend(string t)
        {
            if (Serial != null && Serial.IsOpen)
            {
                Trace.WriteLine("com<-" + t);
                Serial.WriteLine(t);
            }
        }

        public void Send(dynamic j)
        {
            string jsn = JsonConvert.SerializeObject(j);
#if false
            Trace.WriteLine("j->" + jsn);
            return;
#endif
            if (Serial != null && Serial.IsOpen)
                SerialSend(jsn);
            if (Mq != null && Mq.IsConnected)
                Mq.Publish("robot1/Cmd", UTF8Encoding.ASCII.GetBytes(jsn));
        }

        public void Close()
        {
            try
            {
                serialThread.Join();  // wait for thread to end
            }
            catch (Exception ex)
            {
            }

            if (Serial != null && Serial.IsOpen)
                Serial.Close();
            if (Serial != null && Mq.IsConnected)
                MqttClose();
        }

        public bool waitForEvent()
        {
            return waitForEvent(defaultWaitTimeOut);
        }

        public bool waitForEvent(TimeSpan timeOut)
        {
            simpleEventFlag = false;
            DateTime timeOutAt = DateTime.Now + timeOut;
            while (!simpleEventFlag)
            {
                //Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { })); // doEvents
                Thread.Sleep(100);
                if (DateTime.Now > timeOutAt)
                {
                    Send(new { Cmd = "ESC", Value = 0 });
                    Trace.WriteLine("TimeOut waiting for event");
                    throw new TimeoutException();
                    //return false;
                }
            }
            return true;
        }
    }

    public class PilotSerial : SerialPort
    {
        public delegate void ReceiveHandler(dynamic json);
        public event ReceiveHandler OnReceive;

        int recvIdx = 0;
        byte[] recvbuf = new byte[4096];

        public PilotSerial(string portName, int baudRate) : base(portName, baudRate) { }

        public void Start()
        {
            byte[] buffer = new byte[1024];
            Action kickoffRead = null;

            kickoffRead = delegate
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
            };

            kickoffRead();
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

                if (recvIdx >= recvbuf.Length)
                    System.Diagnostics.Debugger.Break();    // overflow
            }
        }
    }
}
