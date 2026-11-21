using System;

namespace CarrotBacktesting.NET.Abstraction.Indicators;

/// <summary>
/// 只读指标数值流契约（Indicator Stream）。
/// 代表指标产出的连续时序数值切片（如 MACD 的 DIF/DEA、布林带上轨/下轨、或单值指标自身的数据线）。
/// 仅暴露安全只读访问能力，内置负数时序索引器与防偷价守卫，热路径零堆分配（Zero GC）。
/// </summary>
public interface IIndicatorStream
{
    /// <summary>
    /// 统一负数时序索引器。
    /// 0 代表当前时步，-1 代表上一期，-k 代表前 k 期。
    /// 传入正数 offset &gt; 0 时强制抛出 <see cref="ArgumentOutOfRangeException"/>，坚决杜绝未来函数偷价。
    /// </summary>
    /// <param name="offset">时序偏移量（&lt;= 0）。</param>
    /// <returns>指定历史时间步的数值；若未就绪或越界则返回 <see cref="double.NaN"/>。</returns>
    double this[int offset] { get; }

    /// <summary>
    /// 便捷属性：获取当前时间步指标值（等价于 this[0]）。
    /// </summary>
    double Current => this[0];

    /// <summary>
    /// 获取指标是否已达到所需滑动窗口周期并完成预热就绪。
    /// </summary>
    bool IsReady { get; }
}
