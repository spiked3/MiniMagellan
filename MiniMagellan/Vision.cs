using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MiniMagellan
{
    public class Vision : IBehavior
    {
        // kinda following how Dave did it, I think. thanks Dave.

        public bool Lock { get; set; }

        enum VisionState { Casual, Focused, Found, FinalApproach };

        enum ServoState { Idle, SweepUp, SweepDown, IdleCentrered }

        enum PixyState {  Ignore, FindMax };

        double AngleOfMaxBlob;

        VisionState SubState = VisionState.Casual;
        ServoState servoState = ServoState.Idle;
        PixyState pixyState = PixyState.Ignore;

        // todo some sort of sequence map, servo values / degrees matrix thingy
        List<int> servoValues = new List<int> { 10, 45, 90, 135, 170 };
        int servoIdx = 2, servoStep = 1;

        public void TaskRun()
        {
            // todo mqtt subscribe to pixy

            // if finished, exit task
            while (Program.State != RobotState.Shutdown)
            {
                while (Lock)
                    Thread.SpinWait(100);

                switch (SubState)
                {
                    case VisionState.Casual:
                        //Program.Pilot.Send(new { Cmd = "Srvo", Value = servoValues[servoIdx] });
                        //if (servoIdx == servoValues.Count - 1)
                        //    servoStep = -1;
                        //else if (servoIdx == 0)
                        //    servoStep = 1;
                        //servoIdx += servoStep;

                        // todo what I'd like to really do is start casually sweeping when we are 
                        // within X meters of an action waypoint

                        // we need to be sure/high probability it is a cone in order to abandon waypoint navigation
                        // otherwise we keep pixy aimed ?

                        break;

                    case VisionState.Focused:
                        // todo ballistic and do a full sweep?
                        break;

                    case VisionState.Found:
                        // todo so we take over navigation, ballistic ?
                        break;

                    case VisionState.FinalApproach:
                        // todo used during ballistic approach
                        // todo implement backup when bumper touched
                        // and distance limits
                        break;
                }
                Thread.Sleep(1000);     // todo faster after getting this stuff to work

            }
            xCon.WriteLine("^wVision exiting");
        }

        void MqttPixyRecvd()
        {
            if (pixyState == PixyState.FindMax)
                ; // todo something
        }

        void HeadingToCone()
        {
            // todo depends on where servo found its largest blob + current Heading
        }

        public string GetStatus()
        {
            return ("^wUndefined");
        }
    }
}
