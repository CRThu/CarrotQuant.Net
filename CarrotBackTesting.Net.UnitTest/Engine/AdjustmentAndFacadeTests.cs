using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Indicators;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
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
            var context = new EngineContext(ts, broker);

            // 注入因子: sh.600000 为 2.0, sz.000001 为 1.5
            context.SetDailyFactors(new double[] { 2.0, 1.5 });

            // 步进至第 0 步
            context.UpdateStep(0, meta.TradeDates[0]);

            // 1. 收盘价: 后复权 vs 未复权
            Assert.AreEqual(20.0, context.GetClose("sh.600000", PriceMode.Post), 1e-12); // 10.0 * 2.0
            Assert.AreEqual(10.0, context.GetClose("sh.600000", PriceMode.Raw), 1e-12);
            Assert.AreEqual(30.0, context.GetClose(1, PriceMode.Post), 1e-12);             // 20.0 * 1.5
            Assert.AreEqual(20.0, context.GetClose(1, PriceMode.Raw), 1e-12);

            // 2. 开盘价
            Assert.AreEqual(18.0, context.GetOpen("sh.600000", PriceMode.Post), 1e-12);  // 9.0 * 2.0
            Assert.AreEqual(9.0, context.GetOpen("sh.600000", PriceMode.Raw), 1e-12);

            // 3. 成交量: 默认保持 Raw 物理量
            Assert.AreEqual(1000.0, context.GetVolume("sh.600000"), 1e-12);
            Assert.AreEqual(1000.0, context.GetVolume(0), 1e-12);

            // 4. 通用字段 GetField
            Assert.AreEqual(20.0, context.GetField("sh.600000", "close", PriceMode.Post), 1e-12);
            Assert.AreEqual(10.0, context.GetField(0, "close", PriceMode.Raw), 1e-12);

            // 5. 越界与未知标的防御返回 NaN
            Assert.IsTrue(double.IsNaN(context.GetClose("unknown.symbol")));
            Assert.IsTrue(double.IsNaN(context.GetClose(999)));

            // 6. 增量更新因子
            context.UpdateFactor(0, 3.0); // 更新 sh.600000 因子为 3.0
            Assert.AreEqual(30.0, context.GetClose("sh.600000", PriceMode.Post), 1e-12); // 10.0 * 3.0
            Assert.AreEqual(10.0, context.GetClose("sh.600000", PriceMode.Raw), 1e-12);  // 物理价格不受影响
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
                        Mode = "post",
                        Table = "adjustments",
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
            Assert.AreEqual(3.0, context.DailyFactors[0], 1e-12);
            Assert.AreEqual(1.2, context.DailyFactors[1], 1e-12);
            Assert.AreEqual(1.0, context.DailyFactors[2], 1e-12);

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
                        Mode = "post",
                        Table = "adjustments",
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
                        Mode = "post",
                        Table = "non_existent_adjustments"
                    }
                }
            };

            var engine = BacktestingEngine.Create(sessionConfig, ts, StrategyPipeline.Sequential(), events);

            // 启用了 post 模式但除权表不存在，必须抛出强类型 InvalidOperationException
            Assert.ThrowsException<InvalidOperationException>(() => engine.Run());
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
            var strategy = new GenericTestStrategy(ctx =>
            {
                recordedPostPrices.Add(ctx.GetClose("sh.600000", PriceMode.Post));
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
                        Mode = "post",
                        Table = "adjustments",
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

            var context = new EngineContext(ts, new FakeBroker(), priceFields: new[] { "close" });
            context.SetDailyFactors(new[] { 2.0 }); // 因子 2.0
            context.UpdateStep(0, DateTime.UtcNow);

            // 价格类字段在 Post 模式下乘以因子
            Assert.AreEqual(20.0, context.GetClose(0, PriceMode.Post), 1e-12);
            Assert.AreEqual(20.0, context.GetField(0, "close", PriceMode.Post), 1e-12);

            // 非价格类字段（pe）即使请求 PriceMode.Post 也严禁盲目乘因子，保持原始值 15.0！
            Assert.AreEqual(15.0, context.GetField(0, "pe", PriceMode.Post), 1e-12);
            Assert.AreEqual(15.0, context.GetField(0, "pe", PriceMode.Raw), 1e-12);
        }

        [TestMethod]
        public void EngineContext_GetVolume_ShouldAlwaysReturnRaw()
        {
            var meta = new SimpleMarketMetadata(new[] { DateTime.UtcNow }, new[] { "sh.600000" });
            var ts = new MockTimeseriesRegistry(meta);
            ts.RegisterMatrix("volume", new double[,] { { 50000.0 } });

            var context = new EngineContext(ts, new FakeBroker());
            context.SetDailyFactors(new[] { 2.5 });
            context.UpdateStep(0, DateTime.UtcNow);

            // 成交量严格保持物理原始量（Raw 股数），防止产生虚构成交量
            Assert.AreEqual(50000.0, context.GetVolume(0, PriceMode.Raw), 1e-12);
            Assert.AreEqual(50000.0, context.GetVolume(0, PriceMode.Post), 1e-12);
            Assert.AreEqual(50000.0, context.GetVolume("sh.600000", PriceMode.Post), 1e-12);
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
            public IMarketMetadata Metadata { get; }

            public MockTimeseriesRegistry(IMarketMetadata metadata) => Metadata = metadata;

            public void RegisterMatrix(string field, double[,] data)
            {
                _matrices[field] = new MockMatrix(data);
            }

            public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged
            {
                if (_matrices.TryGetValue(fieldName, out var m))
                {
                    return (IReadOnlyMatrix2D<T>)(object)m;
                }
                throw new KeyNotFoundException($"Field {fieldName} not found.");
            }

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

            public CarrotBacktesting.NET.Abstraction.Data.ReadOnlyRowView<double> GetRowView(int row, int col, int len) => throw new NotImplementedException();
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
            public string SubmitOrder(OrderRequest request) => "ord-1";
            public void CancelOrder(string orderId) { }
            public OrderStatus GetOrderStatus(string orderId) => OrderStatus.Filled;

#pragma warning disable CS0067
            public event Action<string, OrderStatus>? OnOrderStatusChanged;
            public event Action<ExecutionReport>? OnTrade;
            public event Action<PositionEvent>? OnPositionChanged;
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

        #endregion
    }
}
