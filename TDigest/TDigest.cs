using C5;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;

namespace TDigest {

    public class TDigest {
        private C5.TreeDictionary<double, Centroid> _centroids;
        private Random _rand;
        private double _count;
        private double _accuracy;
        private double _K;

        public double Count {
            get { return _count; } 
        }

        /// <summary>
        /// Construct a T-Digest
        /// </summary>
        /// <param name="accuracy">Controls the trade-off between accuracy and memory consumption/performance. 
        /// Default value is .05, higher values result in better performance and decreased memory usage, while
        /// lower values result in better accuracy</param>
        /// <param name="K">K value</param>
        public TDigest(double accuracy = 0.05, double K = 25) {
            _centroids = new TreeDictionary<double, Centroid>();
            _rand = new Random();
            _count = 0;
            _accuracy = accuracy;
            _K = K;
        }

        /// <summary>
        /// Add a new value to the T-Digest
        /// </summary>
        /// <param name="value">The value to add</param>
        /// <param name="weight">The relative weight associated with this value. Default is 1 for all values.</param>
        public void Add(double value, double weight = 1) {
            _count += weight;
            if (_centroids.Count == 0) {
                _centroids.Add(value, new Centroid(value, weight));
                return;
            }

            var candidates = GetClosestCentroids(value).Select(c => new {
                Threshold = GetThreshold(ComputeCentroidQuantile(c)),
                Centroid = c
            })
            .Where(c => c.Centroid.Count + weight < c.Threshold)
            .ToList();

            while (candidates.Count > 0 & weight > 0) {
                var cData = candidates[_rand.Next() % candidates.Count];
                var delta_w = Math.Min(cData.Threshold - cData.Centroid.Count, weight);

                _centroids.Remove(cData.Centroid.Mean);
                cData.Centroid.Count += delta_w;
                cData.Centroid.Mean += delta_w * (value - cData.Centroid.Mean) / cData.Centroid.Count;
                _centroids.Add(cData.Centroid.Mean, cData.Centroid);
                weight -= delta_w;

                candidates.Remove(cData);
            }

            if (weight > 0) _centroids.Add(value, new Centroid(value, weight));
            if (_centroids.Count > (_K / _accuracy)) Compress();
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

            double q = quantile * _count;
            double t = 0;
            int i=0;
            Centroid last = null;
            foreach (Centroid centroid in _centroids.Values) {
                last = centroid;
                double k = centroid.Count;
                if (q < t + k) {
                    double delta;
                    if (i == 0) {
                        Centroid successor = _centroids.Successor(centroid.Mean).Value;
                        delta = successor.Mean - centroid.Mean;
                    }
                    else if (i == _centroids.Count - 1) {
                        Centroid predecessor = _centroids.Predecessor(centroid.Mean).Value;
                        delta = centroid.Mean - predecessor.Mean;
                    }
                    else {
                        Centroid successor = _centroids.Successor(centroid.Mean).Value;
                        Centroid predecessor = _centroids.Predecessor(centroid.Mean).Value;
                        delta = (successor.Mean - predecessor.Mean) / 2;
                    }

                    return centroid.Mean + ((q - t) / k - .5) * delta;
                }
                t += k;
                i++;
            }

            return last.Mean;
        }

        private double ComputeCentroidQuantile(Centroid centroid) {
            double sum = 0;
            foreach (Centroid c in _centroids.Values) {
                if (c.Mean > centroid.Mean) break;
                sum += c.Count; 
            }

            double denom = _count;
            return (centroid.Count / 2 + sum) / denom;
        }

        private IEnumerable<Centroid> GetClosestCentroids(double x) {
            C5.KeyValuePair<double, Centroid> successor;
            C5.KeyValuePair<double, Centroid> predecessor;
            bool hasSuccessor = _centroids.TryWeakSuccessor(x, out successor);
            bool hasPredecessor = _centroids.TryWeakPredecessor(x, out predecessor);

            if (hasSuccessor & hasPredecessor) {
                double aDiff = Math.Abs(successor.Value.Mean - x);
                double bDiff = Math.Abs(predecessor.Value.Mean - x);

                if (aDiff < bDiff) yield return successor.Value;
                else if (bDiff < aDiff) yield return predecessor.Value;
                else {
                    yield return successor.Value;
                    yield return predecessor.Value;
                }
            }
            else if (hasSuccessor) yield return successor.Value;
            else if (hasPredecessor) yield return predecessor.Value;
            else yield break;
        }

        private double GetThreshold(double q) {
            return 4 * _count * _accuracy * q * (1 - q);
        }

        private void Compress() {
            TDigest newTDigest = new TDigest(_accuracy, _K);
            List<Centroid> temp = _centroids.Values.ToList();
            Shuffle(temp);
            foreach (Centroid centroid in temp) {
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
