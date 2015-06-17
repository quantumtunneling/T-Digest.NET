using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace StatsLib.Tests {
    [TestClass]
    public class Tests {

        [TestMethod]
        public void TestUniformDistribution() {
            Random r = new Random();

            TDigest digest = new TDigest();
            List<double> actual = new List<double>();
            for (int i = 0; i < 10000; i++) {
                var v = r.NextDouble();
                digest.Add(v);
                actual.Add(v);
            }

            actual.Sort();
            Assert.AreEqual(10000, actual.Count);
            Assert.AreEqual(10000, digest.Count);

            var avgError = GetAvgError(actual, digest);
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

            var avgError = GetAvgError(actual, digest);
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

            var avgError = GetAvgError(actual, digest);
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

            var avgError = GetAvgError(actual, digest);
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
            List<double> actualA = new List<double>();
            for (int i = 0; i < 10000; i++) {
                var n = (r.Next() % 50) + (r.Next() % 50);
                digestA.Add(n);
                actualA.Add(n);
            }

            TDigest digestB = new TDigest();
            List<double> actualB = new List<double>();
            for (int i = 0; i < 10000; i++) {
                var n = (r.Next() % 100) + (r.Next() % 100);
                digestB.Add(n);
                actualB.Add(n);
            }

            var actual = actualA.Concat(actualB).ToList();
            actual.Sort();

            var digest = TDigest.Merge(digestA, digestB);
            Assert.AreEqual(actual.Count, digest.Count);

            var avgError = GetAvgError(actual, digest);
            Assert.IsTrue(avgError < .5);
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

            Assert.AreEqual(digestA.Count, digestB.Count);
            Assert.AreEqual(digestA.CentroidCount, digestB.CentroidCount);
            Assert.AreEqual(digestA.CompressionConstant, digestB.CompressionConstant);
            Assert.AreEqual(digestA.Accuracy, digestB.Accuracy);

            var areEqual = Enumerable.Range(1, 999)
                .Select(n => n / 1000.0)
                .All(q => digestA.Quantile(q) == digestB.Quantile(q));

            Assert.IsTrue(areEqual, "Serialized TDigest is not the same as original");
        }

        private double GetAvgError(IList<double> all, TDigest digest) {
            return Enumerable.Range(1, 999)
                .Select(n => n / 1000.0)
                .Select(q => Math.Abs(all.Quantile(q)-digest.Quantile(q)))
                .Average();
        }
    }

    public static class Ext {
        public static double Quantile(this IList<double> l, double q) {
            var qIdx = (int)(Math.Round(q * l.Count));
            return l[qIdx];
        }
    }
}
