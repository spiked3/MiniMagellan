using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static MiniMagellan.Program;

namespace MiniMagellan
{
    public class Vision : IBehavior
    {
        // navigation goes ballistic when action
        // so this is a back ground task, which is different from when I originally wrote the comments

        // so as task - keep an eye open
        // plus add some general purpose search specific things, eg Sweep

        public bool Lock { get; set; }

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

                Thread.Sleep(100);
            }
            Trace.WriteLine("Vision exiting");
        }

        public string GetStatus()
        {
            return ($"Undefined");
        }
    }
}
    


