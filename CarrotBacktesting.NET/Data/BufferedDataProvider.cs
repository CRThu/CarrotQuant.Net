using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using Carrot.Memory;
using Carrot.Memory.Abstractions;

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
        private bool _disposed;
        
        // 缓存已加载的 Buffer，保证同一个字段只加载一次
        private readonly ConcurrentDictionary<string, object> _bufferCache = new(StringComparer.OrdinalIgnoreCase);

        public IMarketMetadata Metadata => _metadata;

        public bool HasField(string fieldName) => _registry.FieldExists(fieldName);

        public BufferedDataProvider(
            IMarketMetadata metadata, 
            IFieldRegistry registry, 
            IMarketSeriesSource source, 
            string storageRoot)
        {
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _storageRoot = storageRoot;
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

        private PagedBuffer2D<T> CreateAndLoadBuffer<T>(string fieldName) where T : unmanaged
        {
            int totalDays = _metadata.TradeDates.Count;
            if (totalDays <= 0) throw new InvalidOperationException("Metadata TradeDates count must be > 0.");

            var options = new PagedBuffer2DOptions { 
                Width = _metadata.Symbols.Count, 
                PageSize = 1024, 
                RowCount = totalDays, 
                RootPath = _storageRoot 
            };
            
            // 使用 Carrot.Memory 核心工厂直接创建，利用配置自动选择 Provider
            var buffer = PagedBuffer2DFactory.Open<T>(_storageRoot, options);

            // 使用临时数组中转，因为 Source 要求 Contiguous Span，且缓存读取性能更好
            T[] temp = ArrayPool<T>.Shared.Rent(totalDays);
            try
            {
                for (int colIdx = 0; colIdx < _metadata.Symbols.Count; colIdx++)
                {
                    string symbol = _metadata.Symbols[colIdx];
                    // 假设 ReadSymbolSeries 将数据读取到 temp Span 中
                    _source.ReadSymbolSeries(symbol, fieldName, 0, totalDays, temp.AsSpan(0, totalDays));

                    // 优化：使用 columnView 接口进行批量写入
                    var columnView = buffer.GetColumnView(0, colIdx, totalDays);
                    for (int r = 0; r < totalDays; r++)
                    {
                        columnView[r] = temp[r];
                    }
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
