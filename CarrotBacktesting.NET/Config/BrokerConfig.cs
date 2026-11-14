using System;
using YamlDotNet.Serialization;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Config
{
    /// <summary>
    /// 虚拟经纪商配置，掌管账户初始资金、结算天数、交易费率、滑点与风控规则。
    /// 对应 YAML 配置中的 broker 节点。
    /// </summary>
    public class BrokerConfig
    {
        /// <summary>
        /// 账户初始可用资金（本金），默认为 1,000,000.0 元。
        /// </summary>
        public double InitialCash { get; set; } = 1000000.0;

        /// <summary>
        /// 结算可用天数：0 即 T+0（日内即时可用，现代通用量化基准）；1 即 T+1（A 股次日可用）。默认为 0。
        /// </summary>
        public int SettleDays { get; set; } = 0;

        /// <summary>
        /// 是否允许做空。默认 false（多头现货安全基线，防止意外裸空）。
        /// </summary>
        public bool AllowShort { get; set; } = false;

        /// <summary>
        /// 基础手续费/佣金费率（如 0.001 代表千分之一综合摩擦）。默认为 0.001。
        /// </summary>
        public double FeeRate { get; set; } = 0.001;

        /// <summary>
        /// 单笔成交最低手续费门槛（通用国际市场为 0.0；A 股券商实战可设 5.0）。默认为 0.0。
        /// </summary>
        public double MinFee { get; set; } = 0.0;

        /// <summary>
        /// 固定比例滑点（如 0.001 代表千一；0.0 表示无滑点）。默认为 0.001。
        /// </summary>
        public double Slippage { get; set; } = 0.001;

        /// <summary>
        /// 成交量流动性硬约束：单笔最大允许成交量占当前 Bar 成交量的最大比例（如 0.05 表示 5%）。
        /// 默认 0.05，严格杜绝流动性不足或无量封板时的虚假成交作弊；设为 0.0 则不限制。
        /// </summary>
        public double VolumeLimit { get; set; } = 0.05;

        /// <summary>
        /// 是否开启涨跌停检查。开启后若挂载了 OnCheckLimit 则执行拦截。默认为 false。
        /// </summary>
        public bool CheckLimit { get; set; } = false;

        /// <summary>
        /// 默认合约乘数。股票按“股”计算为 1.0，期货按“手”计算填一手规格（如螺纹钢填 10.0）。默认为 1.0。
        /// </summary>
        public double Multiplier { get; set; } = 1.0;

        /// <summary>
        /// 默认保证金率。股票全额现货为 1.0，期货按杠杆率填写（如 10 倍杠杆填 0.1）。默认为 1.0。
        /// </summary>
        public double MarginRate { get; set; } = 1.0;

        private string _timeZoneId = "UTC";
        private TimeZoneInfo _timeZone = TimeZoneInfo.Utc;

        /// <summary>
        /// 交易所清算时区标识（遵循 IANA 标准如 "Asia/Shanghai"、"America/New_York" 或 "UTC"）。
        /// 默认为 "UTC"。系统自动跨平台自适应解析并缓存 TimeZoneInfo。
        /// </summary>
        public string TimeZoneId
        {
            get => _timeZoneId;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _timeZoneId = "UTC";
                    _timeZone = TimeZoneInfo.Utc;
                    return;
                }
                _timeZoneId = value;
                // 严格遵循规范第 8 条与第 12 条：若时区无效，显式抛出强类型异常，坚决杜绝静默重置为 UTC
                _timeZone = TimeZoneInfo.FindSystemTimeZoneById(value);
            }
        }

        /// <summary>
        /// 缓存的时区对象，避免热路径重复查找。
        /// </summary>
        [YamlIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public TimeZoneInfo TimeZone
        {
            get => _timeZone;
            set
            {
                _timeZone = value ?? TimeZoneInfo.Utc;
                _timeZoneId = _timeZone.Id;
            }
        }

        #region 对称 Hook 矩阵 (仅在代码中挂载，YAML 序列化时自动忽略)

        /// <summary>
        /// 成交量流动性检查 Hook。用于自定义日内动态成交量、分档分批撮合模型。
        /// 参数：(OrderRequest order, IDataRow bar) -> bool (是否允许成交或合规)
        /// </summary>
        [YamlIgnore]
        public Func<OrderRequest, IDataRow, bool>? OnCheckVolume { get; set; }

        /// <summary>
        /// 结算可用天数 Hook。用于多资产混合时按标的动态返回（如股票返回 1，ETF/期货返回 0）。
        /// 参数：(string symbol) -> int (结算天数)
        /// </summary>
        [YamlIgnore]
        public Func<string, int>? OnGetSettleDays { get; set; }

        /// <summary>
        /// 涨跌停封板与停牌拦截 Hook。由用户自主比对数据列（如 limit_up）或自定义板块阈值。
        /// 参数：(OrderRequest order, IDataRow bar) -> bool (是否通过检查，false 则拦截)
        /// </summary>
        [YamlIgnore]
        public Func<OrderRequest, IDataRow, bool>? OnCheckLimit { get; set; }

        /// <summary>
        /// 手续费与税费计算 Hook。覆盖默认费率，用于实现单向印花税、阶梯佣金或返佣。
        /// 参数：(OrderRequest order, double turnover) -> double (实际应扣费用)
        /// </summary>
        [YamlIgnore]
        public Func<OrderRequest, double, double>? OnCalculateFee { get; set; }

        /// <summary>
        /// 滑点计算 Hook。覆盖默认固定比例滑点，用于实现非线性冲击成本模型。
        /// 参数：(OrderRequest order, double basePrice, IDataRow bar) -> double (实际撮合价格)
        /// </summary>
        [YamlIgnore]
        public Func<OrderRequest, double, IDataRow, double>? OnCalculateSlippage { get; set; }

        /// <summary>
        /// 合约乘数 Hook。在多品种期货回测中，显式由用户读取维表获取一手乘数。
        /// 参数：(string symbol, IFieldRegistry? fields) -> double (乘数)
        /// </summary>
        [YamlIgnore]
        public Func<string, IFieldRegistry?, double>? OnGetMultiplier { get; set; }

        /// <summary>
        /// 保证金率 Hook。在多品种期货回测中，显式由用户读取维表获取保证金率。
        /// 参数：(string symbol, IFieldRegistry? fields) -> double (保证金率)
        /// </summary>
        [YamlIgnore]
        public Func<string, IFieldRegistry?, double>? OnGetMarginRate { get; set; }

        #endregion
    }
}
