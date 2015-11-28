﻿using System;
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
            xCon.WriteLine(string.Format("^wBehavior {0} started", id));
        }

        internal void RemoveBehavior(string id)
        {
            xCon.WriteLine(string.Format("^yRemoveBehavior {0}", id));
            IBehavior b = idMap[id];
            behaviorMap[b].Abort();
            idMap.Remove(id);
            behaviorMap.Remove(b);
            threadMap.Remove(b);
        }

        internal void RemoveBehavior(IBehavior b)
        {
            var id = threadMap[b];
            xCon.WriteLine(string.Format("^yRemoveBehavior {0}", id));
            behaviorMap[b].Abort();
            idMap.Remove(id);
            behaviorMap.Remove(b);
            threadMap.Remove(b);
        }

        internal void Pause()
        {
            xCon.WriteLine("^yArbitrator pause");
            foreach (IBehavior b in behaviorMap.Keys)
                b.Lock = true;
        }

        internal void EnterBallisticSection(IBehavior b)
        {
            xCon.WriteLine(string.Format("^r{0} entered ballistic section...", threadMap[b]));
            foreach (IBehavior ib in behaviorMap.Keys)
                if (ib != b)
                    b.Lock = true;
        }

        internal void LeaveBallisticSection(IBehavior b)
        {
            xCon.WriteLine("^w...finished ballistic section");
            foreach (IBehavior ib in behaviorMap.Keys)
                b.Lock = false;
        }
    }
}
