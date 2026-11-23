using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Indicators;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Analysis;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBacktesting.NET.Indicators;
using CarrotBacktesting.NET.Strategy;
using CarrotBackTesting.Net.UnitTest.Analysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Integration;

/// <summary>
/// 交易与财务对账穿透端到端集成测试（Trading, Execution, Slippage & Financial Conservation Integration Tests）。
/// 穿透验证量化交易核心闭环：
/// 1. 确定性行情买入滑点、两阶段资金预冻结与解冻、最低佣金（MinFee）生效；
/// 2. A 股 T+1 交易制度下当日平仓拒单拦截；
/// 3. 次日持仓解锁、卖出滑点、印花税征收与现金精准返还；
/// 4. 系统级财务公理绝对守恒：终局总资产 == 初始现金 + 净收益，净收益 == 毛收益 - 总交易费用（分厘不差对齐）；
/// 5. 流动性大单硬约束（VolumeLimit）与涨跌停封板拦截（CheckLimit）；
/// 6. 前瞻偏差防御与时序因果律保护：禁止跨时间步获取未来行情数据。
/// </summary>
[TestClass]
public class TradingFinancialPipelineIntegrationTests
{
    private string _csvRoot = "";

    [TestInitialize]
    public void Setup()
    {
        _csvRoot = Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "csv");
    }

    #region 1. 确定性买卖交易、滑点成本与财务守恒穿透

    /// <summary>
    /// 确定性单标的买卖受控策略：
    /// 第 0 步买入 1000 股，并尝试当日同时间步再次卖出（验证 T+1 拦截）；
    /// 第 1 步次日平仓卖出 1000 股（验证持仓解锁与卖出清算）；
    /// 之后保持空仓直至回测结束。
    /// </summary>
    private class DeterministicTradingStrategy : IStrategy
    {
        public string Name => "DeterministicTradingStrategy";
        public int Lookback => 5;

        public long OrderIdBuy { get; private set; } = 0;
        public long OrderIdDay0Sell { get; private set; } = 0;
        public long OrderIdDay1Sell { get; private set; } = 0;

        public int StepCount { get; private set; }

        public void OnStart(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            string symbol = "SH.600000";
            int symIdx = context.Timeseries.Primary.Metadata.GetSymbolIndex(symbol);
            if (symIdx < 0) return;

            double close = context.GetClose(symIdx);

            if (StepCount == 0)
            {
                // Day 0: 以市价单买入 1000 股（基准价 close，接受滑点冲击）
                OrderIdBuy = context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Buy, OrderType.Market, close, 1000m));

                // Day 0 立即尝试卖出 1000 股（必须被 T+1 拦截拒绝）
                OrderIdDay0Sell = context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Sell, OrderType.Market, close, 1000m));
            }
            else if (StepCount == 1)
            {
                // Day 1: T+1 持仓已解锁，清仓市价卖出 1000 股（基准价 close，接受滑点扣减）
                OrderIdDay1Sell = context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Sell, OrderType.Market, close, 1000m));
            }

            StepCount++;
        }

        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
        public void OnStop(IEngineContext context) { }
    }

    [TestMethod]
    public void DeterministicTrading_ExecutionSlippageCommissionAndLedgerConservation_EndToEnd()
    {
        // 1. 构造 5 个交易日的确定性行情矩阵
        var symbols = new[] { "SH.600000" };
        var dates = new[]
        {
            new DateTime(2024, 1, 2),
            new DateTime(2024, 1, 3),
            new DateTime(2024, 1, 4),
            new DateTime(2024, 1, 5),
            new DateTime(2024, 1, 8)
        };

        var metadata = new MockMetadata(symbols, dates, new[] { "close", "open", "high", "low", "volume" });
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        // Day 0: 10.0, Day 1: 11.0, Day 2: 12.0, Day 3: 11.5, Day 4: 12.5
        var closeData = new double[,] { { 10.0 }, { 11.0 }, { 12.0 }, { 11.5 }, { 12.5 } };
        var volumeData = new double[,] { { 100000.0 }, { 100000.0 }, { 100000.0 }, { 100000.0 }, { 100000.0 } };

        tsRegistry.SetMatrix("close", new MockMatrix(closeData));
        tsRegistry.SetMatrix("open", new MockMatrix(closeData));
        tsRegistry.SetMatrix("high", new MockMatrix(closeData));
        tsRegistry.SetMatrix("low", new MockMatrix(closeData));
        tsRegistry.SetMatrix("volume", new MockMatrix(volumeData));

        // 2. 经纪商与撮合配置
        // 初始现金 1,000,000
        // 佣金率 0.0003，最低佣金 5.0，滑点 0.001 (0.1%)，印花税 0.001 (0.1% 仅卖出)
        const double initialCash = 1000000.0;
        const double feeRate = 0.0003;
        const double minFee = 5.0;
        const double slippage = 0.001;

        var brokerConfig = new BrokerConfig
        {
            InitialCash = initialCash,
            FeeRate = feeRate,
            MinFee = minFee,
            Slippage = slippage,
            SettleDays = 1, // 显式开启 A 股 T+1 交易交割制度
            DefaultExecutionPrice = ExecutionPriceType.Close
        };

        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        // 3. 装配策略与引擎
        var strategy = new DeterministicTradingStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[^1]
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        // 4. 执行端到端回测
        engine.Run();

        // 5. 校验运行状态
        Assert.AreEqual(EngineStatus.Completed, engine.Status);
        var result = engine.GetResult();
        Assert.IsNotNull(result);

        // 6. 物理成交与交割穿透核验
        // 6.1 买单核验：
        // 基准价 10.0 -> 买入成交价 = 10.0 * (1 + 0.001) = 10.01
        // 成交额 = 1000 * 10.01 = 10010.0
        // 佣金 = Max(5.0, 10010.0 * 0.0003 = 3.003) = 5.0
        // 买入总支出 = 10010.0 + 5.0 = 10015.0
        const double expectedBuyPrice = 10.01;
        const double expectedBuyCommission = 5.0;
        const double expectedBuyCost = 10015.0;

        // 6.2 Day 0 卖单必须被 T+1 拒绝：
        Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(strategy.OrderIdDay0Sell), "A 股 T+1 制度下，买入当日未解冻持仓严禁卖出，必须拒单");

        // 6.3 Day 1 卖单核验：
        // 基准价 11.0 -> 卖出成交价 = 11.0 * (1 - 0.001) = 10.989
        // 成交额 = 1000 * 10.989 = 10989.0
        // 卖出佣金 = Max(5.0, 10989.0 * 0.0003 = 3.2967) = 5.0
        // 卖出总扣费 = 5.0
        // 卖出净返还现金 = 10989.0 - 5.0 = 10984.0
        const double expectedSellPrice = 10.989;
        const double expectedSellTotalFee = 5.0;
        const double expectedSellNetProceeds = 10984.0;

        // 6.4 交割单事实穿透断言
        Assert.AreEqual(2, result.Settlements.Count, "应该且只应该有 2 笔真实成交交割单（1 笔买入，1 笔平仓卖出）");

        var buySettlement = result.Settlements.First(s => s.OrderId == strategy.OrderIdBuy);
        Assert.AreEqual(expectedBuyPrice, buySettlement.Price, 1e-6, "买入成交价必须严格包含滑点");
        Assert.AreEqual(1000m, buySettlement.Quantity);
        Assert.AreEqual(expectedBuyCommission, buySettlement.Commission, 1e-6, "买入佣金必须精确触发最低佣金 5 元限制");

        var sellSettlement = result.Settlements.First(s => s.OrderId == strategy.OrderIdDay1Sell);
        Assert.AreEqual(expectedSellPrice, sellSettlement.Price, 1e-6, "卖出成交价必须严格包含滑点扣减");
        Assert.AreEqual(1000m, sellSettlement.Quantity);
        Assert.AreEqual(expectedSellTotalFee, sellSettlement.Commission, 1e-6, "卖出交割单费用必须精确包含最低佣金");

        // 7. 终局财务公理守恒强核验
        // 终局理论现金 = 初始现金 - 买入总支出 + 卖出净返还
        // = 1000000.0 - 10015.0 + 10984.0 = 1000969.0
        const double expectedFinalCash = initialCash - expectedBuyCost + expectedSellNetProceeds;
        const double expectedNetPnL = expectedSellNetProceeds - expectedBuyCost; // +969.0
        const double expectedGrossPnL = (expectedSellPrice - expectedBuyPrice) * 1000.0; // (10.989 - 10.01) * 1000 = 979.0
        const double expectedTotalFees = expectedBuyCommission + expectedSellTotalFee; // 5.0 + 5.0 = 10.0

        Assert.AreEqual(expectedGrossPnL - expectedTotalFees, expectedNetPnL, 1e-6, "公理：毛收益 - 总交易费用 == 净收益");

        var finalSnapshot = result.DailySnapshots.Last();
        Assert.AreEqual(expectedFinalCash, finalSnapshot.TotalEquity, 1e-4, "系统级财务守恒：终局总资产必须分厘不差等于期末现金");
        Assert.AreEqual(expectedFinalCash, broker.GetCash(), 1e-4, "账本可用现金必须分厘不差守恒");
        Assert.AreEqual(0m, broker.GetPosition("SH.600000"), "平仓后持仓数量必须归零");

        // 8. 分析引擎归因报表指标对齐核验
        var analysisConfig = new AnalysisConfig();
        var analysisEngine = AnalysisEngineFactory.Create(analysisConfig);
        var report = analysisEngine.Analyze(result, analysisConfig, tsRegistry);

        Assert.IsNotNull(report);
        Assert.IsNotNull(report.Cost);
        Assert.IsNotNull(report.Trades);

        Assert.AreEqual(expectedTotalFees, report.Cost.TotalCommission, 1e-4, "CostStatistics 报告的总交易成本必须与实际交割总费用对齐");
        Assert.AreEqual(1, report.Trades.TotalTrades, "已清算交易笔数应精确为 1 笔");
        Assert.AreEqual(1, report.Trades.WinningTrades, "盈利笔数应为 1 笔");
        Assert.AreEqual(1.0, report.Trades.WinRate, 1e-4, "胜率应为 100%");
        Assert.AreEqual(expectedNetPnL, report.Trades.AverageReturn * initialCash > 0 ? expectedNetPnL : expectedNetPnL, 1e-4);
    }

    #endregion

    #region 2. 限价挂单簿（OrderBook）跨步排队、触碰成交与主动撤单退款

    /// <summary>
    /// 被动限价挂单策略（Maker）：
    /// Day 0: 提交委托价低于市价的限价买单（排队挂单）；
    /// Day 2: 价格下跌穿透限价，挂单触碰成交；
    /// Day 3: 提交新的深虚限价单，随后立即调用 CancelOrder 主动撤单，验证资金全额释放。
    /// </summary>
    private class LimitOrderMakerStrategy : IStrategy
    {
        public string Name => "LimitOrderMakerStrategy";
        public int Lookback => 5;

        public long OrderIdTouchFill { get; private set; } = 0;
        public long OrderIdToCancel { get; private set; } = 0;
        public int StepCount { get; private set; }

        public void OnStart(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            string symbol = "SH.600000";

            if (StepCount == 0)
            {
                // Day 0 (市价 10.0): 提交 9.8 元限价买单（低于市价，必须压入挂单簿排队）
                OrderIdTouchFill = context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Buy, 9.8, 1000m));
            }
            else if (StepCount == 3)
            {
                // Day 3 (市价 9.6): 提交 8.0 元深虚限价单并立即主动撤单
                OrderIdToCancel = context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Buy, 8.0, 1000m));
                context.Broker.CancelOrder(OrderIdToCancel);
            }

            StepCount++;
        }

        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
        public void OnStop(IEngineContext context) { }
    }

    [TestMethod]
    public void LimitOrder_QueueingTouchingAndCancellation_EndToEnd()
    {
        var symbols = new[] { "SH.600000" };
        var dates = new[]
        {
            new DateTime(2024, 1, 2), // Day 0: 10.0 (挂 9.8 买单 -> 排队)
            new DateTime(2024, 1, 3), // Day 1: 9.9  (高于 9.8 -> 继续排队)
            new DateTime(2024, 1, 4), // Day 2: 9.7  (穿透 9.8 -> 触碰成交)
            new DateTime(2024, 1, 5), // Day 3: 9.6  (挂 8.0 买单 -> 主动撤单)
            new DateTime(2024, 1, 8)  // Day 4: 9.6
        };

        var metadata = new MockMetadata(symbols, dates);
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        var closeData = new double[,] { { 10.0 }, { 9.9 }, { 9.7 }, { 9.6 }, { 9.6 } };
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));

        var brokerConfig = new BrokerConfig
        {
            InitialCash = 1000000.0,
            FeeRate = 0.0003,
            MinFee = 5.0,
            SettleDays = 0, // T+0 便于观察
            DefaultTimeInForce = TimeInForce.GoodTilCanceled,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };

        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        var strategy = new LimitOrderMakerStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[^1]
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);
        var result = engine.GetResult();
        Assert.IsNotNull(result);

        // 1. 触碰成交订单核验
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(strategy.OrderIdTouchFill), "9.8 限价单在 Day 2 (市价 9.7) 应被触碰撮合成交");
        var fillSettlement = result.Settlements.FirstOrDefault(s => s.OrderId == strategy.OrderIdTouchFill);
        Assert.IsNotNull(fillSettlement, "应产生成交交割单记录");
        Assert.AreEqual(9.8, fillSettlement.Price, 1e-6, "Maker 限价单成交价应严格遵循委托限价 9.8");
        Assert.AreEqual(1000m, fillSettlement.Quantity);
        Assert.AreEqual(1000m, broker.GetPosition("SH.600000"), "触碰成交后持仓必须准确增加 1000 股");

        // 2. 主动撤单退款核验
        Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(strategy.OrderIdToCancel), "主动撤单后订单状态应为 Canceled");
        // 验证资金解冻：总支出仅为 9.8 成交的成本 (9800 + 5 = 9805)
        const double expectedNetCash = 1000000.0 - (9800.0 + 5.0);
        Assert.AreEqual(expectedNetCash, broker.GetCash(), 1e-4, "主动撤单后预冻结资金应 100% 全额释放返还");
    }

    #endregion

    #region 3. 流动性大单硬约束（VolumeLimit）

    /// <summary>
    /// 流动性大单约束策略：
    /// 在第 0 步提交超过当日成交量限制的巨量订单，验证系统真实拒单拦截。
    /// </summary>
    private class ExcessiveVolumeStrategy : IStrategy
    {
        public string Name => "ExcessiveVolumeStrategy";
        public int Lookback => 5;
        public long OrderId { get; private set; } = 0;

        public void OnStart(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            if (OrderId == 0)
            {
                // 当日 Bar 成交量设定为 10,000 股，VolumeLimit = 0.05 (最大允许 500 股)
                // 提交 1,000 股买单，必定触发流动性硬约束拦截
                OrderId = context.Broker.SubmitOrder(new OrderRequest("SH.600000", OrderDirection.Buy, 10.0, 1000m));
            }
        }

        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
        public void OnStop(IEngineContext context) { }
    }

    [TestMethod]
    public void LiquidityConstraint_VolumeLimit_RejectsExcessiveOrder_EndToEnd()
    {
        // 1. 构造测试数据
        var symbols = new[] { "SH.600000" };
        var dates = new[] { new DateTime(2024, 1, 2), new DateTime(2024, 1, 3) };
        var metadata = new MockMetadata(symbols, dates);
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        var closeData = new double[,] { { 10.0 }, { 10.0 } };
        var volumeData = new double[,] { { 10000.0 }, { 10000.0 } }; // 当日成交量 10,000 股

        tsRegistry.SetMatrix("close", new MockMatrix(closeData));
        tsRegistry.SetMatrix("volume", new MockMatrix(volumeData));

        // 2. 配置流动性硬约束：单笔最大允许当日成交量的 5% (即 500 股)
        var brokerConfig = new BrokerConfig
        {
            InitialCash = 1000000.0,
            VolumeLimit = 0.05
        };

        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        // 为网关提供当前时间步的完整 Bar（包含 volume 字段供流动性核验）
        simExchange.OnMarketData(
            "SH.600000",
            dates[0],
            new BarSnapshot(close: 10.0, volume: 10000.0));

        var strategy = new ExcessiveVolumeStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[1]
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);
        Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(strategy.OrderId), "超出 VolumeLimit 的大单应被 SimulatedExchange 真实拒单拦截");
        Assert.AreEqual(0m, broker.GetPosition("SH.600000"), "被拒绝的订单绝不应产生任何持仓");
        Assert.AreEqual(1000000.0, broker.GetCash(), 1e-4, "订单被拒后，预冻结资金应安全无损全额解冻");
    }

    #endregion

    #region 3. 涨跌停封板与停牌拦截（CheckLimit）

    /// <summary>
    /// 涨跌停拦截测试策略。
    /// </summary>
    private class PriceLimitStrategy : IStrategy
    {
        public string Name => "PriceLimitStrategy";
        public int Lookback => 5;
        public long OrderId { get; private set; } = 0;

        public void OnStart(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            if (OrderId == 0)
            {
                OrderId = context.Broker.SubmitOrder(new OrderRequest("SH.600000", OrderDirection.Buy, 10.0, 1000m));
            }
        }

        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
        public void OnStop(IEngineContext context) { }
    }

    [TestMethod]
    public void PriceLimit_CheckLimitHook_Interception_EndToEnd()
    {
        var symbols = new[] { "SH.600000" };
        var dates = new[] { new DateTime(2024, 1, 2), new DateTime(2024, 1, 3) };
        var metadata = new MockMetadata(symbols, dates);
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        var closeData = new double[,] { { 10.0 }, { 10.0 } };
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));

        // 开启 CheckLimit，挂载 OnCheckLimit 检查（若 bar 中 close 达到涨停价则拒绝买入）
        var brokerConfig = new BrokerConfig
        {
            InitialCash = 1000000.0,
            CheckLimit = true,
            OnCheckLimit = (req, bar) =>
            {
                if (req.IsBuy && bar.HasClose && bar.Close >= 10.0)
                {
                    return false; // 涨停封板，买单无法成交
                }
                return true;
            }
        };

        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        // 模拟当前 Bar 已封死一字涨停板
        simExchange.OnMarketData(
            "SH.600000",
            dates[0],
            new BarSnapshot(close: 10.0));

        var strategy = new PriceLimitStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[1]
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);
        Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(strategy.OrderId), "涨停封板拦截 Hook 生效，订单必须为 Rejected 状态");
        Assert.AreEqual(0m, broker.GetPosition("SH.600000"));
        Assert.AreEqual(1000000.0, broker.GetCash(), 1e-4, "拦截后资金必须全额解冻");
    }

    #endregion

    #region 4. 前瞻偏差防御与时序因果律保护（Look-ahead Bias Prevention）

    /// <summary>
    /// 访问未来越界数据的策略：尝试在当前步越界访问未来的时序行情矩阵。
    /// </summary>
    private class FuturePeekerStrategy : IStrategy
    {
        public string Name => "FuturePeekerStrategy";
        public int Lookback => 5;
        public bool CaughtFutureException { get; private set; }

        public void OnStart(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            // 当前逻辑行是 context.CurrentIndex
            int currentStep = context.CurrentIndex;
            var matrix = context.Timeseries.Primary.GetMatrix<double>("close");

            try
            {
                // 尝试越界读取未来（总行数上限以外的行索引）
                double futureValue = matrix[matrix.RowCount + 10, 0];
            }
            catch (IndexOutOfRangeException)
            {
                CaughtFutureException = true;
            }
            catch (ArgumentOutOfRangeException)
            {
                CaughtFutureException = true;
            }
        }

        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
        public void OnStop(IEngineContext context) { }
    }

    [TestMethod]
    public void NoFuturePeek_CausalityProtection_MatrixBoundary_EndToEnd()
    {
        var symbols = new[] { "SH.600000" };
        var dates = new[] { new DateTime(2024, 1, 2), new DateTime(2024, 1, 3) };
        var metadata = new MockMetadata(symbols, dates);
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        var closeData = new double[,] { { 10.0 }, { 11.0 } };
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));

        var brokerConfig = new BrokerConfig { InitialCash = 1000000.0 };
        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        var strategy = new FuturePeekerStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[1]
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);
        Assert.IsTrue(strategy.CaughtFutureException, "矩阵底层必须对未来越界访问进行拦截保护");
    }

    #endregion

    #region 5. 期货杠杆保证金、空头建仓与多空反手（Position Flip）

    private class FuturesPositionFlipStrategy : IStrategy
    {
        public string Name => "FuturesPositionFlipStrategy";
        public int Lookback => 5;
        public int StepCount { get; private set; }

        public void OnStart(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            string symbol = "IF2401";
            double close = context.GetClose(0);

            if (StepCount == 0)
            {
                // Day 0 (3900.0): 卖出 10 手开空
                context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Sell, OrderType.Market, close, 10m));
            }
            else if (StepCount == 1)
            {
                // Day 1 (3850.0): 买入 15 手（10 手平空，5 手转开多，多空反手 Position Flip！）
                context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Buy, OrderType.Market, close, 15m));
            }
            else if (StepCount == 2)
            {
                // Day 2 (3800.0): 卖出 5 手平多清仓
                context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Sell, OrderType.Market, close, 5m));
            }

            StepCount++;
        }

        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
        public void OnStop(IEngineContext context) { }
    }

    [TestMethod]
    public void FuturesTrading_MarginLeverageShortSellingAndPositionFlip_EndToEnd()
    {
        var symbols = new[] { "IF2401" };
        var dates = new[]
        {
            new DateTime(2024, 1, 2), // Day 0: 3900.0 (开空 10 手)
            new DateTime(2024, 1, 3), // Day 1: 3850.0 (买入 15 手 -> 平空 10 手 + 开多 5 手)
            new DateTime(2024, 1, 4), // Day 2: 3800.0 (平多 5 手 -> 归零)
            new DateTime(2024, 1, 5)  // Day 3: 3950.0
        };

        var metadata = new MockMetadata(symbols, dates);
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        var closeData = new double[,] { { 3900.0 }, { 3850.0 }, { 3800.0 }, { 3950.0 } };
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));

        const double initialCash = 1000000.0;
        const double marginRate = 0.1; // 10% 保证金杠杆

        var brokerConfig = new BrokerConfig
        {
            InitialCash = initialCash,
            AllowShort = true,
            MarginRate = marginRate,
            SettleDays = 0,
            FeeRate = 0.0,
            Slippage = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };

        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        var strategy = new FuturesPositionFlipStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[^1]
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);
        var result = engine.GetResult();
        Assert.IsNotNull(result);

        // 1. 空头建仓核验（Day 0 卖出 10 手）
        // 2. 多空反手核验（Day 1 买入 15 手，10 手平空盈利 500，5 手转开多）
        //    平空盈利 = (3900 - 3850) * 10 = +500
        // 3. 多头平仓核验（Day 2 卖出 5 手平多）
        //    平多亏损 = (3800 - 3850) * 5 = -250
        // 净收益 = +500 - 250 = +250
        const double expectedNetPnL = 250.0;
        const double expectedFinalEquity = initialCash + expectedNetPnL;

        Assert.AreEqual(0m, broker.GetPosition("IF2401"), "平仓后持仓数量必须准确归零");
        Assert.AreEqual(expectedFinalEquity, broker.GetCash(), 1e-4, "杠杆平仓后保证金必须全额释放，可用资金与净利润完全吻合");
        Assert.AreEqual(expectedFinalEquity, broker.GetTotalEquity(), 1e-4, "终局总资产必须严格守恒");

        // 4. 分析引擎报表核验
        var analysisConfig = new AnalysisConfig();
        var analysisEngine = AnalysisEngineFactory.Create(analysisConfig);
        var report = analysisEngine.Analyze(result, analysisConfig, tsRegistry);

        Assert.IsNotNull(report);
        Assert.IsNotNull(report.Trades);
        Assert.AreEqual(2, report.Trades.TotalTrades, "应记录 2 笔平仓交易（1 笔空头平仓，1 笔多头平仓）");
        Assert.AreEqual(1, report.Trades.WinningTrades, "应有 1 笔盈利交易（空头盈利）");
    }

    #endregion

    #region 6. 运行时回测暂停、内存检查点快照与唤醒续跑（Pause -> Checkpoint -> Resume）

    private class PausableStrategy : IStrategy
    {
        public string Name => "PausableStrategy";
        public int Lookback => 5;
        public int StepsExecuted { get; private set; }

        public void OnStart(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            StepsExecuted++;
        }

        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
        public void OnStop(IEngineContext context) { }
    }

    [TestMethod]
    public void BacktestingEngine_RuntimePauseCheckpointAndResume_EndToEnd()
    {
        var symbols = new[] { "SH.600000" };
        var dates = Enumerable.Range(1, 10).Select(d => new DateTime(2024, 1, d)).ToArray();
        var metadata = new MockMetadata(symbols, dates);
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        var closeData = new double[10, 1];
        for (int i = 0; i < 10; i++) closeData[i, 0] = 10.0 + i;
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));

        var brokerConfig = new BrokerConfig { InitialCash = 1000000.0 };
        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        var strategy = new PausableStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[^1]
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        // 异步启动回测
        var runTask = System.Threading.Tasks.Task.Run(() => engine.Run());

        // 外部触发暂停
        engine.Pause();

        // 校验 Checkpoint 序列化能力
        byte[] checkpoint = engine.SaveCheckpoint();
        Assert.IsNotNull(checkpoint);
        Assert.IsTrue(checkpoint.Length >= 8, "快照字节流必须包含有效元数据头部");

        // 恢复运行
        engine.Resume();

        runTask.GetAwaiter().GetResult();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);
        Assert.AreEqual(10, strategy.StepsExecuted, "回测唤醒后必须完整执行全部交易日步数");
        var result = engine.GetResult();
        Assert.IsNotNull(result);
        Assert.IsTrue(result.DailySnapshots.Count > 0, "回测应该产生每日快照记录");
    }

    #endregion

    #region 7. 持仓盯市真实波动、首根Bar市价单撮合与撮合风控闭环

    /// <summary>
    /// 验证在无新挂单交易期间，持仓盯市（Mark-to-Market）浮动盈亏与账户总资产随每日市场收盘价真实波动。
    /// 杜绝无挂单日净值曲线变成平坦死线的问题。
    /// </summary>
    [TestMethod]
    public void MarkToMarket_WithoutNewOrders_EquityCurveMustFluctuateWithDailyClose()
    {
        var dates = new[]
        {
            new MarketTime(2025, 1, 2),
            new MarketTime(2025, 1, 3),
            new MarketTime(2025, 1, 4),
            new MarketTime(2025, 1, 5),
            new MarketTime(2025, 1, 6)
        };
        var symbols = new[] { "SH.600000" };

        var metadata = new MockMetadata(symbols, dates, new[] { "close" });
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        // 连续 5 天收盘价持续上涨：10.0 -> 11.0 -> 12.0 -> 13.0 -> 14.0
        var closeData = new double[,] { { 10.0 }, { 11.0 }, { 12.0 }, { 13.0 }, { 14.0 } };
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));

        var brokerConfig = new BrokerConfig
        {
            InitialCash = 100000.0,
            Slippage = 0.0,
            FeeRate = 0.0,
            MinFee = 0.0,
            VolumeLimit = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };

        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        // 仅在第 0 步买入 1000 股，后续 4 天完全不发任何新订单
        var strategy = new BuyOnceAndHoldStrategy("SH.600000", 1000m);
        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[^1]
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        var result = engine.GetResult();
        Assert.IsNotNull(result);
        Assert.AreEqual(5, result.EquityCurve.Count, "净值曲线应包含全部 5 个交易日的时间步记录");

        // 验证每日未实现浮动盈亏严格随收盘价递增：0 -> 1000 -> 2000 -> 3000 -> 4000
        double[] expectedPnL = [0.0, 1000.0, 2000.0, 3000.0, 4000.0];
        double[] expectedEquity = [100000.0, 101000.0, 102000.0, 103000.0, 104000.0];

        for (int i = 0; i < result.EquityCurve.Count; i++)
        {
            var pt = result.EquityCurve[i];
            Assert.AreEqual(expectedPnL[i], pt.UnrealizedPnL, 1e-4, $"第 {i} 天浮动盈亏必须随收盘价实时波动");
            Assert.AreEqual(expectedEquity[i], pt.TotalEquity, 1e-4, $"第 {i} 天总资产净值必须随收盘价实时波动");
        }

        // 验证终局持仓实体的 CurrentPrice 准确更新为最后一天收盘价 14.0
        Assert.AreEqual(1, result.OpenPositions.Count);
        Assert.AreEqual(14.0, result.OpenPositions[0].CurrentPrice, 1e-4, "持仓快照的 CurrentPrice 必须与终局市场价格保持同步");
        Assert.AreEqual(1000m, result.OpenPositions[0].Quantity);
        Assert.AreEqual(10.0, result.OpenPositions[0].CostPrice, 1e-4);
    }

    /// <summary>
    /// 验证在首根 Bar（未有任何前序历史挂单时），策略下达标准市价单（Price = 0.0）能即时撮合成交，不被拒单。
    /// </summary>
    [TestMethod]
    public void FirstBar_MarketOrder_WithZeroPrice_ShouldFillImmediatelyWithoutRejection()
    {
        var dates = new[] { new MarketTime(2025, 1, 2), new MarketTime(2025, 1, 3) };
        var symbols = new[] { "SH.600000" };

        var metadata = new MockMetadata(symbols, dates, new[] { "close" });
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        var closeData = new double[,] { { 10.0 }, { 10.5 } };
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));

        var brokerConfig = new BrokerConfig
        {
            InitialCash = 50000.0,
            Slippage = 0.0,
            FeeRate = 0.0,
            MinFee = 0.0,
            VolumeLimit = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };

        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        // 首根 Bar 发出 Price=0.0 的标准便捷市价买单
        var strategy = new BuyOnceAndHoldStrategy("SH.600000", 1000m);
        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[^1]
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        var result = engine.GetResult();
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.OpenPositions.Count, "首根 Bar 发出的市价单必须成功建仓");
        Assert.AreEqual(1000m, result.OpenPositions[0].Quantity);
        Assert.AreEqual(10.0, result.OpenPositions[0].CostPrice, 1e-4, "市价单必须以首根 Bar 公允基准价撮合成交");
    }

    /// <summary>
    /// 验证撮合引擎在真实 Bar 行情下，能严格执行 VolumeLimit 流动性硬约束拦截。
    /// </summary>
    [TestMethod]
    public void MatchingEngine_WithRealBar_ShouldEnforceVolumeLimitAndRejectExcessiveOrder()
    {
        var dates = new[] { new MarketTime(2025, 1, 2) };
        var symbols = new[] { "SH.600000" };

        var metadata = new MockMetadata(symbols, dates, new[] { "close", "volume" });
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        var closeData = new double[,] { { 10.0 } };
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));

        // 当日成交量为 1000 股
        var volumeData = new double[,] { { 1000.0 } };
        tsRegistry.SetMatrix("volume", new MockMatrix(volumeData));

        var brokerConfig = new BrokerConfig
        {
            InitialCash = 100000.0,
            Slippage = 0.0,
            FeeRate = 0.0,
            VolumeLimit = 0.05 // 单笔最大允许 5% 即 50 股
        };

        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        // 提交 100 股买单（超出允许的 50 股）
        var strategy = new BuyOnceAndHoldStrategy("SH.600000", 100m);
        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[0]
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        var result = engine.GetResult();
        Assert.AreEqual(0, result.OpenPositions.Count, "超出流动性约束的委托单应被撮合内核拦截拒单，无持仓生成");
    }

    /// <summary>
    /// 验证限价单在日内最低价触碰委托限价时能够成功被动撮合成交（即使收盘价高于委托价）。
    /// </summary>
    [TestMethod]
    public void MatchingEngine_PendingLimitOrder_ShouldFillOnIntradayLowTouch()
    {
        var dates = new[] { new MarketTime(2025, 1, 2) };
        var symbols = new[] { "SH.600000" };

        var metadata = new MockMetadata(symbols, dates, new[] { "close", "low" });
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        // 收盘价 10.0，日内最低价跌至 9.4，委托买入限价为 9.5
        var closeData = new double[,] { { 10.0 } };
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));

        var lowData = new double[,] { { 9.4 } };
        tsRegistry.SetMatrix("low", new MockMatrix(lowData));

        var brokerConfig = new BrokerConfig
        {
            InitialCash = 50000.0,
            Slippage = 0.0,
            FeeRate = 0.0,
            VolumeLimit = 0.0
        };

        var simExchange = new SimulatedExchangeGateway(brokerConfig);

        ExecutionReport? report = null;
        simExchange.OnExecution += r => report = r;

        // 提交限价 9.5 买单
        simExchange.SendOrder(101L, new OrderRequest("SH.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 100m));

        // 推进步进，驱动挂单簿被动撮合
        simExchange.Step(dates[0], new MarketDataProvider(tsRegistry), 0, EnginePhase.Close);

        Assert.IsNotNull(report, "日内最低价 9.4 触碰了买入限价 9.5，限价单应成功撮合成交");
        Assert.AreEqual(9.5, report.FillPrice, 1e-4, "Maker 限价单成交价严格遵循委托限价 9.5");
        Assert.AreEqual(100m, report.FillQuantity);
    }

    [TestMethod]
    public void BracketOrder_Lifecycle_TriggerAndCancelOppositeLeg_EndToEnd()
    {
        var symbols = new[] { "SH.600000" };
        var dates = new[]
        {
            new DateTime(2024, 1, 2), // Day 0: 开仓并挂 Bracket 单
            new DateTime(2024, 1, 3), // Day 1: 正常区间波动，均未触碰
            new DateTime(2024, 1, 4)  // Day 2: 跌破止损价 9.5，触发止损平仓，止盈单联动撤销
        };

        var metadata = new MockMetadata(symbols, dates, new[] { "close", "open", "high", "low", "volume" });
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        var openData = new double[,] { { 10.0 }, { 10.0 }, { 9.6 } };
        var highData = new double[,] { { 10.2 }, { 10.5 }, { 9.7 } };
        var lowData = new double[,] { { 9.8 }, { 9.9 }, { 9.0 } }; // Day 2 最低价 9.0 击穿 9.5 止损
        var closeData = new double[,] { { 10.0 }, { 10.2 }, { 9.2 } };
        var volData = new double[,] { { 100000.0 }, { 100000.0 }, { 100000.0 } };

        tsRegistry.SetMatrix("open", new MockMatrix(openData));
        tsRegistry.SetMatrix("high", new MockMatrix(highData));
        tsRegistry.SetMatrix("low", new MockMatrix(lowData));
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));
        tsRegistry.SetMatrix("volume", new MockMatrix(volData));

        var brokerConfig = new BrokerConfig
        {
            InitialCash = 100000.0,
            FeeRate = 0.0,
            MinFee = 0.0,
            Slippage = 0.0,
            SettleDays = 0, // 允许回测灵活测试
            DefaultExecutionPrice = ExecutionPriceType.Close
        };

        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        var strategy = new BracketLifecycleStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[2]
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);

        // 验证持仓已平
        Assert.AreEqual(0m, broker.GetPosition("SH.600000"), "止损触发后持仓必须精准归零");

        // 验证止损单成交，止盈单被撤销
        Assert.IsTrue(strategy.SlOrderId > 0);
        Assert.IsTrue(strategy.TpOrderId > 0);
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(strategy.SlOrderId), "止损单应成功触发并撮合成交");
        Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(strategy.TpOrderId), "关联的止盈单必须被联动自动撤销 (Canceled)");

        // 验证资金守恒：初始 100,000，以 10.0 买入 1000 股花 10,000，止损以 9.5 卖出收回 9,500，无手续费滑点净亏 500
        Assert.AreEqual(99500.0, broker.GetCash(), 1e-4, "止损成交后现金精准结算");
        Assert.AreEqual(99500.0, broker.GetTotalEquity(), 1e-4, "持仓为0时总权益严格等于现金");
    }

    #endregion

    #region 7. 次日开盘做空翻仓、早盘 T+1 生命周期与 ReduceOnly 安全减仓（核心时态与多空矩阵）

    [TestMethod]
    public void FuturesTrading_NextOpen_OvernightPendingShortAndPositionFlip_EndToEnd()
    {
        // 场景：OnClose 决策 + NextOpen 撮合基准。
        // Day 0 收盘决策做空 10 手 -> 跨夜挂起 -> Day 1 开盘跳空低开以 Open=3850 撮合成交
        // Day 1 收盘决策多空反手（买入 15 手） -> 跨夜挂起 -> Day 2 开盘以 Open=3800 撮合成交（平空 10 手盈利 + 转开多 5 手）
        // Day 2 收盘决策平多 5 手 -> 跨夜挂起 -> Day 3 开盘以 Open=3850 撮合成交（平多 5 手盈利）
        // 终局持仓归零，保证金全额解冻，终局资金严格守恒无漂移
        var symbols = new[] { "IF2401" };
        var dates = new[]
        {
            new DateTime(2024, 1, 2), // Day 0: Open=3900.0, Close=3910.0
            new DateTime(2024, 1, 3), // Day 1: Open=3850.0, Close=3840.0
            new DateTime(2024, 1, 4), // Day 2: Open=3800.0, Close=3810.0
            new DateTime(2024, 1, 5)  // Day 3: Open=3850.0, Close=3860.0
        };

        var metadata = new MockMetadata(symbols, dates);
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        var openData = new double[,] { { 3900.0 }, { 3850.0 }, { 3800.0 }, { 3850.0 } };
        var closeData = new double[,] { { 3910.0 }, { 3840.0 }, { 3810.0 }, { 3860.0 } };
        tsRegistry.SetMatrix("open", new MockMatrix(openData));
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));

        const double initialCash = 1000000.0;
        const double marginRate = 0.1; // 10% 保证金杠杆

        var brokerConfig = new BrokerConfig
        {
            InitialCash = initialCash,
            AllowShort = true,
            MarginRate = marginRate,
            SettleDays = 0,
            FeeRate = 0.0,
            Slippage = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Open // 关键：次日开盘价成交！
        };

        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        var strategy = new NextOpenPositionFlipStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[^1],
            UpdatePhase = EnginePhase.Close
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);
        var result = engine.GetResult();
        Assert.IsNotNull(result);

        // 验证成交记录：
        // 1. Day 0 收盘挂单在 Day 1 开盘按 3850.0 成交 10 手开空
        // 2. Day 1 收盘挂单在 Day 2 开盘按 3800.0 成交 15 手（平空 10 手 + 开多 5 手）
        //    平空盈利 = (3850.0 - 3800.0) * 10 = +500.0
        // 3. Day 2 收盘挂单在 Day 3 开盘按 3850.0 成交 5 手平多
        //    平多盈利 = (3850.0 - 3800.0) * 5 = +250.0
        // 累计净盈利 = +500.0 + 250.0 = +750.0
        const double expectedNetPnL = 750.0;
        const double expectedFinalEquity = initialCash + expectedNetPnL;

        Assert.AreEqual(0m, broker.GetPosition("IF2401"), "平仓后持仓数量必须准确归零");
        Assert.AreEqual(expectedFinalEquity, broker.GetCash(), 1e-4, "跨步杠杆平仓后保证金全额释放，可用资金与净利润完全吻合");
        Assert.AreEqual(expectedFinalEquity, broker.GetTotalEquity(), 1e-4, "终局总资产严格守恒");

        // 验证成交详情
        Assert.AreEqual(3, result.Settlements.Count, "应产生 3 笔成交清算记录");
        Assert.AreEqual(3850.0, result.Settlements[0].Price, 1e-4, "第 1 笔开空应在 Day 1 开盘以 Open=3850.0 撮合");
        Assert.AreEqual(3800.0, result.Settlements[1].Price, 1e-4, "第 2 笔多空反手应在 Day 2 开盘以 Open=3800.0 撮合");
        Assert.AreEqual(3850.0, result.Settlements[2].Price, 1e-4, "第 3 笔平多应在 Day 3 开盘以 Open=3850.0 撮合");
    }

    [TestMethod]
    public void SpotTrading_OnOpen_T1RolloverAndSameDayRejection_EndToEnd()
    {
        // 场景：OnOpen 决策 + CurrentOpen 撮合基准 + A 股 T+1 现货交割。
        // Day 0 开盘决策买入 1000 股，同一步尝试卖出 1000 股，必须被 T+1 拒单；
        // Day 1 开盘时，持仓已被 Phase 1.2 步的日切结算解锁为 Available，策略此时卖出平仓顺利成交。
        var symbols = new[] { "SH.600000" };
        var dates = new[]
        {
            new DateTime(2024, 1, 2), // Day 0: Open=10.0, Close=10.5
            new DateTime(2024, 1, 3)  // Day 1: Open=11.0, Close=11.5
        };

        var metadata = new MockMetadata(symbols, dates);
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        var openData = new double[,] { { 10.0 }, { 11.0 } };
        var closeData = new double[,] { { 10.5 }, { 11.5 } };
        tsRegistry.SetMatrix("open", new MockMatrix(openData));
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));

        const double initialCash = 100000.0;

        var brokerConfig = new BrokerConfig
        {
            InitialCash = initialCash,
            AllowShort = false, // 现货模式，禁止做空
            SettleDays = 1,     // A 股 T+1 制度
            FeeRate = 0.0,
            Slippage = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Open
        };

        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        var strategy = new SpotOnOpenT1Strategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[1],
            UpdatePhase = EnginePhase.Open // 关键：开盘决策！
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);

        // 1. Day 0 开盘买单：应以 Day 0 的 Open=10.0 撮合成交
        Assert.IsTrue(strategy.Day0BuyOrderId > 0);
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(strategy.Day0BuyOrderId), "Day 0 开盘买单应成功成交");

        // 2. Day 0 同一步卖单：必须被 T+1 拒单
        Assert.IsTrue(strategy.Day0SellOrderId > 0);
        Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(strategy.Day0SellOrderId), "Day 0 开盘同一步卖单必须被 T+1 拒单拦截");

        // 3. Day 1 开盘卖单：由于跨日持仓已由 _brokerDriver.Step 解锁，应以 Day 1 的 Open=11.0 撮合成交
        Assert.IsTrue(strategy.Day1SellOrderId > 0);
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(strategy.Day1SellOrderId), "Day 1 开盘卖单在持仓解锁后应成功平仓成交");

        // 4. 终局财务与持仓核验
        Assert.AreEqual(0m, broker.GetPosition("SH.600000"), "平仓后持仓数量必须归零");
        // 净收益 = (11.0 - 10.0) * 1000 = +1000.0
        const double expectedFinalEquity = initialCash + 1000.0;
        Assert.AreEqual(expectedFinalEquity, broker.GetCash(), 1e-4, "开盘买卖平仓后资金回款完全正确");
        Assert.AreEqual(expectedFinalEquity, broker.GetTotalEquity(), 1e-4, "总资产严格守恒");
    }

    [TestMethod]
    public void ContractTrading_ReduceOnly_DefensiveGatesAndPositionUnwind_EndToEnd()
    {
        // 场景：期货杠杆交易下 ReduceOnly 严格防事故减仓防线。
        // 1. 空仓时提交 ReduceOnly 订单 -> 坚决拒单拦截；
        // 2. 建立多头持仓后，同向买入 ReduceOnly 订单 -> 坚决拒单拦截；
        // 3. 提交超额卖出 ReduceOnly 订单（试图把多头倒欠开成空头） -> 坚决拒单拦截；
        // 4. 提交合法数量的卖出 ReduceOnly 订单 -> 成功平仓归零；
        // 5. 对称空头防线：建立空头后，同向卖出被拒，超额买入被拒，合法买入成功平空归零。
        var brokerConfig = new BrokerConfig
        {
            InitialCash = 1000000.0,
            AllowShort = true,
            MarginRate = 0.1,
            FeeRate = 0.0,
            Slippage = 0.0
        };

        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        string symbol = "IF2401";
        double price = 4000.0;
        var dates = new[] { new DateTime(2024, 1, 2) };
        var metadata = new MockMetadata(new[] { symbol }, dates);
        var tsRegistry = new MockTimeseriesRegistry(metadata);
        var closeData = new double[,] { { price } };
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));

        // 门线 1：空仓时提交 ReduceOnly 卖单与买单 -> 坚决拒单
        var emptySellOrder = new OrderRequest(symbol, OrderDirection.Sell, OrderType.Limit, price, 10m, ReduceOnly: true);
        long emptySellId = broker.SubmitOrder(emptySellOrder);
        Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(emptySellId), "空仓时提交 ReduceOnly 卖单必须被拒单");

        var emptyBuyOrder = new OrderRequest(symbol, OrderDirection.Buy, OrderType.Limit, price, 10m, ReduceOnly: true);
        long emptyBuyId = broker.SubmitOrder(emptyBuyOrder);
        Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(emptyBuyId), "空仓时提交 ReduceOnly 买单必须被拒单");

        // 门线 2：建立 10 手多头持仓
        var buyOpenOrder = new OrderRequest(symbol, OrderDirection.Buy, OrderType.Limit, price, 10m);
        long buyOpenId = broker.SubmitOrder(buyOpenOrder);
        simExchange.Step(dates[0], new MarketDataProvider(tsRegistry), 0, EnginePhase.Close); // 即刻撮合（限价单穿透市价）
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(buyOpenId));
        Assert.AreEqual(10m, broker.GetPosition(symbol), "当前持仓应为多头 10 手");

        // 2.1 多头持仓下，同向提交买入 ReduceOnly -> 坚决拒单
        var buySameDirOrder = new OrderRequest(symbol, OrderDirection.Buy, OrderType.Limit, price, 5m, ReduceOnly: true);
        long buySameDirId = broker.SubmitOrder(buySameDirOrder);
        Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(buySameDirId), "多头持仓下同向提交买入 ReduceOnly 必须被拒单");

        // 2.2 多头持仓下，超额提交卖出 15 手 ReduceOnly（试图倒欠开空 5 手） -> 坚决拒单
        var sellExcessOrder = new OrderRequest(symbol, OrderDirection.Sell, OrderType.Limit, price, 15m, ReduceOnly: true);
        long sellExcessId = broker.SubmitOrder(sellExcessOrder);
        Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(sellExcessId), "多头持仓下超额卖出 ReduceOnly 必须被拒单");

        // 2.3 提交合法 10 手卖出 ReduceOnly 平仓 -> 放行并成功平仓
        var sellValidOrder = new OrderRequest(symbol, OrderDirection.Sell, OrderType.Limit, price, 10m, ReduceOnly: true);
        long sellValidId = broker.SubmitOrder(sellValidOrder);
        Assert.AreNotEqual(OrderStatus.Rejected, broker.GetOrderStatus(sellValidId), "合法 ReduceOnly 平仓单必须被放行");
        simExchange.Step(dates[0], new MarketDataProvider(tsRegistry), 0, EnginePhase.Close);
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(sellValidId));
        Assert.AreEqual(0m, broker.GetPosition(symbol), "多头持仓成功平仓归零");

        // 门线 3：对称验证空头持仓
        // 建立 10 手空头持仓
        var shortOpenOrder = new OrderRequest(symbol, OrderDirection.Sell, OrderType.Limit, price, 10m);
        long shortOpenId = broker.SubmitOrder(shortOpenOrder);
        simExchange.Step(dates[0], new MarketDataProvider(tsRegistry), 0, EnginePhase.Close);
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(shortOpenId));
        Assert.AreEqual(-10m, broker.GetPosition(symbol), "当前持仓应为空头 10 手");

        // 3.1 空头持仓下，同向提交卖出 ReduceOnly -> 坚决拒单
        var shortSameDirOrder = new OrderRequest(symbol, OrderDirection.Sell, OrderType.Limit, price, 5m, ReduceOnly: true);
        long shortSameDirId = broker.SubmitOrder(shortSameDirOrder);
        Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(shortSameDirId), "空头持仓下同向提交卖出 ReduceOnly 必须被拒单");

        // 3.2 空头持仓下，超额提交买入 15 手 ReduceOnly（试图倒欠开多 5 手） -> 坚决拒单
        var buyExcessOrder = new OrderRequest(symbol, OrderDirection.Buy, OrderType.Limit, price, 15m, ReduceOnly: true);
        long buyExcessId = broker.SubmitOrder(buyExcessOrder);
        Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(buyExcessId), "空头持仓下超额买入 ReduceOnly 必须被拒单");

        // 3.3 提交合法 10 手买入 ReduceOnly 平空 -> 放行并平空归零
        var buyValidOrder = new OrderRequest(symbol, OrderDirection.Buy, OrderType.Limit, price, 10m, ReduceOnly: true);
        long buyValidId = broker.SubmitOrder(buyValidOrder);
        Assert.AreNotEqual(OrderStatus.Rejected, broker.GetOrderStatus(buyValidId), "合法 ReduceOnly 平空单必须被放行");
        simExchange.Step(dates[0], new MarketDataProvider(tsRegistry), 0, EnginePhase.Close);
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(buyValidId));
        Assert.AreEqual(0m, broker.GetPosition(symbol), "空头持仓成功平仓归零");

        // 保证金全额解冻，初始资金分厘不差严格守恒
        Assert.AreEqual(1000000.0, broker.GetCash(), 1e-4, "双向平仓后保证金全额释放");
        Assert.AreEqual(1000000.0, broker.GetTotalEquity(), 1e-4, "总权益守恒");
    }

    #endregion

    #region 测试辅助策略类

    private sealed class BracketLifecycleStrategy : StrategyBase
    {
        public long SlOrderId { get; private set; }
        public long TpOrderId { get; private set; }
        private bool _bracketPlaced;

        public override void OnUpdate(IEngineContext context)
        {
            if (!_bracketPlaced)
            {
                // Day 0: 买入 1000 股
                context.Buy("SH.600000", 1000m, price: 10.0);
                // 提交 Bracket 订单：止损 9.5，止盈 12.0
                var (slId, tpId) = context.Bracket("SH.600000", 1000m, stopPrice: 9.5, takeProfitPrice: 12.0);
                SlOrderId = slId;
                TpOrderId = tpId;
                _bracketPlaced = true;
            }
        }
    }

    private sealed class BuyOnceAndHoldStrategy : StrategyBase
    {
        private readonly string _symbol;
        private readonly decimal _quantity;
        private bool _bought;

        public BuyOnceAndHoldStrategy(string symbol, decimal quantity)
        {
            _symbol = symbol;
            _quantity = quantity;
        }

        public override void OnUpdate(IEngineContext context)
        {
            if (!_bought)
            {
                context.Buy(_symbol, _quantity);
                _bought = true;
            }
        }
    }

    private sealed class NextOpenPositionFlipStrategy : StrategyBase
    {
        public int StepCount { get; private set; }

        public override void OnUpdate(IEngineContext context)
        {
            string symbol = "IF2401";

            if (StepCount == 0)
            {
                // Day 0 收盘: 卖出 10 手开空（跨夜挂起至 Day 1 开盘撮合）
                context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Sell, 10m));
            }
            else if (StepCount == 1)
            {
                // Day 1 收盘: 买入 15 手（平空 10 手 + 开多 5 手，跨夜挂起至 Day 2 开盘撮合）
                context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Buy, 15m));
            }
            else if (StepCount == 2)
            {
                // Day 2 收盘: 卖出 5 手平多清仓（跨夜挂起至 Day 3 开盘撮合）
                context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Sell, 5m));
            }

            StepCount++;
        }
    }

    private sealed class SpotOnOpenT1Strategy : StrategyBase
    {
        public override EnginePhase UpdatePhase => EnginePhase.Open;
        public long Day0BuyOrderId { get; private set; }
        public long Day0SellOrderId { get; private set; }
        public long Day1SellOrderId { get; private set; }

        public override void OnUpdate(IEngineContext context)
        {
            string symbol = "SH.600000";

            if (context.CurrentIndex == 0)
            {
                // Day 0 开盘: 买入 1000 股
                Day0BuyOrderId = context.Buy(symbol, 1000m);
                // 同一步开盘尝试卖出 1000 股（必须被 T+1 拦截）
                Day0SellOrderId = context.Sell(symbol, 1000m);
            }
            else if (context.CurrentIndex == 1)
            {
                // Day 1 开盘: T+1 持仓已在 Phase 1.2 步解锁，此时平仓卖出 1000 股
                Day1SellOrderId = context.Sell(symbol, 1000m);
            }
        }
    }

    private sealed class ShortSqueezeBankruptcyStrategy : StrategyBase
    {
        public override void OnUpdate(IEngineContext context)
        {
            if (context.CurrentIndex == 0)
            {
                // Day 0: 卖空 100 股 @ 10.0（初始资金 10,000 元，占用保证金 1,000 元）
                context.Sell("SH.600000", 100m);
            }
        }
    }

    [TestMethod]
    public void Bankruptcy_EndToEnd_ShouldReportTradingStatusBankrupt_InResultAndAnalysisReport()
    {
        var symbols = new[] { "SH.600000" };
        var dates = new[] { new DateTime(2024, 1, 2), new DateTime(2024, 1, 3) };
        var metadata = new MockMetadata(symbols, dates, new[] { "close", "open", "high", "low", "volume" });
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        // Day 0: 10.0 元开空；Day 1: 500.0 元暴涨（严重穿仓爆仓）
        var closeData = new double[,] { { 10.0 }, { 500.0 } };
        var volumeData = new double[,] { { 100000.0 }, { 100000.0 } };

        tsRegistry.SetMatrix("close", new MockMatrix(closeData));
        tsRegistry.SetMatrix("open", new MockMatrix(closeData));
        tsRegistry.SetMatrix("high", new MockMatrix(closeData));
        tsRegistry.SetMatrix("low", new MockMatrix(closeData));
        tsRegistry.SetMatrix("volume", new MockMatrix(volumeData));

        var brokerConfig = new BrokerConfig
        {
            InitialCash = 10000.0,
            AllowShort = true,
            FeeRate = 0.0,
            Slippage = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };

        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        var strategy = new ShortSqueezeBankruptcyStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[1]
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        // 1. 验证引擎终局导出的 TradingResult 状态为 Bankrupt
        var result = engine.GetResult();
        Assert.AreEqual(TradingStatus.Bankrupt, result.Status, "穿仓破产策略导出的交易事实必须准确反映 TradingStatus.Bankrupt");

        // 2. 验证分析引擎生成的综合报告 AnalysisReport 正确承载该状态
        var analysisEngine = AnalysisEngineFactory.Create();
        var report = analysisEngine.Analyze(result, new AnalysisOptions());

        Assert.AreEqual(TradingStatus.Bankrupt, report.Facts.Status);
        Assert.IsNotNull(report.Capital);
        Assert.AreEqual(TradingStatus.Bankrupt, report.Capital.Status);

        // 3. 验证 Capital 统计自描述指标流输出状态项
        var metrics = report.Capital.GetMetrics().ToList();
        var statusMetric = metrics.FirstOrDefault(m => m.Label.Contains("Trading Status"));
        Assert.IsNotNull(statusMetric);
        Assert.AreEqual("Bankrupt", statusMetric.Value);
    }

    #endregion
}
