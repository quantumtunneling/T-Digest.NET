using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace StatsLib.Tests {
    [TestClass]
    public class Tests {

        [TestMethod]
        public void TestFixForNegativeQuantileBug()
        {
            var r = new Random();
            var numbers = new List<int>();
            var digest = new TDigest();
            for (var i = 0; i < 10 * 1000; i++)
            {
                var n = r.NextDouble() < 0.001 ? 10001 : r.Next(0, 100);
                digest.Add(n);
                numbers.Add(n);
                var q99 = digest.Quantile(0.99);
                Assert.IsTrue(q99 >= 0, string.Format("q99: {0}, numbers: {1}", q99, string.Join(",", numbers)));
            }
        }

        [TestMethod]
        public void TestUniformDistribution() {
            Random r = new Random();

            TDigest digest = new TDigest();
            List<double> actual = new List<double>();
            for (int i = 0; i < 50000; i++) {
                var v = r.NextDouble();
                digest.Add(v);
                actual.Add(v);
            }

            actual.Sort();
            Assert.AreEqual(50000, actual.Count);
            Assert.AreEqual(50000, digest.Count);

            Assert.IsTrue(GetAvgError(actual, digest) < .01);
            Assert.IsTrue(MaxIsEqual(actual, digest));
            Assert.IsTrue(MinIsEqual(actual, digest));
            var avgError = GetAvgPercentileError(actual, digest);
            Assert.IsTrue(avgError < .0005);
        }

        [TestMethod]
        public void TestConstantValue() {
            Random r = new Random();

            TDigest digest = new TDigest();
            List<double> actual = new List<double>();
            for (int i = 0; i < 10000; i++) {
                digest.Add(100);
                actual.Add(100);
            }
            actual.Sort();

            Assert.IsTrue(GetAvgError(actual, digest) < .01);
            Assert.IsTrue(MaxIsEqual(actual, digest));
            Assert.IsTrue(MinIsEqual(actual, digest));
            var avgError = GetAvgPercentileError(actual, digest);
            Assert.IsTrue(avgError == 0);
        }

        [TestMethod]
        public void TestSequential() {
            Random r = new Random();
            TDigest digest = new TDigest(.01);
            List<double> actual = new List<double>();
            for (int i = 0; i < 10000; i++) {
                digest.Add(i);
                actual.Add(i);
            }
            actual.Sort();

            Assert.IsTrue(GetAvgError(actual, digest) < .01);
            Assert.IsTrue(MaxIsEqual(actual, digest));
            Assert.IsTrue(MinIsEqual(actual, digest));
            var avgError = GetAvgPercentileError(actual, digest);
            Assert.IsTrue(avgError < 5);
        }

        [TestMethod]
        public void TestNormalDistribution() {
            Random r = new Random();
            TDigest digest = new TDigest();
            List<double> actual = new List<double>();
            for (int i = 0; i < 10000; i++) {
                var n = (r.Next() % 100) + (r.Next() % 100);
                digest.Add(n);
                actual.Add(n);
            }
            actual.Sort();

            var z = digest.Quantile(0);

            Assert.IsTrue(GetAvgError(actual, digest) < .01);
            Assert.IsTrue(MaxIsEqual(actual, digest));
            Assert.IsTrue(MinIsEqual(actual, digest));
            var avgError = GetAvgPercentileError(actual, digest);
            Assert.IsTrue(avgError < .5);
        }

        [TestMethod]
        public void TestEdgeCases() {
            // No elements
            TDigest digest = new TDigest();
            try {
                digest.Quantile(.5);
                Assert.Fail("Didn't throw exception when quantile() called before adding any elements"); 
            }
            catch (InvalidOperationException) {
            }

            // one element
            digest.Add(50);
            var v = digest.Quantile(.5);
            Assert.AreEqual(50, v);

            v = digest.Quantile(0);
            Assert.AreEqual(50, v);

            v = digest.Quantile(1);
            Assert.AreEqual(50, v);

            // Two elements
            digest.Add(100);
            v = digest.Quantile(1);
            Assert.AreEqual(100, v);
        }

        [TestMethod]
        public void TestMerge() {
            Random r = new Random();

            TDigest digestA = new TDigest();
            TDigest digestAll = new TDigest();
            List<double> actual = new List<double>();
            for (int i = 0; i < 10000; i++) {
                var n = (r.Next() % 50) + (r.Next() % 50);
                digestA.Add(n);
                digestAll.Add(n);
                actual.Add(n);
            }

            TDigest digestB = new TDigest();
            List<double> actualB = new List<double>();
            for (int i = 0; i < 10000; i++) {
                var n = (r.Next() % 100) + (r.Next() % 100);
                digestB.Add(n);
                digestAll.Add(n);
                actual.Add(n);
            }

            actual.Sort();

            var merged = TDigest.Merge(digestA, digestB);
            Assert.AreEqual(actual.Count, merged.Count);

            var avgError = GetAvgPercentileError(actual, merged);
            Assert.IsTrue(avgError < .5);

            var trueAvg = actual.Average();
            var deltaAvg = Math.Abs(digestAll.Average - merged.Average);
            Assert.IsTrue(deltaAvg < .01);
        }

        [TestMethod]
        public void TestSerialization() {
            Random r = new Random();

            TDigest digestA = new TDigest();
            for (int i = 0; i < 10000; i++) {
                var n = (r.Next() % 50) + (r.Next() % 50);
                digestA.Add(n);
            }

            byte[] s = digestA.Serialize();
            TDigest digestB = new TDigest(s);

            var a = digestA.GetDistribution();
            var b = digestB.GetDistribution();
            for (int i=0; i<a.Length; i++) {
                var ce = a[i].Count == b[i].Count;
                var me = a[i].Value == b[i].Value;
                Assert.IsTrue(ce && me, "Centroid means or counts are not equal after serialization");
            }

            Assert.AreEqual(digestA.Average, digestB.Average, "Averages are not equal after serialization");
            Assert.AreEqual(digestA.Count, digestB.Count, "Counts are not equal after serialization");
            Assert.AreEqual(digestA.CentroidCount, digestB.CentroidCount, "Centroid Counts are not equal after serialization");
            Assert.AreEqual(digestA.CompressionConstant, digestB.CompressionConstant, "Compression Constants are not equal after serialization");
            Assert.AreEqual(digestA.Accuracy, digestB.Accuracy, "Accuracies are not equal after serialization");

            var differences = Enumerable.Range(1, 999)
                .Select(n => n / 1000.0)
                .Where(q => digestA.Quantile(q) != digestB.Quantile(q))
                .Select(q => new { q, A = digestA.Quantile(q), B = digestB.Quantile(q) })
                .ToList();

            var areEqual = !differences.Any();

            Assert.IsTrue(areEqual, "Serialized TDigest is not the same as original");
        }

        private double GetAvgPercentileError(IList<double> all, TDigest digest) {
            return Enumerable.Range(1, 999)
                .Select(n => n / 1000.0)
                .Select(q => Math.Abs(all.Quantile(q)-digest.Quantile(q)))
                .Average();
        }

        private double GetAvgError(IList<double> actual, TDigest digest) {
            return Math.Abs(actual.Average() - digest.Average);
        }

        private bool MaxIsEqual(IList<double> actual, TDigest digest) {
            return actual.Max() == digest.Max;
        }

        private bool MinIsEqual(IList<double> actual, TDigest digest) {
            return actual.Min() == digest.Min;
        }
    }

    public static class Ext {
        public static double Quantile(this IList<double> l, double q) {
            var qIdx = (int)(Math.Round(q * l.Count));
            return l[qIdx];
        }
    }
}
