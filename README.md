# T-Digest.NET

.NET Implementation of the relatively new T-Digest quantile estimation algorithm. Useful for calculating highly accurate Quantiles or Percentiles from on-line streaming data, or data-sets that are too large to store in memory and sort, which is required to calculate the true quantile. 

The Nuget package for this Implementation can be found <a href="https://www.nuget.org/packages/TDigest/">here</a>

The T-Digest white paper can be found <a href="https://github.com/tdunning/t-digest/blob/master/docs/t-digest-paper/histo.pdf">here</a>

Example Code:

    using StatsLib;
    ...
    
    Random r = new Random();
    TDigest digest = new TDigest();
    
    for (var i=0; i<1000000; i++) {
    	var value = r.nextDouble();
    	digest.Add(value);
    }
    
    var median = digest.Quantile(.5);
    var n99th = digest.Quantile(.99);
    var n999th = digest.Quantile(.999);
