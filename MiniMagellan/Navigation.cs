using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static MiniMagellan.Program;
using static System.Math;

namespace MiniMagellan
{
    public class Navigation : IBehavior
    {
        enum NavState { Rotating, Moving, Stopped };

        public bool Lock { get; set; }
        NavState subState;

        bool EscapeInProgress = false;
        WayPoint EscapeWaypoint;

        readonly double DEG_PER_RAD = 180 / PI;

        readonly TimeSpan rotateTimeout = new TimeSpan(0, 0, 15);  // seconds
        DateTime Timeout;
        WayPoint currentWayPoint;

        public float hdgToWayPoint
        {
            get
            {
                float theta = (float)(Atan2((X - currentWayPoint.X), - (Y - currentWayPoint.Y)));
                return (float)(theta * DEG_PER_RAD);
            }
        }
        public float distanceToWayPoint
        {
            get
            {
                var a = currentWayPoint.X - X;
                var b = currentWayPoint.Y - Y;
                float d = (float)Sqrt(a * a + b * b);
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
            while (Program.State != RobotState.Shutdown)
            {
                while (Lock)
                    Thread.SpinWait(100);

                if (Program.ShowTaskRunTrace)
                    Program._T("Navigation::TaskRun");

                if (Program.State == RobotState.Idle)
                {
                    if (currentWayPoint?.isAction ?? false)
                    {
                        Program.State = RobotState.Action;
                        continue;
                    }

                    if (Program.WayPoints.Count == 0)
                    {
                        Program.State = RobotState.Finished;
                        continue;
                    }

                    currentWayPoint = Program.WayPoints.Pop();
                    if (EscapeInProgress && currentWayPoint == EscapeWaypoint)
                        EscapeInProgress = false;
                    subState = NavState.Rotating;
                    Program.State = RobotState.Navigating;
                    Timeout = DateTime.Now + rotateTimeout;
                    Program.Pilot.Send(new { Cmd = "ROT", Hdg = hdgToWayPoint });
                    subState = NavState.Rotating;

                }

                if (Program.State == RobotState.Navigating)
                {
                    if (subState == NavState.Moving)
                    {
                        var NewSpeed = 50;
                        if (distanceToWayPoint < 5)
                            NewSpeed = 30;
                        if (distanceToWayPoint < 1)
                            NewSpeed = 15;
                        Program.Pilot.Send(new { Cmd = "MOV", Pwr = NewSpeed, Hdg = hdgToWayPoint, Dist = distanceToWayPoint });
                        subState = NavState.Moving;

                        continue;
                    }
                }

                if (Program.State == RobotState.Action)
                {
                    Program.Ar.EnterBallisticSection(this);

                    Thread.Sleep(1000);

                    Program.Ar.LeaveBallisticSection(this);
                    currentWayPoint.isAction = false;
                    Program.State = RobotState.Idle;
                    continue;
                }

                Thread.Sleep(1000 / 2);    // todo waitOne with timeout instead
            }

            Program.Pilot.Send(new { Cmd = "ESC", Value = 0 });
            Trace.WriteLine("Navigation exiting");
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
                            Program.Pilot.Send(new { Cmd = "Mov", M1 = 0.0, M2 = 0.0 });
                            subState = NavState.Stopped;
                            Trace.WriteLine("Unexpected Bumper");

                            // save current waypoint
                            // push new Waypoints for avoid, then push original waypoint
                            EscapeWaypoint = currentWayPoint;
                            EscapeInProgress = true;

                            Trace.WriteLine("Inserting Fake Escape WayPoint");
                            Program.WayPoints.Push(EscapeWaypoint);
                            Program.WayPoints.Push(new WayPoint { X = 0.0F, Y = 0.0F, isAction = true });

                            Program.State = RobotState.Idle;
                        }
                        break;

                    case "Move":
                        if (subState == NavState.Moving && ((string)json.V).Equals("1"))
                        {
                            Trace.WriteLine("Move completed");
                            Program.State = RobotState.Idle;
                        }
                        break;

                    case "Rotate":
                        if (subState == NavState.Rotating && ((string)json.V).Equals("1"))
                        {
                            Trace.WriteLine("Rotate completed");
                            subState = NavState.Moving;
                        }
                        break;
                }
            }
        }

        public void Suspend(object l)
        {
            Trace.WriteLine($"Navigation::Suspend...");
            Monitor.Wait(l);
            Monitor.Exit(l);
        }

        public string GetStatus()
        {
            return currentWayPoint == null ?
            "No Waypoint" :
            $"CurrentWayPoint({currentWayPoint?.X ?? float.NaN:F2},{currentWayPoint?.Y ?? float.NaN:F2})\nHeadingToWp({hdgToWayPoint})\nDistanceToWp({distanceToWayPoint})\nEscapInProgress({EscapeInProgress})";
        }
    }
}
