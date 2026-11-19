using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Sylvan.Data.Csv;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// CSV 数据读取流式解析扩展工具类（跨时序、事件与维表共享）。
    /// 提供高性能列查找、无装箱泛型数值转换及统一时间戳解析。
    /// </summary>
    public static class CsvReaderExtensions
    {
        /// <summary>
        /// 忽略大小写查找目标列索引，未找到返回 -1。
        /// 避免抛出和捕获异常带来的巨大栈回溯性能损耗。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FindColumn(this CsvDataReader reader, string name)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 按候选名称顺序依次查找列索引，返回首个匹配的列索引；均未找到返回 -1。
        /// 典型用于候选时间列 ("datetime", "timestamp") 的轻量探测。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FindColumn(this CsvDataReader reader, params string[] candidateNames)
        {
            foreach (var name in candidateNames)
            {
                int idx = reader.FindColumn(name);
                if (idx >= 0) return idx;
            }
            return -1;
        }

        /// <summary>
        /// 构造全表大小写不敏感的列名到序号映射字典。
        /// </summary>
        public static Dictionary<string, int> GetOrdinalMap(this CsvDataReader reader)
        {
            var map = new Dictionary<string, int>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                map[reader.GetName(i)] = i;
            }
            return map;
        }

        /// <summary>
        /// 从 CSV 当前行读取并解析为 <see cref="MarketTime"/>。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MarketTime ParseTime(this CsvDataReader reader, int colIdx, TimeZoneInfo? sourceTimeZone = null)
        {
            if (colIdx < 0 || reader.IsDBNull(colIdx)) return MarketTime.MinValue;

            Type fieldType = reader.GetFieldType(colIdx);
            if (fieldType == typeof(DateTime))
            {
                DateTime dtVal = reader.GetDateTime(colIdx);
                return MarketTime.FromExchange(dtVal, sourceTimeZone);
            }

            if (fieldType == typeof(long))
            {
                long tsVal = reader.GetInt64(colIdx);
                return MarketTime.FromUnixTimeMilliseconds(tsVal);
            }

            ReadOnlySpan<char> span = reader.GetFieldSpan(colIdx);
            if (span.IsEmpty || span.IsWhiteSpace()) return MarketTime.MinValue;

            if (MarketTime.TryParse(span, sourceTimeZone, out var mt))
            {
                return mt;
            }

            // CSV 文本若未绑定 Schema 且为 13 位毫秒戳或 10 位秒戳，作为数值降级解析
            if (span.Length == 13 && long.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out long tsMs))
            {
                return MarketTime.FromUnixTimeMilliseconds(tsMs);
            }
            if (span.Length == 10 && long.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out long tsSec))
            {
                return MarketTime.FromUnixTimeSeconds(tsSec);
            }

            return MarketTime.MinValue;
        }

        /// <summary>
        /// 联合解析当前行的时间戳，返回 <see cref="MarketTime"/>。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MarketTime ParseTime(this CsvDataReader reader, int dtIdx, int tsIdx, TimeZoneInfo? sourceTimeZone = null)
        {
            if (dtIdx >= 0)
            {
                MarketTime mt = reader.ParseTime(dtIdx, sourceTimeZone);
                if (mt != MarketTime.MinValue) return mt;
            }

            if (tsIdx >= 0)
            {
                return reader.ParseTime(tsIdx, sourceTimeZone);
            }

            return MarketTime.MinValue;
        }

        /// <summary>
        /// 读取非托管泛型数值，底层采用 Unsafe.As 零装箱零拷贝转换。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadValue<T>(this CsvDataReader reader, int colIdx) where T : unmanaged
        {
            return CsvValueConverter<T>.Read(reader, colIdx);
        }

        /// <summary>
        /// 泛型无装箱转换内核，利用泛型静态分发避免运行时分支判断。
        /// </summary>
        private static class CsvValueConverter<T> where T : unmanaged
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static T Read(CsvDataReader reader, int colIdx)
            {
                if (colIdx < 0 || reader.IsDBNull(colIdx)) return MarketDataAligner.GetNaNValue<T>();

                ReadOnlySpan<char> span = reader.GetFieldSpan(colIdx);
                if (span.IsEmpty || span.IsWhiteSpace()) return MarketDataAligner.GetNaNValue<T>();

                if (typeof(T) == typeof(double)) { double val = reader.GetDouble(colIdx); return Unsafe.As<double, T>(ref val); }
                if (typeof(T) == typeof(float)) { float val = reader.GetFloat(colIdx); return Unsafe.As<float, T>(ref val); }
                if (typeof(T) == typeof(long)) { long val = reader.GetInt64(colIdx); return Unsafe.As<long, T>(ref val); }
                if (typeof(T) == typeof(int)) { int val = reader.GetInt32(colIdx); return Unsafe.As<int, T>(ref val); }
                if (typeof(T) == typeof(bool)) { bool val = reader.GetBoolean(colIdx); return Unsafe.As<bool, T>(ref val); }
                if (typeof(T) == typeof(byte)) { byte val = reader.GetByte(colIdx); return Unsafe.As<byte, T>(ref val); }

                return ParseValueFallback(reader.GetString(colIdx));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static T ParseValueFallback(string? valStr)
            {
                if (string.IsNullOrWhiteSpace(valStr)) return MarketDataAligner.GetNaNValue<T>();
                try
                {
                    object converted = Convert.ChangeType(valStr, typeof(T), CultureInfo.InvariantCulture);
                    return (T)converted;
                }
                catch
                {
                    return MarketDataAligner.GetNaNValue<T>();
                }
            }
        }
    }
}
