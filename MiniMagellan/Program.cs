using NDesk.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using static System.Math;

namespace MiniMagellan
{
    // todo trace incoming and outgoing pilot traffic

    public struct MenuStruct
    {

    }
    public class Program : TraceListener
    {
        public enum RobotState { Init, Navigating, Searching, Action, Idle, Finished, UnExpectedBumper, Shutdown };
        public static RobotState State = RobotState.Init;
        public static float X, Y, H;
        public static Pilot Pilot;
        public static WayPoints WayPoints;
        public static Arbitrator Ar;

        public static bool ViewTaskRunFlag = false;

        public static float ResetHeading { get; private set; }

        public static void _T([CallerMemberName] String T = "")
        {
            Trace.WriteLine("::" + T);
        }

        int GetChoice(string[] choices)
        {
            for (;;)
            {
                int i = 0;
                new List<string>(choices).ForEach(x => { Console.WriteLine($"{i++}) {x}"); });
                var k = Console.ReadKey(true);
                try
                {
                    return int.Parse(new string(k.KeyChar, 1));
                }
                catch (Exception) { }
            }
        }

        internal static bool NextWayPoint()
        {
            // todo
            return false;
        }

        static void Main(string[] args)
        {
            new Program().Main1(args);
        }

        void Main1(string[] args)
        {
            Trace.Listeners.Add(this);

            Trace.WriteLine("Spiked3.com MiniMagellan Kernel - (c) 2015-2016 Mike Partain");
            // startup parms
            string pilotString = "com15";
            //string pilotString = "com15";
            //string pilotString = "192.168.42.1";
            var p = new OptionSet
            { { "pilot=", (v) => { pilotString = v; } } };
            p.Parse(Environment.GetCommandLineArgs());

            // pilot, listen for events
            Pilot = Pilot.Factory(pilotString);
            Pilot.OnPilotReceive += PilotReceive;

            // arbiter
            Ar = new Arbitrator();

            // add behaviors
            Ar.AddBehavior("Navigation", new Navigation());
            Ar.AddBehavior("Vision", new Vision());

            // menu
            for (;;)
            {
                var n = GetChoice(new string[] { "Exit",
                    "Config/Reset", "Load WayPoints", "Start Autonomous", "State","simulate: Rotate Complete","simulate: Move Complete","simulate: Bumper Pressed" });
                if (n == 0)
                    break;
                switch (n)
                {
                    case 1:
                        configPilot();
                        break;
                    case 2:
                        ListenWayPoints();
                        break;
                    case 3:
                        Trace.WriteLine("Starting autonomous");
                        State = RobotState.Idle;
                        break;
                    case 4:
                        ViewStatus();
                        break;
                    case 5:
                        Trace.Write(">#> ");
                        Pilot.Serial_OnReceive(new { T = "Rotate", V = "1" });
                        break;
                    case 6:
                        Trace.Write(">#> ");
                        Pilot.Serial_OnReceive(new { T = "Move", V = "1" });
                        break;
                    case 7:
                        Trace.Write(">#> ");
                        Pilot.Serial_OnReceive(new { T = "Bumper", V = "1" });
                        break;
                }
            }

            Trace.WriteLine("Kernel Stopping");
            State = RobotState.Shutdown;
            Pilot.Send(new { Cmd = "ESC", Value = 0 });
            System.Threading.Thread.Sleep(500);
        }

        private static void ViewStatus()
        {
            Console.WriteLine($"\nState({State}) X({X}) Y({Y}) H({H}) WayPoints({WayPoints?.Count ?? 0})");
            Ar.threadMap.All(kv => {
                Console.WriteLine($"{kv.Value}: {kv.Key.GetStatus()}");
                return true;
            });
            Console.WriteLine($"Waypoints:");
            WayPoints?.All(wp => {
                Console.WriteLine($"[{wp.X}, {wp.Y}{(wp.isAction  ? ", Action":"")}]");
                return true;
            });
            Console.WriteLine();
        }

        void ListenWayPoints()
        {
#if false
            System.Diagnostics.Trace.WriteLine($"Loaded fake WayPoints", "1");
            WayPoints = new WayPoints();
            // add in reverse order (FILO)
            WayPoints.Push(new WayPoint { X = 0, Y = 0, isAction = false });
            WayPoints.Push(new WayPoint { X = 1, Y = 0, isAction = false });
            WayPoints.Push(new WayPoint { X = 1, Y = 1, isAction = false });
            WayPoints.Push(new WayPoint { X = 0, Y = 1, isAction = true });
#else
            System.Diagnostics.Trace.WriteLine($"Listen for WayPoints", "1");
            MqttClient Mq;
            //string broker = "192.168.42.1";
            string broker = "127.0.0.1";
            Mq = new MqttClient(broker);
            System.Diagnostics.Trace.WriteLine($".connecting", "1");
            Mq.Connect("MM1");
            System.Diagnostics.Trace.WriteLine($".Connected to MQTT @ {broker}", "1");
            Mq.MqttMsgPublishReceived += PublishReceived;

            Mq.Subscribe(new string[] { "Navplan/WayPoints" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });

            int n = -1;
            while (n != 0)
                n = GetChoice(new string[] { "Exit" });

            Mq.Disconnect();
            System.Diagnostics.Trace.WriteLine($".Disconnect MQTT @ {broker}", "1");
#endif
        }

        private void PublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            dynamic jsn = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(e.Message));
            Program.ResetHeading = jsn.ResetHdg;
            List<WayPoint> wps = new List<WayPoint>(jsn.WayPoints.Count);
            foreach (var t in jsn.WayPoints)
                wps.Add(new WayPoint { X = t[0], Y = t[1], isAction = (int)t[2] !=0 ? true : false });
            Program.WayPoints = new WayPoints();
            for (int i = wps.Count - 1; i >= 0; i--)
                Program.WayPoints.Push(wps[i]);
            Console.WriteLine($"WayPoint received and loaded");
        }

        static void configPilot()
        {
            Pilot.Send(new { Cmd = "CONFIG", TPM = 336, MMX = 450, StrRv = -1 });
            Pilot.Send(new { Cmd = "CONFIG", M1 = new int[] { 1, -1 }, M2 = new int[] { -1, 1 } });
            Pilot.Send(new { Cmd = "RESET", Hdg = ResetHeading });
        }

        static void PilotReceive(dynamic json)
        {
            // if Bumper && !Action it is a bad thing
            switch ((string)json.T)
            {
                case "Pose":
                    X = json.X;
                    Y = json.Y;
                    H = json.H;
                    break;
                case "Event":
                    break;
                case "Log":
                case "Error":
                case "Debug":
                    break;
            }
        }

        public override void Write(string message)
        {
            Console.Write(message);
        }

        public override void WriteLine(string message)
        {
            Console.WriteLine(message);
        }
    }
}
