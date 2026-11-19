using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 表级双标尺索引存储管理组件。
    /// 专职负责标的代码词表（symbols.idx，行号对应列索引）与交易时间戳轴（time.idx，二进制连续 long 存储）
    /// 的物理落盘、原子替换、前缀单调扩充与幂等一致性校验。
    /// </summary>
    public class TimeseriesIndexStore
    {
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
        /// 初始化表级双标尺索引存储管理器。
        /// </summary>
        /// <param name="storeRoot">物理存储根目录</param>
        /// <param name="tableId">时序表唯一标识符</param>
        public TimeseriesIndexStore(string storeRoot, string tableId)
        {
            if (string.IsNullOrWhiteSpace(storeRoot))
            {
                throw new ArgumentException("Store root directory cannot be null or empty.", nameof(storeRoot));
            }
            if (string.IsNullOrWhiteSpace(tableId))
            {
                throw new ArgumentException("Table ID cannot be null or empty.", nameof(tableId));
            }

            _storeRoot = Path.GetFullPath(storeRoot);
            _tableId = tableId;
        }

        /// <summary>
        /// 获取表级物理存储目录路径：{storeRoot}/{tableId}
        /// </summary>
        public string GetTableDir()
        {
            return Path.Combine(_storeRoot, _tableId);
        }

        /// <summary>
        /// 获取表级标的代码索引文件物理路径：{storeRoot}/{tableId}/symbols.idx
        /// </summary>
        public string GetSymbolsIndexPath()
        {
            return Path.Combine(GetTableDir(), SymbolsIndexFileName);
        }

        /// <summary>
        /// 获取表级时间戳索引文件物理路径：{storeRoot}/{tableId}/time.idx
        /// </summary>
        public string GetTimeIndexPath()
        {
            return Path.Combine(GetTableDir(), TimeIndexFileName);
        }

        /// <summary>
        /// 读取表级代码词表索引 (symbols.idx)。
        /// </summary>
        /// <returns>标的代码列表；若文件不存在则返回空列表</returns>
        public IReadOnlyList<string> ReadSymbols()
        {
            string path = GetSymbolsIndexPath();
            lock (_gate)
            {
                if (!File.Exists(path)) return Array.Empty<string>();
                return File.ReadAllLines(path, Encoding.UTF8);
            }
        }

        /// <summary>
        /// 原子写入表级标的代码索引 (symbols.idx)。
        /// 支持单调扩充与幂等校验：现有代码集若与写入完全一致则免写；若写入为现有前缀子集则保留更宽索引；
        /// 若写入为现有前缀扩充则写入更宽代码集；若内容产生冲突则抛出强类型异常。
        /// </summary>
        /// <param name="symbols">代码列表（按列索引升序严格排列）</param>
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

            lock (_gate)
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

        /// <summary>
        /// 从纯二进制连续内存读取表级时间戳索引 (time.idx)。
        /// </summary>
        /// <returns>时间戳列表；若文件不存在则返回空列表</returns>
        public IReadOnlyList<MarketTime> ReadTradeDates()
        {
            string path = GetTimeIndexPath();
            lock (_gate)
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

        /// <summary>
        /// 原子全量覆盖或幂等验证表级时间戳索引 (time.idx)。
        /// </summary>
        /// <param name="tradeDates">交易时间戳列表（按行索引升序排列）</param>
        public void WriteTradeDates(IReadOnlyList<MarketTime> tradeDates)
        {
            ArgumentNullException.ThrowIfNull(tradeDates);
            string tableDir = GetTableDir();
            Directory.CreateDirectory(tableDir);
            string path = GetTimeIndexPath();
            string tmpPath = path + ".tmp";

            lock (_gate)
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

        /// <summary>
        /// 增量追加交易时间戳至表级时间戳索引 (time.idx)。
        /// 支持幂等跳过已有行与断言重入保护。
        /// </summary>
        /// <param name="newTradeDates">待追加的时间戳列表</param>
        /// <param name="targetRowCount">追加后的目标总行数（用于并发与重入校验；&lt;= 0 时不作严格前置总行校验）</param>
        public void AppendTradeDates(IReadOnlyList<MarketTime> newTradeDates, int targetRowCount = -1)
        {
            if (newTradeDates == null || newTradeDates.Count == 0) return;

            string tableDir = GetTableDir();
            Directory.CreateDirectory(tableDir);
            string path = GetTimeIndexPath();

            lock (_gate)
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

        /// <summary>
        /// 清理整张时序表的物理目录与所有索引、分块数据。
        /// </summary>
        public void ClearTable()
        {
            lock (_gate)
            {
                string tableDir = GetTableDir();
                if (Directory.Exists(tableDir))
                {
                    Directory.Delete(tableDir, true);
                }
            }
        }
    }
}
