using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Utility
{
    public static class IEnumerableExtensions
    {
        public static double Median(this IEnumerable<double> source)
        {
            var sorted = source.OrderBy(n => n).ToList();
            if (sorted.Count == 0) return 0;
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 0 ?
                (sorted[mid - 1] + sorted[mid]) / 2.0 :
                sorted[mid];
        }

        public static double StandardDeviation(this IEnumerable<double> source)
        {
            var values = source.ToList();
            int count = values.Count;
            if (count <= 1) return 0;

            double avg = values.Average();
            double sum = values.Sum(d => Math.Pow(d - avg, 2));
            return Math.Sqrt(sum / count);
        }
    }
}
