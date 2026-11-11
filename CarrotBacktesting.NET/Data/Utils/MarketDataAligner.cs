using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 市场行情时间序列数据日历对齐组件。
    /// 将离散物理记录投射至全局权威交易日历网格，物理缺失单元格严格置 NaN（整型采用 DolphinDB/Kdb+ 标准哨兵值 MinValue）。
    /// 遵循第一性原理：数据层忠实反映物理数据真相，零业务状态假设，零跨文件倒查。
    /// </summary>
    public static class MarketDataAligner
    {
        /// <summary>
        /// 获取非托管数值类型的标准 NaN / 缺失哨兵值。
        /// 浮点类型采用 IEEE 754 硬件 NaN (double.NaN / float.NaN)；
        /// 整型采用金融时序标准哨兵值 (long.MinValue / int.MinValue)，杜绝以 0 充当缺失导致的业务歧义。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T GetNaNValue<T>() where T : unmanaged
        {
            if (typeof(T) == typeof(double))
            {
                double nan = double.NaN;
                return Unsafe.As<double, T>(ref nan);
            }
            if (typeof(T) == typeof(float))
            {
                float nan = float.NaN;
                return Unsafe.As<float, T>(ref nan);
            }
            if (typeof(T) == typeof(long))
            {
                long nullVal = long.MinValue;
                return Unsafe.As<long, T>(ref nullVal);
            }
            if (typeof(T) == typeof(int))
            {
                int nullVal = int.MinValue;
                return Unsafe.As<int, T>(ref nullVal);
            }
            if (typeof(T) == typeof(short))
            {
                short nullVal = short.MinValue;
                return Unsafe.As<short, T>(ref nullVal);
            }
            if (typeof(T) == typeof(sbyte))
            {
                sbyte nullVal = sbyte.MinValue;
                return Unsafe.As<sbyte, T>(ref nullVal);
            }
            if (typeof(T) == typeof(byte))
            {
                byte nullVal = byte.MaxValue;
                return Unsafe.As<byte, T>(ref nullVal);
            }
            return default;
        }

        /// <summary>
        /// 将标的的离散原始记录投射到目标全局交易日历网格。
        /// 物理缺失单元格填充 NaN。零额外 GC 堆分配，采用双游标单遍流式扫描完成。
        /// </summary>
        /// <typeparam name="T">非托管数值类型</typeparam>
        /// <param name="globalTradeDates">市场全局权威交易日历（升序）</param>
        /// <param name="startIndex">目标时间窗口在全局日历中的起始索引</param>
        /// <param name="length">目标时间窗口长度（必须等于 destination.Length）</param>
        /// <param name="recordDates">原始物理文件记录的日期列表（升序）</param>
        /// <param name="recordValues">原始物理文件记录的指标数值（必须与 recordDates 等长）</param>
        /// <param name="destination">目标输出内存块</param>
        public static void AlignAndFill<T>(
            IReadOnlyList<DateTime> globalTradeDates,
            int startIndex,
            int length,
            ReadOnlySpan<DateTime> recordDates,
            ReadOnlySpan<T> recordValues,
            Span<T> destination) where T : unmanaged
        {
            ArgumentNullException.ThrowIfNull(globalTradeDates);

            if (startIndex < 0 || startIndex + length > globalTradeDates.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex), "Requested window is outside global trade dates boundaries.");
            }
            if (destination.Length != length)
            {
                throw new ArgumentException($"Destination span length ({destination.Length}) must match requested length ({length}).");
            }
            if (recordDates.Length != recordValues.Length)
            {
                throw new ArgumentException($"Record dates count ({recordDates.Length}) must match record values count ({recordValues.Length}).");
            }

            T nanVal = GetNaNValue<T>();
            destination.Fill(nanVal);

            if (recordDates.Length == 0)
            {
                return;
            }

            DateTime windowStartDate = globalTradeDates[startIndex].Date;
            int rawIdx = 0;

            // 快速跳过窗口起始日之前的历史记录
            while (rawIdx < recordDates.Length && recordDates[rawIdx].Date < windowStartDate)
            {
                rawIdx++;
            }

            // 双游标流式扫描日历
            for (int i = 0; i < length && rawIdx < recordDates.Length; i++)
            {
                DateTime curDate = globalTradeDates[startIndex + i].Date;

                // 跳过非交易日杂质行或重复记录
                while (rawIdx < recordDates.Length && recordDates[rawIdx].Date < curDate)
                {
                    rawIdx++;
                }

                if (rawIdx < recordDates.Length && recordDates[rawIdx].Date == curDate)
                {
                    destination[i] = recordValues[rawIdx++];
                }
            }
        }

        /// <summary>
        /// 判定指定数值是否为 NaN 或缺失哨兵值。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValueNaN<T>(T val) where T : unmanaged
        {
            if (typeof(T) == typeof(double))
            {
                return double.IsNaN(Unsafe.As<T, double>(ref val));
            }
            if (typeof(T) == typeof(float))
            {
                return float.IsNaN(Unsafe.As<T, float>(ref val));
            }
            if (typeof(T) == typeof(long))
            {
                return Unsafe.As<T, long>(ref val) == long.MinValue;
            }
            if (typeof(T) == typeof(int))
            {
                return Unsafe.As<T, int>(ref val) == int.MinValue;
            }
            if (typeof(T) == typeof(short))
            {
                return Unsafe.As<T, short>(ref val) == short.MinValue;
            }
            if (typeof(T) == typeof(sbyte))
            {
                return Unsafe.As<T, sbyte>(ref val) == sbyte.MinValue;
            }
            if (typeof(T) == typeof(byte))
            {
                return Unsafe.As<T, byte>(ref val) == byte.MaxValue;
            }
            return false;
        }
    }
}
