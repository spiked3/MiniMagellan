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
        // navigation goes ballistic when action
        // so this is a back ground task, which is different from when I originally wrote the comments

        // so as task - keep an eye open
        // plus add some general purpose search specific things, eg Sweep

        public bool Lock { get; set; }

        enum VisionState { Casual, Focused, Found, Guiding };

        VisionState SubState = VisionState.Casual;

        // todo some sort of sequence map, servo values / degrees matrix thingy
        List<int> servoValues = new List<int> { 10, 45, 90, 135, 170 };
        int servoIdx = 2, servoStep = 1;

        public void TaskRun()
        {
            // if finished, exit task
            while (Program.State != RobotState.Shutdown)
            {
                while (Lock)
                    Thread.SpinWait(100);

                // if navigating just keep an eye out for detect
                // if searching
                //     sweep servo looking, found switch to Action (ballistic) approach / touch
                //     not found???
                // 
                // ballistic: rotate until centered, move slowly to it, limit distance
                //    not touched ???
                switch (SubState)
                {
                    case VisionState.Casual:
                        Program.Pilot.Send(new { Cmd = "Srvo", Value = servoValues[servoIdx] });
                        if (servoIdx == servoValues.Count - 1)
                            servoStep = -1;
                        else if (servoIdx == 0)
                            servoStep = 1;
                        servoIdx += servoStep;
                        Thread.Sleep(1000);
                        break;
                }
            }
            xCon.WriteLine("^wVision exiting");
        }

        public string GetStatus()
        {
            return ("^wUndefined");
        }
    }
}
    


