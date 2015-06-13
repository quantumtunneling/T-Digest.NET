using C5;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace TDigest {
    public static class Ext {
        private static Random _rand = new Random();

        public static void Shuffle<T>(this System.Collections.Generic.IList<T> list) {
            int n = list.Count;
            while (n > 1) {
                n--;
                int k = _rand.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }
    }

    internal class Centroid {
        public double Mean { get; set; }
        public double Count { get; set; }

        public Centroid(double mean, double count) {
            Mean = mean;
            Count = count;
        }
    }

    public class TDigest {
        private C5.TreeDictionary<double, Centroid> _centroids;
        private Random _rand;
        private double _count;
        private double _delta;
        private double _K;

        public static void Main(string[] args) {
            TDigest t = new TDigest();

            var r = new Random();
            for (int i = 0; i < 1000000; i++) {
                t.Update(r.NextDouble() * 100);
            }
            var nn = t.Quantile(.999);
        }

        public TDigest(double delta = 0.01, double K = 25) {
            _centroids = new TreeDictionary<double, Centroid>();
            _rand = new Random();
            _count = 0;
            _delta = delta;
            _K = K;
        }

        private double ComputeCentroidQuantile(Centroid centroid) {
            double sum = 0;
            foreach (var c in _centroids.Values) {
                if (c.Mean > centroid.Mean) break;
                sum += c.Count; 
            }

            double denom = _count;
            return (centroid.Count / 2 + sum) / denom;
        }

        private Centroid[] GetNeighbors(double x, bool inclusive) {
            C5.KeyValuePair<double, Centroid> successor;
            C5.KeyValuePair<double, Centroid> predecessor;
            bool hasSuccessor;
            bool hasPredecessor;

            if (inclusive) {
                hasSuccessor = _centroids.TryWeakSuccessor(x, out successor);
                hasPredecessor = _centroids.TryWeakPredecessor(x, out predecessor);
            }
            else {
                hasSuccessor = _centroids.TrySuccessor(x, out successor);
                hasPredecessor = _centroids.TryPredecessor(x, out predecessor);
            }

            if (hasSuccessor && hasPredecessor) return new[] { successor.Value, predecessor.Value };
            else if (hasSuccessor) return new[] { successor.Value };
            else if (hasPredecessor) return new[] { predecessor.Value };
            else return null;
        }

        private IEnumerable<Centroid> GetClosestCentroids(double x) {
            C5.KeyValuePair<double, Centroid> successor;
            C5.KeyValuePair<double, Centroid> predecessor;
            bool hasSuccessor;
            bool hasPredecessor;

            hasSuccessor = _centroids.TryWeakSuccessor(x, out successor);
            hasPredecessor = _centroids.TryWeakPredecessor(x, out predecessor);

            if (hasPredecessor & hasPredecessor) {
                var aDiff = Math.Abs(successor.Value.Mean - x);
                var bDiff = Math.Abs(predecessor.Value.Mean - x);

                if (aDiff < bDiff) yield return successor.Value;
                else if (bDiff < aDiff) yield return predecessor.Value;
                else {
                    yield return successor.Value;
                    yield return predecessor.Value;
                }
            }
            else if (hasSuccessor) yield return successor.Value;
            else yield return predecessor.Value;
        }

        private double GetThreshold(double q) {
            return 4 * _count * _delta * q * (1 - q);
        }

        public void Update(double x, double w = 1) {
            _count += w;
            if (_centroids.Count == 0) {
                _centroids.Add(x, new Centroid(x, w));
                return;
            }

            var candidates = GetNeighbors(x, true)
                .Select(c => new {
                    Threshold = GetThreshold(ComputeCentroidQuantile(c)),
                    Centroid = c
                })
                .Where(c => c.Centroid.Count + w < c.Threshold)
                .ToList();

            while (candidates.Count > 0 & w > 0) {
                var c = candidates[_rand.Next() % candidates.Count];
                var delta_w = Math.Min(c.Threshold - c.Centroid.Count, w);

                _centroids.Remove(c.Centroid.Mean);
                c.Centroid.Count += delta_w;
                c.Centroid.Mean += delta_w * (x - c.Centroid.Mean) / c.Centroid.Count;
                _centroids.Add(c.Centroid.Mean, c.Centroid);
                w -= delta_w;
                
                candidates.Remove(c);
            }

            if (w > 0) _centroids.Add(x, new Centroid(x, w));
            if (_centroids.Count > (_K / _delta)) Compress();
        }

        public void Compress() {
            var newTDigest = new TDigest(_delta, _K);
            var temp = _centroids.Values.ToList();
            temp.Shuffle();
            foreach (var centroid in temp) {
                newTDigest.Update(centroid.Mean, centroid.Count);
            }
            _centroids = newTDigest._centroids; 
        }

        public double Quantile(double quantile) {
            if (quantile < 0 || quantile > 1) {
                throw new ArgumentOutOfRangeException("quantile must be between 0 and 1");
            }

            var q = quantile * _count;
            double t = 0;
            int i=0;
            Centroid last = null;
            foreach (var centroid in _centroids.Values) {
                last = centroid;
                var k = centroid.Count;
                if (q < t + k) {
                    double delta;
                    if (i == 0) {
                        var ceiling = _centroids.Successor(centroid.Mean).Value;
                        delta = ceiling.Mean - centroid.Mean;
                    }
                    else if (i == _centroids.Count - 1) {
                        var floor = _centroids.Predecessor(centroid.Mean).Value;
                        delta = centroid.Mean - floor.Mean;
                    }
                    else {
                        var ceiling = _centroids.Successor(centroid.Mean).Value;
                        var floor = _centroids.Predecessor(centroid.Mean).Value;
                        delta = (ceiling.Mean - floor.Mean) / 2;
                    }

                    var ret = centroid.Mean + ((q - t) / k - .5) * delta;
                }
                t += k;
                i++;
            }

            return last.Mean;
        }
    }
}
