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
        private static Random _rand;
        private double _count;
        private double _accuracy;
        private double _K;

        /// <summary>
        /// Returns the sum of the weights of all objects added to the Digest. 
        /// Since the default weight for each object is 1, this will be equal to the number
        /// of objects added to the digest unless custom weights are used.
        /// </summary>
        public double Count {
            get { return _count; } 
        }

        /// <summary>
        /// Returns the number of Internal Centroid objects allocated. 
        /// The number of these objects is directly proportional to the amount of memory used.
        /// </summary>
        public int CentroidCount {
            get { return _centroids.Count; }
        }

        /// <summary>
        /// Merge two T-Digests
        /// </summary>
        /// <param name="a">The first T-Digest</param>
        /// <param name="b">The second T-Digest</param>
        /// <returns>A T-Digest created by merging the specified T-Digests</returns>
        public static TDigest Merge(TDigest a, TDigest b) {
            TDigest merged = new TDigest();
            Centroid[] combined = a._centroids.Values.Concat(b._centroids.Values).ToArray();
            Shuffle(combined);
            foreach (var c in combined) {
                merged.Add(c.Mean, c.Count);
            }
            return merged;
        }

        /// <summary>
        /// Construct a T-Digest
        /// </summary>
        /// <param name="accuracy">Controls the trade-off between accuracy and memory consumption/performance. 
        /// Default value is .05, higher values result in better performance and decreased memory usage, while
        /// lower values result in better accuracy</param>
        /// <param name="K">K value</param>
        public TDigest(double accuracy = 0.02, double K = 25) {
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

            var closest = GetClosestCentroids(value);

            var candidates = closest
                .Select(c => new {
                    Threshold = GetThreshold(ComputeCentroidQuantile(c)),
                    Centroid = c
                })
                .Where(c => c.Centroid.Count + weight < c.Threshold)
                .ToList();

            while (candidates.Count > 0 & weight > 0) {
                var cData = candidates[_rand.Next() % candidates.Count];
                var delta_w = Math.Min(cData.Threshold - cData.Centroid.Count, weight);

                double oldMean;
                if (cData.Centroid.Update(delta_w, value, out oldMean)) {
                    ReInsertCentroid(oldMean, cData.Centroid);
                }

                weight -= delta_w;
                candidates.Remove(cData);
            }

            if (weight > 0) {
                var toAdd = new Centroid(value, weight);
                if (_centroids.FindOrAdd(value, ref toAdd)) {
                    double oldMean;
                    if (toAdd.Update(weight, toAdd.Mean, out oldMean)) {
                        ReInsertCentroid(oldMean, toAdd);
                    }                        
                }
            }
            if (_centroids.Count > (_K / _accuracy)) {
                Compress();
            }
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
            if (_centroids.Count == 0) {
                throw new InvalidOperationException("Cannot call Quantile() method until first Adding values to the digest");
            }

            if (_centroids.Count == 1) {
                return _centroids.First().Value.Mean;
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

            if (!_centroids.TryWeakSuccessor(x, out successor)) {
                yield return _centroids.Predecessor(x).Value;
                yield break;
            }

            if (successor.Value.Mean == x || !_centroids.TryPredecessor(x, out predecessor)) {
                yield return successor.Value;
                yield break;
            }

            double sDiff = Math.Abs(successor.Value.Mean - x);
            double pDiff = Math.Abs(successor.Value.Mean - x);

            if (sDiff < pDiff) yield return successor.Value;
            else if (pDiff < sDiff) yield return predecessor.Value;
            else {
                yield return successor.Value;
                yield return predecessor.Value;
            }
        }

        private double GetThreshold(double q) {
            return 4 * _count * _accuracy * q * (1 - q);
        }

        public bool Compress() {
            Debug.WriteLine("PreCompress: "+CentroidCount);
            TDigest newTDigest = new TDigest(_accuracy, _K);
            List<Centroid> temp = _centroids.Values.ToList();
            Shuffle(temp);
            foreach (Centroid centroid in temp) {
                newTDigest.Add(centroid.Mean, centroid.Count);
            }
            _centroids = newTDigest._centroids;
            Debug.WriteLine("PostCompress: "+CentroidCount);
            return true;
        }

        private void ReInsertCentroid(double oldMean, Centroid c) {
            var ret = _centroids.Remove(oldMean);
            _centroids.Add(c.Mean, c);
        }

        private static void Shuffle(System.Collections.Generic.IList<Centroid> centroids) {
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

        public bool Update(double delta_w, double value, out double oldMean) {
            oldMean = Mean;
            Count += delta_w;
            Mean += delta_w * (value - Mean) / Count;
            return oldMean != Mean;
        }
    }
}
