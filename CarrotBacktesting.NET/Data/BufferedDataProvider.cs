using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 全量内存缓冲模式的 DataProvider。
    /// 初始化时执行数据对齐与加载，保障回测阶段的极高性能。
    /// </summary>
    public class BufferedDataProvider : IDataProvider, IDisposable
    {
        private readonly IMarketMetadata _metadata;
        private readonly IFieldRegistry _registry;
        private readonly IMarketSeriesSource _source;
        private readonly string _storageRoot;
        private readonly int _pageSize;
        private readonly bool _enableCache;
        private readonly string _refreshMode;
        private readonly TimeseriesCacheStore? _cacheStore;
        private bool _disposed;
        
        // 缓存已加载的 Buffer，保证同一个字段只加载一次
        private readonly ConcurrentDictionary<string, object> _bufferCache = new(StringComparer.OrdinalIgnoreCase);

        public IMarketMetadata Metadata => _metadata;

        public bool HasField(string fieldName) => _registry.FieldExists(fieldName);

        public BufferedDataProvider(
            IMarketMetadata metadata, 
            IFieldRegistry registry, 
            IMarketSeriesSource source, 
            string storageRoot,
            int pageSize = 4096,
            bool enableCache = false,
            string refreshMode = "lazy",
            string tableId = "default",
            string compression = "lz4")
        {
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _storageRoot = storageRoot;
            _pageSize = pageSize;
            _enableCache = enableCache;
            _refreshMode = refreshMode;

            if (enableCache && !string.IsNullOrWhiteSpace(storageRoot))
            {
                _cacheStore = new TimeseriesCacheStore(storageRoot, tableId, pageSize, compression);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            foreach (var buffer in _bufferCache.Values)
            {
                if (buffer is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            _bufferCache.Clear();
            _disposed = true;
        }

        public IReadOnlyBuffer2D<T> GetBuffer<T>(string fieldName) where T : unmanaged
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            
            // 1. 路由校验
            if (!_registry.FieldExists(fieldName))
            {
                throw new KeyNotFoundException($"Field '{fieldName}' not registered in registry.");
            }

            // 2. 类型检查 (确保 requested T 与 field 实际类型一致)
            var fieldType = _registry.GetFieldType(fieldName);
            if (fieldType != typeof(T))
            {
                throw new InvalidOperationException($"Type mismatch for field '{fieldName}'. Expected {fieldType}, requested {typeof(T)}.");
            }

            // 3. 原子加载与缓存
            return (IReadOnlyBuffer2D<T>)_bufferCache.GetOrAdd(fieldName, _ => CreateAndLoadBuffer<T>(fieldName));
        }

        private ChunkBuffer2D<T> CreateAndLoadBuffer<T>(string fieldName) where T : unmanaged
        {
            int totalDays = _metadata.TradeDates.Count;
            if (totalDays <= 0) throw new InvalidOperationException("Metadata TradeDates count must be > 0.");

            if (_enableCache && _cacheStore != null)
            {
                if (string.Equals(_refreshMode, "force", StringComparison.OrdinalIgnoreCase))
                {
                    _cacheStore.Clear(fieldName);
                    var buffer = LoadFromSource<T>(fieldName, totalDays);
                    _cacheStore.WriteBuffer(fieldName, buffer, _metadata.Symbols, _metadata.TradeDates);
                    return buffer;
                }
                else if (string.Equals(_refreshMode, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    if (_cacheStore.IsCacheCompatible(fieldName, _metadata.Symbols, typeof(T), out var meta))
                    {
                        if (meta!.RowCount == totalDays)
                        {
                            if (_metadata.Symbols.Count > meta.Symbols.Count)
                            {
                                // 股票池维度扩展：仅重刷当前活跃分块（单个 bin），历史所有分块物理零触碰！
                                RefreshActiveChunkForExpandedSymbols<T>(fieldName, meta);
                            }

                            return _cacheStore.LoadBuffer<T>(fieldName, _metadata.Symbols.Count);
                        }
                    }

                    var buffer = LoadFromSource<T>(fieldName, totalDays);
                    _cacheStore.WriteBuffer(fieldName, buffer, _metadata.Symbols, _metadata.TradeDates);
                    return buffer;
                }
                else // lazy
                {
                    if (_cacheStore.IsCacheCompatible(fieldName, _metadata.Symbols, typeof(T), out var meta))
                    {
                        try
                        {
                            if (_metadata.Symbols.Count > meta!.Symbols.Count)
                            {
                                // 股票池维度扩展：仅重刷当前活跃分块（单个 bin），历史所有分块物理零触碰！
                                RefreshActiveChunkForExpandedSymbols<T>(fieldName, meta);
                            }

                            // 传入期望代码数量，历史窄分块在载入时自动完成列拓宽与 NaN 填充（零历史重写与零磁盘浪费）
                            return _cacheStore.LoadBuffer<T>(fieldName, _metadata.Symbols.Count);
                        }
                        catch
                        {
                            // 缓存文件损坏或异常时回退全量重建
                        }
                    }

                    var buffer = LoadFromSource<T>(fieldName, totalDays);
                    _cacheStore.WriteBuffer(fieldName, buffer, _metadata.Symbols, _metadata.TradeDates);
                    return buffer;
                }
            }

            return LoadFromSource<T>(fieldName, totalDays);
        }

        private void RefreshActiveChunkForExpandedSymbols<T>(string fieldName, TimeseriesCacheMetadata meta) where T : unmanaged
        {
            if (_cacheStore == null || meta.RowCount <= 0) return;

            int activePageIndex = (meta.RowCount - 1) / _pageSize;
            int startRow = activePageIndex * _pageSize;
            int activeRows = Math.Min(_pageSize, meta.RowCount - startRow);
            int newWidth = _metadata.Symbols.Count;

            T[][] activeData = new T[newWidth][];
            try
            {
                for (int c = 0; c < newWidth; c++)
                {
                    activeData[c] = ArrayPool<T>.Shared.Rent(activeRows);
                    _source.ReadSymbolSeries(_metadata.Symbols[c], fieldName, startRow, activeRows, activeData[c].AsSpan(0, activeRows));
                }

                _cacheStore.RewriteChunk<T>(
                    fieldName,
                    activePageIndex,
                    (globalRow, destRow) =>
                    {
                        int r = globalRow - startRow;
                        for (int c = 0; c < newWidth; c++)
                        {
                            destRow[c] = activeData[c][r];
                        }
                    },
                    newChunkWidth: newWidth,
                    updatedSymbols: _metadata.Symbols);
            }
            finally
            {
                for (int c = 0; c < newWidth; c++)
                {
                    if (activeData[c] != null)
                    {
                        ArrayPool<T>.Shared.Return(activeData[c]);
                    }
                }
            }
        }

        private ChunkBuffer2D<T> LoadFromSource<T>(string fieldName, int totalDays) where T : unmanaged
        {
            var buffer = new ChunkBuffer2D<T>(_metadata.Symbols.Count, _pageSize, totalDays);

            T[] temp = ArrayPool<T>.Shared.Rent(totalDays);
            try
            {
                for (int colIdx = 0; colIdx < _metadata.Symbols.Count; colIdx++)
                {
                    string symbol = _metadata.Symbols[colIdx];
                    _source.ReadSymbolSeries(symbol, fieldName, 0, totalDays, temp.AsSpan(0, totalDays));
                    buffer.SetColumn(0, colIdx, temp.AsSpan(0, totalDays));
                }
            }
            finally
            {
                ArrayPool<T>.Shared.Return(temp);
            }

            return buffer;
        }
    }
}
