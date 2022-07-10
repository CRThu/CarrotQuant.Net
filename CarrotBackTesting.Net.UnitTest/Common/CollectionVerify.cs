using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBackTesting.Net.Common
{
    public static class CollectionVerify
    {
        /// <summary>
        /// 比较两个集合, 返回元素误差(0-1)
        /// </summary>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        /// <returns></returns>
        public static (double min, double max, double avg) CollectionElementsVerify(IEnumerable<double> v1, IEnumerable<double> v2)
        {
            var err = v1.Zip(v2, (x1, x2) => Math.Abs(x1 - x2)).ToArray();
            return (err.Min(), err.Max(), err.Average());
        }
    }
}
