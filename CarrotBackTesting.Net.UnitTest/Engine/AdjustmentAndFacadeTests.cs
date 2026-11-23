using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Indicators;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBacktesting.NET.Indicators;
using CarrotBacktesting.NET.Strategy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine
{
    [TestClass]
    public class AdjustmentAndFacadeTests
    {
        #region 1. 上下文极简取价门面测试

        [TestMethod]
        public void EngineContext_Facades_ShouldMultiplyFactorsCorrectly()
        {
            var meta = new SimpleMarketMetadata(
                new[] { new DateTime(2025, 1, 2), new DateTime(2025, 1, 3) },
                new[] { "sh.600000", "sz.000001" });

            // 构造行情矩阵: 2 行 2 列
            // row 0: sh.600000 = 10.0, sz.000001 = 20.0
            // row 1: sh.600000 = 11.0, sz.000001 = 22.0
            var closeData = new double[,] { { 10.0, 20.0 }, { 11.0, 22.0 } };
            var openData = new double[,] { { 9.0, 19.0 }, { 10.5, 21.0 } };
            var volData = new double[,] { { 1000.0, 2000.0 }, { 1100.0, 2200.0 } };

            var ts = new MockTimeseriesRegistry(meta);
            ts.RegisterMatrix("close", closeData);
            ts.RegisterMatrix("open", openData);
            ts.RegisterMatrix("volume", volData);

            var broker = new FakeBroker();

            // 构造因子矩阵 (2 行 2 列)
            // row 0: sh.600000 = 2.0, sz.000001 = 1.5
            // row 1: sh.600000 = 3.0, sz.000001 = 1.5 (第 1 天发生除权跃迁)
            var factorMatrix = new ChunkMatrix2D<double>(width: 2, pageSize: 4096, initialRowCount: 2);
            factorMatrix[0, 0] = 2.0;
            factorMatrix[0, 1] = 1.5;
            factorMatrix[1, 0] = 3.0;
            factorMatrix[1, 1] = 1.5;

            var stepToDateIndex = new int[] { 0, 1 };
            using var context = new EngineContext(ts, broker, factorMatrix: factorMatrix, stepToDateIndex: stepToDateIndex);

            // 步进至第 0 步
            context.UpdateStep(0, meta.TradeDates[0]);

            // 1. 收盘价: 后复权 vs 未复权
            Assert.AreEqual(20.0, context.GetClose("sh.600000", mode: PriceMode.Post), 1e-12); // 10.0 * 2.0
            Assert.AreEqual(10.0, context.GetClose("sh.600000", mode: PriceMode.Raw), 1e-12);
            Assert.AreEqual(30.0, context.GetClose(1, mode: PriceMode.Post), 1e-12);             // 20.0 * 1.5
            Assert.AreEqual(20.0, context.GetClose(1, mode: PriceMode.Raw), 1e-12);

            // 2. 开盘价
            Assert.AreEqual(18.0, context.GetOpen("sh.600000", mode: PriceMode.Post), 1e-12);  // 9.0 * 2.0
            Assert.AreEqual(9.0, context.GetOpen("sh.600000", mode: PriceMode.Raw), 1e-12);

            // 3. 成交量: 默认保持 Raw 物理量
            Assert.AreEqual(1000.0, context.GetVolume("sh.600000"), 1e-12);
            Assert.AreEqual(1000.0, context.GetVolume(0), 1e-12);

            // 4. 通用字段 GetField
            Assert.AreEqual(20.0, context.GetField("sh.600000", "close", mode: PriceMode.Post), 1e-12);
            Assert.AreEqual(10.0, context.GetField(0, "close", mode: PriceMode.Raw), 1e-12);

            // 5. 越界与未知标的防御返回 NaN
            Assert.IsTrue(double.IsNaN(context.GetClose("unknown.symbol")));
            Assert.IsTrue(double.IsNaN(context.GetClose(999)));

            // 6. 步进至第 1 步 (发生除权跃迁)，测试当期与时序负向回溯
            context.UpdateStep(1, meta.TradeDates[1]);
            Assert.AreEqual(33.0, context.GetClose("sh.600000", offset: 0, mode: PriceMode.Post), 1e-12); // 11.0 * 3.0
            Assert.AreEqual(11.0, context.GetClose("sh.600000", offset: 0, mode: PriceMode.Raw), 1e-12);

            // 时序负向回溯昨日收盘价: 必须严格锚定昨日因子 2.0，而不是被今日因子 3.0 篡改！
            Assert.AreEqual(20.0, context.GetClose("sh.600000", offset: -1, mode: PriceMode.Post), 1e-12); // 10.0 * 2.0
            Assert.AreEqual(10.0, context.GetClose("sh.600000", offset: -1, mode: PriceMode.Raw), 1e-12);

            // 历史左边界越界防御
            Assert.IsTrue(double.IsNaN(context.GetClose("sh.600000", offset: -2)));

            // 非法未来函数熔断防御 (offset > 0 必须抛出 ArgumentOutOfRangeException)
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.GetClose("sh.600000", offset: 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.GetClose(0, offset: 2));
        }

        #endregion

        #region 2. 引擎冷启动预热滚动与跨模态覆盖告警测试

        [TestMethod]
        public void Engine_PrewarmAdjustments_ShouldRollFromBeginningAndWarnDiscrepancies()
        {
            var dates = new List<DateTime>
            {
                new(2025, 1, 2),
                new(2025, 1, 3),
                new(2025, 1, 4)
            };
            var symbols = new List<string> { "sh.600519", "sz.000001", "sh.600000" };
            var meta = new SimpleMarketMetadata(dates, symbols);

            var ts = new MockTimeseriesRegistry(meta);
            ts.RegisterMatrix("close", new double[,]
            {
                { 100.0, 20.0, 10.0 },
                { 102.0, 21.0, 10.5 },
                { 105.0, 22.0, 11.0 }
            });

            // 构造事件表: 包含历史 2024 年除权记录
            // 2024-06-01: sh.600519 因子为 2.5
            // 2024-12-01: sh.600519 因子更新为 3.0
            // 2024-08-01: sz.000001 因子为 1.2
            // sh.600000 故意在除权表中不留任何记录（有行情无因子），用于触发 Warning！
            var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "back_adj_factor", 0 } };
            var eventData = new Dictionary<MarketTime, Dictionary<string, List<DataRow>>>();

            var d1 = new MarketTime(2024, 6, 1);
            eventData[d1] = new Dictionary<string, List<DataRow>>
            {
                ["sh.600519"] = new List<DataRow> { new DataRow("sh.600519", d1, new[] { "2.5" }, ordinals) }
            };

            var d2 = new MarketTime(2024, 8, 1);
            eventData[d2] = new Dictionary<string, List<DataRow>>
            {
                ["sz.000001"] = new List<DataRow> { new DataRow("sz.000001", d2, new[] { "1.2" }, ordinals) }
            };

            var d3 = new MarketTime(2024, 12, 1);
            eventData[d3] = new Dictionary<string, List<DataRow>>
            {
                ["sh.600519"] = new List<DataRow> { new DataRow("sh.600519", d3, new[] { "3.0" }, ordinals) }
            };

            var adjTable = new EventTable("adjustments", new[] { "back_adj_factor" }, eventData);

            var events = new EventRegistry();
            events.Register("adjustments", adjTable);

            var broker = new FakeBroker();
            var pipeline = StrategyPipeline.Sequential();

            // 监听 Warning 日志
            var logs = new List<string>();
            var mockMonitor = new TestMonitor(msg => logs.Add(msg));

            var sessionConfig = new SessionConfig
            {
                Engine = new EngineConfig
                {
                    StartDate = new DateTime(2025, 1, 2),
                    EndDate = new DateTime(2025, 1, 4)
                },
                Data = new DataConfig
                {
                    Adjust = new AdjustmentConfig
                    {
                        Mode = AdjustmentMode.Post,
                        TableId = "adjustments",
                        FactorField = "back_adj_factor"
                    }
                }
            };

            var engine = BacktestingEngine.Create(
                sessionConfig,
                ts,
                pipeline,
                events,
                monitor: mockMonitor);

            // 启动运行
            engine.Run();

            // 1. 验证冷启动后各标的基准因子:
            // sh.600519 沉淀为 3.0 (2024-12-01 最新有效值)
            // sz.000001 沉淀为 1.2
            // sh.600000 沉淀为 1.0 (缺失兜底)
            var context = (EngineContext)engine.Context;
            Assert.AreEqual(3.0, context.MarketData.ToPostPrice(0, 1.0, 0), 1e-12);
            Assert.AreEqual(1.2, context.MarketData.ToPostPrice(1, 1.0, 0), 1e-12);
            Assert.AreEqual(1.0, context.MarketData.ToPostPrice(2, 1.0, 0), 1e-12);

            // 2. 验证跨模态覆盖校验: 必须触发关于 sh.600000 的 Warning 告警！
            bool hasWarning = logs.Exists(l => l.Contains("[Warning]") && l.Contains("sh.600000"));
            Assert.IsTrue(hasWarning, "必须对存在有效行情但全历史除权表中无记录的标的发出 Warning 告警！");
        }

        [TestMethod]
        public void Engine_ListedIn2025WithFirstAdjIn2026_ShouldWarnStrictCorrespondence()
        {
            var dates = new List<DateTime>
            {
                new(2025, 6, 1),
                new(2025, 6, 2)
            };
            var symbols = new List<string> { "sz.001234" };
            var meta = new SimpleMarketMetadata(dates, symbols);

            var ts = new MockTimeseriesRegistry(meta);
            ts.RegisterMatrix("close", new double[,]
            {
                { 20.0 },
                { 21.0 }
            });

            // 构造除权表: 包含 2026-05-20 的首条除权事件（2025 年无任何除权记录）
            var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "back_adj_factor", 0 } };
            var eventData = new Dictionary<MarketTime, Dictionary<string, List<DataRow>>>();

            var d2026 = new MarketTime(2026, 5, 20);
            eventData[d2026] = new Dictionary<string, List<DataRow>>
            {
                ["sz.001234"] = new List<DataRow> { new DataRow("sz.001234", d2026, new[] { "1.5" }, ordinals) }
            };

            var adjTable = new EventTable("adjustments", new[] { "back_adj_factor" }, eventData);
            var events = new EventRegistry();
            events.Register("adjustments", adjTable);

            var logs = new List<string>();
            var mockMonitor = new TestMonitor(msg => logs.Add(msg));

            var sessionConfig = new SessionConfig
            {
                Engine = new EngineConfig
                {
                    StartDate = new DateTime(2025, 6, 1),
                    EndDate = new DateTime(2025, 6, 2)
                },
                Data = new DataConfig
                {
                    Adjust = new AdjustmentConfig
                    {
                        Mode = AdjustmentMode.Post,
                        TableId = "adjustments",
                        FactorField = "back_adj_factor"
                    }
                }
            };

            var engine = BacktestingEngine.Create(
                sessionConfig,
                ts,
                StrategyPipeline.Sequential(),
                events,
                monitor: mockMonitor);

            engine.Run();

            // 严格一一对应校验: 标的于 2025-06-01 有行情，但首条除权在 2026-05-20，必须触发 Warning！
            bool hasStrictWarning = logs.Exists(l =>
                l.Contains("[Warning]") &&
                l.Contains("sz.001234") &&
                l.Contains("首日:2025-06-01") &&
                l.Contains("首条除权:2026-05-20"));

            Assert.IsTrue(hasStrictWarning, "标的产生行情前未提供复权因子（首条除权在未来），必须严格触发一一对应 Warning 告警！");
        }

        [TestMethod]
        public void Engine_PostModeWithMissingTable_ShouldThrowException()
        {
            var meta = new SimpleMarketMetadata(new[] { DateTime.UtcNow }, new[] { "sh.600000" });
            var ts = new MockTimeseriesRegistry(meta);
            var events = new EventRegistry(); // 未注册任何除权表

            var sessionConfig = new SessionConfig
            {
                Data = new DataConfig
                {
                    Adjust = new AdjustmentConfig
                    {
                        Mode = AdjustmentMode.Post,
                        TableId = "non_existent_adjustments"
                    }
                }
            };

            // 启用了 post 模式但除权表不存在，构造或运行期必须及早抛出强类型 InvalidOperationException
            Assert.ThrowsException<InvalidOperationException>(() =>
            {
                var engine = BacktestingEngine.Create(sessionConfig, ts, StrategyPipeline.Sequential(), events);
                engine.Run();
            });
        }

        [TestMethod]
        public void Engine_WithIndicatorRegistry_ShouldAutoStepIndicatorsInRun()
        {
            var dates = new List<DateTime>
            {
                new(2025, 1, 2),
                new(2025, 1, 3),
                new(2025, 1, 4)
            };
            var symbols = new List<string> { "sh.600000" };
            var meta = new SimpleMarketMetadata(dates, symbols);

            var ts = new MockTimeseriesRegistry(meta);
            // 3 行: 10.0, 20.0, 30.0
            ts.RegisterMatrix("close", new double[,]
            {
                { 10.0 },
                { 20.0 },
                { 30.0 }
            });

            var strategy = new TestIndicatorStrategy();
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            var sessionConfig = new SessionConfig();
            var engine = BacktestingEngine.Create(sessionConfig, ts, pipeline);

            engine.Run();

            // 验证策略在第 3 步 (index = 2) 时收到了已就绪的 SMA = 20.0
            Assert.AreEqual(3, strategy.RecordedValues.Count);
            Assert.IsTrue(double.IsNaN(strategy.RecordedValues[0]));
            Assert.IsTrue(double.IsNaN(strategy.RecordedValues[1]));
            Assert.AreEqual(20.0, strategy.RecordedValues[2], 1e-12);
        }

        private class TestIndicatorStrategy : IStrategy
        {
            public string Name => "TestIndicatorStrategy";
            private IndicatorPool<Sma>? _smaPool;
            public List<double> RecordedValues { get; } = new();

            public void OnStart(IEngineContext context)
            {
                _smaPool = context.Indicators.GetOrAdd("sma3", () => new Sma(3));
            }

            public void OnUpdate(IEngineContext context)
            {
                if (_smaPool != null)
                {
                    double val = _smaPool["sh.600000"].Current;
                    RecordedValues.Add(val);
                }
            }
        }

        [TestMethod]
        public void Engine_ExDividendDuringBacktest_ShouldIncrementallyUpdateDailyFactors()
        {
            // 测试回测中途发生除权事件时的增量自适应因子更新链路
            var dates = new List<DateTime>
            {
                new(2025, 1, 2),
                new(2025, 1, 3),
                new(2025, 1, 4)
            };
            var symbols = new List<string> { "sh.600000" };
            var meta = new SimpleMarketMetadata(dates, symbols);

            var ts = new MockTimeseriesRegistry(meta);
            // 物理价格: 10.0, 11.0, 12.0
            ts.RegisterMatrix("close", new double[,]
            {
                { 10.0 },
                { 11.0 },
                { 12.0 }
            });

            // 构造事件表:
            // 2025-01-02: 初始因子 1.0
            // 2025-01-03: 盘中/跨日发生除权事件，因子跃迁为 2.0！
            var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "back_adj_factor", 0 } };
            var eventData = new Dictionary<MarketTime, Dictionary<string, List<DataRow>>>();

            var d1 = new MarketTime(2025, 1, 2);
            eventData[d1] = new Dictionary<string, List<DataRow>>
            {
                ["sh.600000"] = new List<DataRow> { new DataRow("sh.600000", d1, new[] { "1.0" }, ordinals) }
            };

            var d2 = new MarketTime(2025, 1, 3);
            eventData[d2] = new Dictionary<string, List<DataRow>>
            {
                ["sh.600000"] = new List<DataRow> { new DataRow("sh.600000", d2, new[] { "2.0" }, ordinals) }
            };

            var adjTable = new EventTable("adjustments", new[] { "back_adj_factor" }, eventData);
            var events = new EventRegistry();
            events.Register("adjustments", adjTable);

            var recordedPostPrices = new List<double>();
            var recordedLookbackPrices = new List<double>();
            var strategy = new GenericTestStrategy(ctx =>
            {
                recordedPostPrices.Add(ctx.GetClose("sh.600000", mode: PriceMode.Post));
                if (ctx.CurrentIndex > 0)
                {
                    // 测试负向时序回溯 (offset = -1) 昨日复权收盘价
                    recordedLookbackPrices.Add(ctx.GetClose("sh.600000", offset: -1, mode: PriceMode.Post));
                }
            });

            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            var sessionConfig = new SessionConfig
            {
                Engine = new EngineConfig
                {
                    StartDate = new DateTime(2025, 1, 2),
                    EndDate = new DateTime(2025, 1, 4)
                },
                Data = new DataConfig
                {
                    Adjust = new AdjustmentConfig
                    {
                        Mode = AdjustmentMode.Post,
                        TableId = "adjustments",
                        FactorField = "back_adj_factor"
                    }
                }
            };

            var engine = BacktestingEngine.Create(sessionConfig, ts, pipeline, events);
            engine.Run();

            // 验证增量因子供给:
            // Day 1 (1/2): 10.0 * 1.0 = 10.0
            // Day 2 (1/3): 11.0 * 2.0 = 22.0 (增量除权跃迁)
            // Day 3 (1/4): 12.0 * 2.0 = 24.0 (沿用新因子)
            Assert.AreEqual(3, recordedPostPrices.Count);
            Assert.AreEqual(10.0, recordedPostPrices[0], 1e-12);
            Assert.AreEqual(22.0, recordedPostPrices[1], 1e-12);
            Assert.AreEqual(24.0, recordedPostPrices[2], 1e-12);

            // 验证时序因果律负向守恒:
            // 在 Day 2 (1/3) 时回溯昨日收盘价: 昨日物理 10.0 * 昨日因子 1.0 = 10.0 (绝对不能等于 10.0 * 今日因子 2.0 = 20.0!)
            // 在 Day 3 (1/4) 时回溯昨日收盘价: 11.0 * 2.0 = 22.0
            Assert.AreEqual(2, recordedLookbackPrices.Count);
            Assert.AreEqual(10.0, recordedLookbackPrices[0], 1e-12);
            Assert.AreEqual(22.0, recordedLookbackPrices[1], 1e-12);
        }

        [TestMethod]
        public void Engine_AdjustmentFullPipeline_IndicatorPoolSmoothedAndRawExecutionConservation()
        {
            // 场景：复权全链路闭环（节点1至节点6穿透）
            // 4 个交易日: 10送10除权除息（Day 2 物理价腰斩，因子翻倍为 2.0）
            // 节点5：IndicatorPool<Sma> 自动按 PriceMode.Post 平滑步进，Day 2 消除断崖缺口
            // 节点6：策略在 Day 3 均线金叉发出买单，SimulatedExchangeGateway 必须严格以物理价 Raw 成交
            // 账本扣除物理成交金额，终局资金与市值绝对守恒
            var dates = new List<DateTime>
            {
                new(2025, 1, 2), // Day 0: Raw=20.0, Factor=1.0, Post=20.0
                new(2025, 1, 3), // Day 1: Raw=20.0, Factor=1.0, Post=20.0
                new(2025, 1, 4), // Day 2 (除权日): Raw=10.0, Factor=2.0, Post=20.0 (无跳空缺口!)
                new(2025, 1, 5)  // Day 3: Raw=10.5, Factor=2.0, Post=21.0 (均线上行，触发买入)
            };
            var symbols = new List<string> { "sh.600000" };
            var meta = new SimpleMarketMetadata(dates, symbols);

            var ts = new MockTimeseriesRegistry(meta);
            var closeData = new double[,] { { 20.0 }, { 20.0 }, { 10.0 }, { 10.5 } };
            var openData = new double[,] { { 20.0 }, { 20.0 }, { 10.0 }, { 10.5 } };
            var highData = new double[,] { { 20.5 }, { 20.5 }, { 10.5 }, { 11.0 } };
            var lowData = new double[,] { { 19.5 }, { 19.5 }, { 9.5 }, { 10.0 } };
            var volData = new double[,] { { 10000.0 }, { 10000.0 }, { 20000.0 }, { 15000.0 } };

            ts.RegisterMatrix("close", closeData);
            ts.RegisterMatrix("open", openData);
            ts.RegisterMatrix("high", highData);
            ts.RegisterMatrix("low", lowData);
            ts.RegisterMatrix("volume", volData);

            // 构造事件表: 1/2 因子 1.0, 1/4 除权因子跃迁为 2.0
            var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "back_adj_factor", 0 } };
            var eventData = new Dictionary<MarketTime, Dictionary<string, List<DataRow>>>();

            var d1 = new MarketTime(2025, 1, 2);
            eventData[d1] = new Dictionary<string, List<DataRow>>
            {
                ["sh.600000"] = new List<DataRow> { new DataRow("sh.600000", d1, new[] { "1.0" }, ordinals) }
            };

            var d3 = new MarketTime(2025, 1, 4);
            eventData[d3] = new Dictionary<string, List<DataRow>>
            {
                ["sh.600000"] = new List<DataRow> { new DataRow("sh.600000", d3, new[] { "2.0" }, ordinals) }
            };

            var adjTable = new EventTable("adjustments", new[] { "back_adj_factor" }, eventData);
            var events = new EventRegistry();
            events.Register("adjustments", adjTable);

            // 连接真实的 SimulatedExchangeGateway 与 Broker
            const double initialCash = 100000.0;
            var brokerConfig = new BrokerConfig
            {
                InitialCash = initialCash,
                FeeRate = 0.0,
                Slippage = 0.0,
                VolumeLimit = 0.0, // 禁用成交量限制以纯净验证复权与撮合链路
                DefaultExecutionPrice = ExecutionPriceType.Close
            };
            var simExchange = new SimulatedExchangeGateway(brokerConfig);
            var broker = new Broker(simExchange, brokerConfig);

            var recordedSmaValues = new List<double>();
            long orderId = 0;

            var strategy = new FullAdjustmentPipelineStrategy((ctx, smaPool) =>
            {
                double smaVal = smaPool["sh.600000"].Current;
                recordedSmaValues.Add(smaVal);

                // 在 Day 3 (CurrentIndex == 3)，均线由 20.0 升至 20.5，触发买入 1000 股
                if (ctx.CurrentIndex == 3)
                {
                    orderId = ctx.Buy("sh.600000", 1000m);
                }
            });

            var pipeline = StrategyPipeline.Sequential().Add(strategy);

            var sessionConfig = new SessionConfig
            {
                Engine = new EngineConfig
                {
                    StartDate = dates[0],
                    EndDate = dates[^1],
                    UpdatePhase = EnginePhase.Close
                },
                Data = new DataConfig
                {
                    Adjust = new AdjustmentConfig
                    {
                        Mode = AdjustmentMode.Post,
                        TableId = "adjustments",
                        FactorField = "back_adj_factor"
                    }
                }
            };

            using var engine = new BacktestingEngine(
                ts,
                broker,
                pipeline,
                events: events,
                config: sessionConfig.Engine,
                adjustConfig: sessionConfig.Data.Adjust,
                simExchange: simExchange);

            engine.Run();

            // 1. 验证节点 5：指标池后复权平滑计算
            // Day 0 (index 0): 首次数据，SMA(2) = NaN
            // Day 1 (index 1): (20.0 + 20.0) / 2 = 20.0
            // Day 2 (index 2, 除权日): 物理价跳空到 10.0，但后复权为 20.0，SMA(2) = (20.0 + 20.0) / 2 = 20.0（完全平滑无断崖！）
            // Day 3 (index 3): 今日后复权 21.0，SMA(2) = (20.0 + 21.0) / 2 = 20.5
            Assert.AreEqual(4, recordedSmaValues.Count);
            Assert.IsTrue(double.IsNaN(recordedSmaValues[0]));
            Assert.AreEqual(20.0, recordedSmaValues[1], 1e-12);
            Assert.AreEqual(20.0, recordedSmaValues[2], 1e-12, "除权当日指标池必须使用 Post 价格消除跳空缺口");
            Assert.AreEqual(20.5, recordedSmaValues[3], 1e-12);

            // 2. 验证节点 6：真实物理价格撮合（绝非复权价 21.0）
            Assert.IsTrue(orderId > 0);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(orderId));
            var trades = engine.GetResult().Settlements;
            Assert.AreEqual(1, trades.Count);
            Assert.AreEqual(10.5, trades[0].Price, 1e-12, "撮合引擎必须严格以 Raw 物理价格 10.5 撮合成交，绝对不可按复权价 21.0 撮合！");

            // 3. 验证节点 6：账本资金扣减与盯市绝对守恒
            // 买入 1000 股花 10.5 * 1000 = 10500.0 元，剩余可用现金 = 100000 - 10500 = 89500.0 元
            Assert.AreEqual(89500.0, broker.GetCash(), 1e-4, "账本现金必须按物理成交价扣除");
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
            // 终局持仓市值 = 1000 * 10.5 = 10500.0，总资产 = 89500 + 10500 = 100000.0
            Assert.AreEqual(initialCash, broker.GetTotalEquity(), 1e-4, "回测终局总权益严格守恒");
        }

        [TestMethod]
        public void EngineContext_UnreadyAndMissingFields_ShouldReturnNaN()
        {
            var meta = new SimpleMarketMetadata(new[] { DateTime.UtcNow }, new[] { "sh.600000" });
            var ts = new MockTimeseriesRegistry(meta);
            var broker = new FakeBroker();
            var context = new EngineContext(ts, broker);

            // 1. CurrentIndex == -1 未就绪状态下，所有取价门面严格防御返回 NaN
            Assert.IsTrue(double.IsNaN(context.GetClose(0)));
            Assert.IsTrue(double.IsNaN(context.GetOpen(0)));
            Assert.IsTrue(double.IsNaN(context.GetHigh(0)));
            Assert.IsTrue(double.IsNaN(context.GetLow(0)));
            Assert.IsTrue(double.IsNaN(context.GetVolume(0)));
            Assert.IsTrue(double.IsNaN(context.GetField(0, "close")));

            // 2. 步进后读取未注册字段返回 NaN，零异常抛出
            context.UpdateStep(0, DateTime.UtcNow);
            Assert.IsTrue(double.IsNaN(context.GetField(0, "non_existent_matrix")));
            Assert.IsTrue(double.IsNaN(context.GetField(0, "")));
        }

        [TestMethod]
        public void EngineContext_PriceFieldsWhitelist_NonPriceFieldShouldNotMultiplyFactor()
        {
            var meta = new SimpleMarketMetadata(new[] { DateTime.UtcNow }, new[] { "sh.600000" });
            var ts = new MockTimeseriesRegistry(meta);
            // close 属于价格类，pe 属于比率类
            ts.RegisterMatrix("close", new double[,] { { 10.0 } });
            ts.RegisterMatrix("pe", new double[,] { { 15.0 } });

            var factorMatrix = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 1);
            factorMatrix[0, 0] = 2.0; // 因子 2.0
            using var context = new EngineContext(ts, new FakeBroker(), priceFields: new[] { "close" }, factorMatrix: factorMatrix, stepToDateIndex: new[] { 0 });
            context.UpdateStep(0, DateTime.UtcNow);

            // 价格类字段在 Post 模式下乘以因子
            Assert.AreEqual(20.0, context.GetClose(0, mode: PriceMode.Post), 1e-12);
            Assert.AreEqual(20.0, context.GetField(0, "close", mode: PriceMode.Post), 1e-12);

            // 非价格类字段（pe）即使请求 PriceMode.Post 也严禁盲目乘因子，保持原始值 15.0！
            Assert.AreEqual(15.0, context.GetField(0, "pe", mode: PriceMode.Post), 1e-12);
            Assert.AreEqual(15.0, context.GetField(0, "pe", mode: PriceMode.Raw), 1e-12);
        }

        [TestMethod]
        public void EngineContext_GetVolume_ShouldAlwaysReturnRaw()
        {
            var meta = new SimpleMarketMetadata(new[] { DateTime.UtcNow }, new[] { "sh.600000" });
            var ts = new MockTimeseriesRegistry(meta);
            ts.RegisterMatrix("volume", new double[,] { { 50000.0 } });

            var factorMatrix = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 1);
            factorMatrix[0, 0] = 2.5;
            using var context = new EngineContext(ts, new FakeBroker(), factorMatrix: factorMatrix, stepToDateIndex: new[] { 0 });
            context.UpdateStep(0, DateTime.UtcNow);

            // 成交量严格保持物理原始量（Raw 股数），防止产生虚构成交量
            Assert.AreEqual(50000.0, context.GetVolume(0, mode: PriceMode.Raw), 1e-12);
            Assert.AreEqual(50000.0, context.GetVolume(0, mode: PriceMode.Post), 1e-12);
            Assert.AreEqual(50000.0, context.GetVolume("sh.600000", mode: PriceMode.Post), 1e-12);
        }

        [TestMethod]
        public void EngineContext_FutureOffset_AllFacadesMustThrowArgumentOutOfRangeException()
        {
            var meta = new SimpleMarketMetadata(new[] { DateTime.UtcNow }, new[] { "sh.600000" });
            var ts = new MockTimeseriesRegistry(meta);
            ts.RegisterMatrix("close", new double[,] { { 10.0 } });
            ts.RegisterMatrix("open", new double[,] { { 9.0 } });
            ts.RegisterMatrix("high", new double[,] { { 11.0 } });
            ts.RegisterMatrix("low", new double[,] { { 8.5 } });
            ts.RegisterMatrix("volume", new double[,] { { 1000.0 } });

            using var context = new EngineContext(ts, new FakeBroker());
            context.UpdateStep(0, DateTime.UtcNow);

            // 前瞻偏差防御：所有取价门面只要 offset > 0 必须抛出 ArgumentOutOfRangeException
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.GetClose("sh.600000", offset: 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.GetClose(0, offset: 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.GetOpen("sh.600000", offset: 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.GetOpen(0, offset: 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.GetHigh("sh.600000", offset: 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.GetHigh(0, offset: 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.GetLow("sh.600000", offset: 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.GetLow(0, offset: 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.GetVolume("sh.600000", offset: 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.GetVolume(0, offset: 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.GetField("sh.600000", "close", offset: 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.GetField(0, "close", offset: 1));
        }

        [TestMethod]
        public void Engine_MultiStepLookback_ExDividendCausalityConservation()
        {
            // 验证连续多次除权场景下的深层时序因果律守恒
            var dates = new List<DateTime>
            {
                new(2025, 1, 2), // Day 0
                new(2025, 1, 3), // Day 1
                new(2025, 1, 6), // Day 2: 首次除权
                new(2025, 1, 7), // Day 3
                new(2025, 1, 8)  // Day 4: 二次除权
            };
            var symbols = new List<string> { "sh.600000" };
            var meta = new SimpleMarketMetadata(dates, symbols);

            var ts = new MockTimeseriesRegistry(meta);
            // 物理价格矩阵
            ts.RegisterMatrix("close", new double[,]
            {
                { 10.0 }, // Day 0
                { 11.0 }, // Day 1
                { 6.0 },  // Day 2 (除权后物理价 6.0)
                { 6.5 },  // Day 3
                { 4.0 }   // Day 4 (再次除权后物理价 4.0)
            });

            // 构造事件表:
            // 2025-01-02: 初始因子 1.0
            // 2025-01-06: 因子跃迁至 2.0
            // 2025-01-08: 因子跃迁至 3.0
            var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "back_adj_factor", 0 } };
            var eventData = new Dictionary<MarketTime, Dictionary<string, List<DataRow>>>();

            var d0 = new MarketTime(2025, 1, 2);
            eventData[d0] = new Dictionary<string, List<DataRow>>
            {
                ["sh.600000"] = new List<DataRow> { new DataRow("sh.600000", d0, new[] { "1.0" }, ordinals) }
            };
            var d2 = new MarketTime(2025, 1, 6);
            eventData[d2] = new Dictionary<string, List<DataRow>>
            {
                ["sh.600000"] = new List<DataRow> { new DataRow("sh.600000", d2, new[] { "2.0" }, ordinals) }
            };
            var d4 = new MarketTime(2025, 1, 8);
            eventData[d4] = new Dictionary<string, List<DataRow>>
            {
                ["sh.600000"] = new List<DataRow> { new DataRow("sh.600000", d4, new[] { "3.0" }, ordinals) }
            };

            var adjTable = new EventTable("adjustments", new[] { "back_adj_factor" }, eventData);
            var events = new EventRegistry();
            events.Register("adjustments", adjTable);

            // 在回测推进到最后一天 (Day 4) 时，测试深度历史回溯的因子完全正确
            double day4Current = 0.0;
            double day4Yesterday = 0.0;
            double day4Day1 = 0.0;
            double day4Day0 = 0.0;

            var strategy = new GenericTestStrategy(ctx =>
            {
                if (ctx.CurrentIndex == 4)
                {
                    day4Current = ctx.GetClose("sh.600000", offset: 0, mode: PriceMode.Post);
                    day4Yesterday = ctx.GetClose("sh.600000", offset: -1, mode: PriceMode.Post);
                    day4Day1 = ctx.GetClose("sh.600000", offset: -3, mode: PriceMode.Post);
                    day4Day0 = ctx.GetClose("sh.600000", offset: -4, mode: PriceMode.Post);
                }
            });

            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            var sessionConfig = new SessionConfig
            {
                Engine = new EngineConfig
                {
                    StartDate = new DateTime(2025, 1, 2),
                    EndDate = new DateTime(2025, 1, 8)
                },
                Data = new DataConfig
                {
                    Adjust = new AdjustmentConfig
                    {
                        Mode = AdjustmentMode.Post,
                        TableId = "adjustments",
                        FactorField = "back_adj_factor"
                    }
                }
            };

            var engine = BacktestingEngine.Create(sessionConfig, ts, pipeline, events);
            engine.Run();

            // 验证时序因果律与状态守恒：
            // Day 4 (1/8): 物理 4.0 * 因子 3.0 = 12.0
            Assert.AreEqual(12.0, day4Current, 1e-12);
            // Day 3 (1/7, offset -1): 物理 6.5 * 昨日因子 2.0 = 13.0 (绝非 6.5 * 3.0 = 19.5!)
            Assert.AreEqual(13.0, day4Yesterday, 1e-12);
            // Day 1 (1/3, offset -3): 物理 11.0 * 当时因子 1.0 = 11.0 (绝非 11.0 * 3.0 = 33.0!)
            Assert.AreEqual(11.0, day4Day1, 1e-12);
            // Day 0 (1/2, offset -4): 物理 10.0 * 当时因子 1.0 = 10.0 (绝非 10.0 * 3.0 = 30.0!)
            Assert.AreEqual(10.0, day4Day0, 1e-12);
        }

        [TestMethod]
        public void Engine_AdjustmentCache_Lz4PersistenceAndReload()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "CarrotQuant_AdjCacheTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var dates = new List<DateTime>
                {
                    new(2025, 1, 2),
                    new(2025, 1, 3)
                };
                var symbols = new List<string> { "sh.600000" };
                var meta = new SimpleMarketMetadata(dates, symbols);

                var ts = new MockTimeseriesRegistry(meta);
                ts.RegisterMatrix("close", new double[,] { { 10.0 }, { 11.0 } });

                var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "back_adj_factor", 0 } };
                var eventData = new Dictionary<MarketTime, Dictionary<string, List<DataRow>>>();
                var d0 = new MarketTime(2025, 1, 2);
                eventData[d0] = new Dictionary<string, List<DataRow>>
                {
                    ["sh.600000"] = new List<DataRow> { new DataRow("sh.600000", d0, new[] { "2.5" }, ordinals) }
                };
                var adjTable = new EventTable("adjustments", new[] { "back_adj_factor" }, eventData);
                var events = new EventRegistry();
                events.Register("adjustments", adjTable);

                var sessionConfig = new SessionConfig
                {
                    Engine = new EngineConfig
                    {
                        StartDate = new DateTime(2025, 1, 2),
                        EndDate = new DateTime(2025, 1, 3)
                    },
                    Data = new DataConfig
                    {
                        Adjust = new AdjustmentConfig
                        {
                            Mode = AdjustmentMode.Post,
                            TableId = "adjustments",
                            FactorField = "back_adj_factor",
                            Cache = true,
                            StorageRoot = tempDir
                        }
                    }
                };

                // 首次运行：构建并持久化二进制缓存
                using (var engine1 = BacktestingEngine.Create(sessionConfig, ts, StrategyPipeline.Sequential(), events))
                {
                    engine1.Run();
                    var ctx1 = (EngineContext)engine1.Context;
                    Assert.AreEqual(2.5, ctx1.MarketData.ToPostPrice(0, 1.0, 0), 1e-12);
                }

                // 验证磁盘上生成了 LZ4 缓存文件
                string cacheDir = Path.Combine(tempDir, ".cache", "adjustments");
                Assert.IsTrue(Directory.Exists(cacheDir), "必须生成缓存表目录");
                Assert.IsTrue(File.Exists(Path.Combine(cacheDir, "symbols.idx")), "必须生成 symbols.idx");
                Assert.IsTrue(File.Exists(Path.Combine(cacheDir, "time.idx")), "必须生成 time.idx");
                Assert.IsTrue(Directory.Exists(Path.Combine(cacheDir, "back_adj_factor")), "必须生成字段缓存目录");

                // 第二次运行：直接从 LZ4 二进制缓存命中加载
                using (var engine2 = BacktestingEngine.Create(sessionConfig, ts, StrategyPipeline.Sequential(), events))
                {
                    engine2.Run();
                    var ctx2 = (EngineContext)engine2.Context;
                    Assert.AreEqual(2.5, ctx2.MarketData.ToPostPrice(0, 1.0, 0), 1e-12);
                    Assert.AreEqual(27.5, ctx2.GetClose("sh.600000", offset: 0, mode: PriceMode.Post), 1e-12); // 11.0 * 2.5
                    Assert.AreEqual(25.0, ctx2.GetClose("sh.600000", offset: -1, mode: PriceMode.Post), 1e-12); // 10.0 * 2.5
                }
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [TestMethod]
        public void EngineContext_Dispose_ShouldSafelyReleaseFactorMatrix()
        {
            var meta = new SimpleMarketMetadata(new[] { DateTime.UtcNow }, new[] { "sh.600000" });
            var ts = new MockTimeseriesRegistry(meta);
            ts.RegisterMatrix("close", new double[,] { { 10.0 } });

            var factorMatrix = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 1);
            factorMatrix[0, 0] = 2.0;
            var context = new EngineContext(ts, new FakeBroker(), factorMatrix: factorMatrix, stepToDateIndex: new[] { 0 });
            context.UpdateStep(0, DateTime.UtcNow);

            Assert.AreEqual(2.0, context.MarketData.ToPostPrice(0, 1.0, 0), 1e-12);
            Assert.AreEqual(20.0, context.GetClose(0, mode: PriceMode.Post), 1e-12);

            // 显式释放非托管资源
            context.Dispose();

            // 释放后验证：复权降级为物理价，零崩溃或悬挂指针异常
            Assert.AreEqual(10.0, context.GetClose(0, mode: PriceMode.Post), 1e-12);
        }

        [TestMethod]
        public void AdjustmentProvider_FullHistoryLoading_ShouldNotBePrunedByStartDate()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "CarrotQuant_AdjPruneTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // 创建一个模拟的 adjustments Hive 表:
                // year=2024/data.csv (历史除权)
                // year=2025/data.csv (当前除权)
                string dir2024 = Path.Combine(tempDir, "adjustments", "year=2024");
                string dir2025 = Path.Combine(tempDir, "adjustments", "year=2025");
                Directory.CreateDirectory(dir2024);
                Directory.CreateDirectory(dir2025);

                File.WriteAllText(Path.Combine(dir2024, "data.csv"), "date,symbol,back_adj_factor\n2024-06-01,sh.600000,2.0\n");
                File.WriteAllText(Path.Combine(dir2025, "data.csv"), "date,symbol,back_adj_factor\n2025-06-01,sh.600000,3.0\n");

                var resolver = new StorageResolver(tempDir);
                var adjConfig = new AdjustmentConfig
                {
                    Mode = AdjustmentMode.Post,
                    TableId = "adjustments",
                    FactorField = "back_adj_factor",
                    Cache = false
                };

                // 回测时序元数据从 2025-01-02 开始
                var tradeDates = new List<MarketTime>
                {
                    new(2025, 1, 2),
                    new(2025, 6, 2)
                };
                var meta = new DynamicMarketMetadata(new[] { "sh.600000" }, tradeDates);

                // 专职通道加载：即使回测从 2025 年开始，历史 2024 年的除权因子 2.0 仍被完整吸收到 2025-01-02 的基准中！
                var (factorMatrix, stepMap) = AdjustmentProvider.Resolve(adjConfig, meta, resolver: resolver);
                Assert.IsNotNull(factorMatrix);
                Assert.AreEqual(2, factorMatrix.RowCount);
                Assert.AreEqual(1, factorMatrix.Width);

                // 2025-01-02 应当继承 2024-06-01 的除权因子 2.0
                Assert.AreEqual(2.0, factorMatrix[0, 0], 1e-12);
                // 2025-06-02 应当应用 2025-06-01 最新的除权因子 3.0
                Assert.AreEqual(3.0, factorMatrix[1, 0], 1e-12);

                factorMatrix.Dispose();
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [TestMethod]
        public void Engine_WeekendExDividendEvent_ShouldBeAppliedOnNextTradingDay()
        {
            // 真实场景：除权事件发生在周日 (2025-01-05)，交易日为周五 (2025-01-03) 与周一 (2025-01-06)
            var dates = new List<DateTime>
            {
                new(2025, 1, 3), // Friday
                new(2025, 1, 6)  // Monday
            };
            var symbols = new List<string> { "sh.600000" };
            var meta = new SimpleMarketMetadata(dates, symbols);

            var ts = new MockTimeseriesRegistry(meta);
            ts.RegisterMatrix("close", new double[,]
            {
                { 10.0 }, // Friday
                { 5.5 }   // Monday (除权后价格)
            });

            // 构造事件表:
            // 2025-01-03 (周五): 初始因子 1.0
            // 2025-01-05 (周日，非交易日): 除权事件，因子跃迁至 2.0！
            var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "back_adj_factor", 0 } };
            var eventData = new Dictionary<MarketTime, Dictionary<string, List<DataRow>>>();

            var dFri = new MarketTime(2025, 1, 3);
            eventData[dFri] = new Dictionary<string, List<DataRow>>
            {
                ["sh.600000"] = new List<DataRow> { new DataRow("sh.600000", dFri, new[] { "1.0" }, ordinals) }
            };
            var dSun = new MarketTime(2025, 1, 5); // 非交易日周末
            eventData[dSun] = new Dictionary<string, List<DataRow>>
            {
                ["sh.600000"] = new List<DataRow> { new DataRow("sh.600000", dSun, new[] { "2.0" }, ordinals) }
            };

            var adjTable = new EventTable("adjustments", new[] { "back_adj_factor" }, eventData);
            var events = new EventRegistry();
            events.Register("adjustments", adjTable);

            double mondayPostClose = 0.0;
            double mondayLookbackFriday = 0.0;

            var strategy = new GenericTestStrategy(ctx =>
            {
                if (ctx.CurrentIndex == 1) // 周一
                {
                    mondayPostClose = ctx.GetClose("sh.600000", offset: 0, mode: PriceMode.Post);
                    mondayLookbackFriday = ctx.GetClose("sh.600000", offset: -1, mode: PriceMode.Post);
                }
            });

            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            var sessionConfig = new SessionConfig
            {
                Engine = new EngineConfig
                {
                    StartDate = new DateTime(2025, 1, 3),
                    EndDate = new DateTime(2025, 1, 6)
                },
                Data = new DataConfig
                {
                    Adjust = new AdjustmentConfig
                    {
                        Mode = AdjustmentMode.Post,
                        TableId = "adjustments",
                        FactorField = "back_adj_factor"
                    }
                }
            };

            using var engine = BacktestingEngine.Create(sessionConfig, ts, pipeline, events);
            engine.Run();

            // 验证周日非交易日的除权除息被周一精准吸收：
            // 周一收盘后复权价: 5.5 * 2.0 = 11.0 (绝非因丢失周末事件而错误计算为 5.5 * 1.0 = 5.5!)
            Assert.AreEqual(11.0, mondayPostClose, 1e-12);
            // 周一回溯周五收盘价: 10.0 * 1.0 = 10.0 (因果律完全守恒)
            Assert.AreEqual(10.0, mondayLookbackFriday, 1e-12);
        }

        [TestMethod]
        public void Engine_NarrowCacheWidth_ShouldRebuildOrSafelyFallbackWithoutCrashing()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "CarrotQuant_NarrowCacheTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var dates = new List<DateTime> { new(2025, 1, 2) };
                // 1. 首次仅回测 1 只股票 "sh.600000"，生成宽度为 1 的缓存
                var symbols1 = new List<string> { "sh.600000" };
                var meta1 = new SimpleMarketMetadata(dates, symbols1);
                var ts1 = new MockTimeseriesRegistry(meta1);
                ts1.RegisterMatrix("close", new double[,] { { 10.0 } });

                var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "back_adj_factor", 0 } };
                var eventData = new Dictionary<MarketTime, Dictionary<string, List<DataRow>>>();
                var d0 = new MarketTime(2025, 1, 2);
                eventData[d0] = new Dictionary<string, List<DataRow>>
                {
                    ["sh.600000"] = new List<DataRow> { new DataRow("sh.600000", d0, new[] { "2.0" }, ordinals) },
                    ["sz.000001"] = new List<DataRow> { new DataRow("sz.000001", d0, new[] { "3.0" }, ordinals) }
                };
                var adjTable = new EventTable("adjustments", new[] { "back_adj_factor" }, eventData);
                var events = new EventRegistry();
                events.Register("adjustments", adjTable);

                var sessionConfig1 = new SessionConfig
                {
                    Engine = new EngineConfig { StartDate = new DateTime(2025, 1, 2), EndDate = new DateTime(2025, 1, 2) },
                    Data = new DataConfig
                    {
                        Adjust = new AdjustmentConfig
                        {
                            Mode = AdjustmentMode.Post,
                            TableId = "adjustments",
                            FactorField = "back_adj_factor",
                            Cache = true,
                            StorageRoot = tempDir
                        }
                    }
                };

                using (var engine1 = BacktestingEngine.Create(sessionConfig1, ts1, StrategyPipeline.Sequential(), events))
                {
                    engine1.Run();
                }

                // 2. 扩容标的池至 2 只股票（"sh.600000", "sz.000001"）
                var symbols2 = new List<string> { "sh.600000", "sz.000001" };
                var meta2 = new SimpleMarketMetadata(dates, symbols2);
                var ts2 = new MockTimeseriesRegistry(meta2);
                ts2.RegisterMatrix("close", new double[,] { { 10.0, 20.0 } });

                var sessionConfig2 = new SessionConfig
                {
                    Engine = new EngineConfig { StartDate = new DateTime(2025, 1, 2), EndDate = new DateTime(2025, 1, 2) },
                    Data = new DataConfig
                    {
                        Adjust = new AdjustmentConfig
                        {
                            Mode = AdjustmentMode.Post,
                            TableId = "adjustments",
                            FactorField = "back_adj_factor",
                            Cache = true,
                            StorageRoot = tempDir
                        }
                    }
                };

                // 第二次运行：缓存中仅有宽度为 1 的旧数据，系统必须检测出列宽不一致并重新全量构建，严禁抛出 IndexOutOfRangeException
                using var engine2 = BacktestingEngine.Create(sessionConfig2, ts2, StrategyPipeline.Sequential(), events);
                engine2.Run();

                var ctx2 = (EngineContext)engine2.Context;
                Assert.AreEqual(20.0, ctx2.GetClose("sh.600000", mode: PriceMode.Post), 1e-12); // 10.0 * 2.0
                Assert.AreEqual(60.0, ctx2.GetClose("sz.000001", mode: PriceMode.Post), 1e-12); // 20.0 * 3.0
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private class GenericTestStrategy : IStrategy
        {
            public string Name => "GenericTestStrategy";
            private readonly Action<IEngineContext> _onUpdate;
            public GenericTestStrategy(Action<IEngineContext> onUpdate) => _onUpdate = onUpdate;
            public void OnUpdate(IEngineContext context) => _onUpdate(context);
        }

        #endregion

        #region Helpers

        private class SimpleMarketMetadata : IMarketMetadata
        {
            public IReadOnlyList<MarketTime> TradeDates { get; }
            public IReadOnlyList<string> Symbols { get; }
            public IReadOnlyList<string> FieldNames { get; } = new List<string> { "close", "open", "volume" };

            public SimpleMarketMetadata(IReadOnlyList<MarketTime> dates, IReadOnlyList<string> symbols)
            {
                TradeDates = dates;
                Symbols = symbols;
            }

            public SimpleMarketMetadata(IReadOnlyList<DateTime> dates, IReadOnlyList<string> symbols)
                : this(dates.Select(d => (MarketTime)d).ToList(), symbols)
            {
            }

            public Type GetFieldType(string fieldName) => typeof(double);
            public int GetDateIndex(MarketTime date)
            {
                for (int i = 0; i < TradeDates.Count; i++)
                {
                    if (TradeDates[i].Date == date.Date) return i;
                }
                return -1;
            }

            public int GetSymbolIndex(string symbol)
            {
                for (int i = 0; i < Symbols.Count; i++)
                {
                    if (Symbols[i].Equals(symbol, StringComparison.OrdinalIgnoreCase)) return i;
                }
                return -1;
            }
        }

        private class MockTimeseriesRegistry : ITimeseriesRegistry
        {
            private readonly Dictionary<string, MockMatrix> _matrices = new(StringComparer.OrdinalIgnoreCase);
            private readonly MockTimeseriesTable _primaryTable;

            public ITimeseriesTable Primary => _primaryTable;
            public IMarketMetadata Metadata => _primaryTable.Metadata;

            public ITimeseriesTable this[string tableName] => GetTable(tableName);
            public ITimeseriesTable GetTable(string tableName) => tableName == _primaryTable.Name ? _primaryTable : throw new KeyNotFoundException($"时序表 '{tableName}' 不存在。");
            public bool HasTable(string tableName) => tableName == _primaryTable.Name;
            public IReadOnlyList<string> GetTableNames() => new[] { _primaryTable.Name };

            public MockTimeseriesRegistry(IMarketMetadata metadata)
            {
                _primaryTable = new MockTimeseriesTable(metadata, f => _matrices.TryGetValue(f, out var m) ? m : null, HasField);
            }

            public void RegisterMatrix(string field, double[,] data)
            {
                _matrices[field] = new MockMatrix(data);
            }

            public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged => _primaryTable.GetMatrix<T>(fieldName);
            public bool HasField(string fieldName) => _matrices.ContainsKey(fieldName);
            public bool IsUpdated(int tableIndex) => true;
            public bool IsUpdated(string tableName) => true;
            public int GetTableIndex(string tableName) => 0;
            public void StepTo(int rowIndex) { }
            public void Commit() { }
            public void Dispose() { }
        }

        private class MockMatrix : IReadOnlyMatrix2D<double>
        {
            private readonly double[,] _data;
            public int RowCount => _data.GetLength(0);
            public int Width => _data.GetLength(1);

            public MockMatrix(double[,] data) => _data = data;

            public ref readonly double this[int r, int c] => ref _data[r, c];

            public CarrotBacktesting.NET.Abstraction.Data.ReadOnlyRowView<double> GetRowView(int row, int col, int len)
            {
                return new CarrotBacktesting.NET.Abstraction.Data.ReadOnlyRowView<double>(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref _data[row, col], len));
            }
            public CarrotBacktesting.NET.Abstraction.Data.ReadOnlyColumnView<double> GetColumnView(int row, int col, int len) => throw new NotImplementedException();
        }

        private class FakeBroker : IBroker
        {
            public double GetCash() => 100000.0;
            public double GetTotalEquity() => GetCash();
            public decimal GetPosition(string symbol) => 0m;
            public decimal GetAvailablePosition(string symbol) => GetPosition(symbol);
            public double GetPositionCost(string symbol) => 0.0;
            public IReadOnlyDictionary<string, decimal> GetPositions() => new Dictionary<string, decimal>();
            public long SubmitOrder(OrderRequest request) => 1L;
            public void CancelOrder(long orderId) { }
            public OrderStatus GetOrderStatus(long orderId) => OrderStatus.Filled;

#pragma warning disable CS0067
            public event Action<long, OrderStatus>? OnOrderStatusChanged;
            public event Action<ExecutionReport>? OnTrade;
            public event Action<PositionEvent>? OnPositionChanged;
            public event Action<OrderEvent>? OnOrderEvent;
#pragma warning restore CS0067
        }

        private class TestMonitor : IMonitor
        {
            private readonly Action<string> _onLog;
            public TestMonitor(Action<string> onLog) => _onLog = onLog;

            public void Log(string message, LogLevel level = LogLevel.Info) => _onLog($"[{level}] {message}");
            public void ReportMetric(string metricName, double value) { }
            public void Alert(string message) { }
        }

        private class FullAdjustmentPipelineStrategy : IStrategy
        {
            public string Name => "FullAdjustmentPipelineStrategy";
            public int Lookback => 5;
            private readonly Action<IEngineContext, IndicatorPool<Sma>> _onUpdate;
            private IndicatorPool<Sma>? _smaPool;

            public FullAdjustmentPipelineStrategy(Action<IEngineContext, IndicatorPool<Sma>> onUpdate)
            {
                _onUpdate = onUpdate;
            }

            public void OnStart(IEngineContext context)
            {
                _smaPool = context.Indicators.GetOrAdd("sma2", () => new Sma(2));
            }

            public void OnUpdate(IEngineContext context)
            {
                if (_smaPool != null)
                {
                    _onUpdate(context, _smaPool);
                }
            }

            public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
            public void OnStop(IEngineContext context) { }
        }

        #endregion
    }
}
