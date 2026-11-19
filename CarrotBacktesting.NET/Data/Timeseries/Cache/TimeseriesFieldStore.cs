using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using CarrotBacktesting.NET.Config;
using K4os.Compression.LZ4;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 单字段时序物理分块与元数据存储管理组件。
    /// 专职负责单个字段的自描述元数据 (meta.json)、按 PageSize 切分的二进制分块 (chunk_{i}.bin)
    /// 的读写、LZ4 压缩解压、内存列拓宽与实例级并发隔离。
    /// </summary>
    public class TimeseriesFieldStore
    {
        private const string MetaFileName = "meta.json";

        private readonly string _storeRoot;
        private readonly string _tableId;
        private readonly string _fieldName;
        private readonly int _pageSize;
        private readonly string _compression;
        private readonly TimeseriesIndexStore _indexStore;
        private readonly object _gate = new();

        /// <summary>
        /// 持久化物理根目录路径。
        /// </summary>
        public string StoreRoot => _storeRoot;

        /// <summary>
        /// 时序表唯一标识符。
        /// </summary>
        public string TableId => _tableId;

        /// <summary>
        /// 字段唯一标识名称。
        /// </summary>
        public string FieldName => _fieldName;

        /// <summary>
        /// 分页行数大小。
        /// </summary>
        public int PageSize => _pageSize;

        /// <summary>
        /// 压缩算法名称（如 "lz4", "none"）。
        /// </summary>
        public string Compression => _compression;

        /// <summary>
        /// 所属表的表级双标尺索引管理器。
        /// </summary>
        public TimeseriesIndexStore IndexStore => _indexStore;

        /// <summary>
        /// 字段物理存储目录路径：{storeRoot}/{tableId}/{fieldName}
        /// </summary>
        public string FieldDir => Path.Combine(_storeRoot, _tableId, _fieldName);

        /// <summary>
        /// 初始化单字段时序物理分块与元数据存储管理器。
        /// </summary>
        /// <param name="storeRoot">物理存储根目录</param>
        /// <param name="tableId">时序表唯一标识符</param>
        /// <param name="fieldName">字段名称</param>
        /// <param name="pageSize">分页行数大小</param>
        /// <param name="compression">压缩算法</param>
        /// <param name="indexStore">表级双标尺索引管理器</param>
        public TimeseriesFieldStore(
            string storeRoot,
            string tableId,
            string fieldName,
            int pageSize,
            string compression,
            TimeseriesIndexStore indexStore)
        {
            if (string.IsNullOrWhiteSpace(storeRoot))
            {
                throw new ArgumentException("Store root directory cannot be null or empty.", nameof(storeRoot));
            }
            if (string.IsNullOrWhiteSpace(tableId))
            {
                throw new ArgumentException("Table ID cannot be null or empty.", nameof(tableId));
            }
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                throw new ArgumentException("Field name cannot be null or empty.", nameof(fieldName));
            }
            if (pageSize <= 0 || (pageSize & (pageSize - 1)) != 0)
            {
                throw new ArgumentException("PageSize 必须为大于 0 且为 2 的整数次幂。", nameof(pageSize));
            }
            ArgumentNullException.ThrowIfNull(indexStore);

            _storeRoot = Path.GetFullPath(storeRoot);
            _tableId = tableId;
            _fieldName = fieldName;
            _pageSize = pageSize;
            _compression = string.IsNullOrWhiteSpace(compression) ? "lz4" : compression.ToLowerInvariant();
            _indexStore = indexStore;
        }

        /// <summary>
        /// 获取分块物理文件路径：{FieldDir}/chunk_{pageIndex}.bin
        /// </summary>
        public string GetChunkPath(int pageIndex)
        {
            return Path.Combine(FieldDir, $"chunk_{pageIndex}.bin");
        }

        /// <summary>
        /// 获取元数据物理文件路径：{FieldDir}/meta.json
        /// </summary>
        public string GetMetaPath()
        {
            return Path.Combine(FieldDir, MetaFileName);
        }

        /// <summary>
        /// 检查当前字段的持久化元数据是否存在。
        /// </summary>
        public bool HasField()
        {
            return File.Exists(GetMetaPath());
        }

        /// <summary>
        /// 读取当前字段的自描述元数据 (meta.json)。
        /// </summary>
        public TimeseriesMetadata? ReadMetadata()
        {
            string metaPath = GetMetaPath();
            if (!File.Exists(metaPath)) return null;

            string json = File.ReadAllText(metaPath);
            return JsonSerializer.Deserialize<TimeseriesMetadata>(json);
        }

        /// <summary>
        /// 原子保存当前字段的自描述元数据 (meta.json)。
        /// </summary>
        public void SaveMetadata(TimeseriesMetadata meta)
        {
            ArgumentNullException.ThrowIfNull(meta);
            Directory.CreateDirectory(FieldDir);
            string metaPath = GetMetaPath();
            string tmpMetaPath = metaPath + ".tmp";

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(meta, options);
            File.WriteAllText(tmpMetaPath, json);

            File.Move(tmpMetaPath, metaPath, overwrite: true);
        }

        /// <summary>
        /// 校验磁盘持久化元数据与当前内存加载期望是否兼容（类型、PageSize 与单调递增前缀兼容性）。
        /// </summary>
        public bool IsCompatible(
            IReadOnlyList<string> expectedSymbols,
            Type expectedType,
            out TimeseriesMetadata? metadata)
        {
            metadata = ReadMetadata();
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
            var existingSymbols = _indexStore.ReadSymbols();
            if (existingSymbols.Count == 0 && metadata.Width > 0)
            {
                return false; // 索引文件缺失或为空
            }

            if (existingSymbols.Count < metadata.Width)
            {
                return false; // 索引残损：代码数小于矩阵声明宽度
            }

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

        /// <summary>
        /// 重写指定页号的历史分块数据，支持原地更新或扩充分块列宽。
        /// </summary>
        public void RewriteChunk<T>(
            int pageIndex,
            Action<int, Span<T>> rowProvider,
            int? newChunkWidth = null,
            IReadOnlyList<string>? updatedSymbols = null) where T : unmanaged
        {
            ArgumentNullException.ThrowIfNull(rowProvider);

            lock (_gate)
            {
                var meta = ReadMetadata();
                if (meta == null)
                {
                    throw new InvalidOperationException($"Store for field '{_fieldName}' does not exist.");
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
                string chunkPath = GetChunkPath(pageIndex);

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
                    var existingSymbols = _indexStore.ReadSymbols();
                    if (updatedSymbols.Count > existingSymbols.Count)
                    {
                        _indexStore.WriteSymbols(updatedSymbols);
                    }
                }

                if (metaChanged)
                {
                    SaveMetadata(meta);
                }
            }
        }

        /// <summary>
        /// 全量写入二维矩阵分块，并同步更新表级双标尺索引与自描述元数据。
        /// </summary>
        public unsafe void WriteMatrix<T>(
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

            lock (_gate)
            {
                Directory.CreateDirectory(FieldDir);

                int totalRows = matrix.RowCount;
                int width = matrix.Width;
                int neededPages = totalRows > 0 ? (int)(((long)totalRows + _pageSize - 1) / _pageSize) : 0;
                bool useLz4 = string.Equals(_compression, "lz4", StringComparison.OrdinalIgnoreCase);

                // 1. 逐块以块级纯连续内存写入二进制数据文件 chunk_{i}.bin
                for (int pageIdx = 0; pageIdx < neededPages; pageIdx++)
                {
                    int startRow = pageIdx * _pageSize;
                    int rowsInChunk = Math.Min(_pageSize, totalRows - startRow);
                    string chunkPath = GetChunkPath(pageIdx);

                    var pageSpan = matrix.GetReadOnlyPageSpan(pageIdx, rowsInChunk);
                    var rawBytes = MemoryMarshal.AsBytes(pageSpan);

                    WriteChunkFile(chunkPath, rawBytes, useLz4);
                }

                // 清理多余旧孤儿分块文件（当重写为较少分块时）
                int orphanPageIdx = neededPages;
                while (true)
                {
                    string orphanChunkPath = GetChunkPath(orphanPageIdx);
                    if (!File.Exists(orphanChunkPath)) break;
                    File.Delete(orphanChunkPath);
                    orphanPageIdx++;
                }

                // 2. 写入表级统一双标尺索引: symbols.idx 与 time.idx
                _indexStore.WriteSymbols(symbols);
                _indexStore.WriteTradeDates(tradeDates);

                // 3. 构建并原子写入自描述元数据契约 (meta.json)
                var chunkWidths = new List<int>(neededPages);
                for (int i = 0; i < neededPages; i++) chunkWidths.Add(width);

                var meta = new TimeseriesMetadata
                {
                    TableId = _tableId,
                    FieldName = _fieldName,
                    DataType = typeof(T).Name,
                    Compression = _compression,
                    PageSize = _pageSize,
                    Width = width,
                    RowCount = totalRows,
                    StartDate = tradeDates.Count > 0 ? tradeDates[0] : null,
                    EndDate = tradeDates.Count > 0 ? tradeDates[^1] : null,
                    ChunkWidths = chunkWidths
                };

                SaveMetadata(meta);
            }
        }

        /// <summary>
        /// 从磁盘完整加载矩阵，支持历史窄分块在内存中自动拓宽至 targetWidth。
        /// </summary>
        public unsafe ChunkMatrix2D<T> LoadMatrix<T>(int? targetWidth = null) where T : unmanaged
        {
            var meta = ReadMetadata();
            if (meta == null)
            {
                throw new FileNotFoundException($"Store metadata not found for field '{_fieldName}' in table '{_tableId}'.");
            }

            if (!string.Equals(meta.DataType, typeof(T).Name, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateTypeMismatchException(_fieldName, meta.DataType, typeof(T).Name);
            }

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
                string chunkPath = GetChunkPath(pageIdx);

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

        /// <summary>
        /// 增量追加新行数据并落盘，同步追加表级时间戳索引并刷新元数据。
        /// </summary>
        public unsafe void AppendRows<T>(
            int deltaRows,
            Action<int, Span<T>> rowProvider,
            IReadOnlyList<MarketTime> newTradeDates) where T : unmanaged
        {
            if (deltaRows <= 0) return;
            ArgumentNullException.ThrowIfNull(rowProvider);
            ArgumentNullException.ThrowIfNull(newTradeDates);

            lock (_gate)
            {
                var meta = ReadMetadata();
                if (meta == null)
                {
                    throw new InvalidOperationException($"Cannot append to non-existent store for field '{_fieldName}'.");
                }
                if (newTradeDates.Count != deltaRows)
                {
                    throw new ArgumentException($"newTradeDates count ({newTradeDates.Count}) must equal deltaRows ({deltaRows}).");
                }

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
                    string chunkPath = GetChunkPath(pageIdx);

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
                _indexStore.AppendTradeDates(newTradeDates, targetRowCount);

                int targetNeededPages = targetRowCount > 0 ? (int)(((long)targetRowCount + _pageSize - 1) / _pageSize) : 0;
                if (meta.ChunkWidths != null)
                {
                    while (meta.ChunkWidths.Count < targetNeededPages)
                    {
                        meta.ChunkWidths.Add(width);
                    }
                }

                SaveMetadata(meta);
            }
        }

        /// <summary>
        /// 尝试从磁盘加载单个分块数据至目标 Span 中。
        /// </summary>
        public bool TryReadChunk<T>(int pageIndex, Span<T> destination, out int rowsRead) where T : unmanaged
        {
            rowsRead = 0;
            var meta = ReadMetadata();
            if (meta == null) return false;

            int totalRows = meta.RowCount;
            int startRow = pageIndex * _pageSize;
            if (startRow >= totalRows) return false;

            int rowsInChunk = Math.Min(_pageSize, totalRows - startRow);
            int chunkWidth = meta.GetChunkWidth(pageIndex);
            string chunkPath = GetChunkPath(pageIndex);

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

        /// <summary>
        /// 清理当前字段的物理存储目录及其所有分块与元数据文件。
        /// </summary>
        public void Clear()
        {
            lock (_gate)
            {
                if (Directory.Exists(FieldDir))
                {
                    Directory.Delete(FieldDir, true);
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

        private static InvalidOperationException CreateTypeMismatchException(string field, string expected, string actual)
        {
            return new InvalidOperationException($"Store type mismatch for field '{field}'. Stored type is '{expected}', requested '{actual}'.");
        }
    }
}
