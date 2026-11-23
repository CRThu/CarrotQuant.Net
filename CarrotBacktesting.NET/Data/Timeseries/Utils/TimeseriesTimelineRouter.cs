using System;
using System.Collections.Generic;
using System.Globalization;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序时间轴对齐计算工具，提供主时钟与子表时间轴之间的因果映射构建方法。
    /// </summary>
    public static class TimeseriesTimelineRouter
    {
        /// <summary>
        /// 解析时间偏移量字符串（支持标准格式如 "15:00:00"、"15:00" 及简写 "15h"、"1d"、"30m"、"10s"）。
        /// </summary>
        public static TimeSpan ParseTimeOffset(string offsetStr)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(offsetStr);
            offsetStr = offsetStr.Trim();

            if (TimeSpan.TryParse(offsetStr, CultureInfo.InvariantCulture, out var ts))
            {
                return ts;
            }

            if (offsetStr.EndsWith("h", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(offsetStr[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double hours))
            {
                return TimeSpan.FromHours(hours);
            }
            if (offsetStr.EndsWith("d", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(offsetStr[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double days))
            {
                return TimeSpan.FromDays(days);
            }
            if (offsetStr.EndsWith("m", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(offsetStr[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes))
            {
                return TimeSpan.FromMinutes(minutes);
            }
            if (offsetStr.EndsWith("s", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(offsetStr[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }

            throw new ArgumentException(
                $"无法解析 time_offset: '{offsetStr}'。格式支持如 '15:00:00', '15h', '1d', '30m'。",
                nameof(offsetStr));
        }

        /// <summary>
        /// 构建主时钟时间轴对应子表的历史可见行映射数组。
        /// </summary>
        /// <param name="primaryDates">主时钟时间序列（按时间递增）。</param>
        /// <param name="subDates">子表记录时间序列（按时间递增）。</param>
        /// <param name="timeOffset">
        /// 可选的子表时间偏移量（如 "15:00:00"、"15h"）。
        /// 若数据已包含具体时刻（如 15:00:00）则无需偏移；若为 00:00:00 纯日期且主轴为日内高频时，可通过此参数平移可见时刻。
        /// </param>
        /// <returns>
        /// 长度与 primaryDates 相同的行索引映射数组。
        /// 元素值为各主时间步可观察到的子表最大行索引；若无可观察数据则为 -1。
        /// </returns>
        public static int[] BuildAsOfMap(
            IReadOnlyList<MarketTime> primaryDates,
            IReadOnlyList<MarketTime> subDates,
            string? timeOffset = null)
        {
            ArgumentNullException.ThrowIfNull(primaryDates);
            ArgumentNullException.ThrowIfNull(subDates);

            int primaryCount = primaryDates.Count;
            int subCount = subDates.Count;

            var map = new int[primaryCount];
            if (primaryCount == 0 || subCount == 0)
            {
                Array.Fill(map, -1);
                return map;
            }

            // 1. 解析可选的时间偏移量
            bool hasExplicitOffset = !string.IsNullOrWhiteSpace(timeOffset);
            TimeSpan explicitOffset = hasExplicitOffset ? ParseTimeOffset(timeOffset!) : TimeSpan.Zero;

            // 2. 检测主轴是否包含日内时间戳（Ticks % TicksPerDay != 0）
            bool primaryIsIntraday = false;
            for (int i = 0; i < primaryCount; i++)
            {
                if ((primaryDates[i].Ticks % TimeSpan.TicksPerDay) != 0)
                {
                    primaryIsIntraday = true;
                    break;
                }
            }

            // 3. 计算子表各数据点生效可见的时间戳
            var subCloseTicks = new long[subCount];
            for (int k = 0; k < subCount; k++)
            {
                long ticks = subDates[k].Ticks;

                if (hasExplicitOffset)
                {
                    // 显式偏移（如纯日期加上 15h）
                    subCloseTicks[k] = ticks + explicitOffset.Ticks;
                }
                else if (primaryIsIntraday)
                {
                    bool isSubDateOnly = (ticks % TimeSpan.TicksPerDay) == 0;
                    if (isSubDateOnly)
                    {
                        // 主轴为日内高频且子表为纯日期无偏移时，默认次日零点闭合
                        subCloseTicks[k] = ticks + TimeSpan.TicksPerDay;
                    }
                    else
                    {
                        // 子表自带时间戳时，以其记录时间作为生效时间
                        subCloseTicks[k] = ticks;
                    }
                }
                else
                {
                    // 主轴为日频时，当日即可见
                    subCloseTicks[k] = ticks;
                }
            }

            // 4. 双指针线性扫描构建映射
            int subCursor = 0;
            for (int i = 0; i < primaryCount; i++)
            {
                long pTicks = primaryDates[i].Ticks;

                while (subCursor < subCount && subCloseTicks[subCursor] <= pTicks)
                {
                    subCursor++;
                }

                map[i] = subCursor - 1;
            }

            return map;
        }
    }
}
