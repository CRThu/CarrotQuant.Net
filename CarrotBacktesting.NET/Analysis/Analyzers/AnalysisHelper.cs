using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Analysis.Analyzers;

/// <summary>
/// 绩效分析器内部通用辅助工具类，收敛日期寻址与统计通用算法，消灭 DRY 重复代码。
/// </summary>
internal static class AnalysisHelper
{
    /// <summary>
    /// 在市场元数据中解析指定时间戳对应的日期索引（支持精确时间与纯日期回退）。
    /// </summary>
    /// <param name="metadata">市场元数据。</param>
    /// <param name="time">待查找的交易时间。</param>
    /// <returns>找到的日期索引；若未命中则返回负数。</returns>
    public static int ResolveDateIndex(IMarketMetadata metadata, MarketTime time)
    {
        int idx = metadata.GetDateIndex(time);
        if (idx >= 0) return idx;

        idx = metadata.GetDateIndex(time.Date);
        if (idx >= 0) return idx;

        // 若元数据时间戳包含时分秒（日内 Bar）而入参为纯日期，利用交易日历升序特性二分匹配
        var tradeDates = metadata.TradeDates;
        MarketTime targetDate = time.Date;
        int low = 0;
        int high = tradeDates.Count - 1;

        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            MarketTime midDate = tradeDates[mid].Date;
            int cmp = midDate.CompareTo(targetDate);

            if (cmp == 0)
            {
                // 命中目标日期，向前收敛至当日首个时间步
                while (mid > 0 && tradeDates[mid - 1].Date == targetDate)
                {
                    mid--;
                }
                return mid;
            }

            if (cmp < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return -1;
    }

    /// <summary>
    /// 计算已排序切片的中位数（常数级内存开销）。
    /// </summary>
    /// <param name="sortedSpan">已排序的数值切片。</param>
    /// <returns>中位数。</returns>
    public static double CalculateMedian(ReadOnlySpan<double> sortedSpan)
    {
        if (sortedSpan.IsEmpty) return 0.0;
        int mid = sortedSpan.Length / 2;
        if ((sortedSpan.Length & 1) != 0)
        {
            return sortedSpan[mid];
        }
        return (sortedSpan[mid - 1] + sortedSpan[mid]) / 2.0;
    }

    /// <summary>
    /// 计算已排序数组的中位数。
    /// </summary>
    public static double CalculateMedian(double[] sortedArray) =>
        CalculateMedian(sortedArray.AsSpan());

    /// <summary>
    /// 计算已排序列表的中位数。
    /// </summary>
    public static double CalculateMedian(List<double> sortedList) =>
        CalculateMedian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(sortedList));
}
