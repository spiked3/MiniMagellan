using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spiked3;

namespace MiniMagellan
{
    public class Approach
    {
        enum ApproachState { Start, Progress, Fail1, Fail2, Complete };

        ApproachState subState = ApproachState.Start;

        readonly double DEG_PER_RAD = 180 / Math.PI;

        readonly int approachSpeed = 20;

        float hdgToWayPoint
        {
            get
            {
                return Program.Nav.hdgToWayPoint;
            }
        }

        public float DistanceToWayPoint
        {
            get
            {
                return Program.Nav.DistanceToWayPoint;
            }
        }

        public Approach() 
        {
            Program.Pilot.OnPilotReceive += PilotReceive;
            Program.Vis.OnLostCone += Vis_OnLostCone;
            Program.Vis.OnFoundCone += Vis_OnFoundCone;
        }

        float coneHeading;
        DateTime approachStartedAt;
        TimeSpan waitForTouch = new TimeSpan(0, 0, 15);

        public void TaskRun()
        {

            // if finished, exit task
            while (Program.State != RobotState.Shutdown)
            {
                switch (subState)
                {
                    case ApproachState.Start:
                        Program.Nav.subState = Navigation.NavState.ApproachWait;                        
                        Trace.t(cc.Status, "Approach.Start");
                        if (Program.Vis.coneFlag != Vision.ConeState.Found)
                            Sweep();
                        else
                        {
                            Program.Pilot.Send(new { Cmd = "Mov", M1 = 0, M2 = 0 });
                            coneHeading = (Program.Vis.servoPosition - 90) + Program.H; // at least we hope so
                            Program.Pilot.Send(new { Cmd = "ROT", Hdg = coneHeading });
                            Trace.t(cc.Status, "Approach Rotating");

                            //Program.Pilot.SimpleEventFlag = false;
                            //while (!Program.Pilot.SimpleEventFlag) 
                            System.Threading.Thread.Sleep(2000);

                            approachStartedAt = DateTime.Now;
                            var a = new { Cmd = "Mov", Hdg = coneHeading, M1 = approachSpeed, M2 = approachSpeed };
                            Program.Pilot.Send(a);
                            Console.WriteLine(a);
                            subState = ApproachState.Progress;
                            Trace.t(cc.Status, "Approach Moving");
                        }
                        break;

                    case ApproachState.Progress:
                        if (DateTime.Now > approachStartedAt + waitForTouch)
                            subState = ApproachState.Fail1;
                        var b = new { Cmd = "Mov", Hdg = coneHeading, M1 = approachSpeed, M2 = approachSpeed };
                        Program.Pilot.Send(b);
                        Console.WriteLine(b);
                        break;

                    case ApproachState.Fail1:
                        Trace.t(cc.Bad, "Approach.Fail1");
                        if (DateTime.Now > approachStartedAt + waitForTouch)
                            subState = ApproachState.Fail2;
                        break;

                    case ApproachState.Fail2:
                        Trace.t(cc.Bad, "Approach.Fail2");
                        subState = ApproachState.Complete;
                        break;
                }

                if (subState == ApproachState.Complete)
                    break;

                // throttle loops
                Program.Delay(500).Wait();
            }
            Trace.t(cc.Warn, "Approach exiting");
        }

        private void Sweep()
        {
            throw new NotImplementedException("Sweep");
            Program.Vis.SubState = Vision.VisionState.Sweep;  // todo sweep
        }

        private void Vis_OnLostCone(object sender, EventArgs e)
        {
            //throw new NotImplementedException();
            // todo we are just going to continue for now, and hope, until we get sweep done
        }

        void Vis_OnFoundCone(object sender, EventArgs e)
        {
        }

        void OnBumperEvent(dynamic json)
        {
            if (((string)json.V).Equals("1")) // todo this is why we are here:)
            {
                Trace.t(cc.Good, "Expected Bumper");
                Program.Pilot.Send(new { Cmd = "Mov", Pwr = -approachSpeed, Dist = .5F });
                Program.Pilot.waitForEvent();
                subState = ApproachState.Complete;
            }
        }

        void PilotReceive(dynamic json)
        {
            switch ((string)json.T)
            {
                case "Bumper":
                    OnBumperEvent(json);
                    break;
            }
        }
    }
}
