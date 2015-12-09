using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MiniMagellan
{
    public class Arbitrator
    {
        // not really an arbitrator, but more of a thread/task manager supporting ballistic
        // behaviors, dynamic add/remove, pause/resume

        public Dictionary<IBehavior, string> behaviorNameMap = new Dictionary<IBehavior, string>();
        Dictionary<IBehavior, Task> behaviorMap = new Dictionary<IBehavior, Task>();
        Dictionary<string, IBehavior> idMap = new Dictionary<string, IBehavior>();

        internal void AddBehavior(string id, IBehavior b)
        {
            Task t = new Task(b.TaskRun);
            behaviorNameMap.Add(b, id);
            behaviorMap.Add(b, t);
            idMap.Add(id, b);
            t.Start();
            Trace.t(cc.Norm, string.Format("^wBehavior {0} started", id));
        }

#if false
        internal void RemoveBehavior(string id)
        {
            xCon.WriteLine(string.Format("^yRemoveBehavior {0}", id));
            IBehavior b = idMap[id];
            behaviorMap[b].Dispose();   // ????
            idMap.Remove(id);
            behaviorMap.Remove(b);
            threadMap.Remove(b);
        }

        internal void RemoveBehavior(IBehavior b)
        {
            var id = threadMap[b];
            xCon.WriteLine(string.Format("^yRemoveBehavior {0}", id));
            behaviorMap[b].Dispose();
            idMap.Remove(id);
            behaviorMap.Remove(b);
            threadMap.Remove(b);
        }

        internal void Pause()
        {
            Trace.t(cc.Warn, "Arbitrator pause");
            foreach (IBehavior b in behaviorMap.Keys)
                b.Lock = true;
        }
#endif

        internal void EnterBallisticSection(IBehavior b)
        {
            Trace.t(cc.Warn, string.Format("^r{0} entered ballistic section...", behaviorNameMap[b]));
            foreach (IBehavior ib in behaviorMap.Keys)
                if (ib != b)
                    b.Lock = true;
        }

        internal void LeaveBallisticSection(IBehavior b)
        {
            Trace.t(cc.Warn, "...finished ballistic section");
            foreach (IBehavior ib in behaviorMap.Keys)
                b.Lock = false;
        }
    }
}
