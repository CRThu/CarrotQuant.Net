using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 高性能动态市场元数据实现。
    /// 满足 <see cref="IMarketMetadata"/> 契约，支持高并发无锁点查与实盘时序推进。
    /// </summary>
    public class DynamicMarketMetadata : IMutableMarketMetadata
    {
        private readonly List<string> _symbols;
        private readonly List<MarketTime> _tradeDates;
        private readonly List<string> _fieldNames;
        private readonly ConcurrentDictionary<string, int> _symbolMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<MarketTime, int> _dateMap = new();
        private readonly ConcurrentDictionary<string, Type> _fieldTypes = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();

        private volatile string[] _cachedSymbols;
        private volatile MarketTime[] _cachedTradeDates;
        private volatile string[] _cachedFieldNames;

        /// <inheritdoc />
        public IReadOnlyList<string> Symbols => _cachedSymbols;

        /// <inheritdoc />
        public IReadOnlyList<string> FieldNames => _cachedFieldNames;

        /// <inheritdoc />
        public IReadOnlyList<MarketTime> TradeDates => _cachedTradeDates;

        /// <summary>
        /// 从已有的基准元数据构造动态市场元数据。
        /// </summary>
        public DynamicMarketMetadata(IMarketMetadata baseMetadata)
        {
            ArgumentNullException.ThrowIfNull(baseMetadata);

            _symbols = new List<string>(baseMetadata.Symbols);
            for (int i = 0; i < _symbols.Count; i++)
            {
                _symbolMap[_symbols[i]] = i;
            }

            _tradeDates = new List<MarketTime>(baseMetadata.TradeDates);
            for (int i = 0; i < _tradeDates.Count; i++)
            {
                _dateMap[_tradeDates[i]] = i;
            }

            _fieldNames = new List<string>(baseMetadata.FieldNames);
            foreach (var field in _fieldNames)
            {
                _fieldTypes[field] = baseMetadata.GetFieldType(field);
            }

            _cachedSymbols = _symbols.ToArray();
            _cachedTradeDates = _tradeDates.ToArray();
            _cachedFieldNames = _fieldNames.ToArray();
        }

        /// <summary>
        /// 直接从初始列表构造。
        /// </summary>
        public DynamicMarketMetadata(IEnumerable<string> symbols, IEnumerable<MarketTime> tradeDates, IDictionary<string, Type>? fieldTypes = null)
        {
            _symbols = new List<string>(symbols ?? Array.Empty<string>());
            for (int i = 0; i < _symbols.Count; i++)
            {
                _symbolMap[_symbols[i]] = i;
            }

            _tradeDates = new List<MarketTime>(tradeDates ?? Array.Empty<MarketTime>());
            for (int i = 0; i < _tradeDates.Count; i++)
            {
                _dateMap[_tradeDates[i]] = i;
            }

            _fieldNames = new List<string>();
            if (fieldTypes != null)
            {
                foreach (var kvp in fieldTypes)
                {
                    _fieldNames.Add(kvp.Key);
                    _fieldTypes[kvp.Key] = kvp.Value;
                }
            }

            _cachedSymbols = _symbols.ToArray();
            _cachedTradeDates = _tradeDates.ToArray();
            _cachedFieldNames = _fieldNames.ToArray();
        }

        /// <inheritdoc />
        public int AppendDate(MarketTime date)
        {
            if (_dateMap.TryGetValue(date, out int existingIndex))
            {
                return existingIndex;
            }

            lock (_gate)
            {
                if (_dateMap.TryGetValue(date, out existingIndex))
                {
                    return existingIndex;
                }

                int index = _tradeDates.Count;
                _tradeDates.Add(date);
                _dateMap[date] = index;
                _cachedTradeDates = _tradeDates.ToArray();
                return index;
            }
        }

        /// <inheritdoc />
        public int AppendSymbol(string symbol)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

            if (_symbolMap.TryGetValue(symbol, out int existingIndex))
            {
                return existingIndex;
            }

            lock (_gate)
            {
                if (_symbolMap.TryGetValue(symbol, out existingIndex))
                {
                    return existingIndex;
                }

                int index = _symbols.Count;
                _symbols.Add(symbol);
                _symbolMap[symbol] = index;
                _cachedSymbols = _symbols.ToArray();
                return index;
            }
        }

        /// <inheritdoc />
        public bool HasField(string fieldName)
        {
            return !string.IsNullOrWhiteSpace(fieldName) && _fieldTypes.ContainsKey(fieldName);
        }

        /// <inheritdoc />
        public Type GetFieldType(string fieldName)
        {
            return _fieldTypes.TryGetValue(fieldName, out var type)
                ? type
                : typeof(double);
        }

        /// <inheritdoc />
        public int GetSymbolIndex(string symbol)
        {
            return _symbolMap.TryGetValue(symbol, out int idx) ? idx : -1;
        }

        /// <inheritdoc />
        public int GetDateIndex(MarketTime date)
        {
            return _dateMap.TryGetValue(date, out int idx) ? idx : -1;
        }
    }
}
