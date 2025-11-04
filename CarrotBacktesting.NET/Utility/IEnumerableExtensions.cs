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
    }
}
