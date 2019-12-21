using C5;
using System;
using System.Linq;
using System.Collections.Generic;

namespace StatsLib {

    public class TDigest {
        private C5.TreeDictionary<double, Centroid> _centroids;
        private static Random _rand;
        private double _count;

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
        /// Gets the Accuracy setting as specified in the constructor. 
        /// Smaller numbers result in greater accuracy at the expense of 
        /// poorer performance and greater memory consumpion
        /// Default is .02
        /// </summary>
        public double Accuracy { get; private set; }

        /// <summary>
        /// The Compression Constant Setting 
        /// </summary>
        public double CompressionConstant { get; private set; }

        /// <summary>
        /// The Average 
        /// </summary>
        public double Average {
            get { return _count > 0 ? _newAvg : 0; }
        }

        /// <summary>
        /// The Max
        /// </summary>
        public double Max { get; private set; }


        /// <summary>
        /// The Min
        /// </summary>
        public double Min { get; private set; }

        private double _newAvg, _oldAvg;

        /// <summary>
        /// Merge two T-Digests
        /// </summary>
        /// <param name="a">The first T-Digest</param>
        /// <param name="b">The second T-Digest</param>
        /// <returns>A T-Digest created by merging the specified T-Digests</returns>
        public static TDigest Merge(TDigest a, TDigest b) {
            TDigest merged = new TDigest();
            Centroid[] combined = a._centroids.Values.Concat(b._centroids.Values).ToArray();
            combined.Shuffle();
            foreach (var c in combined) {
                merged.Add(c.Mean, c.Count);
            }
            merged._newAvg = ((a._newAvg * a._count) + (b._newAvg * b._count)) / (a.Count + b.Count);
            merged._oldAvg = ((a._oldAvg * (a.Count - 1)) + (b._oldAvg * (b.Count - 1))) / (a._count + b._count - 2);

            return merged;
        }

        /// <summary>
        /// Construct a T-Digest,
        /// </summary>
        /// <param name="accuracy">Controls the trade-off between accuracy and memory consumption/performance. 
        /// Default value is .05, higher values result in worse accuracy, but better performance and decreased memory usage, while
        /// lower values result in better accuracy and increased performance and memory usage</param>
        /// <param name="compression">K value</param>
        public TDigest(double accuracy = 0.02, double compression = 25) {
            if (accuracy <= 0) throw new ArgumentOutOfRangeException("Accuracy must be greater than 0");
            if (compression < 15) throw new ArgumentOutOfRangeException("Compression constant must be 15 or greater");

            _centroids = new TreeDictionary<double, Centroid>();
            _rand = new Random();
            _count = 0;
            Accuracy = accuracy;
            CompressionConstant = compression;
        }

        /// <summary>
        /// Construct a TDigest from a serialized string of Bytes created by the Serialize() method
        /// </summary>
        /// <param name="serialized"></param>
        public TDigest(byte[] serialized)
            : this() {
            if (serialized == null) throw new ArgumentNullException("Serialized parameter cannot be null");

            if ((serialized.Length - 48) % 16 != 0) {
                throw new ArgumentException("Serialized data is invalid or corrupted");
            }

            _newAvg = BitConverter.ToDouble(serialized, 0);
            _oldAvg = BitConverter.ToDouble(serialized, 8);
            Accuracy = BitConverter.ToDouble(serialized, 16);
            CompressionConstant = BitConverter.ToDouble(serialized, 24);
            Min = BitConverter.ToDouble(serialized, 32);
            Max = BitConverter.ToDouble(serialized, 40);

            var centroids = Enumerable.Range(0, (serialized.Length - 48) / 16)
                .Select(i => new {
                    Mean = BitConverter.ToDouble(serialized, i * 16 + 48),
                    Count = BitConverter.ToDouble(serialized, i * 16 + 8 + 48)
                })
                .Select(d => new Centroid(d.Mean, d.Count));

            var kvPairs = centroids
                .Select(c => new C5.KeyValuePair<double, Centroid>(c.Mean, c))
                .ToArray();

            _centroids.AddAll(kvPairs);
            _count = centroids.Sum(c => c.Count);
        }

        /// <summary>
        /// Add a new value to the T-Digest. Note that this method is NOT thread safe. 
        /// </summary>
        /// <param name="value">The value to add</param>
        /// <param name="weight">The relative weight associated with this value. Default is 1 for all values.</param>
        public void Add(double value, double weight = 1) {
            if (weight <= 0) throw new ArgumentOutOfRangeException("Weight must be greater than 0");

            var first = _count == 0;
            _count += weight;

            if (first) {
                _oldAvg = value;
                _newAvg = value;
                Min = value;
                Max = value;
            }
            else {
                _newAvg = _oldAvg + (value - _oldAvg) / _count;
                _oldAvg = _newAvg;
                Max = value > Max ? value : Max;
                Min = value < Min ? value : Min;
            }

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

            if (_centroids.Count > (CompressionConstant / Accuracy)) {
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
                throw new ArgumentOutOfRangeException("Quantile must be between 0 and 1");
            }

            if (_centroids.Count == 0) {
                throw new InvalidOperationException("Cannot call Quantile() method until first Adding values to the digest");
            }

            if (_centroids.Count == 1) {
                return _centroids.First().Value.Mean;
            }

            double index = quantile * _count;
            if (index < 1) {
                return Min;
            }
            if (index > Count - 1) {
                return Max;
            }

            Centroid currentNode = _centroids.First().Value;
            Centroid lastNode = _centroids.Last().Value;
            double currentWeight = currentNode.Count;
            if (currentWeight == 2 && index <= 2) {
                // first node is a double weight with one sample at min, sou we can infer location of other sample
                return 2 * currentNode.Mean - Min;
            }

            if (_centroids.Last().Value.Count == 2 && index > Count - 2) {
                // likewise for last centroid
                return 2 * lastNode.Mean - Max;
            }

            double weightSoFar = currentWeight / 2.0;

            if (index < weightSoFar) {
                return WeightedAvg(Min, weightSoFar - index, currentNode.Mean, index - 1);
            }

            foreach (Centroid nextNode in _centroids.Values.Skip(1)) {
                double nextWeight = nextNode.Count;
                double dw = (currentWeight + nextWeight) / 2.0;

                if (index < weightSoFar + dw) {
                    double leftExclusion = 0;
                    double rightExclusion = 0;
                    if (currentWeight == 1) {
                        if (index < weightSoFar + 0.5) {
                            return currentNode.Mean;
                        }
                        else {
                            leftExclusion = 0.5;
                        }
                    }
                    if (nextWeight == 1) {
                        if (index >= weightSoFar + dw - 0.5) {
                            return nextNode.Mean;
                        }
                        else {
                            rightExclusion = 0.5;
                        }
                    }
                    // centroids i and i+1 bracket our current point
                    // we interpolate, but the weights are diminished if singletons are present
                    double weight1 = index - weightSoFar - leftExclusion;
                    double weight2 = weightSoFar + dw - index - rightExclusion;
                    return WeightedAvg(currentNode.Mean, weight2, nextNode.Mean, weight1);
                }

                weightSoFar += dw;
                currentNode = nextNode;
                currentWeight = nextWeight;
            }

            double w1 = index - weightSoFar;
            double w2 = Count - 1 - index;
            return WeightedAvg(currentNode.Mean, w2, Max, w1);
        }

        private double WeightedAvg(double m1, double w1, double m2, double w2) {
            double total = w1 + w2;
            double ret = m1 * w1 / total + m2 * w2 / total;
            return ret;
        }

        /// <summary>
        /// Gets the Distribution of the data added thus far
        /// </summary>
        /// <returns>An array of objects that contain a value (x-axis) and a count (y-axis) 
        /// which can be used to plot a distribution of the data set</returns>
        public DistributionPoint[] GetDistribution() {
            return _centroids.Values
                .Select(c => new DistributionPoint(c.Mean, c.Count))
                .ToArray();
        }

        /// <summary>
        /// Serializes this T-Digest to a byte[]
        /// </summary>
        /// <returns></returns>
        public Byte[] Serialize() {
            var count = _centroids.Values.Sum(c => c.Count);

            var fields = new[] { _newAvg, _oldAvg, Accuracy, CompressionConstant, Min, Max }
                .SelectMany(f => BitConverter.GetBytes(f));

            var data = _centroids.Values
                .SelectMany(c => BitConverter.GetBytes(c.Mean).Concat(BitConverter.GetBytes(c.Count)));

            return fields
                .Concat(data)
                .ToArray();
        }

        private bool Compress() {
            TDigest newTDigest = new TDigest(Accuracy, CompressionConstant);
            List<Centroid> temp = _centroids.Values.ToList();
            temp.Shuffle();

            foreach (Centroid centroid in temp) {
                newTDigest.Add(centroid.Mean, centroid.Count);
            }

            _centroids = newTDigest._centroids;
            return true;
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
            return 4 * _count * Accuracy * q * (1 - q);
        }

        private void ReInsertCentroid(double oldMean, Centroid c) {
            var ret = _centroids.Remove(oldMean);
            _centroids.Add(c.Mean, c);
        }
    }

    public class DistributionPoint {
        public double Value { get; private set; }
        public double Count { get; private set; }

        public DistributionPoint(double value, double count) {
            Value = value;
            Count = count;
        }
    }

    internal class Centroid {
        public double Mean { get; private set; }
        public double Count { get; private set; }

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
