using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBacktesting.NET.Strategy;

namespace CarrotBackTesting.Net.UnitTest.Engine;

/// <summary>
/// 亚 Bar 撮合与多模态时态体系（Milestone 3）TDD 单元测试套件。
/// 验证决策时点（OnClose / OnOpen）、执行价格类型（Open / VWAP / TypicalPrice / Close）、
/// 防未来函数内存安全守卫、回测终局生命周期边界与突发停牌熔断。
/// </summary>
[TestClass]
public class ExecutionTimingAndPriceTests
{
    private static (ITimeseriesRegistry Timeseries, MarketTime[] Dates) CreateMultiFieldTimeseries(
        string symbol,
        double[] open,
        double[] high,
        double[] low,
        double[] close,
        double[]? volume = null,
        double[]? vwap = null)
    {
        int rows = close.Length;
        var dates = new MarketTime[rows];
        var baseDate = new DateTime(2025, 1, 1);
        for (int i = 0; i < rows; i++)
        {
            dates[i] = (MarketTime)baseDate.AddDays(i);
        }

        var metadata = new DynamicMarketMetadata(new[] { symbol }, dates);
        var matrices = new Dictionary<string, IReadOnlyMatrix2D<double>>(StringComparer.OrdinalIgnoreCase);

        ChunkMatrix2D<double> CreateMatrix(double[] values)
        {
            var mat = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: rows);
            for (int r = 0; r < rows; r++) mat[r, 0] = values[r];
            return mat;
        }

        matrices[StandardFields.Close] = CreateMatrix(close);
        matrices[StandardFields.Open] = CreateMatrix(open);
        matrices[StandardFields.High] = CreateMatrix(high);
        matrices[StandardFields.Low] = CreateMatrix(low);

        if (volume != null) matrices[StandardFields.Volume] = CreateMatrix(volume);
        if (vwap != null) matrices["vwap"] = CreateMatrix(vwap);

        var ts = new SimulatedExchangeGatewayTests.MultiMatrixTimeseriesRegistry(metadata, matrices);
        return (ts, dates);
    }

    private sealed class ActionStrategy : StrategyBase
    {
        private readonly Action<IEngineContext> _onUpdate;
        public ActionStrategy(Action<IEngineContext> onUpdate) => _onUpdate = onUpdate;
        public override void OnUpdate(IEngineContext context) => _onUpdate(context);
    }

    [TestMethod]
    public void ExecutionPrice_NextOpen_ShouldFillAtNextOpenPriceWithTakerRole()
    {
        // 场景：3 天行情，OnClose 决策，默认 ExecutionPriceType.Open（次日开盘价成交）
        var open = new[] { 10.0, 11.0, 12.0 };
        var high = new[] { 10.8, 11.8, 12.8 };
        var low = new[] { 9.8, 10.8, 11.8 };
        var close = new[] { 10.5, 11.5, 12.5 };
        var (ts, _) = CreateMultiFieldTimeseries("sh.600000", open, high, low, close);

        var sessionConfig = new SessionConfig
        {
            Broker = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0, // 零滑点以便精确断言
                DefaultExecutionPrice = ExecutionPriceType.Open
            },
            Engine = new EngineConfig
            {
                UpdateTiming = StrategyUpdateTiming.OnClose
            }
        };

        var trades = new List<ExecutionReport>();
        var simGateway = new SimulatedExchangeGateway(sessionConfig.Broker);
        simGateway.OnExecution += trades.Add;
        var broker = new Broker(simGateway, sessionConfig.Broker);

        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(new ActionStrategy(ctx =>
        {
            // 在 Day 0 收盘做出决策买入 100 股
            if (ctx.CurrentIndex == 0)
            {
                ctx.Buy("sh.600000", 100m);
            }
        }));

        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: sessionConfig.Engine,
            simExchange: simGateway);

        engine.Run();

        // 验证：Day 0 收盘发出的买单在 Day 1 开盘撮合成交，价格严格等于 Day 1 的 Open = 11.0
        Assert.AreEqual(1, trades.Count, "应该发生 1 笔成交");
        var fill = trades[0];
        Assert.AreEqual("sh.600000", fill.Symbol);
        Assert.AreEqual(11.0, fill.FillPrice, 1e-4, "成交价应严格等于次日 Open (11.0)");
        Assert.AreEqual(100m, fill.FillQuantity);
        Assert.AreEqual(LiquidityRole.Taker, fill.Role, "市价吃单应标记为 Taker");
    }

    [TestMethod]
    public void ExecutionPrice_NextVwap_ShouldFillAtNextVwapPrice()
    {
        // 场景：OnClose 决策，次日以 VWAP 拆单撮合
        var open = new[] { 10.0, 11.0, 12.0 };
        var high = new[] { 10.8, 11.8, 12.8 };
        var low = new[] { 9.8, 10.8, 11.8 };
        var close = new[] { 10.5, 11.5, 12.5 };
        var vwap = new[] { 10.3, 11.4, 12.3 }; // Day 1 vwap = 11.4
        var (ts, _) = CreateMultiFieldTimeseries("sh.600000", open, high, low, close, vwap: vwap);

        var sessionConfig = new SessionConfig
        {
            Broker = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                DefaultExecutionPrice = ExecutionPriceType.Vwap
            },
            Engine = new EngineConfig
            {
                UpdateTiming = StrategyUpdateTiming.OnClose
            }
        };

        var trades = new List<ExecutionReport>();
        var simGateway = new SimulatedExchangeGateway(sessionConfig.Broker);
        simGateway.OnExecution += trades.Add;
        var broker = new Broker(simGateway, sessionConfig.Broker);

        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(new ActionStrategy(ctx =>
        {
            if (ctx.CurrentIndex == 0) ctx.Buy("sh.600000", 100m);
        }));

        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: sessionConfig.Engine,
            simExchange: simGateway);

        engine.Run();

        Assert.AreEqual(1, trades.Count);
        Assert.AreEqual(11.4, trades[0].FillPrice, 1e-4, "成交价应严格等于次日 VWAP (11.4)");
    }

    [TestMethod]
    public void ExecutionPrice_Average_ShouldFillAtAveragePrice()
    {
        // 场景：配置 Average，次日以 (O+H+L+C)/4 成交
        var open = new[] { 10.0, 11.0, 12.0 };
        var high = new[] { 10.8, 12.0, 12.8 };
        var low = new[] { 9.8, 10.0, 11.8 };
        var close = new[] { 10.5, 11.0, 12.5 };
        // Day 1 AveragePrice = (11.0 + 12.0 + 10.0 + 11.0) / 4 = 11.0
        var (ts, _) = CreateMultiFieldTimeseries("sh.600000", open, high, low, close);

        var sessionConfig = new SessionConfig
        {
            Broker = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                DefaultExecutionPrice = ExecutionPriceType.Average
            },
            Engine = new EngineConfig
            {
                UpdateTiming = StrategyUpdateTiming.OnClose
            }
        };

        var trades = new List<ExecutionReport>();
        var simGateway = new SimulatedExchangeGateway(sessionConfig.Broker);
        simGateway.OnExecution += trades.Add;
        var broker = new Broker(simGateway, sessionConfig.Broker);

        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(new ActionStrategy(ctx =>
        {
            if (ctx.CurrentIndex == 0) ctx.Buy("sh.600000", 100m);
        }));

        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: sessionConfig.Engine,
            simExchange: simGateway);

        engine.Run();

        Assert.AreEqual(1, trades.Count);
        Assert.AreEqual(11.0, trades[0].FillPrice, 1e-4, "成交价应等于四价均值 Average (11.0)");
    }

    [TestMethod]
    public void ExecutionPrice_VwapMissing_ThrowsInvalidDataException()
    {
        // 场景：显式配置 VWAP，但数据源缺失 vwap 且无 volume/amount，严格抛出 InvalidDataException
        var open = new[] { 10.0, 11.0, 12.0 };
        var high = new[] { 10.8, 11.8, 12.8 };
        var low = new[] { 9.8, 10.8, 11.8 };
        var close = new[] { 10.5, 11.5, 12.5 };
        var (ts, _) = CreateMultiFieldTimeseries("sh.600000", open, high, low, close); // 缺失 vwap

        var sessionConfig = new SessionConfig
        {
            Broker = new BrokerConfig
            {
                InitialCash = 100_000.0,
                DefaultExecutionPrice = ExecutionPriceType.Vwap
            },
            Engine = new EngineConfig
            {
                UpdateTiming = StrategyUpdateTiming.OnClose
            }
        };

        var simGateway = new SimulatedExchangeGateway(sessionConfig.Broker);
        var broker = new Broker(simGateway, sessionConfig.Broker);

        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(new ActionStrategy(ctx =>
        {
            if (ctx.CurrentIndex == 0) ctx.Buy("sh.600000", 100m);
        }));

        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: sessionConfig.Engine,
            simExchange: simGateway);

        // 验证：撮合引擎绝不隐式降级，显式抛出 InvalidDataException 熔断
        Assert.ThrowsException<InvalidDataException>(() => engine.Run());
    }

    [TestMethod]
    public void StrategyUpdateTiming_OnOpen_ShouldFillImmediatelyAtCurrentOpen()
    {
        // 场景：OnOpen 决策，开盘即刻以当期 Open 成交
        var open = new[] { 10.0, 11.0, 12.0 };
        var high = new[] { 10.8, 11.8, 12.8 };
        var low = new[] { 9.8, 10.8, 11.8 };
        var close = new[] { 10.5, 11.5, 12.5 };
        var (ts, _) = CreateMultiFieldTimeseries("sh.600000", open, high, low, close);

        var sessionConfig = new SessionConfig
        {
            Broker = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                DefaultExecutionPrice = ExecutionPriceType.Open
            },
            Engine = new EngineConfig
            {
                UpdateTiming = StrategyUpdateTiming.OnOpen
            }
        };

        var trades = new List<ExecutionReport>();
        var simGateway = new SimulatedExchangeGateway(sessionConfig.Broker);
        simGateway.OnExecution += trades.Add;
        var broker = new Broker(simGateway, sessionConfig.Broker);

        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(new ActionStrategy(ctx =>
        {
            // 在 Day 0 开盘决策即刻买入
            if (ctx.CurrentIndex == 0)
            {
                ctx.Buy("sh.600000", 100m);
            }
        }));

        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: sessionConfig.Engine,
            simExchange: simGateway);

        engine.Run();

        Assert.AreEqual(1, trades.Count);
        Assert.AreEqual(10.0, trades[0].FillPrice, 1e-4, "OnOpen 模式下买单应在当期开盘以当期 Open (10.0) 成交");
    }

    [TestMethod]
    public void StrategyUpdateTiming_OnOpen_AccessCurrentCloseOrHighLow_ThrowsInvalidOperationException()
    {
        // 场景：OnOpen 模式下，策略试图读取当期尚未完结的 Close/High/Low，防未来函数守卫抛出 InvalidOperationException
        var open = new[] { 10.0, 11.0, 12.0 };
        var high = new[] { 10.8, 11.8, 12.8 };
        var low = new[] { 9.8, 10.8, 11.8 };
        var close = new[] { 10.5, 11.5, 12.5 };
        var (ts, _) = CreateMultiFieldTimeseries("sh.600000", open, high, low, close);

        var sessionConfig = new SessionConfig
        {
            Broker = new BrokerConfig { InitialCash = 100_000.0 },
            Engine = new EngineConfig { UpdateTiming = StrategyUpdateTiming.OnOpen }
        };

        var simGateway = new SimulatedExchangeGateway(sessionConfig.Broker);
        var broker = new Broker(simGateway, sessionConfig.Broker);

        Exception? capturedException = null;

        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(new ActionStrategy(ctx =>
        {
            if (ctx.CurrentIndex == 1)
            {
                // 1. 读取当期 Open 正常
                double openVal = ctx.GetOpen("sh.600000", offset: 0);
                Assert.AreEqual(11.0, openVal);

                // 2. 读取昨收 Close(offset=-1) 正常
                double prevClose = ctx.GetClose("sh.600000", offset: -1);
                Assert.AreEqual(10.5, prevClose);

                // 3. 尝试读取当期 Close(offset=0)，必须被守卫拦截抛错！
                try
                {
                    ctx.GetClose("sh.600000", offset: 0);
                }
                catch (Exception ex)
                {
                    capturedException = ex;
                }
            }
        }));

        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: sessionConfig.Engine,
            simExchange: simGateway);

        engine.Run();

        Assert.IsNotNull(capturedException, "OnOpen 模式下尝试读取当期 Close 必须触发异常");
        Assert.IsInstanceOfType(capturedException, typeof(InvalidOperationException), "应抛出 InvalidOperationException");
    }

    [TestMethod]
    public void RolloverBoundary_TerminalCancellation_ShouldCancelPendingOrdersAndUnfreeze()
    {
        // 场景：回测最后一根 Bar（Day 1）收盘挂出次日买单，回测结束时挂单自动撤销解冻，无孤儿资金
        var open = new[] { 10.0, 11.0 };
        var high = new[] { 10.8, 11.8 };
        var low = new[] { 9.8, 10.8 };
        var close = new[] { 10.5, 11.5 };
        var (ts, _) = CreateMultiFieldTimeseries("sh.600000", open, high, low, close);

        var sessionConfig = new SessionConfig
        {
            Broker = new BrokerConfig { InitialCash = 100_000.0, DefaultExecutionPrice = ExecutionPriceType.Open },
            Engine = new EngineConfig { UpdateTiming = StrategyUpdateTiming.OnClose }
        };

        var simGateway = new SimulatedExchangeGateway(sessionConfig.Broker);
        var broker = new Broker(simGateway, sessionConfig.Broker);

        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(new ActionStrategy(ctx =>
        {
            // 在最后一根 Bar (Day 1) 收盘发单
            if (ctx.CurrentIndex == 1)
            {
                ctx.Buy("sh.600000", 100m);
            }
        }));

        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: sessionConfig.Engine,
            simExchange: simGateway);

        engine.Run();

        var result = engine.GetResult();
        // 验证：终局挂单被自动撤销，可用现金完全恢复为 100,000 元（无资金冻结残留）
        Assert.AreEqual(100_000.0, engine.Context.AvailableCash, 1e-4, "回测结束收尾时所有未撮合跨步挂单应被撤销解冻");
        Assert.IsTrue(result.OrderEvents.Count > 0);
        var lastEvent = result.OrderEvents[^1];
        Assert.AreEqual(OrderEventType.Canceled, lastEvent.EventType, "最后一笔事件应为 Canceled 撤单归档");
    }

    [TestMethod]
    public void SuspendedSymbol_OnOpen_ShouldRejectAndUnfreeze()
    {
        // 场景：第 T 日收盘买入，第 T+1 日标的突发停牌（整行 NaN），开盘网关熔断拒单解冻
        var metadata = new DynamicMarketMetadata(new[] { "sh.600000" }, new MarketTime[]
        {
            (MarketTime)new DateTime(2025, 1, 1),
            (MarketTime)new DateTime(2025, 1, 2)
        });

        var closeMat = new ChunkMatrix2D<double>(1, 4096, 2);
        closeMat[0, 0] = 10.0;
        closeMat[1, 0] = double.NaN; // 停牌

        var openMat = new ChunkMatrix2D<double>(1, 4096, 2);
        openMat[0, 0] = 10.0;
        openMat[1, 0] = double.NaN; // 停牌

        var matrices = new Dictionary<string, IReadOnlyMatrix2D<double>>(StringComparer.OrdinalIgnoreCase)
        {
            [StandardFields.Close] = closeMat,
            [StandardFields.Open] = openMat
        };

        var ts = new SimulatedExchangeGatewayTests.MultiMatrixTimeseriesRegistry(metadata, matrices);
        var sessionConfig = new SessionConfig
        {
            Broker = new BrokerConfig { InitialCash = 100_000.0, DefaultExecutionPrice = ExecutionPriceType.Open },
            Engine = new EngineConfig { UpdateTiming = StrategyUpdateTiming.OnClose }
        };

        var simGateway = new SimulatedExchangeGateway(sessionConfig.Broker);
        var broker = new Broker(simGateway, sessionConfig.Broker);

        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(new ActionStrategy(ctx =>
        {
            if (ctx.CurrentIndex == 0) ctx.Buy("sh.600000", 100m);
        }));

        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: sessionConfig.Engine,
            simExchange: simGateway);

        engine.Run();

        var result = engine.GetResult();
        // 验证：突发停牌熔断，资金完全退回
        Assert.AreEqual(100_000.0, engine.Context.AvailableCash, 1e-4, "标的停牌时预冻结资金应 100% 解冻");
        var rejectedEvent = result.OrderEvents.FirstOrDefault(e => e.EventType == OrderEventType.Rejected);
        Assert.IsNotNull(rejectedEvent, "停牌订单应记录为 Rejected");
    }

    [TestMethod]
    public void StrategyUpdateTiming_OnOpen_WithVwapExecution_ShouldFillAtClosePhase()
    {
        // 场景：OnOpen 决策，但配置为全天均价 VWAP 成交模式，订单在 Phase 1 挂起并于 Phase 3 收盘按当期 VWAP 撮合成交
        var open = new[] { 10.0, 11.0 };
        var high = new[] { 10.8, 11.8 };
        var low = new[] { 9.8, 10.8 };
        var close = new[] { 10.5, 11.5 };
        var vwap = new[] { 10.3, 11.3 };
        var (ts, _) = CreateMultiFieldTimeseries("sh.600000", open, high, low, close, vwap: vwap);

        var sessionConfig = new SessionConfig
        {
            Broker = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                DefaultExecutionPrice = ExecutionPriceType.Vwap
            },
            Engine = new EngineConfig
            {
                UpdateTiming = StrategyUpdateTiming.OnOpen
            }
        };

        var trades = new List<ExecutionReport>();
        var simGateway = new SimulatedExchangeGateway(sessionConfig.Broker);
        simGateway.OnExecution += trades.Add;
        var broker = new Broker(simGateway, sessionConfig.Broker);

        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(new ActionStrategy(ctx =>
        {
            // 在 Day 0 开盘发单
            if (ctx.CurrentIndex == 0)
            {
                ctx.Buy("sh.600000", 100m);
            }
        }));

        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: sessionConfig.Engine,
            simExchange: simGateway);

        engine.Run();

        Assert.AreEqual(1, trades.Count, "OnOpen 发出的 VWAP 订单应在收盘相位完成撮合");
        Assert.AreEqual(10.3, trades[0].FillPrice, 1e-4, "成交价应严格等于当期 VWAP (10.3)");
    }

    [TestMethod]
    public void StrategyUpdateTiming_OnClose_GtcLimitOrder_ShouldPreserveToNextDayAndFill()
    {
        // 场景：OnClose 决策下提交的 GTC 限价单，跨日保留，次日开盘价格满足触碰条件成功以 Maker 撮合成交
        var open = new[] { 10.0, 9.0 };
        var high = new[] { 10.5, 9.5 };
        var low = new[] { 9.8, 8.8 };
        var close = new[] { 10.2, 9.2 };
        var (ts, _) = CreateMultiFieldTimeseries("sh.600000", open, high, low, close);

        var sessionConfig = new SessionConfig
        {
            Broker = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                FeeRate = 0.0,
                DefaultTimeInForce = TimeInForce.GoodTilCanceled,
                DefaultExecutionPrice = ExecutionPriceType.Open
            },
            Engine = new EngineConfig
            {
                UpdateTiming = StrategyUpdateTiming.OnClose
            }
        };

        var trades = new List<ExecutionReport>();
        var simGateway = new SimulatedExchangeGateway(sessionConfig.Broker);
        simGateway.OnExecution += trades.Add;
        var broker = new Broker(simGateway, sessionConfig.Broker);

        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(new ActionStrategy(ctx =>
        {
            // 在 Day 0 收盘下达 9.0 限价买单（此时收盘价 10.2 未触碰，进入挂单簿）
            if (ctx.CurrentIndex == 0)
            {
                ctx.Broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, 9.0, 100m));
            }
        }));

        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: sessionConfig.Engine,
            simExchange: simGateway);

        engine.Run();

        Assert.AreEqual(1, trades.Count, "Day 0 收盘挂出的 GTC 限价单在 Day 1 开盘跳空至 9.0 时必须成功撮合成交");
        Assert.AreEqual(9.0, trades[0].FillPrice, 1e-4);
        Assert.AreEqual(LiquidityRole.Maker, trades[0].Role, "开盘撮合排队限价单必须为被动挂单方 (Maker)");
    }

    [TestMethod]
    public void ExecutionPrice_Open_MissingOpenPrice_WithOpenField_DoesNotFallbackToClose()
    {
        // 场景：时序源包含 Open 列，但第 T+1 日开盘价缺失（为 NaN），坚决杜绝静默回退借用当期 Close 偷价撮合
        var open = new[] { 10.0, double.NaN };
        var high = new[] { 10.5, 11.0 };
        var low = new[] { 9.8, 10.0 };
        var close = new[] { 10.0, 10.5 };
        var (ts, _) = CreateMultiFieldTimeseries("sh.600000", open, high, low, close);

        var sessionConfig = new SessionConfig
        {
            Broker = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                DefaultExecutionPrice = ExecutionPriceType.Open
            },
            Engine = new EngineConfig
            {
                UpdateTiming = StrategyUpdateTiming.OnClose
            }
        };

        var trades = new List<ExecutionReport>();
        var simGateway = new SimulatedExchangeGateway(sessionConfig.Broker);
        simGateway.OnExecution += trades.Add;
        var broker = new Broker(simGateway, sessionConfig.Broker);

        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(new ActionStrategy(ctx =>
        {
            if (ctx.CurrentIndex == 0) ctx.Buy("sh.600000", 100m);
        }));

        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: sessionConfig.Engine,
            simExchange: simGateway);

        engine.Run();

        // 验证：Day 1 开盘价为 NaN，严禁以 Day 1 的 Close(10.5) 偷价成交！
        Assert.AreEqual(0, trades.Count, "当期 Open 为 NaN 时严禁向后借用当期 Close 偷跑成交");
    }

    [TestMethod]
    public void SuspendedSymbol_ForwardFilledWithZeroVolume_ShouldRejectAndUnfreeze()
    {
        // 场景：第 T+1 日标的前值填充价格（Close=10.0，不是 NaN），但成交量为 0（物理停牌）
        // 撮合引擎必须识别 zero volume，直接熔断拒单并全额解冻可用资金
        var open = new[] { 10.0, 10.0 };
        var high = new[] { 10.5, 10.0 };
        var low = new[] { 9.8, 10.0 };
        var close = new[] { 10.0, 10.0 };
        var volume = new[] { 10_000.0, 0.0 }; // Day 1 停牌无成交量
        var (ts, _) = CreateMultiFieldTimeseries("sh.600000", open, high, low, close, volume: volume);

        var sessionConfig = new SessionConfig
        {
            Broker = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                DefaultExecutionPrice = ExecutionPriceType.Open
            },
            Engine = new EngineConfig
            {
                UpdateTiming = StrategyUpdateTiming.OnClose
            }
        };

        var simGateway = new SimulatedExchangeGateway(sessionConfig.Broker);
        var broker = new Broker(simGateway, sessionConfig.Broker);

        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(new ActionStrategy(ctx =>
        {
            if (ctx.CurrentIndex == 0) ctx.Buy("sh.600000", 100m);
        }));

        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: sessionConfig.Engine,
            simExchange: simGateway);

        engine.Run();

        var result = engine.GetResult();
        // 验证：虽然价格不是 NaN，但成交量为 0 判定停牌熔断，资金完全退回
        Assert.AreEqual(100_000.0, engine.Context.AvailableCash, 1e-4, "标的成交量为 0 (停牌前值填充) 时预冻结资金应 100% 解冻");
        var rejectedEvent = result.OrderEvents.FirstOrDefault(e => e.EventType == OrderEventType.Rejected);
        Assert.IsNotNull(rejectedEvent, "停牌订单必须记录为 Rejected");
        StringAssert.Contains(rejectedEvent.Reason, "volume");
    }

    [TestMethod]
    public void StrategyUpdateTiming_OnOpen_WithCloseExecution_ShouldFillAtClosePhase()
    {
        // 场景：OnOpen 开盘决策提交市价单，但配置为 Close（收盘价撮合）。
        // 订单在 Phase 1 必须挂入挂单簿等待，并在 Phase 3 收盘相位按当期 Close 撮合成交，绝不能在 09:30 开盘瞬间偷看收盘价即时成交！
        var open = new[] { 10.0, 11.0 };
        var high = new[] { 10.8, 11.8 };
        var low = new[] { 9.8, 10.8 };
        var close = new[] { 10.5, 11.5 };
        var (ts, _) = CreateMultiFieldTimeseries("sh.600000", open, high, low, close);

        var sessionConfig = new SessionConfig
        {
            Broker = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                DefaultExecutionPrice = ExecutionPriceType.Close
            },
            Engine = new EngineConfig
            {
                UpdateTiming = StrategyUpdateTiming.OnOpen
            }
        };

        var trades = new List<ExecutionReport>();
        var simGateway = new SimulatedExchangeGateway(sessionConfig.Broker);
        simGateway.OnExecution += trades.Add;
        var broker = new Broker(simGateway, sessionConfig.Broker);

        var pipeline = StrategyPipeline.Sequential();
        pipeline.Add(new ActionStrategy(ctx =>
        {
            // 在 Day 0 开盘决策发出收盘成交市价单
            if (ctx.CurrentIndex == 0)
            {
                ctx.Buy("sh.600000", 100m);
            }
        }));

        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: sessionConfig.Engine,
            simExchange: simGateway);

        engine.Run();

        // 验证：成功在 Day 0 收盘以当期 Close(10.5) 撮合成交
        Assert.AreEqual(1, trades.Count, "OnOpen 发出的 Close 订单应在收盘相位完成撮合");
        Assert.AreEqual(10.5, trades[0].FillPrice, 1e-4, "成交价应严格等于当期收盘价 Close (10.5)");
    }
}
