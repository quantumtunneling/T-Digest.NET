using C5;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace TDigest {

    public class TDigest {
        private C5.TreeDictionary<double, Centroid> _centroids;
        private Random _rand;
        private double _count;
        private double _delta;
        private double _K;

        /// <summary>
        /// Construct a T-Digest
        /// </summary>
        /// <param name="delta">delta value</param>
        /// <param name="K">K value</param>
        public TDigest(double delta = 0.01, double K = 25) {
            _centroids = new TreeDictionary<double, Centroid>();
            _rand = new Random();
            _count = 0;
            _delta = delta;
            _K = K;
        }

        /// <summary>
        /// Add a new value to the T-Digest
        /// </summary>
        /// <param name="value">The value to add</param>
        /// <param name="w">The relative weight associated with this value. Default is 1 for all values.</param>
        public void Add(double value, double w = 1) {
            _count += w;
            if (_centroids.Count == 0) {
                _centroids.Add(value, new Centroid(value, w));
                return;
            }

            var candidates = GetClosestCentroids(value)
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
                c.Centroid.Mean += delta_w * (value - c.Centroid.Mean) / c.Centroid.Count;
                _centroids.Add(c.Centroid.Mean, c.Centroid);
                w -= delta_w;
                
                candidates.Remove(c);
            }

            if (w > 0) _centroids.Add(value, new Centroid(value, w));
            if (_centroids.Count > (_K / _delta)) Compress();
        }

        /// <summary>
        /// Estimates the specified quantile
        /// </summary>
        /// <param name="quantile">The quantile to esimtate. Must be between 0 and 1.</param>
        /// <returns>The value for the estimated quantile</returns>
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

        private double ComputeCentroidQuantile(Centroid centroid) {
            double sum = 0;
            foreach (var c in _centroids.Values) {
                if (c.Mean > centroid.Mean) break;
                sum += c.Count; 
            }

            double denom = _count;
            return (centroid.Count / 2 + sum) / denom;
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

        private void Compress() {
            var newTDigest = new TDigest(_delta, _K);
            var temp = _centroids.Values.ToList();
            Shuffle(temp);
            foreach (var centroid in temp) {
                newTDigest.Add(centroid.Mean, centroid.Count);
            }
            _centroids = newTDigest._centroids; 
        }

        private void Shuffle(System.Collections.Generic.IList<Centroid> centroids) {
            int n = centroids.Count;
            while (n > 1) {
                n--;
                int k = _rand.Next(n + 1);
                Centroid c = centroids[k];
                centroids[k] = centroids[n];
                centroids[n] = c;
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
}
