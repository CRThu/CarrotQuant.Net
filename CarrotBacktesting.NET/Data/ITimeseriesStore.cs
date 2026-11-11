using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序二进制持久化元数据描述契约。
    /// 记录表标识、字段名称、数据类型、压缩算法、定长分页大小、行列规模以及完整时空维度（代码与交易日列表）。
    /// </summary>
    public class TimeseriesMetadata
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
    /// 时序二进制持久化存储抽象契约。
    /// 隔离底层磁盘分块存储、LZ4块压缩与多维度元数据管理，
    /// 为上层时序数据流提供高性能、低开销的持久化与增量检索能力。
    /// </summary>
    public interface ITimeseriesStore
    {
        /// <summary>
        /// 时序表唯一标识符。
        /// </summary>
        string TableId { get; }

        /// <summary>
        /// 单分块逻辑行数容量（默认 4096 行）。
        /// </summary>
        int PageSize { get; }

        /// <summary>
        /// 检查指定字段是否存在持久化数据。
        /// </summary>
        bool HasField(string fieldName);

        /// <summary>
        /// 读取指定字段的时序持久化元数据。
        /// </summary>
        TimeseriesMetadata? ReadMetadata(string fieldName);

        /// <summary>
        /// 校验现有持久化数据是否与目标请求（类型、标的代码单调递增前缀、分块大小）物理兼容。
        /// </summary>
        bool IsCompatible(string fieldName, IReadOnlyList<string> expectedSymbols, Type expectedType, out TimeseriesMetadata? metadata);

        /// <summary>
        /// 将指定字段的分块持久化数据加载为非托管内存矩阵 <see cref="ChunkMatrix2D{T}"/>。
        /// 若传入 <paramref name="targetWidth"/> 且大于历史分块宽度，自动在内存加载时进行列拓宽并填充 NaN。
        /// </summary>
        ChunkMatrix2D<T> LoadMatrix<T>(string fieldName, int? targetWidth = null) where T : unmanaged;

        /// <summary>
        /// 将非托管内存矩阵全量写入分块持久化存储。
        /// </summary>
        void WriteMatrix<T>(string fieldName, ChunkMatrix2D<T> matrix, IReadOnlyList<string> symbols, IReadOnlyList<DateTime> tradeDates) where T : unmanaged;

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
        /// 清空或删除指定字段的所有持久化数据。
        /// </summary>
        void Clear(string fieldName);

        /// <summary>
        /// 清空整个表的所有字段持久化数据。
        /// </summary>
        void ClearTable();
    }
}
