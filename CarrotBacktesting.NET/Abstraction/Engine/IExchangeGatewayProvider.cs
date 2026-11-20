namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 交易所网关提供者接口，允许上层通过契约安全访问底层网关而无需向下强转具体实现。
/// </summary>
public interface IExchangeGatewayProvider
{
    /// <summary>
    /// 获取当前挂载的交易所底层网关。
    /// </summary>
    IExchangeGateway Gateway { get; }
}
