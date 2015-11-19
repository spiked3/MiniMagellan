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
    // todo trace incoming and outgoing pilot traffic to file
    // todo better eStop

    public class Program : TraceListener
    {
        public enum RobotState { Init, Navigating, Searching, Action, Idle, Finished, UnExpectedBumper, Shutdown };
        public static RobotState State = RobotState.Init;
        public static float X, Y, H;
        public static Pilot Pilot;
        public static WayPoints WayPoints;
        public static Arbitrator Ar;
        public static bool ConsoleLockFlag;

        public static void ConsoleLock(ConsoleColor c, Action a)
        {
            // todo make me better :(
            while (ConsoleLockFlag)
                System.Threading.Thread.SpinWait(10);
            ConsoleLockFlag = true;
            Console.ForegroundColor = c;
            a();
            Console.ForegroundColor = ConsoleColor.Gray;
            ConsoleLockFlag = false;
        }

        public static bool ViewTaskRunFlag = false;

        public static float ResetHeading { get; private set; }

        public static void _T([CallerMemberName] String T = "")
        {
            Trace.WriteLine("::" + T);
        }

        char GetCharChoice(Dictionary<char,string> choices)
        {
            for (;;)
            {
                foreach (char c in choices.Keys)
                {
                    ConsoleLock(ConsoleColor.White, () =>
                    {
                        Console.Write($"{c}) ");
                    });
                    ConsoleLock(ConsoleColor.Gray, () =>
                    {
                        Console.WriteLine($"{choices[c]}");
                    });
                }

                var k = Console.ReadKey(true);
                if (choices.ContainsKey(char.ToUpper(k.KeyChar)))
                    return k.KeyChar;
            }
        }

        int GetChoice(string[] choices)
        {
            for (;;)
            {
                int i = 0;
                new List<string>(choices).ForEach(x =>
                {
                    ConsoleLock(ConsoleColor.Gray, () =>
                    {
                        Console.WriteLine($"{i++}) {x}");
                    });
                });
                var k = Console.ReadKey(true);
                try
                {
                    return int.Parse(new string(k.KeyChar, 1));
                }
                catch (Exception) { }
            }
        }

        static void Main(string[] args)
        {
            new Program().Main1(args);
        }

        void Main1(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Trace.Listeners.Add(this);

            Trace.WriteLine("Spiked3.com MiniMagellan Kernel - (c) 2015-2016 Mike Partain");
            // startup parms
            //string pilotString = "127.0.0.1";
            //string pilotString = "com15";
            string pilotString = "192.168.42.1";
            var p = new OptionSet
            { { "pilot=", (v) => { pilotString = v; } } };
            p.Parse(Environment.GetCommandLineArgs());

            // pilot, listen for events
            Pilot = Pilot.Factory(pilotString);
            Pilot.OnPilotReceive += PilotReceive;

            Ar = new Arbitrator();

            // add behaviors
            Ar.AddBehavior("Navigation", new Navigation());
            Ar.AddBehavior("Vision", new Vision());

            int telementryIdx = 0;

            // menu
            for (;;)
            {
                Dictionary<char, string> menu = new Dictionary<char, string>()
                {
                    { 'X', "eXit" },
                    { 'W', "listen Waypoints" },
                    { 'R', "config/Reset" },
                    { 'A', "start Autonomous" },
                    { 'S', "State" },
                    { 'O', "*rOtate" },
                    { 'V', "*moVe" },
                    { 'B', "*Bumper" },
                    { 'T', "Telementry" },
                    { '0', "power 0" }
                };

                char n = Char.ToUpper(GetCharChoice(menu));

                if (n == 'X')
                    break;

                switch (n)
                {
                    case 'W':
                        ListenWayPoints();
                        break;
                    case 'R':
                        configPilot();
                        break;
                    case 'A':
                        Trace.WriteLine("Starting autonomous");
                        State = RobotState.Idle;
                        break;
                    case 'S':
                        ViewStatus();
                        break;
                    case 'O':
                        Trace.Write("**");
                        Pilot.Serial_OnReceive(new { T = "Rotate", V = "1" });
                        break;
                    case 'V':
                        Trace.Write("**");
                        Pilot.Serial_OnReceive(new { T = "Move", V = "1" });
                        break;
                    case 'B':
                        Trace.Write("**");
                        Pilot.Serial_OnReceive(new { T = "Bumper", V = "1" });
                        break;
                    case 'T':
                        Pilot.Send(new { Cmd = "TELEM", Flag = (++telementryIdx % 5) });
                        break;
                    case '0': // EStop
                        Pilot.Send(new { Cmd = "MOV", M1 = 0, M2 = 0 });
                        break;
                }
            }

            Trace.WriteLine("Kernel Stopping");
            Pilot.Close();
            State = RobotState.Shutdown;
            Pilot.Send(new { Cmd = "ESC", Value = 0 });
            System.Threading.Thread.Sleep(1000);
            Console.WriteLine("\nPress any key to continue");
            Console.ReadKey();
        }

        private static void ViewStatus()
        {
            var oldCursorPos = new { left = Console.CursorLeft, top = Console.CursorTop };
            int writtenLines = 0;

            Console.ForegroundColor = ConsoleColor.Yellow;

            Console.WriteLine($"\nState({State}) X({X}) Y({Y}) H({H}) WayPoints({WayPoints?.Count ?? 0})"); writtenLines++;
            Ar.threadMap.All(kv => {
                Console.WriteLine($"{kv.Value}: {kv.Key.GetStatus()}"); writtenLines++;
                return true;
            });
            Console.WriteLine($"Waypoints:"); writtenLines++;
            WayPoints?.All(wp => {
                Console.WriteLine($"[{wp.X}, {wp.Y}{(wp.isAction ? ", Action" : "")}]"); writtenLines++;
                return true;
            });

            Console.WriteLine($"Waypoints:"); writtenLines++;

            //Console.SetCursorPosition(0, oldCursorPos.top + writtenLines);
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        void ListenWayPoints()
        {
            System.Diagnostics.Trace.WriteLine($"Listen for WayPoints");
            MqttClient Mq;
            string broker = "192.168.42.1";
            //string broker = "127.0.0.1";
            Mq = new MqttClient(broker);
            System.Diagnostics.Trace.WriteLine($".connecting");
            Mq.Connect("MM1");
            System.Diagnostics.Trace.WriteLine($".Connected to MQTT @ {broker}");
            Mq.MqttMsgPublishReceived += PublishReceived;

            Mq.Subscribe(new string[] { "Navplan/WayPoints" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });

            int n = -1;
            while (n != 0)
            {
                n = GetChoice(new string[] { "Exit", "Load fake" });
                if (n == 1)
                {
                    System.Diagnostics.Trace.WriteLine($"Loaded fake WayPoints");
                    WayPoints = new WayPoints();
                    // add in reverse order (FILO)
                    WayPoints.Push(new WayPoint { X = 0, Y = 0, isAction = false });
                    WayPoints.Push(new WayPoint { X = 0, Y = 1, isAction = false });
                }
            }

            Mq.Disconnect();
            System.Diagnostics.Trace.WriteLine("..Disconnected MQTT");
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
            Console.Write(message);
        }

        public override void WriteLine(string message)
        {
            Console.WriteLine(message);
        }
    }
}
