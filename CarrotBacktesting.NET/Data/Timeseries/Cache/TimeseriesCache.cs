using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序二进制对齐持久化存储门面 (Facade)。
    /// 统一编排表级双标尺索引管理器 (<see cref="TimeseriesIndexStore"/>)
    /// 与字段级二进制分块元数据存储器 (<see cref="TimeseriesFieldStore"/>)，
    /// 实现 <see cref="ITimeseriesCache"/> 统一契约。
    /// </summary>
    public class TimeseriesCache : ITimeseriesCache
    {
        /// <summary>
        /// 表级标的代码索引文件名 (symbols.idx)。
        /// </summary>
        public const string SymbolsIndexFileName = TimeseriesIndexStore.SymbolsIndexFileName;

        /// <summary>
        /// 表级交易时间戳索引文件名 (time.idx)。
        /// </summary>
        public const string TimeIndexFileName = TimeseriesIndexStore.TimeIndexFileName;

        private readonly string _storeRoot;
        private readonly string _tableId;
        private readonly int _pageSize;
        private readonly string _compression;
        private readonly TimeseriesIndexStore _indexStore;
        private readonly ConcurrentDictionary<string, TimeseriesFieldStore> _fieldStores = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 持久化物理根目录路径。
        /// </summary>
        public string StoreRoot => _storeRoot;

        /// <inheritdoc />
        public string TableId => _tableId;

        /// <inheritdoc />
        public int PageSize => _pageSize;

        /// <summary>
        /// 压缩算法名称（如 "lz4", "none"）。
        /// </summary>
        public string Compression => _compression;

        /// <summary>
        /// 表级双标尺索引管理器。
        /// </summary>
        public TimeseriesIndexStore IndexStore => _indexStore;

        /// <summary>
        /// 初始化时序持久化存储门面管理器。
        /// </summary>
        /// <param name="storeRoot">物理存储根目录（例如 {storage_root}/.cache 或表级存储目录）</param>
        /// <param name="tableId">时序表唯一标识符</param>
        /// <param name="pageSize">分页行数大小，默认为 4096</param>
        /// <param name="compression">压缩格式，支持 "lz4"（默认）与 "none"（无压缩裸块）</param>
        public TimeseriesCache(string storeRoot, string tableId, int pageSize = 4096, string compression = "lz4")
        {
            if (string.IsNullOrWhiteSpace(storeRoot))
            {
                throw new ArgumentException("Store root directory cannot be null or empty.", nameof(storeRoot));
            }
            if (string.IsNullOrWhiteSpace(tableId))
            {
                throw new ArgumentException("Table ID cannot be null or empty.", nameof(tableId));
            }
            if (pageSize <= 0 || (pageSize & (pageSize - 1)) != 0)
            {
                throw new ArgumentException("PageSize 必须为大于 0 且为 2 的整数次幂。", nameof(pageSize));
            }

            _storeRoot = Path.GetFullPath(storeRoot);
            _tableId = tableId;
            _pageSize = pageSize;
            _compression = string.IsNullOrWhiteSpace(compression) ? "lz4" : compression.ToLowerInvariant();
            _indexStore = new TimeseriesIndexStore(_storeRoot, _tableId);
        }

        /// <summary>
        /// 获取或创建指定字段的存储管理子实例。
        /// </summary>
        /// <param name="fieldName">字段名称</param>
        public TimeseriesFieldStore GetFieldStore(string fieldName)
        {
            ArgumentNullException.ThrowIfNull(fieldName);
            return _fieldStores.GetOrAdd(fieldName, name => new TimeseriesFieldStore(
                _storeRoot,
                _tableId,
                name,
                _pageSize,
                _compression,
                _indexStore));
        }

        /// <summary>
        /// 获取指定字段的物理文件存储目录路径：{storeRoot}/{tableId}/{fieldName}
        /// </summary>
        public string GetFieldStoreDir(string fieldName)
        {
            return GetFieldStore(fieldName).FieldDir;
        }

        /// <inheritdoc />
        public string GetTableDir()
        {
            return _indexStore.GetTableDir();
        }

        /// <inheritdoc />
        public string GetSymbolsIndexPath()
        {
            return _indexStore.GetSymbolsIndexPath();
        }

        /// <inheritdoc />
        public string GetTimeIndexPath()
        {
            return _indexStore.GetTimeIndexPath();
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ReadSymbols()
        {
            return _indexStore.ReadSymbols();
        }

        /// <inheritdoc />
        public void WriteSymbols(IReadOnlyList<string> symbols)
        {
            _indexStore.WriteSymbols(symbols);
        }

        /// <inheritdoc />
        public IReadOnlyList<MarketTime> ReadTradeDates()
        {
            return _indexStore.ReadTradeDates();
        }

        /// <inheritdoc />
        public void WriteTradeDates(IReadOnlyList<MarketTime> tradeDates)
        {
            _indexStore.WriteTradeDates(tradeDates);
        }

        /// <inheritdoc />
        public void AppendTradeDates(IReadOnlyList<MarketTime> newTradeDates, int targetRowCount = -1)
        {
            _indexStore.AppendTradeDates(newTradeDates, targetRowCount);
        }

        /// <inheritdoc />
        public bool HasField(string fieldName)
        {
            return GetFieldStore(fieldName).HasField();
        }

        /// <inheritdoc />
        public bool IsCompatible(
            string fieldName,
            IReadOnlyList<string> expectedSymbols,
            Type expectedType,
            out TimeseriesMetadata? metadata)
        {
            return GetFieldStore(fieldName).IsCompatible(expectedSymbols, expectedType, out metadata);
        }

        /// <inheritdoc />
        public void RewriteChunk<T>(
            string fieldName,
            int pageIndex,
            Action<int, Span<T>> rowProvider,
            int? newChunkWidth = null,
            IReadOnlyList<string>? updatedSymbols = null) where T : unmanaged
        {
            GetFieldStore(fieldName).RewriteChunk(pageIndex, rowProvider, newChunkWidth, updatedSymbols);
        }

        /// <inheritdoc />
        public TimeseriesMetadata? ReadMetadata(string fieldName)
        {
            return GetFieldStore(fieldName).ReadMetadata();
        }

        /// <inheritdoc />
        public unsafe void WriteMatrix<T>(
            string fieldName,
            ChunkMatrix2D<T> matrix,
            IReadOnlyList<string> symbols,
            IReadOnlyList<MarketTime> tradeDates) where T : unmanaged
        {
            GetFieldStore(fieldName).WriteMatrix(matrix, symbols, tradeDates);
        }

        /// <inheritdoc />
        public unsafe ChunkMatrix2D<T> LoadMatrix<T>(string fieldName, int? targetWidth = null) where T : unmanaged
        {
            return GetFieldStore(fieldName).LoadMatrix<T>(targetWidth);
        }

        /// <inheritdoc />
        public unsafe void AppendRows<T>(
            string fieldName,
            int deltaRows,
            Action<int, Span<T>> rowProvider,
            IReadOnlyList<MarketTime> newTradeDates) where T : unmanaged
        {
            GetFieldStore(fieldName).AppendRows(deltaRows, rowProvider, newTradeDates);
        }

        /// <inheritdoc />
        public bool TryReadChunk<T>(string fieldName, int pageIndex, Span<T> destination, out int rowsRead) where T : unmanaged
        {
            return GetFieldStore(fieldName).TryReadChunk(pageIndex, destination, out rowsRead);
        }

        /// <inheritdoc />
        public void Clear(string fieldName)
        {
            GetFieldStore(fieldName).Clear();
            _fieldStores.TryRemove(fieldName, out _);
        }

        /// <inheritdoc />
        public void ClearTable()
        {
            _indexStore.ClearTable();
            _fieldStores.Clear();
        }
    }
}
