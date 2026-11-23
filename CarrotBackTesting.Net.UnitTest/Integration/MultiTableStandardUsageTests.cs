using System;
using System.Collections.Generic;
using System.Linq;
using CarrotBacktesting.NET;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBacktesting.NET.Strategy;
using CarrotBackTesting.Net.UnitTest.Analysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Integration;

/// <summary>
/// 多表与异构模态标准用法集成测试（Multi-Table & Multi-Modal Standard Usage Tests）。
/// 分别覆盖独立场景的标准调用范式：
/// 1. 日 - 日（Daily - Daily）：主日线表与辅助日线参考表联合决策；
/// 2. 分钟 - 日（Minute - Daily）：分钟主时钟驱动，盘中 As-Of 因果对齐日线大周期指标过滤；
/// 3. 日 - Event（Daily - Event）：日线行情主步推进，结合时态稀疏事件表执行交易风控拦截；
/// 4. 日 - Field（Daily - Field）：基于标的静态概念维表倒排检索动态圈选股票池并驱动回测。
/// </summary>
[TestClass]
public class MultiTableStandardUsageTests
{
    #region 1. 日 - 日标准用法 (Daily - Daily)

    /// <summary>
    /// 测试主日线表（个股交易标的）与辅助日线表（参考基准或行业指数）联合决策的标准用法。
    /// 策略在 OnUpdate 中读取副表当前切片，作为入场条件参考。
    /// </summary>
    [TestMethod]
    public void DailyPrimary_DailySub_StandardUsage_CrossReferenceStrategy()
    {
        var dates = new[]
        {
            MarketTime.FromDateTime(new DateTime(2023, 1, 3)),
            MarketTime.FromDateTime(new DateTime(2023, 1, 4)),
            MarketTime.FromDateTime(new DateTime(2023, 1, 5))
        };
        var symbols = new[] { "sh.600000" };
        var fieldTypes = new Dictionary<string, Type>
        {
            { "open", typeof(double) },
            { "close", typeof(double) }
        };
        var metadata = new DynamicMarketMetadata(symbols, dates, fieldTypes);

        // 主表：个股价格 (10.0 -> 10.5 -> 11.0)
        var primaryTable = new MockTimeseriesTable(metadata, "stock_daily", isPrimary: true);
        var stockClose = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 3);
        stockClose[0, 0] = 10.0;
        stockClose[1, 0] = 10.5;
        stockClose[2, 0] = 11.0;
        primaryTable.SetMatrix("open", stockClose);
        primaryTable.SetMatrix("close", stockClose);

        // 副表：行业指数 (100.0 -> 108.0 -> 102.0)
        var subTable = new MockTimeseriesTable(metadata, "index_daily", isPrimary: false, subRowMap: new[] { 0, 1, 2 });
        var indexClose = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 3);
        indexClose[0, 0] = 100.0;
        indexClose[1, 0] = 108.0; // Day 1 指数大涨，触发策略买入
        indexClose[2, 0] = 102.0;
        subTable.SetMatrix("open", indexClose);
        subTable.SetMatrix("close", indexClose);

        using var registry = new TimeseriesRegistry(primaryTable, new[] { subTable });
        var brokerConfig = new BrokerConfig
        {
            InitialCash = 100000.0,
            DefaultExecutionPrice = ExecutionPriceType.Open
        };
        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        var strategy = new DailySubReferencingStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);
        var engineConfig = new EngineConfig { Mode = "backtest" };

        using var engine = new BacktestingEngine(
            registry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);
        Assert.AreEqual(1, strategy.BuyExecutionCount, "Day 1 指数满足 > 105.0 条件时应精准触发 1 次买入");
        Assert.AreEqual(1000m, broker.GetPosition("sh.600000"), "策略应成功持有 1000 股个股");
    }

    private class DailySubReferencingStrategy : StrategyBase
    {
        public override EnginePhase UpdatePhase => EnginePhase.Open;
        public int BuyExecutionCount { get; private set; }

        public override void OnUpdate(IEngineContext context)
        {
            // 标准用法：通过 context.Timeseries["sub_table"] 直取辅助日线表的当期切片值
            double indexValue = context.Timeseries["index_daily"].GetCurrentValue<double>("close", 0, context);

            // 当参考副表指数处于强势区间（> 105.0）且无持仓时，买入主表标的
            if (indexValue > 105.0 && context.GetPosition("sh.600000") == 0m)
            {
                double currentStockPrice = context.GetOpen("sh.600000");
                long orderId = context.Buy("sh.600000", 1000m, currentStockPrice);
                if (orderId > 0)
                {
                    BuyExecutionCount++;
                }
            }
        }
    }

    #endregion

    #region 2. 分钟 - 日标准用法 (Minute - Daily)

    /// <summary>
    /// 测试分钟主时钟驱动回测中，读取日线辅助表作为大周期指标过滤的标准用法。
    /// 验证盘中严格 As-Of 因果对齐（日内分钟只能读取前一日线收盘，15:00 收盘瞬间可见当日日线）。
    /// </summary>
    [TestMethod]
    public void MinutePrimary_DailySub_StandardUsage_MacroFilterStrategy()
    {
        // 2 个交易日，每日 4 根代表性 5 分钟 Bar
        var minuteDates = new List<MarketTime>
        {
            // Day 1 (01-03)
            MarketTime.FromDateTime(new DateTime(2023, 1, 3, 9, 35, 0)),  // Step 0
            MarketTime.FromDateTime(new DateTime(2023, 1, 3, 10, 0, 0)),  // Step 1
            MarketTime.FromDateTime(new DateTime(2023, 1, 3, 14, 55, 0)), // Step 2
            MarketTime.FromDateTime(new DateTime(2023, 1, 3, 15, 0, 0)),  // Step 3 (Day 1 收盘)

            // Day 2 (01-04)
            MarketTime.FromDateTime(new DateTime(2023, 1, 4, 9, 35, 0)),  // Step 4
            MarketTime.FromDateTime(new DateTime(2023, 1, 4, 10, 0, 0)),  // Step 5 (触发策略盘中突破)
            MarketTime.FromDateTime(new DateTime(2023, 1, 4, 14, 55, 0)), // Step 6
            MarketTime.FromDateTime(new DateTime(2023, 1, 4, 15, 0, 0)),  // Step 7 (Day 2 收盘)
        };

        var dailyDates = new List<MarketTime>
        {
            MarketTime.FromDateTime(new DateTime(2023, 1, 3, 15, 0, 0)), // Day 1 日线收盘 (Index 0)
            MarketTime.FromDateTime(new DateTime(2023, 1, 4, 15, 0, 0)), // Day 2 日线收盘 (Index 1)
        };

        var symbols = new[] { "sh.600000" };
        var fieldTypes = new Dictionary<string, Type>
        {
            { "open", typeof(double) },
            { "close", typeof(double) }
        };

        var primaryMeta = new DynamicMarketMetadata(symbols, minuteDates, fieldTypes);
        var subMeta = new DynamicMarketMetadata(symbols, dailyDates, fieldTypes);

        // 主表：分钟价格
        var primaryTable = new MockTimeseriesTable(primaryMeta, "minute_kline", isPrimary: true);
        var minClose = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 8);
        minClose[0, 0] = 10.0; minClose[1, 0] = 10.1; minClose[2, 0] = 10.2; minClose[3, 0] = 10.3; // Day 1
        minClose[4, 0] = 10.4; minClose[5, 0] = 10.6; minClose[6, 0] = 10.7; minClose[7, 0] = 10.8; // Day 2
        primaryTable.SetMatrix("open", minClose);
        primaryTable.SetMatrix("close", minClose);

        // 副表：日线收盘 (Day 1: 100.0, Day 2: 120.0)
        // 使用标准的 As-Of 映射构建副表
        int[] asOfMap = TimeseriesTimelineRouter.BuildAsOfMap(minuteDates, dailyDates, timeOffset: null);
        var subTable = new MockTimeseriesTable(subMeta, "daily_trend", isPrimary: false, subRowMap: asOfMap);
        var dayClose = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 2);
        dayClose[0, 0] = 100.0;
        dayClose[1, 0] = 120.0;
        subTable.SetMatrix("open", dayClose);
        subTable.SetMatrix("close", dayClose);

        using var registry = new TimeseriesRegistry(primaryTable, new[] { subTable });
        var brokerConfig = new BrokerConfig
        {
            InitialCash = 100000.0,
            DefaultExecutionPrice = ExecutionPriceType.Open
        };
        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        var strategy = new MinuteWithDailyFilterStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);
        var engineConfig = new EngineConfig { Mode = "backtest" };

        using var engine = new BacktestingEngine(
            registry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);

        // 验证因果对齐历史观测记录：
        // Step 0~2 (Day 1 盘中): 日线尚未收盘，返回 NaN
        Assert.IsTrue(double.IsNaN(strategy.ObservedDailyCloses[0]));
        Assert.IsTrue(double.IsNaN(strategy.ObservedDailyCloses[1]));
        Assert.IsTrue(double.IsNaN(strategy.ObservedDailyCloses[2]));
        // Step 3 (Day 1 15:00 收盘瞬间): 首次可见 Day 1 日线 100.0
        Assert.AreEqual(100.0, strategy.ObservedDailyCloses[3]);
        // Step 4~6 (Day 2 盘中): 严防前瞻，始终只能看到前一交易日日线 100.0
        Assert.AreEqual(100.0, strategy.ObservedDailyCloses[4]);
        Assert.AreEqual(100.0, strategy.ObservedDailyCloses[5]);
        Assert.AreEqual(100.0, strategy.ObservedDailyCloses[6]);
        // Step 7 (Day 2 15:00 收盘瞬间): 可见 Day 2 日线 120.0
        Assert.AreEqual(120.0, strategy.ObservedDailyCloses[7]);

        // 策略在 Day 2 盘中 10:00 (Step 5) 依据前一日线过滤 (100.0) 结合分钟突破买入 1000 股
        Assert.AreEqual(1, strategy.BuyExecutionCount);
        Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
    }

    private class MinuteWithDailyFilterStrategy : StrategyBase
    {
        public override EnginePhase UpdatePhase => EnginePhase.Open;
        public List<double> ObservedDailyCloses { get; } = new();
        public int BuyExecutionCount { get; private set; }
        private bool _hasOrdered;

        public override void OnUpdate(IEngineContext context)
        {
            // 标准用法：分钟级策略在 OnUpdate 中调用 GetCurrentValue 获取对应 As-Of 日线值
            double dailyClose = context.Timeseries["daily_trend"].GetCurrentValue<double>("close", 0, context);
            ObservedDailyCloses.Add(dailyClose);

            // 当日线趋势已建立（前一日线收盘 >= 100.0），且分钟价格突破 10.5 时，盘中挂单买入
            double currentMinutePrice = context.GetOpen("sh.600000");
            if (!_hasOrdered && !double.IsNaN(dailyClose) && dailyClose >= 100.0 && currentMinutePrice >= 10.5 && context.GetPosition("sh.600000") == 0m)
            {
                long orderId = context.Buy("sh.600000", 1000m, currentMinutePrice);
                if (orderId > 0)
                {
                    BuyExecutionCount++;
                    _hasOrdered = true;
                }
            }
        }
    }

    #endregion

    #region 3. 日 - Event 标准用法 (Daily - Event)

    /// <summary>
    /// 测试日线行情主步推进中，结合稀疏事件流表（如停牌/分红/监管公告）做条件风控拦截的标准用法。
    /// </summary>
    [TestMethod]
    public void DailyTimeseries_EventTable_StandardUsage_SuspendedEventGuard()
    {
        var dates = new[]
        {
            MarketTime.FromDateTime(new DateTime(2023, 1, 3)), // Day 0: 停牌事件发生
            MarketTime.FromDateTime(new DateTime(2023, 1, 4)), // Day 1: 恢复交易
            MarketTime.FromDateTime(new DateTime(2023, 1, 5))  // Day 2: 正常交易
        };
        var symbols = new[] { "sh.600000" };
        var fieldTypes = new Dictionary<string, Type>
        {
            { "open", typeof(double) },
            { "close", typeof(double) }
        };
        var metadata = new DynamicMarketMetadata(symbols, dates, fieldTypes);

        var primaryTable = new MockTimeseriesTable(metadata, "stock_daily", isPrimary: true);
        var stockClose = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 3);
        stockClose[0, 0] = 10.0;
        stockClose[1, 0] = 10.5;
        stockClose[2, 0] = 11.0;
        primaryTable.SetMatrix("open", stockClose);
        primaryTable.SetMatrix("close", stockClose);

        using var timeseries = new TimeseriesRegistry(primaryTable);

        // 构造事件注册表并挂载停牌事件表（Day 0 停牌）
        var events = new EventRegistry();
        var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "reason", 0 },
            { "is_suspended", 1 }
        };

        var suspendedEvents = new Dictionary<MarketTime, Dictionary<string, List<DataRow>>>
        {
            [MarketTime.FromDateTime(new DateTime(2023, 1, 3))] = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase)
            {
                ["sh.600000"] = new List<DataRow>
                {
                    new DataRow(new[] { "核查停牌", "1" }, ordinals)
                }
            }
        };

        var eventTable = new EventTable("trading_events", new[] { "reason", "is_suspended" }, suspendedEvents);
        events.Register("trading_events", eventTable);

        var brokerConfig = new BrokerConfig
        {
            InitialCash = 100000.0,
            DefaultExecutionPrice = ExecutionPriceType.Open
        };
        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        var strategy = new EventGuardedTradingStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);
        var engineConfig = new EngineConfig { Mode = "backtest" };

        using var engine = new BacktestingEngine(
            timeseries,
            broker,
            pipeline,
            events: events,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);
        // Day 0 停牌被拦截，Day 1 复牌后成功买入 1 次，Day 2 已持仓不再买入
        Assert.AreEqual(1, strategy.SuspendedInterceptionCount, "Day 0 停牌应触发 1 次事件风控拦截");
        Assert.AreEqual(1, strategy.SuccessfulOrderCount, "Day 1 复牌后应成功提交订单");
        Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
    }

    private class EventGuardedTradingStrategy : StrategyBase
    {
        public override EnginePhase UpdatePhase => EnginePhase.Open;
        public int SuspendedInterceptionCount { get; private set; }
        public int SuccessfulOrderCount { get; private set; }

        public override void OnUpdate(IEngineContext context)
        {
            string symbol = "sh.600000";

            // 标准用法 1：通过 context.Events.HasNewEvents 轻量探测当前时间步是否有事件到达
            MarketTime currentDate = context.CurrentTime.Date;
            if (context.Events.HasNewEvents(currentDate, "trading_events"))
            {
                // 标准用法 2：通过 GetRows 获取指定时间步、主体的事件记录列表
                var eventRows = context.Events["trading_events"].GetRows(currentDate, symbol);
                if (eventRows != null && eventRows.Count > 0)
                {
                    int isSuspended = eventRows[0].GetInt("is_suspended");
                    if (isSuspended == 1)
                    {
                        SuspendedInterceptionCount++;
                        return; // 停牌拦截，安全跳过下单
                    }
                }
            }

            // 正常交易日入场
            if (context.GetPosition(symbol) == 0m)
            {
                long orderId = context.Buy(symbol, 1000m, context.GetOpen(symbol));
                if (orderId > 0)
                {
                    SuccessfulOrderCount++;
                }
            }
        }
    }

    #endregion

    #region 4. 日 - Field 标准用法 (Daily - Field)

    /// <summary>
    /// 测试基于标的静态概念维表（FieldTable）倒排检索动态圈选股票池并驱动日线回测的标准用法。
    /// </summary>
    [TestMethod]
    public void DailyTimeseries_FieldTable_StandardUsage_DynamicConceptStockPool()
    {
        var dates = new[]
        {
            MarketTime.FromDateTime(new DateTime(2023, 1, 3)),
            MarketTime.FromDateTime(new DateTime(2023, 1, 4))
        };
        var symbols = new[] { "sh.600000", "sz.000001", "sz.000002" };
        var fieldTypes = new Dictionary<string, Type>
        {
            { "open", typeof(double) },
            { "close", typeof(double) }
        };
        var metadata = new DynamicMarketMetadata(symbols, dates, fieldTypes);

        // 3 只股票行情
        var primaryTable = new MockTimeseriesTable(metadata, "stock_daily", isPrimary: true);
        var stockClose = new ChunkMatrix2D<double>(width: 3, pageSize: 4096, initialRowCount: 2);
        stockClose[0, 0] = 10.0; stockClose[0, 1] = 15.0; stockClose[0, 2] = 20.0;
        stockClose[1, 0] = 10.5; stockClose[1, 1] = 15.5; stockClose[1, 2] = 20.5;
        primaryTable.SetMatrix("open", stockClose);
        primaryTable.SetMatrix("close", stockClose);

        using var timeseries = new TimeseriesRegistry(primaryTable);

        // 构造静态概念维表：
        // sh.600000 -> 银行板块
        // sz.000001 -> 银行板块
        // sz.000002 -> 房地产板块
        var fields = new FieldRegistry();
        var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "board_name", 0 } };
        var conceptData = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sh.600000"] = new List<DataRow> { new DataRow(new[] { "银行板块" }, ordinals) },
            ["sz.000001"] = new List<DataRow> { new DataRow(new[] { "银行板块" }, ordinals) },
            ["sz.000002"] = new List<DataRow> { new DataRow(new[] { "房地产板块" }, ordinals) }
        };
        var fieldTable = new FieldTable("concept_table", new[] { "board_name" }, conceptData);
        fields.Register("concept_table", fieldTable);

        var brokerConfig = new BrokerConfig
        {
            InitialCash = 100000.0,
            DefaultExecutionPrice = ExecutionPriceType.Open
        };
        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        var strategy = new ConceptSelectedPoolStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);
        var engineConfig = new EngineConfig { Mode = "backtest" };

        using var engine = new BacktestingEngine(
            timeseries,
            broker,
            pipeline,
            fields: fields,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);

        // 验证：策略仅对倒排选中的两只银行股下单，房地产股票绝不下单
        Assert.AreEqual(1000m, broker.GetPosition("sh.600000"), "银行板块标的 sh.600000 应成功建仓");
        Assert.AreEqual(1000m, broker.GetPosition("sz.000001"), "银行板块标的 sz.000001 应成功建仓");
        Assert.AreEqual(0m, broker.GetPosition("sz.000002"), "房地产标的 sz.000002 绝不应被交易");
    }

    private class ConceptSelectedPoolStrategy : StrategyBase
    {
        public override EnginePhase UpdatePhase => EnginePhase.Open;
        private readonly List<string> _targetStockPool = new();

        public override void OnStart(IEngineContext context)
        {
            base.OnStart(context);

            // 标准用法：在策略初始化生命周期中，利用 FieldTable 的倒排索引高效圈定目标股票池
            var matchedSymbols = context.Fields["concept_table"].GetKeys("银行板块");
            _targetStockPool.AddRange(matchedSymbols);
        }

        public override void OnUpdate(IEngineContext context)
        {
            // 标准用法：仅针对维表圈选出的有效股票池进行循环遍历与下单
            foreach (var symbol in _targetStockPool)
            {
                if (context.GetPosition(symbol) == 0m)
                {
                    double price = context.GetOpen(symbol);
                    context.Buy(symbol, 1000m, price);
                }
            }
        }
    }

    #endregion
}
