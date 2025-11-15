using ScottPlot.Palettes;
using System;

namespace CarrotBacktesting.NET.Utility.ScottPlot
{
    public static class HistogramHelper
    {
        public static (double[] bins, string[] labels) GetBins(double binSize, double firstBin, double lastBin, string format = "P0")
        {
            if (lastBin <= firstBin)
                throw new ArgumentException($"{lastBin} must be greater than {nameof(firstBin)}");

            // -4~+4  -Inf~-4   -4~-2   -2~0    0~+2    +2~+4   +4~+Inf
            // bin:   -Inf      -4      -2      0       +2      +4
            // disp:  <-4       -4      -2      +2      +4      >+4
            int binCount = (int)((lastBin - firstBin) / binSize) + 2;

            var bins = new double[binCount];
            bins[0] = double.NegativeInfinity;
            for (int i = 1; i < bins.Length; i++)
            {
                bins[i] = firstBin + (i - 1) * binSize;
            }

            var labels = new string[binCount];
            for (int i = 1; i < bins.Length - 1; i++)
            {
                double lower = bins[i];
                double upper = bins[i + 1];
                if (bins[i] >= 0)
                    labels[i] = $"+{upper.ToString(format)}";
                else
                    labels[i] = $"{lower.ToString(format)}";
            }
            labels[0] = $"<{bins[1].ToString(format)}";
            labels[binCount - 1] = $">{bins[binCount - 1].ToString(format)}";
            return (bins, labels);
        }

        /// <summary>
        /// 将一维数据根据给定的分箱边界进行计数(每一个分箱值为其左侧最小值)
        /// </summary>
        /// <param name="data"></param>
        /// <param name="bins"></param>
        /// <returns></returns>
        public static int[] ToHist(this IEnumerable<double> data, double[] bins)
        {
            int[] counts = new int[bins.Length];
            foreach (var value in data)
            {
                for (int i = bins.Length - 1; i >= 0; i--)
                {
                    if (value >= bins[i])
                    {
                        counts[i]++;
                        break;
                    }
                }
            }
            return counts;
        }

        /// <summary>
        /// 使用 Freedman-Diaconis Rule 计算直方图的推荐分箱数量。
        /// </summary>
        /// <param name="data">输入的 double 数组数据。</param>
        /// <returns>推荐的分箱数量 (整数)。</returns>
        public static int CalculateFDBins(double[] data)
        {
            if (data == null || data.Length < 2)
            {
                // 对于少于 2 个数据点的数组，无法计算 IQR，返回一个默认值。
                return 1;
            }

            int N = data.Length;

            // --- 1. 排序数据以计算四分位数 ---
            var sortedData = data.OrderBy(x => x).ToArray();

            // --- 2. 计算四分位数 Q1 和 Q3 ---

            // Q1 索引：(N * 0.25)
            // 使用插值或简单近似。这里使用 Type 7 / R-7 近似（标准做法）：
            double Q1 = GetQuantile(sortedData, 0.25);

            // Q3 索引：(N * 0.75)
            double Q3 = GetQuantile(sortedData, 0.75);

            // --- 3. 计算 IQR (四分位数间距) ---
            double IQR = Q3 - Q1;

            // 4. 获取数据的范围 (Max - Min)
            double dataRange = sortedData.Last() - sortedData.First();

            // 避免除以零和不必要的计算
            if (IQR == 0 || dataRange == 0)
            {
                // 如果所有数据都一样，或者 IQR 为零，使用平方根法则作为回退
                return (int)Math.Ceiling(Math.Sqrt(N));
            }

            // --- 5. 计算 Bin 宽度 (H) ---
            // H = 2 * IQR / N^(1/3)
            double binWidth = 2.0 * IQR / Math.Pow(N, 1.0 / 3.0);

            // --- 6. 计算推荐的分箱数量 ---
            // Bins = Ceiling((Max - Min) / H)
            int numBins = (int)Math.Ceiling(dataRange / binWidth);

            return numBins;
        }

        /// <summary>
        /// 辅助方法：计算分位数（使用 Type 7 / R-7 近似）
        /// </summary>
        public static double GetQuantile(double[] sortedData, double probability)
        {
            int N = sortedData.Length;
            // 计算索引 j = p * (N - 1)
            double index = probability * (N - 1);
            int lower = (int)Math.Floor(index);
            int upper = (int)Math.Ceiling(index);

            // 如果索引是整数
            if (lower == upper)
            {
                return sortedData[lower];
            }

            // 否则进行线性插值
            double fraction = index - lower;
            return sortedData[lower] * (1 - fraction) + sortedData[upper] * fraction;
        }
    }
}