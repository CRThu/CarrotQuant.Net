using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 纵向时序市场数据源抽象基类（模板方法模式）。
    /// 统一封装元数据路由、参数边界校验、ArrayPool 内存池租借与扩容、日历排序以及 MarketDataAligner 网格对齐管线。
    /// 派生类仅需专注于特定存储格式（CSV、Parquet 等）的底层物理数据抽取。
    /// </summary>
    public abstract class TimeseriesSourceBase : ITimeseriesSource, IDisposable
    {
        protected readonly IStorageResolver _resolver;
        protected readonly string _tableId;
        protected readonly DateTime? _startDate;
        protected readonly DateTime? _endDate;
        protected List<string> _symbols = new();
        protected Dictionary<string, int>? _symbolMap;
        protected List<DateTime> _tradeDates = new();
        private bool _disposed;

        /// <inheritdoc />
        public IReadOnlyList<string> Symbols => _symbols;

        /// <inheritdoc />
        public IReadOnlyList<string> FieldNames => _resolver.GetFieldNames(_tableId);

        /// <inheritdoc />
        public IReadOnlyList<DateTime> TradeDates => _tradeDates;

        /// <summary>
        /// 基类受保护构造函数。
        /// </summary>
        protected TimeseriesSourceBase(
            IStorageResolver resolver,
            string tableId,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _tableId = tableId ?? throw new ArgumentNullException(nameof(tableId));
            _startDate = startDate;
            _endDate = endDate;
        }

        /// <inheritdoc />
        public Type GetFieldType(string fieldName)
        {
            return _resolver.GetFieldType(_tableId, fieldName);
        }

        /// <inheritdoc />
        public int GetSymbolIndex(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return -1;

            if (_symbolMap == null)
            {
                var map = new Dictionary<string, int>(_symbols.Count, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _symbols.Count; i++)
                {
                    map[_symbols[i]] = i;
                }
                _symbolMap = map;
            }

            return _symbolMap.TryGetValue(symbol, out int idx) ? idx : -1;
        }

        /// <inheritdoc />
        public int GetDateIndex(DateTime date)
        {
            return _tradeDates.BinarySearch(date);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadSymbolSeries<T>(string symbol, string fieldName, int startIndex, int length, Span<T> destination) where T : unmanaged
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (startIndex < 0 || startIndex + length > _tradeDates.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex), "Requested range is out of global TradeDates boundaries.");
            }

            if (destination.Length != length)
            {
                throw new ArgumentException($"Destination span length ({destination.Length}) must match requested length ({length}).");
            }

            DateTime minDate = _tradeDates[startIndex];
            DateTime maxDate = _tradeDates[startIndex + length - 1];

            int capacity = Math.Max(length, 16);
            DateTime[] rawDates = ArrayPool<DateTime>.Shared.Rent(capacity);
            T[] rawValues = ArrayPool<T>.Shared.Rent(capacity);

            try
            {
                int count = ReadRawSeriesFromFile(symbol, fieldName, minDate, maxDate, ref rawDates, ref rawValues);

                if (count > 1 && rawDates[0] > rawDates[count - 1])
                {
                    Array.Sort(rawDates, rawValues, 0, count);
                }

                // 统一通过 MarketDataAligner 完成全局日历投影，物理缺失天然填充标准 NaN / 哨兵
                MarketDataAligner.AlignAndFill(
                    _tradeDates,
                    startIndex,
                    length,
                    rawDates.AsSpan(0, count),
                    rawValues.AsSpan(0, count),
                    destination);
            }
            finally
            {
                ArrayPool<DateTime>.Shared.Return(rawDates);
                ArrayPool<T>.Shared.Return(rawValues);
            }
        }

        /// <summary>
        /// 抽象物理抽取模板方法：从物理文件中提取指定品种和指标在窗口 [minDate, maxDate] 内的离散记录。
        /// </summary>
        /// <typeparam name="T">非托管数值类型。</typeparam>
        /// <param name="symbol">标的代码。</param>
        /// <param name="fieldName">指标字段名。</param>
        /// <param name="minDate">窗口最小时间。</param>
        /// <param name="maxDate">窗口最大时间。</param>
        /// <param name="rawDates">池化日期数组（需支持按需扩容）。</param>
        /// <param name="rawValues">池化数值数组（需支持按需扩容）。</param>
        /// <returns>提取出的物理记录有效行数。</returns>
        protected abstract int ReadRawSeriesFromFile<T>(
            string symbol,
            string fieldName,
            DateTime minDate,
            DateTime maxDate,
            ref DateTime[] rawDates,
            ref T[] rawValues) where T : unmanaged;

        /// <summary>
        /// 缓冲区扩容辅助工具：当提取行数达到上限时，自适应向 ArrayPool 申请双倍容量。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static void EnsureCapacity<T>(ref DateTime[] rawDates, ref T[] rawValues, int count) where T : unmanaged
        {
            if (count >= rawDates.Length)
            {
                int newCap = rawDates.Length * 2;
                var newDates = ArrayPool<DateTime>.Shared.Rent(newCap);
                var newVals = ArrayPool<T>.Shared.Rent(newCap);
                rawDates.AsSpan(0, count).CopyTo(newDates);
                rawValues.AsSpan(0, count).CopyTo(newVals);
                ArrayPool<DateTime>.Shared.Return(rawDates);
                ArrayPool<T>.Shared.Return(rawValues);
                rawDates = newDates;
                rawValues = newVals;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Dispose(true);
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }
}
