using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spiked3;

namespace MiniMagellan
{
    public class Navigation : IBehavior
    {
        enum NavState { Rotating, MoveStart, Moving, MovingToConeStart, Stopped, BumperReverse, MovingToCone };

        public bool Lock { get; set; }
        NavState subState;

        bool EscapeInProgress = false;
        WayPoint EscapeWaypoint;

        readonly double DEG_PER_RAD = 180 / Math.PI;

        readonly TimeSpan rotateTimeout = new TimeSpan(0, 0, 15);  // seconds
        DateTime Timeout;

        public WayPoint CurrentWayPoint;
        public double lastHdg;

        readonly int moveSpeed = 40;

        public float hdgToWayPoint
        {
            get
            {
                float theta = (float)(Math.Atan2((CurrentWayPoint.X - Program.X), (CurrentWayPoint.Y - Program.Y)));
                return (float)(theta * DEG_PER_RAD);
            }
        }

        public float hdgToCone
        {
            get
            {
                return Program.H + (Program.Vis.servoPosition - 90);
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
            Program.Vis.OnLostCone += Vis_OnLostCone;
            Program.Vis.OnFoundCone += Vis_OnFoundCone;
        }

        public void TaskRun()
        {
            Trace.t(cc.Norm, "Navigation TaskRun started");

            Program.Pilot.Send(new { Cmd = "ESC", Value = 1 });

            // if finished, exit task
            while (Program.State != RobotState.Shutdown)
            {
                while (Lock)
                    Thread.SpinWait(100);

                switch (Program.State)
                {
                    case RobotState.Idle:
                        OnIdle();
                        break;

                    case RobotState.Navigating:
                        OnNavigating();
                        break;
                }
                // throttle loops
                Delay(100).Wait();      // todo is this working?
            }

            Program.Pilot.Send(new { Cmd = "ESC", Value = 0 });
            Trace.t(cc.Warn, "Navigation exiting");
        }

        void OnIdle()
        {
            Trace.t(cc.Norm, "OnIdle");
            if (Program.WayPoints == null || Program.WayPoints.Count == 0)
            {
                Program.Pilot.Send(new { Cmd = "MOV", M1 = 0, M2 = 0 });    // make sure we're stopped
                Trace.t(cc.Norm, "Finished");
                Program.State = RobotState.Finished;
                CurrentWayPoint = null;
                Program.Pilot.Send(new { Cmd = "ROT", Hdg = 0 }); // todo for convience, rotate to 0 at end
            }
            else
            {
                Trace.t(cc.Norm, "Pop Waypoint");
                CurrentWayPoint = Program.WayPoints.Pop();

                if (EscapeInProgress && CurrentWayPoint == EscapeWaypoint)
                    EscapeInProgress = false;

                Program.State = RobotState.Navigating;
                if (DistanceToWayPoint > .05)
                {
                    subState = NavState.Rotating;
                    Timeout = DateTime.Now + rotateTimeout;
                    lastHdg = hdgToWayPoint;
                    Trace.t(cc.Norm, "Rotating");
                    Program.Pilot.Send(new { Cmd = "ROT", Hdg = lastHdg });
                    subState = NavState.Rotating;
                }
                else
                {
                    Trace.t(cc.Norm, "Moving");
                    subState = NavState.MoveStart;
                }
            }
        }

        void Vis_OnFoundCone(object sender, EventArgs e)
        {
            //Trace.t(cc.Warn, "Vis_OnFoundCone");
            if (subState == NavState.Moving && CurrentWayPoint.isAction && DistanceToWayPoint < 5)
                subState = NavState.MovingToConeStart;
        }

        void Vis_OnLostCone(object sender, EventArgs e)
        {
            //Trace.t(cc.Warn, "Vis_OnLostCone");
            if (subState == NavState.MovingToConeStart)
            {
                System.Diagnostics.Debugger.Break();
            }
        }
        TimeSpan moveCmdInterval = new TimeSpan(0, 0, 2);
        TimeSpan ms = new TimeSpan(0, 0, 0, 0, 1);
        DateTime lastMoveCmdAt = DateTime.Now;
        void OnNavigating()
        {
            if (subState == NavState.MoveStart)
            {
                Trace.t(cc.Norm, "Navigating MoveStart");

                lastHdg = hdgToWayPoint;
                Program.Pilot.Send(new { Cmd = "MOV", Pwr = moveSpeed, Hdg = hdgToWayPoint, Dist = DistanceToWayPoint });
                lastMoveCmdAt = DateTime.Now;
                subState = NavState.Moving;
            }
            else if (subState == NavState.Moving)
            {
                if (DateTime.Now > lastMoveCmdAt + moveCmdInterval)
                {
                    Program.Pilot.Send(new { Cmd = "MOV", Pwr = moveSpeed, Hdg = hdgToWayPoint, Dist = DistanceToWayPoint });
                    lastMoveCmdAt = DateTime.Now;
                }
            }

            //else if (subState == NavState.MovingToConeStart)
            //{
            //    Trace.t(cc.Norm, string.Format("Navigating MoveToConeStart hdg({0})", hdgToCone));
            //    Program.Pilot.Send(new { Cmd = "MOV", Pwr = 20, Hdg = hdgToCone });
            //    subState = NavState.MovingToCone;
            //}
        }

        private void OnRotateComplete(dynamic json)
        {
            if (subState == NavState.Rotating && ((string)json.V).Equals("1"))
            {
                Trace.t(cc.Good, "Rotate completed");
                subState = NavState.MoveStart;
            }
        }

        private void OnMoveComplete(dynamic json)
        {
            if (subState == NavState.MovingToConeStart)
            {
                // we didnt find the cone or else it would have been a bumper
                throw new NotImplementedException("move complete for action");
            }
            else if (subState == NavState.Moving && ((string)json.V).Equals("1"))
            {
                Trace.t(cc.Good, "Move completed");
                Program.State = RobotState.Idle;
            }
        }

        void OnBumperEvent(dynamic json)
        {
            if (((string)json.V).Equals("1"))
            {
                // obstacle!!!!!
                if (CurrentWayPoint.isAction)
                {
                    Trace.t(cc.Good, "Expected Bumper");
                    Program.Pilot.Send(new { Cmd = "Mov", M1 = 0, M2 = 0 });

                    // backup
                    Program.Ar.EnterBallisticSection(this);

                    Program.Pilot.Send(new { Cmd = "Mov", M1 = -40, M2 = -40 });
                    Thread.Sleep(1000);     // 1 second reverse
                    Program.Pilot.Send(new { Cmd = "Mov", M1 = 0, M2 = 0 });
                    Program.State = RobotState.Idle;        // action complete

                    Program.Ar.LeaveBallisticSection(this);
                }
                else
                {
                    Trace.t(cc.Bad, "Unexpected Bumper");
                    Program.Pilot.Send(new { Cmd = "Mov", M1 = 0, M2 = 0 });
                    subState = NavState.BumperReverse;

                    // backup
                    Program.Ar.EnterBallisticSection(this);

                    Program.Pilot.Send(new { Cmd = "Mov", M1 = -40, M2 = -40 });
                    Thread.Sleep(1000);     // 1 second reverse
                    Program.Pilot.Send(new { Cmd = "Mov", M1 = 0, M2 = 0 });

                    // todo obstacle during escape
                    Program.State = RobotState.eStop;
                    Program.Ar.LeaveBallisticSection(this);
#if !__MonoCS__
                    //Debugger.Break();
#endif
                    return;

                    // save current waypoint
                    // re push, dont repush current if already escaping
                    // push new Waypoint for avoid 

                    EscapeWaypoint = CurrentWayPoint;
                    EscapeInProgress = true;

                    Trace.t(cc.Warn, "Inserting Fake Escape WayPoint");

                    Program.WayPoints.Push(EscapeWaypoint);

                    Program.WayPoints.Push(new WayPoint { X = 0.0F, Y = 0.0F, isAction = true });

                    Program.State = RobotState.Idle;
                }
            }
        }

        void PilotReceive(dynamic json)
        {
            if (Program.State == RobotState.Navigating)
            {
                switch ((string)json.T)
                {
                    case "Bumper":
                        OnBumperEvent(json);
                        break;

                    case "Move":
                        OnMoveComplete(json);
                        break;

                    case "Rotate":
                        OnRotateComplete(json);
                        break;
                }
            }
        }

        public string GetStatus()
        {
            return CurrentWayPoint == null ?
            "No Waypoint" :
            string.Format("^cCurrentWayPoint({0:F1},{1:F1})\nHeadingToWp({2:F1})\nDistanceToWp({3:F3})\nEscapeInProgress({4})",
            CurrentWayPoint.X, CurrentWayPoint.Y, hdgToWayPoint, DistanceToWayPoint, EscapeInProgress);
        }

        // this is built in to net 4.5 - but Mono is .net 4.0
        static TaskCompletionSource<bool> tcs;
        static System.Timers.Timer timer;
        public static Task Delay(double milliseconds)
        {
            if (timer == null)
            {
                tcs = new TaskCompletionSource<bool>();
                timer = new System.Timers.Timer { AutoReset = false };
                timer.Elapsed += (obj, args) =>
                {
                    tcs.TrySetResult(true);
                };
            }

            timer.Interval = milliseconds;
            timer.Start();
            return tcs.Task;
        }
    }
}
