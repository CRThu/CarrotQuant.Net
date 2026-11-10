using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarrotBacktesting.NET.Config;
using K4os.Compression.LZ4;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序二进制缓存元数据描述契约。
    /// 记录表标识、字段名称、数据类型、压缩算法、定长分页大小、行列规模以及完整时空维度（代码与交易日列表）。
    /// </summary>
    public class TimeseriesCacheMetadata
    {
        [JsonPropertyName("table_id")]
        public string TableId { get; set; } = "";

        [JsonPropertyName("field_name")]
        public string FieldName { get; set; } = "";

        [JsonPropertyName("data_type")]
        public string DataType { get; set; } = "";

        [JsonPropertyName("compression")]
        public string Compression { get; set; } = "lz4";

        [JsonPropertyName("page_size")]
        public int PageSize { get; set; } = 4096;

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("row_count")]
        public int RowCount { get; set; }

        [JsonPropertyName("start_date")]
        public DateTime? StartDate { get; set; }

        [JsonPropertyName("end_date")]
        public DateTime? EndDate { get; set; }

        [JsonPropertyName("symbols")]
        public List<string> Symbols { get; set; } = new();

        [JsonPropertyName("trade_dates")]
        public List<string> TradeDates { get; set; } = new();

        [JsonPropertyName("chunk_widths")]
        public List<int>? ChunkWidths { get; set; }

        /// <summary>
        /// 获取指定分块的物理列宽（自描述分块宽度，支持历史窄分块与当前活跃分块共存）。
        /// </summary>
        public int GetChunkWidth(int pageIndex)
        {
            if (ChunkWidths != null && pageIndex >= 0 && pageIndex < ChunkWidths.Count)
            {
                return ChunkWidths[pageIndex];
            }
            return Width;
        }
    }

    /// <summary>
    /// 时序二进制对齐缓存存储管理器。
    /// 负责按 4096 行（或自定义定长）将行情宽表切分为 chunk_{i}.bin 文件进行物理持久化，
    /// 原生支持 LZ4 超高速块压缩（3~5GB/s 解压吞吐，节省 60%~70% 磁盘 I/O），
    /// 提供纯非托管二进制零拷贝读写、增量行追加（Append）与原子元数据落盘能力。
    /// </summary>
    public class TimeseriesCacheStore
    {
        private const string MetaFileName = "meta.json";
        private readonly string _cacheRoot;
        private readonly string _tableId;
        private readonly int _pageSize;
        private readonly string _compression;

        public string CacheRoot => _cacheRoot;
        public string TableId => _tableId;
        public int PageSize => _pageSize;
        public string Compression => _compression;

        /// <summary>
        /// 初始化缓存管理器。
        /// </summary>
        /// <param name="cacheRoot">缓存物理根目录（例如 {storage_root}/.cache 或表级目录）</param>
        /// <param name="tableId">时序表唯一标识符</param>
        /// <param name="pageSize">分页行数大小，默认为 4096</param>
        /// <param name="compression">压缩格式，支持 "lz4"（默认）与 "none"（无压缩裸块）</param>
        public TimeseriesCacheStore(string cacheRoot, string tableId, int pageSize = 4096, string compression = "lz4")
        {
            if (string.IsNullOrWhiteSpace(cacheRoot))
            {
                throw new ArgumentException("Cache root directory cannot be null or empty.", nameof(cacheRoot));
            }
            if (string.IsNullOrWhiteSpace(tableId))
            {
                throw new ArgumentException("Table ID cannot be null or empty.", nameof(tableId));
            }
            if (pageSize <= 0 || (pageSize & (pageSize - 1)) != 0)
            {
                throw new ArgumentException("PageSize 必须为大于 0 且为 2 的整数次幂。", nameof(pageSize));
            }

            _cacheRoot = Path.GetFullPath(cacheRoot);
            _tableId = tableId;
            _pageSize = pageSize;
            _compression = string.IsNullOrWhiteSpace(compression) ? "lz4" : compression.ToLowerInvariant();
        }

        /// <summary>
        /// 获取指定字段的缓存文件存储目录路径：{cacheRoot}/{tableId}/{fieldName}
        /// </summary>
        public string GetFieldCacheDir(string fieldName)
        {
            return Path.Combine(_cacheRoot, _tableId, fieldName);
        }

        /// <summary>
        /// 检查指定字段的持久化缓存是否存在且元数据有效。
        /// </summary>
        public bool HasCache(string fieldName)
        {
            string dir = GetFieldCacheDir(fieldName);
            string metaPath = Path.Combine(dir, MetaFileName);
            return File.Exists(metaPath);
        }

        /// <summary>
        /// 深度校验指定字段的缓存是否与当前运行环境严格兼容。
        /// 检查维度涵盖：数据类型、列宽数量（Width）、股票代码清单及顺序（Symbols）、分页大小（PageSize）。
        /// 彻底防御因股票新增（IPO 上市）或退市导致列维度变更时的跨列寻址污染。
        /// </summary>
        /// <param name="fieldName">字段名称</param>
        /// <param name="expectedSymbols">期望的代码全集列表</param>
        /// <param name="expectedType">期望的数据类型</param>
        /// <param name="metadata">若有效则输出元数据对象</param>
        /// <returns>若完全兼容返回 true，否则返回 false</returns>
        public bool IsCacheCompatible(
            string fieldName, 
            IReadOnlyList<string> expectedSymbols, 
            Type expectedType, 
            out TimeseriesCacheMetadata? metadata)
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
        /// 精确重写/刷新单个指定分块（chunk_{pageIndex}.bin），保持其他所有历史分块物理零触碰。
        /// 彻底将 I/O 放大比严格限制在单个分块以内（几十 MB），适用于局部数据订正、单块增量回填以及当前活跃分块扩列。
        /// </summary>
        /// <typeparam name="T">非托管数据类型</typeparam>
        /// <param name="fieldName">字段名称</param>
        /// <param name="pageIndex">目标分块索引（0-based）</param>
        /// <param name="rowProvider">行填充提供器 (globalRowIndex, destinationRowSpan)</param>
        /// <param name="newChunkWidth">可选的新分块列宽（若当前分块需扩列，则传入最新宽度）</param>
        /// <param name="updatedSymbols">可选的最新代码列表（配合扩列时更新元数据代码列表）</param>
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
                throw new InvalidOperationException($"Cache for field '{fieldName}' does not exist.");
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
            string dir = GetFieldCacheDir(fieldName);
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

        /// <summary>
        /// 读取指定字段的缓存元数据。若不存在则返回 null。
        /// </summary>
        public TimeseriesCacheMetadata? ReadMetadata(string fieldName)
        {
            string dir = GetFieldCacheDir(fieldName);
            string metaPath = Path.Combine(dir, MetaFileName);
            if (!File.Exists(metaPath)) return null;

            string json = File.ReadAllText(metaPath);
            return JsonSerializer.Deserialize<TimeseriesCacheMetadata>(json);
        }

        /// <summary>
        /// 将内存容器全量写入磁盘分块缓存文件（chunk_{i}.bin 与 meta.json）。
        /// 写入采用先写入 .tmp 后原子替换原则。
        /// </summary>
        public unsafe void WriteBuffer<T>(
            string fieldName,
            ChunkBuffer2D<T> buffer,
            IReadOnlyList<string> symbols,
            IReadOnlyList<DateTime> tradeDates) where T : unmanaged
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentNullException.ThrowIfNull(symbols);
            ArgumentNullException.ThrowIfNull(tradeDates);

            if (buffer.Width != symbols.Count)
            {
                throw new ArgumentException($"Buffer width ({buffer.Width}) does not match symbols count ({symbols.Count}).");
            }
            if (buffer.RowCount != tradeDates.Count)
            {
                throw new ArgumentException($"Buffer row count ({buffer.RowCount}) does not match tradeDates count ({tradeDates.Count}).");
            }

            string dir = GetFieldCacheDir(fieldName);
            Directory.CreateDirectory(dir);

            int totalRows = buffer.RowCount;
            int width = buffer.Width;
            int neededPages = totalRows > 0 ? (int)(((long)totalRows + _pageSize - 1) / _pageSize) : 0;
            bool useLz4 = string.Equals(_compression, "lz4", StringComparison.OrdinalIgnoreCase);

            // 1. 逐块以块级纯连续内存写入二进制数据文件 chunk_{i}.bin
            for (int pageIdx = 0; pageIdx < neededPages; pageIdx++)
            {
                int startRow = pageIdx * _pageSize;
                int rowsInChunk = Math.Min(_pageSize, totalRows - startRow);
                string chunkPath = Path.Combine(dir, $"chunk_{pageIdx}.bin");

                var pageSpan = buffer.GetReadOnlyPageSpan(pageIdx, rowsInChunk);
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

            var meta = new TimeseriesCacheMetadata
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

        /// <summary>
        /// 从磁盘二进制缓存直接恢复并组装 ChunkBuffer2D 容器。
        /// 支持分块各自独立列宽，在载入时根据目标宽度自动在内存中完成列拓宽与 NaN 填充（零历史重写、零磁盘浪费）。
        /// </summary>
        /// <param name="fieldName">字段名称</param>
        /// <param name="targetWidth">目标内存列宽（若为 null 则使用 meta.Width，若大于各分块宽度则自动扩充）</param>
        public unsafe ChunkBuffer2D<T> LoadBuffer<T>(string fieldName, int? targetWidth = null) where T : unmanaged
        {
            var meta = ReadMetadata(fieldName);
            if (meta == null)
            {
                throw new FileNotFoundException($"Cache metadata not found for field '{fieldName}' in table '{_tableId}'.");
            }

            if (!string.Equals(meta.DataType, typeof(T).Name, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateTypeMismatchException(fieldName, meta.DataType, typeof(T).Name);
            }

            string dir = GetFieldCacheDir(fieldName);
            int totalRows = meta.RowCount;
            int width = Math.Max(meta.Width, targetWidth ?? meta.Width);

            var buffer = new ChunkBuffer2D<T>(width, _pageSize, totalRows);
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
                        var targetSpan = buffer.GetPageSpan(pageIdx, rowsInChunk);
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

                            var pageSpan = buffer.GetPageSpan(pageIdx, rowsInChunk);
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
                    buffer.Dispose();
                    throw;
                }
            }

            return buffer;
        }

        /// <summary>
        /// 向现有二进制缓存文件末尾增量追加若干新行（Append）。
        /// 自动完成对末尾 Chunk 的解压填补、LZ4 重新压缩与新分块创建。
        /// </summary>
        /// <param name="fieldName">字段名称</param>
        /// <param name="deltaRows">新增行的数量</param>
        /// <param name="rowProvider">按绝对行索引获取单行切片的回调委托</param>
        /// <param name="newTradeDates">新追加行的日期列表</param>
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
                throw new InvalidOperationException($"Cannot append to non-existent cache for field '{fieldName}'.");
            }
            if (newTradeDates.Count != deltaRows)
            {
                throw new ArgumentException($"newTradeDates count ({newTradeDates.Count}) must equal deltaRows ({deltaRows}).");
            }

            string dir = GetFieldCacheDir(fieldName);
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

        /// <summary>
        /// 清空或删除指定字段的所有持久化缓存数据。
        /// </summary>
        public void Clear(string fieldName)
        {
            string dir = GetFieldCacheDir(fieldName);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }

        /// <summary>
        /// 清空整个表的所有字段缓存。
        /// </summary>
        public void ClearTable()
        {
            string tableDir = Path.Combine(_cacheRoot, _tableId);
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

        private void SaveMetadata(string fieldName, TimeseriesCacheMetadata meta)
        {
            string dir = GetFieldCacheDir(fieldName);
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
            return new InvalidOperationException($"Cache type mismatch for field '{field}'. Stored type is '{expected}', requested '{actual}'.");
        }
    }
}
