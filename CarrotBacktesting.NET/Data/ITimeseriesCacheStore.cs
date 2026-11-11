using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序二进制持久化缓存存储器抽象契约。
    /// 隔离具体的磁盘文件系统布局、压缩算法与元数据序列化实现。
    /// </summary>
    public interface ITimeseriesCacheStore
    {
        /// <summary>
        /// 缓存物理根目录路径。
        /// </summary>
        string CacheRoot { get; }

        /// <summary>
        /// 表标识符。
        /// </summary>
        string TableId { get; }

        /// <summary>
        /// 单分块行数容量（默认 4096 行）。
        /// </summary>
        int PageSize { get; }

        /// <summary>
        /// 压缩算法名称（如 "lz4", "none"）。
        /// </summary>
        string Compression { get; }

        /// <summary>
        /// 检查指定字段是否存在持久化缓存。
        /// </summary>
        bool HasCache(string fieldName);

        /// <summary>
        /// 读取指定字段的缓存元数据。
        /// </summary>
        TimeseriesCacheMetadata? ReadMetadata(string fieldName);

        /// <summary>
        /// 校验现有缓存是否与目标请求（类型、标的代码单调递增前缀、分块大小）物理兼容。
        /// </summary>
        bool IsCacheCompatible(string fieldName, IReadOnlyList<string> expectedSymbols, Type expectedType, out TimeseriesCacheMetadata? metadata);

        /// <summary>
        /// 将指定字段的分块持久化数据加载为非托管内存容器 <see cref="ChunkBuffer2D{T}"/>。
        /// 若传入 <paramref name="targetWidth"/> 且大于历史分块宽度，自动在内存加载时进行列拓宽并填充 NaN。
        /// </summary>
        ChunkBuffer2D<T> LoadBuffer<T>(string fieldName, int? targetWidth = null) where T : unmanaged;

        /// <summary>
        /// 将非托管内存容器全量写入分块持久化存储。
        /// </summary>
        void WriteBuffer<T>(string fieldName, ChunkBuffer2D<T> buffer, IReadOnlyList<string> symbols, IReadOnlyList<DateTime> tradeDates) where T : unmanaged;

        /// <summary>
        /// 单分块原地精准重刷（仅重写指定页索引的 chunk_{pageIndex}.bin，历史分块物理零触碰）。
        /// </summary>
        void RewriteChunk<T>(
            string fieldName, 
            int pageIndex, 
            Action<int, Span<T>> rowProvider,
            int? newChunkWidth = null,
            IReadOnlyList<string>? updatedSymbols = null) where T : unmanaged;

        /// <summary>
        /// 跨分块增量追加新行。若跨越页容量自动开辟新分块，历史分块物理零触碰。
        /// </summary>
        void AppendRows<T>(
            string fieldName, 
            int deltaRows, 
            Action<int, Span<T>> rowProvider, 
            IReadOnlyList<DateTime> newTradeDates) where T : unmanaged;

        /// <summary>
        /// 清空或删除指定字段的所有持久化缓存数据。
        /// </summary>
        void Clear(string fieldName);

        /// <summary>
        /// 清空整个表的所有字段缓存。
        /// </summary>
        void ClearTable();
    }
}
