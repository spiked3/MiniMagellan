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

        float coneHeading
        {
            get
            {
                return Program.H + (Program.Vis.servoPosition - 90);
            }
        }

        DateTime approachStartedAt;
        TimeSpan waitForTouch = new TimeSpan(0, 0, 15);

        public Approach() 
        {
        }

        public void TaskRun()
        {
            Program.Pilot.OnPilotReceive += PilotReceive;
            Program.Vis.OnLostCone += Vis_OnLostCone;
            Program.Vis.OnFoundCone += Vis_OnFoundCone;

            // if finished, exit task
            while (Program.State != RobotState.Shutdown)
            {
                if (Program.State == RobotState.Approach)
                    switch (subState)
                    {
                        case ApproachState.Start:
                            Trace.t(cc.Status, "Approach.Start");
                            if (Program.Vis.coneFlag != Vision.ConeState.Found)
                                Sweep();
                            else
                            {
                                Program.Pilot.Send(new { Cmd = "Mov", M1 = 0, M2 = 0 });

                                Trace.t(cc.Status, "Approach Rotating");
                                Program.Pilot.Send(new { Cmd = "ROT", Hdg = coneHeading });
                                Program.Pilot.waitForEvent();

                                approachStartedAt = DateTime.Now;
                                var a = new { Cmd = "Mov", Hdg = coneHeading, M1 = approachSpeed, M2 = approachSpeed };
                                Program.Pilot.Send(a);
                                //Console.WriteLine(a);
                                subState = ApproachState.Progress;
                                Trace.t(cc.Status, "Approach Moving");
                            }
                            break;

                        case ApproachState.Progress:
                            if (DateTime.Now > approachStartedAt + waitForTouch)
                            {
                                subState = ApproachState.Fail1;
                                Program.Pilot.Send(new { Cmd = "Mov", M1 = 0, M2 = 0 });
                                break;
                            }
                            var b = new { Cmd = "Mov", Hdg = coneHeading, Pwr = approachSpeed };
                            Program.Pilot.Send(b);
                            //Console.WriteLine(b);
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
                {
                    Program.Pilot.Send(new { Cmd = "Mov", Pwr = -approachSpeed, Dist = .2F });
                    Program.Pilot.waitForEvent();
                    Program.State = RobotState.Idle;
                    break;
                }

                // throttle loops
                Program.Delay(100).Wait();
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
                subState = ApproachState.Complete;
            }
        }

        void PilotReceive(dynamic json)
        {
            switch ((string)json.T)
            {
                case "Bumper":
                    if (subState == ApproachState.Progress)
                        OnBumperEvent(json);
                    break;
            }
        }
    }
}
