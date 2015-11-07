using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiniMagellan
{
    public interface IBehavior
    {
        string GetStatus();
        bool Lock { get; set; }
        void TaskRun();
    }
}
