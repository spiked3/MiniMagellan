using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MiniMagellan
{
    public class Arbitrator
    {
        // not really an arbitrator, but more of a thread manager supporting ballistic
        // behaviors, dynamic add/remove, pause/resume
        public Dictionary<IBehavior, string> threadMap = new Dictionary<IBehavior, string>();
        Dictionary<IBehavior, Thread> behaviorMap = new Dictionary<IBehavior, Thread>();
        Dictionary<string, IBehavior> idMap = new Dictionary<string, IBehavior>();

        internal void AddBehavior(string id, IBehavior b)
        {
            var t = new Thread(b.TaskRun);

            threadMap.Add(b, id);
            behaviorMap.Add(b, t);
            idMap.Add(id, b);

            t.Start();
            Trace.WriteLine($"Behavior {id} started");
        }

        internal void RemoveBehavior(string id)
        {
            Trace.WriteLine($"RemoveBehavior {id}");
            IBehavior b = idMap[id];
            behaviorMap[b].Abort();
            idMap.Remove(id);
            behaviorMap.Remove(b);
            threadMap.Remove(b);
        }

        internal void RemoveBehavior(IBehavior b)
        {
            var id = threadMap[b];
            Trace.WriteLine($"RemoveBehavior {id}");
            behaviorMap[b].Abort();
            idMap.Remove(id);
            behaviorMap.Remove(b);
            threadMap.Remove(b);
        }

        internal void Pause()
        {
            Trace.WriteLine($"Arbitrator pause");
            foreach (IBehavior b in behaviorMap.Keys)
                b.Lock = true;
        }

        internal void EnterBallisticSection(IBehavior b)
        {
            Trace.WriteLine($"{threadMap[b]} entered ballistic section...");
            foreach (IBehavior ib in behaviorMap.Keys)
                if (ib != b)
                    b.Lock = true;
        }

        internal void LeaveBallisticSection(IBehavior b)
        {
            Trace.WriteLine($"...finished ballistic section");
            foreach (IBehavior ib in behaviorMap.Keys)
                b.Lock = false;
        }
    }
}
