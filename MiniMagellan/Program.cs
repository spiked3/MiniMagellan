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
using Spiked3;

namespace MiniMagellan
{
    public enum RobotState { Init, Navigating, Searching, Action, Idle, Finished, UnExpectedBumper, Shutdown };

    // todo trace incoming and outgoing pilot traffic to file

    public class Program : TraceListener
    {
        public static RobotState State = RobotState.Init;
        public static float X, Y, H;
        public static Pilot Pilot;
        public static WayPoints WayPoints;
        public static Arbitrator Ar;

        public bool ConsoleLockFlag;
        MqttClient Mq;

        //string PilotString = "localhost";
        //string PilotString = "com15";
        //public static string PilotString = "com15";
        public static string PilotString = "192.168.42.1";

        public static float ResetHeading { get; private set; }

        public static void _T(String t)
        {
            Trace.WriteLine("Program::" + t);
        }

        char GetCharChoice(Dictionary<char, string> choices)
        {
            lock (xCon.consoleLock)
                xCon.WriteLine(null);
            for (;;)
            {
                foreach (char c in choices.Keys)
                {
                    lock (xCon.consoleLock)
                        xCon.Write(string.Format("^g{0}) ^!{1}\n", c, choices[c]));
                }

                while (!Console.KeyAvailable)
                    System.Threading.Thread.Sleep(200);

                var k = Console.ReadKey(false);

                if (k.Key == ConsoleKey.Escape)
                    return (char)0;

                if (choices.ContainsKey(char.ToUpper(k.KeyChar)))
                    return k.KeyChar;
            }
        }

        public bool Linux { get { return Environment.OSVersion.Platform == PlatformID.Unix ||
                    Environment.OSVersion.Platform == PlatformID.MacOSX ||
                    (int)Environment.OSVersion.Platform == 128; } }

        static void Main(string[] args)
        {
            new Program().Main1(args);
        }

        void Main1(string[] args)
        {
            Trace.Listeners.Add(this);

            xCon.WriteLine("*m^w   Spiked3.com MiniMagellan Kernel - (c) 2015-2016 Mike Partain   ");
            xCon.WriteLine("*w^z  \\^c || break to stop  ");

#if __MonoCS__
            xCon.Write("Mono / ");
#else
            xCon.Write(".Net / ");

#endif
            if (Linux)
                xCon.WriteLine("Unix");
            else
                xCon.WriteLine("Windows");


            Console.CancelKeyPress += Console_CancelKeyPress;
            Console.TreatControlCAsInput = false;

            // startup parms
            var parms = new OptionSet
            { { "pilot=", (v) => { PilotString = v; } } };
            parms.Parse(Environment.GetCommandLineArgs());

            // pilot, listen for events
            Pilot = Pilot.Factory(PilotString);
            Pilot.OnPilotReceive += PilotReceive;

            Ar = new Arbitrator();

            // add behaviors
            Navigation Nav = new Navigation();
            Ar.AddBehavior("Navigation", Nav);
            Ar.AddBehavior("Vision", new Vision());

            int telementryIdx = 0;

            // menu
            Dictionary<char, string> menu = new Dictionary<char, string>() {
                    { 'W', "Waypoints (MQTT)" },
                    { 'F', "Fake waypoints" },
                    { 'R', "Config/Reset" },
                    { 'Z', "Close MQTT" },
                    { 'A', "Autonomous Start" },
                    { 'S', "State" },
                    { 'O', ".rOtate" },
                    { 'V', ".moVe" },
                    { 'B', ".Bumper" },
                    { 'T', "Telementry" },
                    { ' ', "eStop (space bar)" } };
            for (;;)
            {
                // use ^C to break
                char n = Char.ToUpper(GetCharChoice(menu));

                if (State == RobotState.Shutdown)
                    return;

                switch (n)
                {
                    case 'W':
                        ListenWayPoints();
                        break;
                    case 'F':
                        FakeWayPoints();
                        break;
                    case 'Z':
                        CloseMqtt();
                        break;
                    case 'R':
                        configPilot();
                        break;
                    case 'A':
                        xCon.WriteLine(string.Format("^yStarting autonomous @ {0}", DateTime.Now.ToLongTimeString()));
                        State = RobotState.Idle;
                        break;
                    case 'S':
                        ViewStatus();
                        break;
                    case 'O':
                        xCon.Write("^c#");
                        X = Nav.CurrentWayPoint.X;
                        Y = Nav.CurrentWayPoint.Y;
                        H = (float)Nav.lastHdg;
                        Pilot.Serial_OnReceive(new { T = "Rotate", V = "1" });
                        break;
                    case 'V':
                        xCon.Write("^c#");
                        Pilot.Serial_OnReceive(new { T = "Move", V = "1" });
                        break;
                    case 'B':
                        xCon.Write("^c#");
                        Pilot.Serial_OnReceive(new { T = "Bumper", V = "1" });
                        break;
                    case 'T':
                        Pilot.Send(new { Cmd = "TELEM", Flag = (++telementryIdx % 5) });
                        break;
                    case ' ': // EStop
                        Pilot.Send(new { Cmd = "MOV", M1 = 0, M2 = 0 });
                        Pilot.Send(new { Cmd = "ESC", Value = 0 });
                        break;
                }
            }
        }

        private void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            ShutDown();
            xCon.WriteLine("\nPress any key to continue");
#if !__MonoCS__
            Console.ReadKey();
#endif
        }

        private void ShutDown()
        {
            xCon.WriteLine("Kernel Stopping");
            State = RobotState.Shutdown;
            Pilot.Send(new { Cmd = "ESC", Value = 0 });
            System.Threading.Thread.Sleep(500);
            Pilot.Close();
        }

        private static void ViewStatus()
        {
            lock (xCon.consoleLock)
            {
                xCon.WriteLine(string.Format("^mState({0}) X({1:F3}) Y({2:F3}) H({3:F1}) WayPoints({4})",
                    State, X, Y, H, WayPoints != null ? WayPoints.Count.ToString() : "None"));
                Ar.threadMap.All(kv =>
                {
                    xCon.WriteLine(string.Format("^w{0}: {1}", kv.Value, kv.Key.GetStatus()));
                    return true;
                });
                xCon.WriteLine("^mWaypoints:");
                if (WayPoints != null)
                    WayPoints.All(wp =>
                    {
                        xCon.WriteLine(string.Format("^w[{0:F3}, {1:F3}{2}]", wp.X, wp.Y, (wp.isAction ? ", Action" : "")));
                        return true;
                    });
            }
        }

        void ListenWayPoints()
        {
            xCon.WriteLine("^yListen for WayPoints");
            Mq = new MqttClient(PilotString);
            Trace.WriteLine("^y.connecting");
            Mq.Connect("MM1");
            Trace.WriteLine(string.Format("^y.Connected to MQTT @ {0}", PilotString));
            Mq.MqttMsgPublishReceived += PublishReceived;
            Mq.Subscribe(new string[] { "Navplan/WayPoints" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
        }

        void FakeWayPoints()
        {
            // {"ResetHdg":0,"WayPoints":[[0, 1, 0],[1, 1, 0],[0, 1, 0],[0, 0, 0]]}
            xCon.WriteLine("^yLoaded fake WayPoints");
            WayPoints = new WayPoints();
            // add in reverse order (FILO)
            WayPoints.Push(new WayPoint { X = 0, Y = 0, isAction = false });
            WayPoints.Push(new WayPoint { X = 0, Y = 1, isAction = false });
            WayPoints.Push(new WayPoint { X = 1, Y = 1, isAction = false });
            WayPoints.Push(new WayPoint { X = 0, Y = 1, isAction = false });
        }

        void CloseMqtt()
        {
            if (Mq != null && Mq.IsConnected)
            {
                Mq.Disconnect();
                Trace.WriteLine("^y..Disconnected MQTT");
            }
        }

        private void PublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            dynamic jsn = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(e.Message));
            Program.ResetHeading = jsn.ResetHdg;
            List<WayPoint> wps = new List<WayPoint>(jsn.WayPoints.Count);
            foreach (var t in jsn.WayPoints)
                wps.Add(new WayPoint { X = t[0], Y = t[1], isAction = (int)t[2] !=0 ? true : false });
            Program.WayPoints = new WayPoints();
            for (int i = wps.Count - 1; i >= 0; i--)    // pushed in reverse, FILO
                Program.WayPoints.Push(wps[i]);
            xCon.WriteLine("^gWayPoint received and loaded");
        }

        static void configPilot()
        {
            Pilot.Send(new { Cmd = "CONFIG", TPM = 336, MMX = 450, StrRv = -1 });
            Pilot.Send(new { Cmd = "CONFIG", M1 = new int[] { 1, -1 }, M2 = new int[] { -1, 1 } });
            Pilot.Send(new { Cmd = "RESET", Hdg = ResetHeading });
            Pilot.Send(new { Cmd = "ESC", Value = 1 });
        }

        static void PilotReceive(dynamic json)
        {
            // todo if Bumper && !Action it is a bad thing
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
            xCon.Write("^r" + message);
        }

        public override void WriteLine(string message)
        {
            xCon.WriteLine("^r" + message);
        }
    }
}
