using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CarrotBacktesting.NET.Config;
using K4os.Compression.LZ4;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序二进制对齐持久化存储管理器。
    /// 负责按 4096 行（或自定义定长）将行情宽表切分为 chunk_{i}.bin 文件进行物理持久化，
    /// 原生支持 LZ4 超高速块压缩（3~5GB/s 解压吞吐，节省 60%~70% 磁盘 I/O），
    /// 提供纯非托管二进制零拷贝读写、增量行追加（Append）与原子元数据落盘能力。
    /// </summary>
    public class TimeseriesCache : ITimeseriesCache
    {
        private const string MetaFileName = "meta.json";

        /// <summary>
        /// 表级标的代码索引文件名 (symbols.idx)。
        /// </summary>
        public const string SymbolsIndexFileName = "symbols.idx";

        /// <summary>
        /// 表级交易时间戳索引文件名 (time.idx)。
        /// </summary>
        public const string TimeIndexFileName = "time.idx";

        private readonly string _storeRoot;
        private readonly string _tableId;
        private readonly int _pageSize;
        private readonly string _compression;
        private readonly object _tableGate = new();
        private readonly ConcurrentDictionary<string, object> _fieldGates = new(StringComparer.OrdinalIgnoreCase);

        private object GetFieldGate(string fieldName) => _fieldGates.GetOrAdd(fieldName, _ => new object());

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
        /// 初始化时序持久化存储管理器。
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
        }

        /// <summary>
        /// 获取指定字段的物理文件存储目录路径：{storeRoot}/{tableId}/{fieldName}
        /// </summary>
        public string GetFieldStoreDir(string fieldName)
        {
            return Path.Combine(_storeRoot, _tableId, fieldName);
        }

        /// <inheritdoc />
        public string GetTableDir()
        {
            return Path.Combine(_storeRoot, _tableId);
        }

        /// <inheritdoc />
        public string GetSymbolsIndexPath()
        {
            return Path.Combine(GetTableDir(), SymbolsIndexFileName);
        }

        /// <inheritdoc />
        public string GetTimeIndexPath()
        {
            return Path.Combine(GetTableDir(), TimeIndexFileName);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ReadSymbols()
        {
            string path = GetSymbolsIndexPath();
            lock (_tableGate)
            {
                if (!File.Exists(path)) return Array.Empty<string>();
                return File.ReadAllLines(path, Encoding.UTF8);
            }
        }

        /// <inheritdoc />
        public void WriteSymbols(IReadOnlyList<string> symbols)
        {
            ArgumentNullException.ThrowIfNull(symbols);
            for (int i = 0; i < symbols.Count; i++)
            {
                var s = symbols[i];
                if (string.IsNullOrWhiteSpace(s))
                {
                    throw new ArgumentException("Symbol cannot be null, empty, or whitespace.", nameof(symbols));
                }
                if (s.Contains('\r') || s.Contains('\n'))
                {
                    throw new ArgumentException($"Symbol '{s}' contains invalid newline characters.", nameof(symbols));
                }
            }

            string tableDir = GetTableDir();
            Directory.CreateDirectory(tableDir);
            string path = GetSymbolsIndexPath();
            string tmpPath = path + ".tmp";

            lock (_tableGate)
            {
                if (File.Exists(path))
                {
                    var existing = File.ReadAllLines(path, Encoding.UTF8);
                    if (existing.Length == symbols.Count)
                    {
                        bool match = true;
                        for (int i = 0; i < existing.Length; i++)
                        {
                            if (!string.Equals(existing[i], symbols[i], StringComparison.Ordinal))
                            {
                                match = false;
                                break;
                            }
                        }
                        if (match) return; // 幂等：完全一致，免写
                        throw new InvalidOperationException($"Symbols mismatch in table '{_tableId}'. Existing count is {existing.Length}, but contents differ.");
                    }
                    else if (existing.Length > symbols.Count)
                    {
                        // 现有代码集更宽（其他字段已扩展或为全市场宽表），检查当前写入是否为其前缀
                        for (int i = 0; i < symbols.Count; i++)
                        {
                            if (!string.Equals(existing[i], symbols[i], StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException($"Symbols conflict in table '{_tableId}'. Subset symbols differ from existing prefix at index {i}.");
                            }
                        }
                        // 当前为现有前缀子集，保留现有更宽的表级索引，杜绝破坏性截断
                        return;
                    }
                    else // symbols.Count > existing.Length
                    {
                        // 当前代码集扩充，校验现有是否为新集合的前缀
                        for (int i = 0; i < existing.Length; i++)
                        {
                            if (!string.Equals(existing[i], symbols[i], StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException($"Symbols conflict in table '{_tableId}'. Expansion symbols differ from existing prefix at index {i}.");
                            }
                        }
                        // 合法单调递增扩充，继续写入更宽的代码集
                    }
                }

                File.WriteAllLines(tmpPath, symbols, new UTF8Encoding(false));
                File.Move(tmpPath, path, overwrite: true);
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<MarketTime> ReadTradeDates()
        {
            string path = GetTimeIndexPath();
            lock (_tableGate)
            {
                if (!File.Exists(path)) return Array.Empty<MarketTime>();

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long length = fs.Length;
                if (length % sizeof(long) != 0)
                {
                    throw new InvalidDataException($"Corrupted time index file '{path}'. File size ({length} bytes) is not a multiple of {sizeof(long)}.");
                }

                int count = (int)(length / sizeof(long));
                if (count == 0) return Array.Empty<MarketTime>();

                var result = new MarketTime[count];
                Span<byte> byteSpan = MemoryMarshal.AsBytes(result.AsSpan());
                fs.ReadExactly(byteSpan);
                return result;
            }
        }

        /// <inheritdoc />
        public void WriteTradeDates(IReadOnlyList<MarketTime> tradeDates)
        {
            ArgumentNullException.ThrowIfNull(tradeDates);
            string tableDir = GetTableDir();
            Directory.CreateDirectory(tableDir);
            string path = GetTimeIndexPath();
            string tmpPath = path + ".tmp";

            lock (_tableGate)
            {
                if (File.Exists(path))
                {
                    long existingBytes = new FileInfo(path).Length;
                    if (existingBytes % sizeof(long) != 0)
                    {
                        throw new InvalidDataException($"Corrupted time index file '{path}'. File size ({existingBytes} bytes) is not a multiple of {sizeof(long)}.");
                    }

                    int existingCount = (int)(existingBytes / sizeof(long));
                    if (existingCount >= tradeDates.Count)
                    {
                        // 现有时间轴更长或相等，校验前缀一致性
                        using var fsExisting = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var buf = new MarketTime[tradeDates.Count];
                        fsExisting.ReadExactly(MemoryMarshal.AsBytes(buf.AsSpan()));
                        for (int i = 0; i < tradeDates.Count; i++)
                        {
                            if (buf[i].Ticks != tradeDates[i].Ticks)
                            {
                                throw new InvalidOperationException($"Time index conflict in table '{_tableId}' at row {i}. Existing ticks {buf[i].Ticks} != incoming {tradeDates[i].Ticks}.");
                            }
                        }
                        // 前缀完全一致，且现有时间轴已包含当前区间，保留现有更长/同长的时间轴，杜绝截断
                        return;
                    }
                    else if (existingCount > 0)
                    {
                        // 待写入时间轴更长，校验现有前缀一致性
                        using var fsExisting = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var buf = new MarketTime[existingCount];
                        fsExisting.ReadExactly(MemoryMarshal.AsBytes(buf.AsSpan()));
                        for (int i = 0; i < existingCount; i++)
                        {
                            if (buf[i].Ticks != tradeDates[i].Ticks)
                            {
                                throw new InvalidOperationException($"Time index conflict in table '{_tableId}' at row {i}. Existing ticks {buf[i].Ticks} != incoming {tradeDates[i].Ticks}.");
                            }
                        }
                    }
                }

                using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                {
                    if (tradeDates is MarketTime[] array)
                    {
                        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(array.AsSpan());
                        fs.Write(bytes);
                    }
                    else if (tradeDates is List<MarketTime> list)
                    {
                        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(list));
                        fs.Write(bytes);
                    }
                    else
                    {
                        int byteCount = tradeDates.Count * sizeof(long);
                        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
                        try
                        {
                            Span<MarketTime> span = MemoryMarshal.Cast<byte, MarketTime>(rented.AsSpan(0, byteCount));
                            for (int i = 0; i < tradeDates.Count; i++)
                            {
                                span[i] = tradeDates[i];
                            }
                            fs.Write(rented.AsSpan(0, byteCount));
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(rented);
                        }
                    }
                }

                File.Move(tmpPath, path, overwrite: true);
            }
        }

        /// <inheritdoc />
        public void AppendTradeDates(IReadOnlyList<MarketTime> newTradeDates, int targetRowCount = -1)
        {
            if (newTradeDates == null || newTradeDates.Count == 0) return;

            string tableDir = GetTableDir();
            Directory.CreateDirectory(tableDir);
            string path = GetTimeIndexPath();

            lock (_tableGate)
            {
                long existingBytes = 0;
                if (File.Exists(path))
                {
                    existingBytes = new FileInfo(path).Length;
                    if (existingBytes % sizeof(long) != 0)
                    {
                        throw new InvalidDataException($"Corrupted time index file '{path}'. File size ({existingBytes} bytes) is not a multiple of {sizeof(long)}.");
                    }
                }

                int existingCount = (int)(existingBytes / sizeof(long));

                if (targetRowCount > 0)
                {
                    if (existingCount >= targetRowCount)
                    {
                        return;
                    }

                    int baseCount = targetRowCount - newTradeDates.Count;
                    if (existingCount < baseCount)
                    {
                        throw new InvalidOperationException(
                            $"Time index '{path}' is out of sync. Existing count ({existingCount}) is less than expected base count ({baseCount}) for target row count ({targetRowCount}).");
                    }
                }

                int skip = 0;
                if (targetRowCount > 0)
                {
                    int baseCount = targetRowCount - newTradeDates.Count;
                    if (existingCount > baseCount)
                    {
                        skip = existingCount - baseCount;
                    }
                }

                if (skip >= newTradeDates.Count)
                {
                    return;
                }

                int toWriteCount = newTradeDates.Count - skip;
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 65536);

                if (newTradeDates is MarketTime[] array)
                {
                    ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(array.AsSpan(skip, toWriteCount));
                    fs.Write(bytes);
                }
                else if (newTradeDates is List<MarketTime> list)
                {
                    ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(list).Slice(skip, toWriteCount));
                    fs.Write(bytes);
                }
                else
                {
                    int byteCount = toWriteCount * sizeof(long);
                    byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
                    try
                    {
                        Span<MarketTime> span = MemoryMarshal.Cast<byte, MarketTime>(rented.AsSpan(0, byteCount));
                        for (int i = 0; i < toWriteCount; i++)
                        {
                            span[i] = newTradeDates[skip + i];
                        }
                        fs.Write(rented.AsSpan(0, byteCount));
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                }
            }
        }

        /// <inheritdoc />
        public bool HasField(string fieldName)
        {
            string dir = GetFieldStoreDir(fieldName);
            string metaPath = Path.Combine(dir, MetaFileName);
            return File.Exists(metaPath);
        }

        /// <inheritdoc />
        public bool IsCompatible(
            string fieldName, 
            IReadOnlyList<string> expectedSymbols, 
            Type expectedType, 
            out TimeseriesMetadata? metadata)
        {
            metadata = ReadMetadata(fieldName);
            if (metadata == null) return false;

            // 1. 类型校验
            if (!string.Equals(metadata.DataType, expectedType.Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 2. 分页大小校验
            if (metadata.PageSize != _pageSize)
            {
                return false;
            }

            // 3. 股票代码集合单调递增前缀兼容性校验（Scheme B 核心契约）
            var existingSymbols = ReadSymbols();
            // 防御 symbols.idx 缺失或为空导致假阳性判定
            if (existingSymbols.Count == 0 && metadata.Width > 0)
            {
                return false; // 索引文件缺失或为空
            }

            if (existingSymbols.Count < metadata.Width)
            {
                return false; // 索引残损：代码数小于矩阵声明宽度
            }

            // 只要 existingSymbols 是 expectedSymbols 的前缀（即已有股票的唯一 index 守恒），
            // 历史分块完全可以复用，在加载时自动进行内存列拓宽（Auto-expand in Memory）！
            if (existingSymbols.Count > expectedSymbols.Count)
            {
                return false; // 期望代码集缩减，不属于单调递增扩充
            }

            for (int i = 0; i < existingSymbols.Count; i++)
            {
                if (!string.Equals(existingSymbols[i], expectedSymbols[i], StringComparison.Ordinal))
                {
                    return false; // 代码顺序被篡改或不匹配
                }
            }

            return true;
        }

        /// <inheritdoc />
        public void RewriteChunk<T>(
            string fieldName, 
            int pageIndex, 
            Action<int, Span<T>> rowProvider,
            int? newChunkWidth = null,
            IReadOnlyList<string>? updatedSymbols = null) where T : unmanaged
        {
            ArgumentNullException.ThrowIfNull(rowProvider);

            lock (GetFieldGate(fieldName))
            {
                var meta = ReadMetadata(fieldName);
                if (meta == null)
                {
                    throw new InvalidOperationException($"Store for field '{fieldName}' does not exist.");
                }

                int totalRows = meta.RowCount;
                int neededPages = totalRows > 0 ? (int)(((long)totalRows + _pageSize - 1) / _pageSize) : 0;

                if (pageIndex < 0 || pageIndex >= neededPages)
                {
                    throw new ArgumentOutOfRangeException(nameof(pageIndex), $"PageIndex {pageIndex} is out of range (0..{neededPages - 1}).");
                }

                int startRow = pageIndex * _pageSize;
                int rowsInChunk = Math.Min(_pageSize, totalRows - startRow);
                int width = newChunkWidth ?? meta.GetChunkWidth(pageIndex);
                bool useLz4 = string.Equals(meta.Compression, "lz4", StringComparison.OrdinalIgnoreCase);
                string dir = GetFieldStoreDir(fieldName);
                string chunkPath = Path.Combine(dir, $"chunk_{pageIndex}.bin");

                int bufferLen = rowsInChunk * width;
                T[] rentedBuffer = ArrayPool<T>.Shared.Rent(bufferLen);
                try
                {
                    Span<T> chunkSpan = rentedBuffer.AsSpan(0, bufferLen);
                    for (int r = 0; r < rowsInChunk; r++)
                    {
                        int globalRow = startRow + r;
                        rowProvider(globalRow, chunkSpan.Slice(r * width, width));
                    }

                    ReadOnlySpan<byte> rawBytes = MemoryMarshal.AsBytes(chunkSpan);
                    WriteChunkFile(chunkPath, rawBytes, useLz4);
                }
                finally
                {
                    ArrayPool<T>.Shared.Return(rentedBuffer);
                }

                // 若分块宽度发生调整或代码扩充，同步更新自描述元数据
                bool metaChanged = false;
                if (meta.ChunkWidths == null)
                {
                    meta.ChunkWidths = new List<int>(neededPages);
                    for (int i = 0; i < neededPages; i++) meta.ChunkWidths.Add(meta.Width);
                    metaChanged = true;
                }

                while (meta.ChunkWidths.Count < neededPages)
                {
                    meta.ChunkWidths.Add(meta.Width);
                    metaChanged = true;
                }

                if (newChunkWidth.HasValue && meta.ChunkWidths[pageIndex] != newChunkWidth.Value)
                {
                    meta.ChunkWidths[pageIndex] = newChunkWidth.Value;
                    if (newChunkWidth.Value > meta.Width)
                    {
                        meta.Width = newChunkWidth.Value;
                    }
                    metaChanged = true;
                }

                if (updatedSymbols != null)
                {
                    var existingSymbols = ReadSymbols();
                    if (updatedSymbols.Count > existingSymbols.Count)
                    {
                        WriteSymbols(updatedSymbols);
                    }
                }

                if (metaChanged)
                {
                    SaveMetadata(fieldName, meta);
                }
            }
        }

        /// <inheritdoc />
        public TimeseriesMetadata? ReadMetadata(string fieldName)
        {
            string dir = GetFieldStoreDir(fieldName);
            string metaPath = Path.Combine(dir, MetaFileName);
            if (!File.Exists(metaPath)) return null;

            string json = File.ReadAllText(metaPath);
            return JsonSerializer.Deserialize<TimeseriesMetadata>(json);
        }

        /// <inheritdoc />
        public unsafe void WriteMatrix<T>(
            string fieldName,
            ChunkMatrix2D<T> matrix,
            IReadOnlyList<string> symbols,
            IReadOnlyList<MarketTime> tradeDates) where T : unmanaged
        {
            ArgumentNullException.ThrowIfNull(matrix);
            ArgumentNullException.ThrowIfNull(symbols);
            ArgumentNullException.ThrowIfNull(tradeDates);

            if (matrix.Width != symbols.Count)
            {
                throw new ArgumentException($"Matrix width ({matrix.Width}) does not match symbols count ({symbols.Count}).");
            }
            if (matrix.RowCount != tradeDates.Count)
            {
                throw new ArgumentException($"Matrix row count ({matrix.RowCount}) does not match tradeDates count ({tradeDates.Count}).");
            }

            lock (GetFieldGate(fieldName))
            {
                string dir = GetFieldStoreDir(fieldName);
                Directory.CreateDirectory(dir);

                int totalRows = matrix.RowCount;
                int width = matrix.Width;
                int neededPages = totalRows > 0 ? (int)(((long)totalRows + _pageSize - 1) / _pageSize) : 0;
                bool useLz4 = string.Equals(_compression, "lz4", StringComparison.OrdinalIgnoreCase);

                // 1. 逐块以块级纯连续内存写入二进制数据文件 chunk_{i}.bin
                for (int pageIdx = 0; pageIdx < neededPages; pageIdx++)
                {
                    int startRow = pageIdx * _pageSize;
                    int rowsInChunk = Math.Min(_pageSize, totalRows - startRow);
                    string chunkPath = Path.Combine(dir, $"chunk_{pageIdx}.bin");

                    var pageSpan = matrix.GetReadOnlyPageSpan(pageIdx, rowsInChunk);
                    var rawBytes = MemoryMarshal.AsBytes(pageSpan);

                    WriteChunkFile(chunkPath, rawBytes, useLz4);
                }

                // 清理多余旧孤儿分块文件（当重写为较少分块时）
                int orphanPageIdx = neededPages;
                while (true)
                {
                    string orphanChunkPath = Path.Combine(dir, $"chunk_{orphanPageIdx}.bin");
                    if (!File.Exists(orphanChunkPath)) break;
                    File.Delete(orphanChunkPath);
                    orphanPageIdx++;
                }

                // 2. 写入表级统一双标尺索引: symbols.idx 与 time.idx
                WriteSymbols(symbols);
                WriteTradeDates(tradeDates);

                // 3. 构建并原子写入自描述元数据契约 (meta.json)
                var chunkWidths = new List<int>(neededPages);
                for (int i = 0; i < neededPages; i++) chunkWidths.Add(width);

                var meta = new TimeseriesMetadata
                {
                    TableId = _tableId,
                    FieldName = fieldName,
                    DataType = typeof(T).Name,
                    Compression = _compression,
                    PageSize = _pageSize,
                    Width = width,
                    RowCount = totalRows,
                    StartDate = tradeDates.Count > 0 ? tradeDates[0] : null,
                    EndDate = tradeDates.Count > 0 ? tradeDates[^1] : null,
                    ChunkWidths = chunkWidths
                };

                SaveMetadata(fieldName, meta);
            }
        }

        /// <inheritdoc />
        public unsafe ChunkMatrix2D<T> LoadMatrix<T>(string fieldName, int? targetWidth = null) where T : unmanaged
        {
            var meta = ReadMetadata(fieldName);
            if (meta == null)
            {
                throw new FileNotFoundException($"Store metadata not found for field '{fieldName}' in table '{_tableId}'.");
            }

            if (!string.Equals(meta.DataType, typeof(T).Name, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateTypeMismatchException(fieldName, meta.DataType, typeof(T).Name);
            }

            string dir = GetFieldStoreDir(fieldName);
            int totalRows = meta.RowCount;
            int width = Math.Max(meta.Width, targetWidth ?? meta.Width);

            var matrix = new ChunkMatrix2D<T>(width, _pageSize, totalRows);
            int neededPages = totalRows > 0 ? (int)(((long)totalRows + _pageSize - 1) / _pageSize) : 0;
            bool useLz4 = string.Equals(meta.Compression, "lz4", StringComparison.OrdinalIgnoreCase);

            T emptyVal = MarketDataAligner.GetNaNValue<T>();

            for (int pageIdx = 0; pageIdx < neededPages; pageIdx++)
            {
                int startRow = pageIdx * _pageSize;
                int rowsInChunk = Math.Min(_pageSize, totalRows - startRow);
                int chunkWidth = meta.GetChunkWidth(pageIdx);
                string chunkPath = Path.Combine(dir, $"chunk_{pageIdx}.bin");

                try
                {
                    if (chunkWidth == width)
                    {
                        // 快速路径：单次连续内存块复制
                        var targetSpan = matrix.GetPageSpan(pageIdx, rowsInChunk);
                        var targetBytes = MemoryMarshal.AsBytes(targetSpan);
                        ReadChunkFile(chunkPath, targetBytes, useLz4);
                    }
                    else
                    {
                        // 内存自动拓宽路径（历史窄分块在载入时自动扩展至目标宽度，零历史重写与零磁盘浪费）
                        T[] tempChunk = ArrayPool<T>.Shared.Rent(rowsInChunk * chunkWidth);
                        try
                        {
                            Span<byte> rawBytes = MemoryMarshal.AsBytes(tempChunk.AsSpan(0, rowsInChunk * chunkWidth));
                            ReadChunkFile(chunkPath, rawBytes, useLz4);

                            var pageSpan = matrix.GetPageSpan(pageIdx, rowsInChunk);
                            for (int r = 0; r < rowsInChunk; r++)
                            {
                                var srcRow = tempChunk.AsSpan(r * chunkWidth, chunkWidth);
                                var dstRow = pageSpan.Slice(r * width, width);

                                srcRow.CopyTo(dstRow.Slice(0, chunkWidth));
                                dstRow.Slice(chunkWidth).Fill(emptyVal);
                            }
                        }
                        finally
                        {
                            ArrayPool<T>.Shared.Return(tempChunk);
                        }
                    }
                }
                catch
                {
                    matrix.Dispose();
                    throw;
                }
            }

            return matrix;
        }

        /// <inheritdoc />
        public unsafe void AppendRows<T>(
            string fieldName,
            int deltaRows,
            Action<int, Span<T>> rowProvider,
            IReadOnlyList<MarketTime> newTradeDates) where T : unmanaged
        {
            if (deltaRows <= 0) return;
            ArgumentNullException.ThrowIfNull(rowProvider);
            ArgumentNullException.ThrowIfNull(newTradeDates);

            lock (GetFieldGate(fieldName))
            {
                var meta = ReadMetadata(fieldName);
                if (meta == null)
                {
                    throw new InvalidOperationException($"Cannot append to non-existent store for field '{fieldName}'.");
                }
                if (newTradeDates.Count != deltaRows)
                {
                    throw new ArgumentException($"newTradeDates count ({newTradeDates.Count}) must equal deltaRows ({deltaRows}).");
                }

                string dir = GetFieldStoreDir(fieldName);
                int oldRowCount = meta.RowCount;
                int width = meta.Width;
                int targetRowCount = oldRowCount + deltaRows;
                bool useLz4 = string.Equals(meta.Compression, "lz4", StringComparison.OrdinalIgnoreCase);

                int currRow = oldRowCount;
                int remaining = deltaRows;

                while (remaining > 0)
                {
                    int pageIdx = currRow / _pageSize;
                    int rowInPage = currRow % _pageSize;
                    int canWrite = Math.Min(_pageSize - rowInPage, remaining);
                    string chunkPath = Path.Combine(dir, $"chunk_{pageIdx}.bin");

                    int totalRowsInChunk = rowInPage + canWrite;
                    int bufferLen = totalRowsInChunk * width;
                    T[] rentedBuffer = ArrayPool<T>.Shared.Rent(bufferLen);
                    try
                    {
                        Span<T> chunkSpan = rentedBuffer.AsSpan(0, bufferLen);

                        // 1. 若当前 Chunk 原先已存在旧行，前置旧行必须严格存在并载入
                        if (rowInPage > 0)
                        {
                            if (!File.Exists(chunkPath))
                            {
                                throw new FileNotFoundException($"Cannot append rows to chunk_{pageIdx}.bin at offset {rowInPage}: chunk file is missing.", chunkPath);
                            }
                            Span<byte> oldBytes = MemoryMarshal.AsBytes(chunkSpan.Slice(0, rowInPage * width));
                            ReadChunkFile(chunkPath, oldBytes, useLz4);
                        }

                        // 2. 灌入追加的新行
                        for (int i = 0; i < canWrite; i++)
                        {
                            rowProvider(currRow + i, chunkSpan.Slice((rowInPage + i) * width, width));
                        }

                        // 3. 重新编码落盘
                        ReadOnlySpan<byte> fullChunkBytes = MemoryMarshal.AsBytes(chunkSpan);
                        WriteChunkFile(chunkPath, fullChunkBytes, useLz4);
                    }
                    finally
                    {
                        ArrayPool<T>.Shared.Return(rentedBuffer);
                    }

                    currRow += canWrite;
                    remaining -= canWrite;
                }

                // 更新元数据并落盘
                meta.RowCount = targetRowCount;
                meta.EndDate = newTradeDates[^1];

                // 增量追加至表级 time.idx 索引文件
                AppendTradeDates(newTradeDates, targetRowCount);

                int targetNeededPages = targetRowCount > 0 ? (int)(((long)targetRowCount + _pageSize - 1) / _pageSize) : 0;
                if (meta.ChunkWidths != null)
                {
                    while (meta.ChunkWidths.Count < targetNeededPages)
                    {
                        meta.ChunkWidths.Add(width);
                    }
                }

                SaveMetadata(fieldName, meta);
            }
        }

        /// <inheritdoc />
        public bool TryReadChunk<T>(string fieldName, int pageIndex, Span<T> destination, out int rowsRead) where T : unmanaged
        {
            rowsRead = 0;
            var meta = ReadMetadata(fieldName);
            if (meta == null) return false;

            int totalRows = meta.RowCount;
            int startRow = pageIndex * _pageSize;
            if (startRow >= totalRows) return false;

            int rowsInChunk = Math.Min(_pageSize, totalRows - startRow);
            int chunkWidth = meta.GetChunkWidth(pageIndex);
            string dir = GetFieldStoreDir(fieldName);
            string chunkPath = Path.Combine(dir, $"chunk_{pageIndex}.bin");

            if (!File.Exists(chunkPath)) return false;

            bool useLz4 = string.Equals(meta.Compression, "lz4", StringComparison.OrdinalIgnoreCase);

            try
            {
                if (chunkWidth * rowsInChunk > destination.Length)
                {
                    return false;
                }

                Span<byte> targetBytes = MemoryMarshal.AsBytes(destination.Slice(0, rowsInChunk * chunkWidth));
                ReadChunkFile(chunkPath, targetBytes, useLz4);
                rowsRead = rowsInChunk;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        public void Clear(string fieldName)
        {
            lock (GetFieldGate(fieldName))
            {
                string dir = GetFieldStoreDir(fieldName);
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        /// <inheritdoc />
        public void ClearTable()
        {
            lock (_tableGate)
            {
                string tableDir = Path.Combine(_storeRoot, _tableId);
                if (Directory.Exists(tableDir))
                {
                    Directory.Delete(tableDir, true);
                }
            }
        }

        private static void WriteChunkFile(string chunkPath, ReadOnlySpan<byte> rawBytes, bool useLz4)
        {
            string tmpPath = chunkPath + ".tmp";
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
            {
                if (useLz4)
                {
                    int maxOutput = LZ4Codec.MaximumOutputSize(rawBytes.Length);
                    byte[] rented = ArrayPool<byte>.Shared.Rent(maxOutput + 4);
                    try
                    {
                        int encodedLen = LZ4Codec.Encode(rawBytes, rented.AsSpan(4), LZ4Level.L00_FAST);
                        BinaryPrimitives.WriteInt32LittleEndian(rented.AsSpan(0, 4), rawBytes.Length);
                        fs.Write(rented.AsSpan(0, encodedLen + 4));
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                }
                else
                {
                    fs.Write(rawBytes);
                }
            }

            File.Move(tmpPath, chunkPath, overwrite: true);
        }

        private static void ReadChunkFile(string chunkPath, Span<byte> targetBytes, bool useLz4)
        {
            if (!File.Exists(chunkPath))
            {
                throw new FileNotFoundException($"Chunk file not found: {chunkPath}");
            }

            if (useLz4)
            {
                using var fs = new FileStream(chunkPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
                long fileLen = fs.Length;
                if (fileLen < 4)
                {
                    throw new InvalidDataException($"Corrupt compressed chunk file: {chunkPath}");
                }

                byte[] rented = ArrayPool<byte>.Shared.Rent((int)fileLen);
                try
                {
                    fs.ReadExactly(rented.AsSpan(0, (int)fileLen));
                    int uncompressedLen = BinaryPrimitives.ReadInt32LittleEndian(rented.AsSpan(0, 4));
                    if (uncompressedLen != targetBytes.Length)
                    {
                        throw new InvalidDataException($"Decompressed length mismatch in '{chunkPath}'. Expected {targetBytes.Length}, header specifies {uncompressedLen}.");
                    }

                    int decoded = LZ4Codec.Decode(rented.AsSpan(4, (int)fileLen - 4), targetBytes);
                    if (decoded != targetBytes.Length)
                    {
                        throw new InvalidDataException($"LZ4 decoding failed for '{chunkPath}'. Decoded {decoded} bytes, expected {targetBytes.Length}.");
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
            else
            {
                var fi = new FileInfo(chunkPath);
                if (fi.Length != targetBytes.Length)
                {
                    throw new InvalidDataException($"Chunk file '{chunkPath}' size mismatch. Expected {targetBytes.Length} bytes, got {fi.Length} bytes.");
                }

                using var fs = new FileStream(chunkPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
                fs.ReadExactly(targetBytes);
            }
        }

        private void SaveMetadata(string fieldName, TimeseriesMetadata meta)
        {
            string dir = GetFieldStoreDir(fieldName);
            string metaPath = Path.Combine(dir, MetaFileName);
            string tmpMetaPath = metaPath + ".tmp";

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(meta, options);
            File.WriteAllText(tmpMetaPath, json);

            File.Move(tmpMetaPath, metaPath, overwrite: true);
        }

        private static InvalidOperationException CreateTypeMismatchException(string field, string expected, string actual)
        {
            return new InvalidOperationException($"Store type mismatch for field '{field}'. Stored type is '{expected}', requested '{actual}'.");
        }
    }
}
