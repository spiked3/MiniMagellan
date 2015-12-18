using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spiked3;

namespace MiniMagellan
{
    public class Navigation
    {
        public enum NavState { Rotating, MoveStart, Moving, Stopped, BumperReverse, ApproachWait };
        public NavState subState;

        bool EscapeInProgress = false;
        WayPoint EscapeWaypoint;

        readonly double DEG_PER_RAD = 180 / Math.PI;

        readonly TimeSpan rotateTimeout = new TimeSpan(0, 0, 15);  // seconds
        DateTime Timeout;

        public WayPoint CurrentWayPoint;
        public double lastHdg;

        readonly int moveSpeed = 30;

        TimeSpan moveCmdInterval = new TimeSpan(0, 0, 2);
        TimeSpan ms = new TimeSpan(0, 0, 0, 0, 1);
        DateTime lastMoveCmdAt = DateTime.Now;

        public float hdgToWayPoint
        {
            get
            {
                float theta = (float)(Math.Atan2((CurrentWayPoint.X - Program.X), (CurrentWayPoint.Y - Program.Y)));
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
            Program.Vis.OnFoundCone += Vis_OnFoundCone;
        }

        public void TaskRun()
        {
            Trace.t(cc.Norm, "Navigation TaskRun started");

            Program.Pilot.Send(new { Cmd = "ESC", Value = 1 });

            // if finished, exit task
            while (Program.State != RobotState.Shutdown)
            {
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
                Program.Delay(1000).Wait();      // todo is this working?
            }

            Program.Pilot.Send(new { Cmd = "ESC", Value = 0 });
            Trace.t(cc.Warn, "Navigation exiting");
        }

        void OnIdle()
        {
            if (subState == NavState.ApproachWait)
                System.Diagnostics.Debugger.Break();

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
            if (CurrentWayPoint != null)
            {
                if (subState == NavState.Moving && CurrentWayPoint.isAction && DistanceToWayPoint < 1)
                    BeginApproach();
            }
        }

        void BeginApproach()
        {
            Trace.t(cc.Warn, "starting and waiting for Approach task");
            var T = new Task(new Approach().TaskRun);
            subState = NavState.ApproachWait;
            T.Start();
            T.Wait();
            Trace.t(cc.Good, "Approach completed");
        }

        void OnNavigating()
        {
            if (subState == NavState.MoveStart)
            {
                Trace.t(cc.Norm, "Navigating MoveStart");
                if (CurrentWayPoint.isAction)
                    Trace.t(cc.Warn, "isAction");
                lastHdg = hdgToWayPoint;
                if (CurrentWayPoint.isAction)
                    Program.Pilot.Send(new { Cmd = "MOV", Pwr = moveSpeed, Hdg = hdgToWayPoint, Dist = DistanceToWayPoint - .6 });
                else
                    Program.Pilot.Send(new { Cmd = "MOV", Pwr = moveSpeed, Hdg = hdgToWayPoint, Dist = DistanceToWayPoint });

                lastMoveCmdAt = DateTime.Now;
                subState = NavState.Moving;
            }
            else if (subState == NavState.Moving)
            {
                if (DateTime.Now > lastMoveCmdAt + moveCmdInterval)
                {
                    Program.Pilot.Send(new { Cmd = "MOVxx", Pwr = moveSpeed, Hdg = hdgToWayPoint, Dist = DistanceToWayPoint });
                    lastMoveCmdAt = DateTime.Now;
                }
            }
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
            if (CurrentWayPoint.isAction)
            {
                BeginApproach();
            }
            else if (subState == NavState.Moving && ((string)json.V).Equals("1"))
            {
                Trace.t(cc.Good, "Move completed");
                Program.State = RobotState.Idle;
            }
        }

        void OnBumperEvent(dynamic json)
        {
            //? should not get here during approach, because our spin flag will be set
            if (((string)json.V).Equals("1") )
            {
                // obstacle!!!!!
                Trace.t(cc.Bad, "Unexpected Bumper");
                Program.Pilot.Send(new { Cmd = "Mov", M1 = 0, M2 = 0 });
                subState = NavState.BumperReverse;

                Program.Pilot.Send(new { Cmd = "Mov", Pwr = -30, Dist = .5F });
                Program.Pilot.waitForEvent();

                // todo obstacle during escape
                Program.State = RobotState.eStop;

                // todo add avoidance
                return;

                // save current waypoint
                // if !escaping:
                //      (re)push current
                // push new Waypoint for avoid 
                // escaping = true

                if (!EscapeInProgress)
                {
                    Program.WayPoints.Push(CurrentWayPoint);
                    EscapeInProgress = true;
                    EscapeWaypoint = CurrentWayPoint;       // when we get there we'll know the escape worked
                }

                WayPoint bypassWaypoint = new WayPoint { };  // todo  finish
                Program.WayPoints.Push(bypassWaypoint);

                Program.State = RobotState.Idle;
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

        static int thisInstant
        {
            get
            {
                DateTime n = DateTime.Now;
                return n.Second * 1000 + n.Millisecond;
            }
        }


    }
}
