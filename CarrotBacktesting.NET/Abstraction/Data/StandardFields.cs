namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 标准时序行情物理字段常量定义（单一事实来源 SSOT）。
/// 收敛全系统对于价格与成交量行情五要素及风控极值列名的定义，杜绝散装魔术字符串。
/// </summary>
public static class StandardFields
{
    /// <summary>开盘价列名。</summary>
    public const string Open = "open";

    /// <summary>最高价列名。</summary>
    public const string High = "high";

    /// <summary>最低价列名。</summary>
    public const string Low = "low";

    /// <summary>收盘价列名（交易基准价）。</summary>
    public const string Close = "close";

    /// <summary>成交量列名。</summary>
    public const string Volume = "volume";

    /// <summary>成交额 / 成交金额列名（用于计算 VWAP）。</summary>
    public const string Amount = "amount";

    /// <summary>涨停价列名。</summary>
    public const string LimitUp = "limit_up";

    /// <summary>跌停价列名。</summary>
    public const string LimitDown = "limit_down";
}
