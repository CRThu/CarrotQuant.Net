using System;
using System.Runtime.CompilerServices;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 面向撮合引擎与风控判定的不可变轻量行情切片值类型。
/// 严格 48 字节紧凑内存布局（6 * 8 字节 double），全字段原生支持缺失（内部以 double.NaN 作为天然哨兵）。
/// 纯栈/寄存器传递，零堆分配、零 GC 开销。
/// </summary>
public readonly struct BarSnapshot : IEquatable<BarSnapshot>
{
    // ==================== 1. 核心物理数值（无 Raw 污染，直接直观命名） ====================
    public readonly double Open;
    public readonly double High;
    public readonly double Low;
    public readonly double Close;
    public readonly double Volume;
    public readonly double Amount;

    // ==================== 2. 对称状态探测（单指令 !double.IsNaN 测试） ====================
    public bool HasOpen => !double.IsNaN(Open);
    public bool HasHigh => !double.IsNaN(High);
    public bool HasLow => !double.IsNaN(Low);
    public bool HasClose => !double.IsNaN(Close);
    public bool HasVolume => !double.IsNaN(Volume);
    public bool HasAmount => !double.IsNaN(Amount);

    /// <summary>
    /// 是否包含任何有效行情（Close、Open、High、Low 任一有效）。
    /// </summary>
    public bool IsValid => HasClose || HasOpen || HasHigh || HasLow;

    // ==================== 3. 高性能回退辅助方法（替代 ?? 运算符，零 GC） ====================
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double OpenOr(double fallback) => !double.IsNaN(Open) ? Open : fallback;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double HighOr(double fallback) => !double.IsNaN(High) ? High : fallback;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double LowOr(double fallback) => !double.IsNaN(Low) ? Low : fallback;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double CloseOr(double fallback) => !double.IsNaN(Close) ? Close : fallback;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double VolumeOr(double fallback) => !double.IsNaN(Volume) ? Volume : fallback;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double AmountOr(double fallback) => !double.IsNaN(Amount) ? Amount : fallback;

    /// <summary>
    /// 衍生推导成交均价（VWAP = Amount / Volume）。
    /// </summary>
    public double? Vwap => (HasAmount && HasVolume && Volume > 0.0) ? (Amount / Volume) : null;

    // ==================== 4. 构造函数 ====================
    /// <summary>
    /// 高速原始 double 构造函数（直接赋值，零转换开销）。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BarSnapshot(
        double open,
        double high,
        double low,
        double close,
        double volume = double.NaN,
        double amount = double.NaN)
    {
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        Amount = amount;
    }

    /// <summary>
    /// 兼容可空数值的可选构造函数。未传字段自动填充为 double.NaN。
    /// </summary>
    public BarSnapshot(
        double? open = null,
        double? high = null,
        double? low = null,
        double? close = null,
        double? volume = null,
        double? amount = null)
    {
        Open = open ?? double.NaN;
        High = high ?? double.NaN;
        Low = low ?? double.NaN;
        Close = close ?? double.NaN;
        Volume = volume ?? double.NaN;
        Amount = amount ?? double.NaN;
    }

    /// <summary>
    /// 空行情切片哨兵实例（全部维度为 double.NaN）。
    /// </summary>
    public static readonly BarSnapshot Empty = new(
        double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);

    // ==================== 5. 从动态数据行提取强类型行情切片 ====================
    /// <summary>
    /// 从动态数据行提取强类型行情切片。
    /// </summary>
    public static BarSnapshot FromDataRow(IDataRow? row, double fallbackPrice = double.NaN)
    {
        if (row == null)
        {
            return !double.IsNaN(fallbackPrice) && fallbackPrice > 0.0
                ? new BarSnapshot(close: fallbackPrice)
                : Empty;
        }

        double close = row.GetDouble(StandardFields.Close, fallbackPrice);
        double open = row.GetDouble(StandardFields.Open, double.NaN);
        double high = row.GetDouble(StandardFields.High, double.NaN);
        double low = row.GetDouble(StandardFields.Low, double.NaN);
        double volume = row.GetDouble(StandardFields.Volume, double.NaN);
        double amount = row.GetDouble(StandardFields.Amount, double.NaN);

        return new BarSnapshot(open, high, low, close, volume, amount);
    }

    /// <summary>
    /// 按标准列名安全读取字段值。
    /// </summary>
    public double GetDouble(string columnName, double defaultValue = 0.0)
    {
        if (string.Equals(columnName, StandardFields.Close, StringComparison.OrdinalIgnoreCase)) return CloseOr(defaultValue);
        if (string.Equals(columnName, StandardFields.Open, StringComparison.OrdinalIgnoreCase)) return OpenOr(defaultValue);
        if (string.Equals(columnName, StandardFields.High, StringComparison.OrdinalIgnoreCase)) return HighOr(defaultValue);
        if (string.Equals(columnName, StandardFields.Low, StringComparison.OrdinalIgnoreCase)) return LowOr(defaultValue);
        if (string.Equals(columnName, StandardFields.Volume, StringComparison.OrdinalIgnoreCase)) return VolumeOr(defaultValue);
        if (string.Equals(columnName, StandardFields.Amount, StringComparison.OrdinalIgnoreCase)) return AmountOr(defaultValue);
        return defaultValue;
    }

    /// <summary>
    /// 检查切片中是否包含指定有效列名。
    /// </summary>
    public bool ContainsColumn(string columnName)
    {
        if (string.Equals(columnName, StandardFields.Close, StringComparison.OrdinalIgnoreCase)) return HasClose;
        if (string.Equals(columnName, StandardFields.Open, StringComparison.OrdinalIgnoreCase)) return HasOpen;
        if (string.Equals(columnName, StandardFields.High, StringComparison.OrdinalIgnoreCase)) return HasHigh;
        if (string.Equals(columnName, StandardFields.Low, StringComparison.OrdinalIgnoreCase)) return HasLow;
        if (string.Equals(columnName, StandardFields.Volume, StringComparison.OrdinalIgnoreCase)) return HasVolume;
        if (string.Equals(columnName, StandardFields.Amount, StringComparison.OrdinalIgnoreCase)) return HasAmount;
        return false;
    }

    // ==================== 6. 等值比对 ====================
    public bool Equals(BarSnapshot other) =>
        (double.IsNaN(Open) ? double.IsNaN(other.Open) : Open.Equals(other.Open)) &&
        (double.IsNaN(High) ? double.IsNaN(other.High) : High.Equals(other.High)) &&
        (double.IsNaN(Low) ? double.IsNaN(other.Low) : Low.Equals(other.Low)) &&
        (double.IsNaN(Close) ? double.IsNaN(other.Close) : Close.Equals(other.Close)) &&
        (double.IsNaN(Volume) ? double.IsNaN(other.Volume) : Volume.Equals(other.Volume)) &&
        (double.IsNaN(Amount) ? double.IsNaN(other.Amount) : Amount.Equals(other.Amount));

    public override bool Equals(object? obj) => obj is BarSnapshot other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Open, High, Low, Close, Volume, Amount);

    public static bool operator ==(BarSnapshot left, BarSnapshot right) => left.Equals(right);
    public static bool operator !=(BarSnapshot left, BarSnapshot right) => !left.Equals(right);
}
