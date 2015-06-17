using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatsLib.CMD {
    class Program {
        static void Main(string[] args) {
            Random r = new Random();
            TDigest digest = new TDigest();
            for (int i = 0; i < 100000; i++) {
                var n = (r.Next() % 100) + (r.Next() % 100);
                digest.Add(n);
            }
        }
    }
}
