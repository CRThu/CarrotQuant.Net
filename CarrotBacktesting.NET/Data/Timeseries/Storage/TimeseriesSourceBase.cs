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
        protected readonly TableMetadata _metadata;
        protected readonly IStorageResolver _resolver;
        protected readonly string _tableId;
        protected readonly MarketTime? _startDate;
        protected readonly MarketTime? _endDate;
        protected readonly TimeZoneInfo? _sourceTimeZone;
        protected List<string> _symbols = new();
        protected Dictionary<string, int>? _symbolMap;
        protected List<MarketTime> _tradeDates = new();
        private bool _disposed;

        /// <inheritdoc />
        public IReadOnlyList<string> Symbols => _symbols;

        /// <inheritdoc />
        public IReadOnlyList<string> FieldNames => _metadata.FieldNames;

        /// <inheritdoc />
        public IReadOnlyList<MarketTime> TradeDates => _tradeDates;

        /// <summary>
        /// 数据源配置的时区信息（若未配置则为 null）。
        /// </summary>
        public TimeZoneInfo? SourceTimeZone => _sourceTimeZone;

        /// <summary>
        /// 核心受保护构造函数，接收不可变表元数据与统一调度器。
        /// </summary>
        protected TimeseriesSourceBase(
            TableMetadata metadata,
            IStorageResolver resolver,
            MarketTime? startDate = null,
            MarketTime? endDate = null,
            string? timeZone = null)
        {
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _tableId = metadata.TableId;
            _startDate = startDate;
            _endDate = endDate;
            if (!string.IsNullOrWhiteSpace(timeZone))
            {
                _sourceTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            }
        }



        /// <inheritdoc />
        public Type GetFieldType(string fieldName)
        {
            return _metadata.GetFieldType(fieldName);
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
        public int GetDateIndex(MarketTime date)
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

            MarketTime minDate = _tradeDates[startIndex];
            MarketTime maxDate = _tradeDates[startIndex + length - 1];

            int capacity = Math.Max(length, 16);
            MarketTime[] rawDates = ArrayPool<MarketTime>.Shared.Rent(capacity);
            T[] rawValues = ArrayPool<T>.Shared.Rent(capacity);

            try
            {
                int count = ReadRawSeriesFromFile(symbol, fieldName, minDate, maxDate, ref rawDates, ref rawValues);

                if (count > 1)
                {
                    bool isSorted = true;
                    for (int i = 1; i < count; i++)
                    {
                        if (rawDates[i] < rawDates[i - 1])
                        {
                            isSorted = false;
                            break;
                        }

                        if (rawDates[i] == rawDates[i - 1] &&
                            !EqualityComparer<T>.Default.Equals(rawValues[i], rawValues[i - 1]))
                        {
                            throw new InvalidDataException(
                                $"标的 '{symbol}' 在时间戳 {rawDates[i]} 存在冲突的重复记录 ({rawValues[i - 1]} vs {rawValues[i]})，数据完整性损坏，严禁隐式覆盖。");
                        }
                    }

                    if (!isSorted)
                    {
                        Array.Sort(rawDates, rawValues, 0, count);

                        for (int i = 1; i < count; i++)
                        {
                            if (rawDates[i] == rawDates[i - 1] &&
                                !EqualityComparer<T>.Default.Equals(rawValues[i], rawValues[i - 1]))
                            {
                                throw new InvalidDataException(
                                    $"标的 '{symbol}' 在时间戳 {rawDates[i]} 存在冲突的重复记录 ({rawValues[i - 1]} vs {rawValues[i]})，数据完整性损坏，严禁隐式覆盖。");
                            }
                        }
                    }
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
                ArrayPool<MarketTime>.Shared.Return(rawDates);
                ArrayPool<T>.Shared.Return(rawValues);
            }
        }

        /// <summary>
        /// 抽象物理抽取模板方法：从物理文件中提取指定品种和指标在窗口 [minDate, maxDate] 内的离散记录。
        /// </summary>
        protected abstract int ReadRawSeriesFromFile<T>(
            string symbol,
            string fieldName,
            MarketTime minDate,
            MarketTime maxDate,
            ref MarketTime[] rawDates,
            ref T[] rawValues) where T : unmanaged;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static void EnsureCapacity<T>(ref MarketTime[] dates, ref T[] values, int requiredCount)
        {
            if (requiredCount >= dates.Length)
            {
                int newCapacity = dates.Length * 2;
                MarketTime[] newDates = ArrayPool<MarketTime>.Shared.Rent(newCapacity);
                T[] newValues = ArrayPool<T>.Shared.Rent(newCapacity);

                Array.Copy(dates, newDates, dates.Length);
                Array.Copy(values, newValues, values.Length);

                ArrayPool<MarketTime>.Shared.Return(dates);
                ArrayPool<T>.Shared.Return(values);

                dates = newDates;
                values = newValues;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
