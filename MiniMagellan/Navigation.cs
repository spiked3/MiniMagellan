using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spiked3;

namespace MiniMagellan
{
    public class Navigation : IBehavior
    {
        enum NavState { Rotating, MoveStart, Moving, Stopped, BumperReverse, ActionComplete };

        public bool Lock { get; set; }
        NavState subState;

        bool EscapeInProgress = false;
        WayPoint EscapeWaypoint;

        //public bool expectingBumper { get; set; }

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
                float theta = (float)(Math.Atan2( (CurrentWayPoint.X - Program.X), (CurrentWayPoint.Y - Program.Y) ));
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
            xCon.WriteLine("^wNavigation started");

            Program.Pilot.Send(new { Cmd = "ESC", Value = 1 });

            // if finished, exit task
            while (Program.State != RobotState.Shutdown)
            {
                while (Lock)
                    Thread.SpinWait(100);

                switch (Program.State)
                {
                    case RobotState.Idle:
                        if (CurrentWayPoint != null && CurrentWayPoint.isAction)
                        {
                            Program.State = RobotState.Action;
                            continue;
                        }

                        if (Program.WayPoints == null || Program.WayPoints.Count == 0)
                        {
                            Program.Pilot.Send(new { Cmd = "MOV", M1 = 0, M2 = 0 });    // make sure we're stopped
                            xCon.WriteLine("^yWayPoint stack empty");
                            Program.State = RobotState.Finished;
                            CurrentWayPoint = null;
                        }
                        else
                        {
                            CurrentWayPoint = Program.WayPoints.Pop();
                            //expectingBumper = CurrentWayPoint.isAction;

                            if (EscapeInProgress && CurrentWayPoint == EscapeWaypoint)
                                EscapeInProgress = false;
                            Program.State = RobotState.Navigating;
                            if (DistanceToWayPoint > .05)
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

                    case RobotState.Navigating:
                        if (subState == NavState.MoveStart)
                        {
                            var NewSpeed = 50;
                            //if (DistanceToWayPoint < 5)
                            //    NewSpeed = 50;
                            //if (DistanceToWayPoint < 1)
                            //    NewSpeed = 40;
                            lastHdg = hdgToWayPoint;
                            Program.Pilot.Send(new { Cmd = "MOV", Pwr = NewSpeed, Hdg = lastHdg, Dist = DistanceToWayPoint });
                            subState = NavState.Moving;
                            Thread.Sleep(500);
                        }
                        break;

                    case RobotState.Action:
                        Program.Ar.EnterBallisticSection(this);

                        Thread.Sleep(1000);

                        Program.Ar.LeaveBallisticSection(this);
                        CurrentWayPoint.isAction = false;
                        Program.State = RobotState.Idle;
                        break;
                }
            }

            Program.Pilot.Send(new { Cmd = "ESC", Value = 0 });
            xCon.WriteLine("^wNavigation exiting");
        }

        void PilotReceive(dynamic json)
        {
            if (Program.State == RobotState.Navigating)
            {
                switch ((string)json.T)
                {
                    case "Bumper":
                        if (((string)json.V).Equals("1"))
                        {
                            // obstacle!!!!!
                            if (CurrentWayPoint.isAction)
                            {
                                //expectingBumper = false;
                                Program.Pilot.Send(new { Cmd = "Mov", M1 = 0, M2 = 0 });
                                xCon.WriteLine("^gExpected Bumper");

                                // backup .5
                                // curious if this works, otherwise just time wait
                                Program.Pilot.Send(new { Cmd = "Mov", M1 = -40, M2 = -40, Dist = .5f });
                                Program.Pilot.waitForEvent();
                                Program.Pilot.Send(new { Cmd = "Mov", M1 = 0, M2 = 0 });
                                Program.State = RobotState.Idle;        // action complete
                            }
                            else
                            {
                                Program.Pilot.Send(new { Cmd = "Mov", M1 = 0, M2 = 0 });
                                subState = NavState.BumperReverse;
                                xCon.WriteLine("^rUnexpected Bumper");
                                
                                // backup .5
                                // curious if this works, otherwise just time wait
                                Program.Pilot.Send(new { Cmd = "Mov", M1 = -40, M2 = -40, Dist = .5f });
                                Program.Pilot.waitForEvent();
                                Program.Pilot.Send(new { Cmd = "Mov", M1 = 0, M2 = 0 });

                                // todo obstacle during escape
                                Program.State = RobotState.eStop;
#if !__MonoCS__
                                Debugger.Break();
#endif
                                break;

                                // save current waypoint
                                // re push, dont repush current if already escaping
                                // push new Waypoint for avoid 

                                EscapeWaypoint = CurrentWayPoint;
                                EscapeInProgress = true;

                                Trace.WriteLine("Inserting Fake Escape WayPoint");

                                Program.WayPoints.Push(EscapeWaypoint);

                                Program.WayPoints.Push(new WayPoint { X = 0.0F, Y = 0.0F, isAction = true });

                                Program.State = RobotState.Idle;
                            }
                        }
                        break;

                    case "Move":
                        if (subState == NavState.Moving && ((string)json.V).Equals("1"))
                        {
                            xCon.WriteLine("^gMove completed");
                            Program.State = RobotState.Idle;
                        }
                        break;

                    case "Rotate":
                        if (subState == NavState.Rotating && ((string)json.V).Equals("1"))
                        {
                            xCon.WriteLine("^gRotate completed");
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
            string.Format("CurrentWayPoint({0:F1},{1:F1})\nHeadingToWp({2:F1})\nDistanceToWp({3:F3})\nEscapeInProgress({4})",
            CurrentWayPoint.X, CurrentWayPoint.Y, hdgToWayPoint, DistanceToWayPoint, EscapeInProgress);
        }
    }
}
