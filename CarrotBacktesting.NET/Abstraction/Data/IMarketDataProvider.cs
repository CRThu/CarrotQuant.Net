using System;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 行情与复权数据提供接口。
/// 缺省提供未复权行情（PriceMode.Raw）；指定 PriceMode.Post 时按复权因子折算。
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>
    /// 全局市场元数据（包含标的代码与交易日历网格）。
    /// </summary>
    IMarketMetadata Metadata { get; }

    /// <summary>
    /// 底层时序注册表。
    /// </summary>
    ITimeseriesRegistry Timeseries { get; }

    /// <summary>
    /// 获取指定标的列索引在指定时步的字段数值。
    /// </summary>
    /// <param name="symbolIndex">标的列索引。</param>
    /// <param name="fieldName">字段名称。</param>
    /// <param name="stepIndex">时步索引。</param>
    /// <param name="mode">价格模式（缺省为 Raw 未复权）。</param>
    /// <returns>字段数值；若数据不存在或越界返回 double.NaN。</returns>
    double GetField(int symbolIndex, string fieldName, int stepIndex, PriceMode mode = PriceMode.Raw);

    /// <summary>
    /// 通过标的代码查询指定时步的字段数值。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="fieldName">字段名称。</param>
    /// <param name="stepIndex">时步索引。</param>
    /// <param name="mode">价格模式（缺省为 Raw 未复权）。</param>
    /// <returns>字段数值；若标的未找到或数据不存在返回 double.NaN。</returns>
    double GetField(string symbol, string fieldName, int stepIndex, PriceMode mode = PriceMode.Raw);

    /// <summary>
    /// 获取全市场指定字段在指定时步的只读行切片。
    /// </summary>
    /// <param name="fieldName">字段名称。</param>
    /// <param name="stepIndex">时步索引。</param>
    /// <param name="mode">价格模式（缺省为 Raw 未复权）。</param>
    /// <returns>全市场切片；若字段不存在或越界返回空切片。</returns>
    ReadOnlySpan<double> GetRow(string fieldName, int stepIndex, PriceMode mode = PriceMode.Raw);

    /// <summary>
    /// 获取指定标的列索引在指定时步的行情切片（OHLCV）。
    /// </summary>
    /// <param name="symbolIndex">标的列索引。</param>
    /// <param name="stepIndex">时步索引。</param>
    /// <param name="mode">价格模式（缺省为 Raw 未复权）。</param>
    /// <returns>行情切片结构；若无效返回 BarSnapshot.Empty。</returns>
    BarSnapshot GetBar(int symbolIndex, int stepIndex, PriceMode mode = PriceMode.Raw);

    /// <summary>
    /// 通过标的代码获取指定时步的行情切片（OHLCV）。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="stepIndex">时步索引。</param>
    /// <param name="mode">价格模式（缺省为 Raw 未复权）。</param>
    /// <returns>行情切片结构；若未找到返回 BarSnapshot.Empty。</returns>
    BarSnapshot GetBar(string symbol, int stepIndex, PriceMode mode = PriceMode.Raw);

    /// <summary>
    /// 将后复权价格折算为未复权物理价格。
    /// </summary>
    /// <param name="symbolIndex">标的列索引。</param>
    /// <param name="postPrice">后复权价格。</param>
    /// <param name="stepIndex">时步索引。</param>
    /// <returns>未复权价格。</returns>
    double ToRawPrice(int symbolIndex, double postPrice, int stepIndex);

    /// <summary>
    /// 将后复权价格折算为未复权物理价格。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="postPrice">后复权价格。</param>
    /// <param name="stepIndex">时步索引。</param>
    /// <returns>未复权价格。</returns>
    double ToRawPrice(string symbol, double postPrice, int stepIndex);

    /// <summary>
    /// 将未复权物理价格折算为后复权价格。
    /// </summary>
    /// <param name="symbolIndex">标的列索引。</param>
    /// <param name="rawPrice">未复权价格。</param>
    /// <param name="stepIndex">时步索引。</param>
    /// <returns>后复权价格。</returns>
    double ToPostPrice(int symbolIndex, double rawPrice, int stepIndex);

    /// <summary>
    /// 将未复权物理价格折算为后复权价格。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="rawPrice">未复权价格。</param>
    /// <param name="stepIndex">时步索引。</param>
    /// <returns>后复权价格。</returns>
    double ToPostPrice(string symbol, double rawPrice, int stepIndex);

    /// <summary>
    /// 是否已加载并启用了复权数据。
    /// </summary>
    bool HasAdjustments { get; }
}
