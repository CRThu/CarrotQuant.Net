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
    /// 时序二进制对齐持久化存储管理器。
    /// 负责按 4096 行（或自定义定长）将行情宽表切分为 chunk_{i}.bin 文件进行物理持久化，
    /// 原生支持 LZ4 超高速块压缩（3~5GB/s 解压吞吐，节省 60%~70% 磁盘 I/O），
    /// 提供纯非托管二进制零拷贝读写、增量行追加（Append）与原子元数据落盘能力。
    /// </summary>
    public class TimeseriesStore : ITimeseriesStore
    {
        private const string MetaFileName = "meta.json";
        private readonly string _storeRoot;
        private readonly string _tableId;
        private readonly int _pageSize;
        private readonly string _compression;

        /// <summary>
        /// 持久化物理根目录路径。
        /// </summary>
        public string StoreRoot => _storeRoot;

        /// <summary>
        /// 兼容属性：物理根目录路径。
        /// </summary>
        public string CacheRoot => _storeRoot;

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
        public TimeseriesStore(string storeRoot, string tableId, int pageSize = 4096, string compression = "lz4")
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

        /// <summary>
        /// 兼容方法：获取指定字段的物理文件存储目录路径。
        /// </summary>
        public string GetFieldCacheDir(string fieldName) => GetFieldStoreDir(fieldName);

        /// <inheritdoc />
        public bool HasField(string fieldName)
        {
            string dir = GetFieldStoreDir(fieldName);
            string metaPath = Path.Combine(dir, MetaFileName);
            return File.Exists(metaPath);
        }

        /// <summary>
        /// 检查指定字段的持久化数据是否存在且元数据有效。
        /// </summary>
        public bool HasCache(string fieldName) => HasField(fieldName);

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
            // 只要 metadata.Symbols 是 expectedSymbols 的前缀（即已有股票的唯一 index 守恒），
            // 历史分块完全可以复用，在加载时自动进行内存列拓宽（Auto-expand in Memory）！
            if (metadata.Symbols.Count > expectedSymbols.Count)
            {
                return false; // 期望代码集缩减，不属于单调递增扩充
            }

            for (int i = 0; i < metadata.Symbols.Count; i++)
            {
                if (!string.Equals(metadata.Symbols[i], expectedSymbols[i], StringComparison.Ordinal))
                {
                    return false; // 代码顺序被篡改或不匹配
                }
            }

            return true;
        }

        /// <summary>
        /// 兼容方法：深度校验指定字段的持久化数据是否与当前运行环境严格兼容。
        /// </summary>
        public bool IsCacheCompatible(
            string fieldName, 
            IReadOnlyList<string> expectedSymbols, 
            Type expectedType, 
            out TimeseriesMetadata? metadata) => IsCompatible(fieldName, expectedSymbols, expectedType, out metadata);

        /// <inheritdoc />
        public void RewriteChunk<T>(
            string fieldName, 
            int pageIndex, 
            Action<int, Span<T>> rowProvider,
            int? newChunkWidth = null,
            IReadOnlyList<string>? updatedSymbols = null) where T : unmanaged
        {
            ArgumentNullException.ThrowIfNull(rowProvider);

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

            T[] chunkBuffer = new T[rowsInChunk * width];
            for (int r = 0; r < rowsInChunk; r++)
            {
                int globalRow = startRow + r;
                rowProvider(globalRow, chunkBuffer.AsSpan(r * width, width));
            }

            ReadOnlySpan<byte> rawBytes = MemoryMarshal.AsBytes(chunkBuffer.AsSpan());
            WriteChunkFile(chunkPath, rawBytes, useLz4);

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

            if (updatedSymbols != null && updatedSymbols.Count > meta.Symbols.Count)
            {
                meta.Symbols = new List<string>(updatedSymbols);
                metaChanged = true;
            }

            if (metaChanged)
            {
                SaveMetadata(fieldName, meta);
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
            IReadOnlyList<DateTime> tradeDates) where T : unmanaged
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

            // 2. 构建并原子写入元数据契约
            var dateStrings = new List<string>(tradeDates.Count);
            for (int i = 0; i < tradeDates.Count; i++)
            {
                dateStrings.Add(tradeDates[i].ToString("yyyy-MM-dd"));
            }

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
                Symbols = new List<string>(symbols),
                TradeDates = dateStrings,
                ChunkWidths = chunkWidths
            };

            SaveMetadata(fieldName, meta);
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
                        // 极速快路径：单次裸内存直灌
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
            IReadOnlyList<DateTime> newTradeDates) where T : unmanaged
        {
            if (deltaRows <= 0) return;
            ArgumentNullException.ThrowIfNull(rowProvider);
            ArgumentNullException.ThrowIfNull(newTradeDates);

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
                T[] chunkBuffer = new T[totalRowsInChunk * width];

                // 1. 若当前 Chunk 原先已存在旧行，先读出旧行数据至缓冲区
                if (rowInPage > 0 && File.Exists(chunkPath))
                {
                    Span<byte> oldBytes = MemoryMarshal.AsBytes(chunkBuffer.AsSpan(0, rowInPage * width));
                    ReadChunkFile(chunkPath, oldBytes, useLz4);
                }

                // 2. 灌入追加的新行
                for (int i = 0; i < canWrite; i++)
                {
                    rowProvider(currRow + i, chunkBuffer.AsSpan((rowInPage + i) * width, width));
                }

                // 3. 重新编码落盘
                ReadOnlySpan<byte> fullChunkBytes = MemoryMarshal.AsBytes(chunkBuffer.AsSpan(0, totalRowsInChunk * width));
                WriteChunkFile(chunkPath, fullChunkBytes, useLz4);

                currRow += canWrite;
                remaining -= canWrite;
            }

            // 更新元数据并落盘
            meta.RowCount = targetRowCount;
            meta.EndDate = newTradeDates[^1];
            for (int i = 0; i < newTradeDates.Count; i++)
            {
                meta.TradeDates.Add(newTradeDates[i].ToString("yyyy-MM-dd"));
            }

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

        /// <inheritdoc />
        public void Clear(string fieldName)
        {
            string dir = GetFieldStoreDir(fieldName);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }

        /// <inheritdoc />
        public void ClearTable()
        {
            string tableDir = Path.Combine(_storeRoot, _tableId);
            if (Directory.Exists(tableDir))
            {
                Directory.Delete(tableDir, true);
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

            if (File.Exists(chunkPath)) File.Delete(chunkPath);
            File.Move(tmpPath, chunkPath);
        }

        private static void ReadChunkFile(string chunkPath, Span<byte> targetBytes, bool useLz4)
        {
            if (!File.Exists(chunkPath))
            {
                throw new FileNotFoundException($"Chunk file not found: {chunkPath}");
            }

            if (useLz4)
            {
                byte[] fileBytes = File.ReadAllBytes(chunkPath);
                if (fileBytes.Length < 4)
                {
                    throw new InvalidDataException($"Corrupt compressed chunk file: {chunkPath}");
                }

                int uncompressedLen = BinaryPrimitives.ReadInt32LittleEndian(fileBytes.AsSpan(0, 4));
                if (uncompressedLen != targetBytes.Length)
                {
                    throw new InvalidDataException($"Decompressed length mismatch in '{chunkPath}'. Expected {targetBytes.Length}, header specifies {uncompressedLen}.");
                }

                int decoded = LZ4Codec.Decode(fileBytes.AsSpan(4), targetBytes);
                if (decoded != targetBytes.Length)
                {
                    throw new InvalidDataException($"LZ4 decoding failed for '{chunkPath}'. Decoded {decoded} bytes, expected {targetBytes.Length}.");
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

            if (File.Exists(metaPath)) File.Delete(metaPath);
            File.Move(tmpMetaPath, metaPath);
        }

        private static InvalidOperationException CreateTypeMismatchException(string field, string expected, string actual)
        {
            return new InvalidOperationException($"Store type mismatch for field '{field}'. Stored type is '{expected}', requested '{actual}'.");
        }
    }
}
