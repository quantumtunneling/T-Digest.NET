using System;
using System.Collections.Generic;

namespace StatsLib {
    public static class Extensions {

        public static void Shuffle<T>(this IList<T> list) {
            int n = list.Count;
            var rand = new Random();
            while (n > 1) {
                n--;
                int k = rand.Next(n + 1);
                T c = list[k];
                list[k] = list[n];
                list[n] = c;
            }
        }
    }
}
