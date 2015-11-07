using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiniMagellan
{
    public class WayPoint
    {
        public float X { get; set; }
        public float Y { get; set; }
        public bool isAction { get; set; }
    }

    public class WayPoints : Stack<WayPoint>
    {
    }
}
