using System;
using System.Collections.Generic;

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
        public DateTime Time { get; }

        /// <summary>
        /// 基础数据字典
        /// Key: 股票代码, Value: 该股票在本帧的基础数据
        /// </summary>
        public IReadOnlyDictionary<string, StockFrame> PrimaryData { get; }

        /// <summary>
        /// 字符串类型的扩展数据
        /// Key: 字段名 (e.g. "is_st"), Value: (Key: 股票代码, Value: 字段值)
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> StringExtendedData { get; }

        /// <summary>
        /// Double类型的扩展数据
        /// Key: 字段名 (e.g. "pe_ratio"), Value: (Key: 股票代码, Value: 字段值)
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> DoubleExtendedData { get; }

        public MarketFrame(DateTime time,
                           Dictionary<string, StockFrame> primaryData,
                           Dictionary<string, IReadOnlyDictionary<string, string>> stringExtendedData,
                           Dictionary<string, IReadOnlyDictionary<string, double>> doubleExtendedData)
        {
            Time = time;
            PrimaryData = primaryData;
            StringExtendedData = stringExtendedData;
            DoubleExtendedData = doubleExtendedData;
        }

        /// <summary>
        /// 获取指定股票的基础数据
        /// </summary>
        public bool TryGetPrimaryFrame(string stockCode, out StockFrame frame)
        {
            return PrimaryData.TryGetValue(stockCode, out frame);
        }

        /// <summary>
        /// 获取指定股票的字符串类型扩展数据值
        /// </summary>
        public bool TryGetStringValue(string stockCode, string fieldName, out string? value)
        {
            if (StringExtendedData.TryGetValue(fieldName, out var stockValues) &&
                stockValues.TryGetValue(stockCode, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// 获取指定股票的Double类型扩展数据值
        /// </summary>
        public bool TryGetDoubleValue(string stockCode, string fieldName, out double value)
        {
            if (DoubleExtendedData.TryGetValue(fieldName, out var stockValues) &&
                stockValues.TryGetValue(stockCode, out value))
            {
                return true;
            }

            value = default;
            return false;
        }
    }
}