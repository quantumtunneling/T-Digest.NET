using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace StatsLib.Benchmarks {
    class Program {
        static void Main() {
            BenchmarkRunner.Run<SortedDigest>();
        }
    }

    [SimpleJob(RuntimeMoniker.Net461)]
    [MemoryDiagnoser]
    public class SortedDigest {
        [Params(1_000, 10_000, 100_000)]
        public int N { get; set; }

        [Params(true, false)]
        public bool Asc { get; set; }

        [Benchmark]
        public double Build() {
            TDigest digest = new TDigest();
            double denominator = N;
            for (int i = 0; i < N; ++i) {
                // Add normalized values in interval [0.0, 1.0)
                int numerator = Asc ? i : (N - i - 1);
                digest.Add(numerator / denominator);
            }

            double count = digest.Count;
            if (count != N) {
                throw new InvalidOperationException($"Bad benchmark; digest.Count ({count}) != N ({N})");
            }

            return count;
        }
    }
}
