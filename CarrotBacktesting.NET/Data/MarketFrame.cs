using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 市场帧 (市场横截面)
    /// <br/>
    /// 包含单个时间点上，所有活跃资产的数据。
    /// </summary>
    public class MarketFrame
    {
        /// <summary>
        /// 当前帧的日期时间
        /// </summary>
        public DateTime Time;

        /// <summary>
        /// 基础数据数组。数组索引对应于 MarketStorage.Symbols 中的股票索引。
        /// 使用可空结构体来表示某股票当天是否无数据。
        /// </summary>
        public StockFrame?[] PrimaryData;

        /// <summary>
        /// 字符串类型的扩展数据
        /// Key: 字段名, Value: 对应字段的所有股票值数组 (索引与股票列表对齐)
        /// </summary>
        public Dictionary<string, string?[]> StringExtendedData;

        /// <summary>
        /// Double类型的扩展数据
        /// Key: 字段名, Value: 对应字段的所有股票值数组 (索引与股票列表对齐)
        /// </summary>
        public Dictionary<string, double?[]> DoubleExtendedData;

        [JsonConstructor]
        public MarketFrame(DateTime time,
                           StockFrame?[] primaryData,
                           Dictionary<string, string?[]> stringExtendedData,
                           Dictionary<string, double?[]> doubleExtendedData)
        {
            Time = time;
            PrimaryData = primaryData;
            StringExtendedData = stringExtendedData;
            DoubleExtendedData = doubleExtendedData;
        }
    }
}