using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;


// todo this is intended to be THE library driver for pilot, Mono, native, serial or MQTT
//  todo expand it as such
namespace MiniMagellan
{
    public class Pilot
    {
        MqttClient Mq;
        private PilotSerial Serial;
        static float X, Y, H;

        public string CommStatus { get; internal set; }

        public delegate void ReceiveHandler(dynamic json);
        public event ReceiveHandler OnPilotReceive;

        public static Pilot Factory(string uri)     // not really a uri, yet
        {
            Pilot p = new Pilot();

            if (uri.Contains("com"))
                p.SerialOpen(uri);
                // todo ideally verify serial port is a pilot
            else
                p.MqttOpen(uri);

            StringBuilder b = new StringBuilder();
            if (p.Serial != null && p.Serial.IsOpen)
                b.Append($"{p.Serial.PortName} open");
            if (p.Mq != null && p.Mq.IsConnected)
                b.Append($"Mqtt ({uri}) connected");
            p.CommStatus = b.ToString();

            p.OnPilotReceive += p.Internal_OnPilotReceive;

            return p;
        }

        void Internal_OnPilotReceive(dynamic j)
        {
            string jsn = JsonConvert.SerializeObject(j);
            Program.ConsoleLock(ConsoleColor.Cyan, () =>
            {
                if ((string)j.T != "Pose")
                {
                    Trace.Write('>');
                    Trace.WriteLine(jsn);
                }
            });

            //if (simpleEventFlag)
            //    Console.WriteLine("during wait:" + j);

            switch ((string)(j.T))
            {
                case "Pose":
                    X = j.X;
                    Y = j.Y;
                    H = j.H;
                    break;

                case "Event":
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
                Mq.Connect("pTest");
            }
            catch (Exception)
            {
                throw;
            }
            Trace.WriteLine($"Connected to MQTT @ {c}", "1");
            Mq.Subscribe(new string[] { "robot1/#" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
        }

        private void MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            switch (e.Topic)
            {
                case "robot1":
                    string j = Encoding.UTF8.GetString(e.Message);
                    dynamic jsn = null;
                    if (OnPilotReceive != null)
                        try
                        {
                            jsn = JsonConvert.DeserializeObject(j);
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine(ex.Message);
                            Trace.WriteLine(j);
                            return;
                            //throw;
                        }
                        OnPilotReceive(jsn);
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
                Serial.Start();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
            Trace.WriteLine($"Serial opened({Serial.IsOpen}) on {Serial.PortName}", "2");
        }

        public void Serial_OnReceive(dynamic json)
        {
            if (OnPilotReceive != null)    // bubble
                OnPilotReceive(json);
        }

        void SerialSend(string t)
        {
            if (Serial?.IsOpen ?? false)
            {
                Trace.WriteLine("com<-" + t);
                Serial.WriteLine(t);
            }
        }

        public void Send(dynamic j)
        {
            string jsn = JsonConvert.SerializeObject(j);
            Console.ForegroundColor = ConsoleColor.Magenta;
            Trace.Write('<');
            Trace.WriteLine(jsn);
            Console.ForegroundColor = ConsoleColor.Gray;
            if (Serial?.IsOpen ?? false)
                SerialSend(jsn);
            if (Mq?.IsConnected ?? false)
                Mq.Publish("robot1/Cmd", UTF8Encoding.ASCII.GetBytes(jsn));
        }

        internal void Close()
        {
            if (Serial?.IsOpen ?? false)
                Serial.Close();
            if (Mq?.IsConnected ?? false)
                MqttClose();
        }

        TimeSpan waitTimeOut = new TimeSpan(0, 0, 0, 45);		// time out in seconds
        bool simpleEventFlag;

        public bool waitForEvent()
        {
            return waitForEvent(waitTimeOut);
        }

        public bool waitForEvent(TimeSpan timeOut)
        {
            simpleEventFlag = false;
            DateTime timeOutAt = DateTime.Now + timeOut;
            while (!simpleEventFlag)
            {
                System.Threading.Thread.Sleep(100);
                if (DateTime.Now > timeOutAt)
                {
                    Send(new { Cmd = "ESC", Value = 0 });
                    Trace.WriteLine("TimeOut waiting for event");
                    Environment.Exit(16);
                }
            }
            return true;
        }
    }
}
