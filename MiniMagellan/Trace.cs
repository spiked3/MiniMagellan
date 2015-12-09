using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MiniMagellan
{
    public enum cc { Norm, Good, Bad, Warn, Status }

    // todo trace incoming and outgoing pilot traffic to file
    public static class Trace
    {
        static Dictionary<cc, string> ccDict = new Dictionary<cc, string>();

        static Trace()
        {
            ccDict.Add(cc.Good, "^g");
            ccDict.Add(cc.Bad, "^r");
            ccDict.Add(cc.Warn, "^y");
            ccDict.Add(cc.Norm, "^w");
            ccDict.Add(cc.Status, "^c");
            //ccDict.Add(cc.??, "^m");
        }

        public static void t(cc cc, string t)
        {
            xCon.WriteLine(ccDict[cc] + t);
        }
    }
}
