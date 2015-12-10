﻿using NDesk.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using Spiked3;

namespace MiniMagellan
{
    public enum RobotState { Init, Navigating, Searching, Action, Idle, Finished, UnExpectedBumper, Shutdown, eStop };


    public class Program 
    {
        public static RobotState State = RobotState.Init;
        public static float X, Y, H;
        public static Pilot Pilot;
        public static WayPoints WayPoints;
        public static Arbitrator Ar;
        public static Navigation Nav;
        public static Vision Vis;

        public bool ConsoleLockFlag;
        MqttClient Mq;

        public static string PilotString = "localhost"; // use cmd argument -pilot to set differently

        public static float ResetHeading { get; private set; }

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

        public bool Unix { get { return Environment.OSVersion.Platform == PlatformID.Unix ||
                    Environment.OSVersion.Platform == PlatformID.MacOSX ||
                    (int)Environment.OSVersion.Platform == 128; } }

        static void Main(string[] args)
        {
            new Program().Main1(args);
        }

        void Main1(string[] args)
        {
            xCon.WriteLine("*w^z   Spiked3.com MiniMagellan Kernel - (c) 2015-2016 Mike Partain   ");
            xCon.WriteLine("*w^z  \\^c || break to stop  ");

#if __MonoCS__
            xCon.Write("Mono / ");
#else
            xCon.Write(".Net / ");
#endif
            if (Unix)
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
            Nav = new Navigation();
            Ar.AddBehavior("Navigation", Nav);
            Vis = new Vision();
            Ar.AddBehavior("Vision", Vis);

            int telementryIdx = 0;

            new Task(ListenWayPointsTask).Start();

            // menu
            Dictionary<char, string> menu = new Dictionary<char, string>() {
                    { 'F', "Fake waypoints" },
                    { 'C', "Config/reset" },
                    { 'A', "Autonomous Start" },
                    { 'S', "State" },
                    { 'R', ".Rotate" },
                    { 'M', ".Move" },
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
                        break;
                    case 'F':
                        FakeWayPoints();
                        break;
                    case 'C':
                        configPilot();
                        break;
                    case 'A':
                        Trace.t(cc.Warn, string.Format("^yStarting autonomous @ {0}", DateTime.Now.ToLongTimeString()));
                        State = RobotState.Idle;
                        break;
                    case 'S':
                        ViewStatus();
                        break;
                    case 'R':
                        xCon.Write("^c#");
                        X = Nav.CurrentWayPoint.X;
                        Y = Nav.CurrentWayPoint.Y;
                        H = (float)Nav.lastHdg;
                        Pilot.Serial_OnReceive(new { T = "Rotate", V = "1" });
                        break;
                    case 'M':
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
                        Trace.t(cc.Bad, "eStop");
                        State = RobotState.eStop;
                        Pilot.Send(new { Cmd = "MOV", M1 = 0, M2 = 0 });
                        Pilot.Send(new { Cmd = "ESC", Value = 0 });
                        break;
                }
            }
        }

        private void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            ShutDown();
            xCon.WriteLine("^w\nPress any key to continue");
//#if !__MonoCS__
            Console.ReadKey();
//#endif
        }

        private void ShutDown()
        {
            Trace.t(cc.Warn, "Kernel Stopping");
            State = RobotState.Shutdown;
            Pilot.Send(new { Cmd = "ESC", Value = 0 });
            System.Threading.Thread.Sleep(500);
            Pilot.Close();
        }

        void ViewStatus()
        {
            lock (xCon.consoleLock)
            {
                Trace.t(cc.Status, string.Format("State({0}) X({1:F3}) Y({2:F3}) H({3:F1}) WayPoints({4})",
                    State, X, Y, H, WayPoints != null ? WayPoints.Count.ToString() : "None"));
                Ar.behaviorNameMap.All(kv =>
                {
                    Trace.t(cc.Status, string.Format("{0}: {1}", kv.Value, kv.Key.GetStatus()));
                    return true;
                });
                Trace.t(cc.Status, "Waypoints:");
                if (WayPoints != null)
                    WayPoints.All(wp =>
                    {
                        Trace.t(cc.Status, string.Format("[{0:F3}, {1:F3}{2}]", wp.X, wp.Y, (wp.isAction ? ", Action" : "")));
                        return true;
                    });
            }
        }

        void ListenWayPointsTask()
        {
            Mq = new MqttClient(PilotString);
            Trace.t(cc.Norm, string.Format("listenWayPointsTask, connecting to MQTT @ {0}", PilotString));
            Mq.Connect("MM1");
            Trace.t(cc.Norm, "listenWayPointsTask connected");
            Mq.MqttMsgPublishReceived += PublishReceived;
            Mq.Subscribe(new string[] { "Navplan/WayPoints" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
        }

        void FakeWayPoints()
        {
            // {"ResetHdg":0,"WayPoints":[[0, 1, 0],[1, 1, 0],[0, 1, 0],[0, 0, 0]]}
            Trace.t(cc.Good, "Loaded fake WayPoints");
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
                Trace.t(cc.Warn, "Program(main) disconnected MQTT");
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
            Trace.t(cc.Good, "WayPoint received and loaded");
        }

        static void configPilot()
        {
            Pilot.Send(new { Cmd = "CONFIG", TPM = 332, MMX = 450, StrRv = -1 });
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
    }
}
