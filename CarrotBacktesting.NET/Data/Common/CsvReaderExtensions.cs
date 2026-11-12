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
        /// 解析当前行指定列的日期（只保留 Date 精度）。
        /// 自动适配 DateTime、Unix 毫秒时间戳以及 ISO 8601 / Invariant / Local 文本日期格式。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DateTime ParseDate(this CsvDataReader reader, int colIdx)
        {
            if (colIdx < 0 || reader.IsDBNull(colIdx)) return DateTime.MinValue;

            Type fieldType = reader.GetFieldType(colIdx);
            if (fieldType == typeof(DateTime))
            {
                return reader.GetDateTime(colIdx).Date;
            }

            if (fieldType == typeof(long))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(colIdx)).DateTime.Date;
            }

            string dtStr = reader.GetString(colIdx);
            if (string.IsNullOrWhiteSpace(dtStr)) return DateTime.MinValue;

            if (long.TryParse(dtStr, out long ts))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(ts).DateTime.Date;
            }

            if (DateTime.TryParse(dtStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return dt.Date;
            }

            if (DateTime.TryParse(dtStr, out var dtLocal))
            {
                return dtLocal.Date;
            }

            return DateTime.MinValue;
        }

        /// <summary>
        /// 联合解析当前行的时间戳，优先从 datetime 列获取，缺失时回退至 timestamp 列。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DateTime ParseDate(this CsvDataReader reader, int dtIdx, int tsIdx)
        {
            if (dtIdx >= 0)
            {
                DateTime dt = reader.ParseDate(dtIdx);
                if (dt != DateTime.MinValue) return dt;
            }

            if (tsIdx >= 0)
            {
                return reader.ParseDate(tsIdx);
            }

            return DateTime.MinValue;
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
                if (colIdx < 0 || reader.IsDBNull(colIdx)) return default;

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
                if (string.IsNullOrWhiteSpace(valStr)) return default;
                try
                {
                    object converted = Convert.ChangeType(valStr, typeof(T), CultureInfo.InvariantCulture);
                    return (T)converted;
                }
                catch
                {
                    return default;
                }
            }
        }
    }
}
