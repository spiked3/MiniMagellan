using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
//using MiniMagellan.Program;
//using System.Math;
using Spiked3;

namespace MiniMagellan
{
    public class Navigation : IBehavior
    {
        enum NavState { Rotating, MoveStart, Moving, Stopped };

        public bool Lock { get; set; }
        NavState subState;

        bool EscapeInProgress = false;
        WayPoint EscapeWaypoint;

        readonly double DEG_PER_RAD = 180 / Math.PI;

        readonly TimeSpan rotateTimeout = new TimeSpan(0, 0, 15);  // seconds
        DateTime Timeout;

        public WayPoint CurrentWayPoint;
        public double lastHdg;

        // todo put these in robot
        public float hdgToWayPoint
        {
            get
            {
                float theta = (float)(Math.Atan2((CurrentWayPoint.X - Program.X), (CurrentWayPoint.Y - Program.Y) ));
                return (float)(theta * DEG_PER_RAD);
            }
        }
        public float DistanceToWayPoint
        {
            get
            {
                var a = CurrentWayPoint.X - Program.X;
                var b = CurrentWayPoint.Y - Program.Y;
                float d = (float)Math.Sqrt(a * a + b * b);
                return d;
            }
        }

        public Navigation()
        {
            Program.Pilot.OnPilotReceive += PilotReceive;
        }

        public void TaskRun()
        {
            Program.Pilot.Send(new { Cmd = "ESC", Value = 1 });

            // if finished, exit task
            while (Program.State != Program.RobotState.Shutdown)
            {
                while (Lock)
                    Thread.SpinWait(100);

                switch (Program.State)
                {
                    case Program.RobotState.Idle:
                        if (CurrentWayPoint.isAction)
                        {
                            Program.State = Program.RobotState.Action;
                            continue;
                        }

                        if (Program.WayPoints.Count == 0)
                        {
                            Program.Pilot.Send(new { Cmd = "MOV", M1 = 0, M2 = 0 });    // make sure we're stopped

                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine("WayPoint stack empty");
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Program.State = Program.RobotState.Finished;
                            CurrentWayPoint = null;
                        }
                        else
                        {
                            CurrentWayPoint = Program.WayPoints.Pop();
                            if (EscapeInProgress && CurrentWayPoint == EscapeWaypoint)
                                EscapeInProgress = false;
                            Program.State = Program.RobotState.Navigating;
                            if (DistanceToWayPoint > .5)
                            {
                                subState = NavState.Rotating;
                                Timeout = DateTime.Now + rotateTimeout;
                                lastHdg = hdgToWayPoint;
                                Program.Pilot.Send(new { Cmd = "ROT", Hdg = lastHdg });
                                subState = NavState.Rotating;
                            }
                            else
                            {
                                subState = NavState.Moving;
                            }
                        }
                        break;

                        // todo pilot firmware change +++ affects Dave
                        // I would prefer to not have move reset PIDs
                        // but since they do, we need to send only once, and wait
                        // and not try to correct heading in transit
                    case Program.RobotState.Navigating:
                        if (subState == NavState.MoveStart)
                        {
                            var NewSpeed = 50;
                            //if (DistanceToWayPoint < 5)
                            //    NewSpeed = 50;
                            //if (DistanceToWayPoint < 1)
                            //    NewSpeed = 40;
                            Program.ConsoleLock(ConsoleColor.Cyan, () =>
                            {
                                lastHdg = hdgToWayPoint;
                                Program.Pilot.Send(new { Cmd = "MOV", Pwr = NewSpeed, Hdg = lastHdg, Dist = DistanceToWayPoint });
                            });
                            subState = NavState.Moving;
                            Thread.Sleep(500);
                        }
                        break;

                    case Program.RobotState.Action:
                        Program.Ar.EnterBallisticSection(this);

                        Thread.Sleep(1000);

                        Program.Ar.LeaveBallisticSection(this);
                        CurrentWayPoint.isAction = false;
                        Program.State = Program.RobotState.Idle;
                        break;
                }
            }

            Program.Pilot.Send(new { Cmd = "ESC", Value = 0 });
            Trace.WriteLine("Navigation exiting");
        }

        void PilotReceive(dynamic json)
        {
            if (Program.State == Program.RobotState.Navigating)
            {
                switch ((string)json.T)
                {
                    case "Bumper":
                        if (((string)json.V).Equals("1"))
                        {
                            // obstacle!!!!!
                            Program.Pilot.Send(new { Cmd = "Mov", M1 = 0.0, M2 = 0.0 });
                            subState = NavState.Stopped;
                            Program.ConsoleLock(ConsoleColor.Red, () => { Trace.WriteLine("Unexpected Bumper"); });

                            // todo obstacle during escape
                            // save current waypoint
                            // re push, dont repush current if already escaping
                            // push new Waypoint for avoid 

                            EscapeWaypoint = CurrentWayPoint;
                            EscapeInProgress = true;

                            Trace.WriteLine("Inserting Fake Escape WayPoint");

                            Program.WayPoints.Push(EscapeWaypoint);

                            Program.WayPoints.Push(new WayPoint { X = 0.0F, Y = 0.0F, isAction = true });

                            Program.State = Program.RobotState.Idle;
                        }
                        break;

                    case "Move":
                        if (subState == NavState.Moving && ((string)json.V).Equals("1"))
                        {
                            Program.ConsoleLock(ConsoleColor.Green, () => { Trace.WriteLine("Move completed"); });
                            Program.State = Program.RobotState.Idle;
                        }
                        break;

                    case "Rotate":
                        if (subState == NavState.Rotating && ((string)json.V).Equals("1"))
                        {
                            Program.ConsoleLock(ConsoleColor.Green, () => { Trace.WriteLine("Rotate completed"); });
                            subState = NavState.MoveStart;
                        }
                        break;
                }
            }
        }

        public string GetStatus()
        {
            return CurrentWayPoint == null ?
            "No Waypoint" :
            string.Format("CurrentWayPoint({0:F2},{1:F2})\nHeadingToWp({2})\nDistanceToWp({3})\nEscapeInProgress({4})",
            CurrentWayPoint.X, CurrentWayPoint.Y, hdgToWayPoint, DistanceToWayPoint, EscapeInProgress );
        }
    }
}
