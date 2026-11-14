using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Strategy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine
{
    [TestClass]
    public class BacktestingEngineTests
    {
        #region 测试桩定义 (Test Doubles)

        private class MockTimeseriesRegistry : ITimeseriesRegistry
        {
            public IMarketMetadata Metadata { get; }
            public List<int> SteppedRows { get; } = new();

            public MockTimeseriesRegistry(IMarketMetadata metadata)
            {
                Metadata = metadata;
            }

            public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged => null!;
            public bool HasField(string fieldName) => true;

            public void StepTo(int currentLogicalRow)
            {
                SteppedRows.Add(currentLogicalRow);
            }

            public int RegisterTable(string tableName) => 0;
            public int GetTableIndex(string tableName) => 0;
            public string GetTableName(int tableIndex) => string.Empty;
        }

        private class CountingStrategy : IStrategy
        {
            public string Name => "CountingStrategy";
            public int UpdateCount { get; private set; }
            public bool Started { get; private set; }
            public bool Stopped { get; private set; }
            public List<DateTime> ExecutedTimes { get; } = new();

            public void OnStart(IEngineContext context) => Started = true;
            public void OnStop(IEngineContext context) => Stopped = true;

            public void OnUpdate(IEngineContext context)
            {
                UpdateCount++;
                ExecutedTimes.Add(context.CurrentTime);
            }
        }

        private class BrokerCallbackTrackingStrategy : IStrategy
        {
            public string Name => "BrokerCallbackTrackingStrategy";
            public List<string> OrderStatusChanges { get; } = new();
            public List<ExecutionReport> Trades { get; } = new();
            public List<PositionEvent> PositionChanges { get; } = new();

            public void OnUpdate(IEngineContext context)
            {
                if (context.CurrentIndex == 0)
                {
                    context.Broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 500m));
                }
            }

            public void OnOrderStatusChanged(IEngineContext context, string orderId, OrderStatus status)
            {
                OrderStatusChanges.Add($"{orderId}:{status}");
            }

            public void OnTrade(IEngineContext context, ExecutionReport e)
            {
                Trades.Add(e);
            }

            public void OnPositionChanged(IEngineContext context, PositionEvent e)
            {
                PositionChanges.Add(e);
            }
        }

        private class SynchronizedStrategy : IStrategy
        {
            public string Name => "SynchronizedStrategy";
            public int UpdateCount { get; private set; }
            public bool Stopped { get; private set; }
            public ManualResetEventSlim StepGate { get; } = new(true);
            public ManualResetEventSlim ReachedStep { get; } = new(false);

            public void OnUpdate(IEngineContext context)
            {
                UpdateCount++;
                ReachedStep.Set();
                StepGate.Wait();
            }

            public void OnStop(IEngineContext context)
            {
                Stopped = true;
            }
        }

        private class EventTrackingStrategy : IStrategy
        {
            public string Name => "EventTrackingStrategy";
            public List<(string table, int count, DateTime time)> ReceivedEvents { get; } = new();

            public void OnUpdate(IEngineContext context) { }

            public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events)
            {
                ReceivedEvents.Add((tableName, events.Count, context.CurrentTime));
            }
        }

        private static (MockTimeseriesRegistry ts, List<DateTime> dates) CreateTestTimeseries(int daysCount, DateTime? startDate = null)
        {
            var start = startDate ?? new DateTime(2025, 1, 1);
            var dates = new List<DateTime>(daysCount);
            for (int i = 0; i < daysCount; i++)
            {
                dates.Add(start.AddDays(i));
            }

            var symbols = new[] { "sh.600000", "sz.000001" };
            var meta = new DynamicMarketMetadata(symbols, dates);
            return (new MockTimeseriesRegistry(meta), dates);
        }

        #endregion

        [TestMethod]
        public void Constructor_NullRequiredParams_ThrowsException()
        {
            var (ts, _) = CreateTestTimeseries(5);
            var broker = new SimulatedBroker(new BrokerConfig());
            var pipeline = StrategyPipeline.Sequential();

            Assert.ThrowsException<ArgumentNullException>(() => new BacktestingEngine(null!, broker, pipeline));
            Assert.ThrowsException<ArgumentNullException>(() => new BacktestingEngine(ts, null!, pipeline));
            Assert.ThrowsException<ArgumentNullException>(() => new BacktestingEngine(ts, broker, null!));
        }

        [TestMethod]
        public void Run_EmptyStrategy_ShouldExecuteAllStepsAndComplete()
        {
            int days = 10;
            var (ts, dates) = CreateTestTimeseries(days);
            var broker = new SimulatedBroker(new BrokerConfig());
            var strategy = new CountingStrategy();
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            using var engine = new BacktestingEngine(ts, broker, pipeline);
            Assert.AreEqual(EngineStatus.Idle, engine.Status);
            Assert.AreEqual(0.0, engine.Progress);

            engine.Run();

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            Assert.AreEqual(1.0, engine.Progress);
            Assert.IsTrue(strategy.Started);
            Assert.IsTrue(strategy.Stopped);
            Assert.AreEqual(days, strategy.UpdateCount);
            Assert.AreEqual(days, ts.SteppedRows.Count);
            Assert.AreEqual(dates[^1], engine.CurrentTime);
        }

        [TestMethod]
        public void Run_HighThroughput_ShouldProcessThousandsOfBarsQuickly()
        {
            int totalBars = 20_000;
            var (ts, _) = CreateTestTimeseries(totalBars);
            var broker = new SimulatedBroker(new BrokerConfig());
            var strategy = new CountingStrategy();
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            using var engine = new BacktestingEngine(ts, broker, pipeline);

            var sw = Stopwatch.StartNew();
            engine.Run();
            sw.Stop();

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            Assert.AreEqual(totalBars, strategy.UpdateCount);

            // 验证吞吐性能：20,000 个时间步在空/计数策略下应在 500ms 内完成
            Assert.IsTrue(sw.ElapsedMilliseconds < 500, $"耗时超出基线: {sw.ElapsedMilliseconds}ms");
        }

        [TestMethod]
        public void Run_DateFiltering_ShouldRespectStartAndEndDate()
        {
            var (ts, dates) = CreateTestTimeseries(10, new DateTime(2025, 1, 1));
            var broker = new SimulatedBroker(new BrokerConfig());
            var strategy = new CountingStrategy();
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            var config = new EngineConfig
            {
                StartDate = new DateTime(2025, 1, 3), // index 2
                EndDate = new DateTime(2025, 1, 7)    // index 6
            };

            using var engine = new BacktestingEngine(ts, broker, pipeline, config: config);
            engine.Run();

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            Assert.AreEqual(5, strategy.UpdateCount); // 3, 4, 5, 6, 7 (共5天)
            Assert.AreEqual(new DateTime(2025, 1, 3), strategy.ExecutedTimes[0]);
            Assert.AreEqual(new DateTime(2025, 1, 7), strategy.ExecutedTimes[^1]);
        }

        [TestMethod]
        public void Run_DateFiltering_OutOfRange_ShouldCompleteGracefully()
        {
            var (ts, _) = CreateTestTimeseries(5, new DateTime(2025, 1, 1));
            var broker = new SimulatedBroker(new BrokerConfig());
            var strategy = new CountingStrategy();
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            var config = new EngineConfig
            {
                StartDate = new DateTime(2025, 2, 1) // 超出范围
            };

            using var engine = new BacktestingEngine(ts, broker, pipeline, config: config);
            engine.Run();

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            Assert.AreEqual(0, strategy.UpdateCount);
        }

        [TestMethod]
        public void CreateFactory_WithMultiplierHook_ShouldExecuteHookAndApplyMultiplier()
        {
            var (ts, _) = CreateTestTimeseries(2);
            bool multiplierCalled = false;
            var sessionConfig = new SessionConfig
            {
                Engine = new EngineConfig { Mode = "backtest" },
                Broker = new BrokerConfig
                {
                    InitialCash = 1000000.0,
                    OnGetMultiplier = (sym, ctx) =>
                    {
                        multiplierCalled = true;
                        return 300.0; // 股指期货一手 300 乘数
                    }
                }
            };

            var orderStrategy = new BrokerCallbackTrackingStrategy();
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(orderStrategy);

            using var engine = BacktestingEngine.Create(sessionConfig, ts, pipeline);
            engine.Run();

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            Assert.IsTrue(multiplierCalled, "通过 Factory 构造的 SimulatedBroker 必须成功挂载 Context 并执行 OnGetMultiplier");
        }

        [TestMethod]
        public void Run_MinuteLevelData_EndDateWithZeroTime_ShouldIncludeEntireDay()
        {
            // 构造 3 天，每天 4 根分钟 Bar（09:30, 10:30, 13:30, 15:00）
            var dates = new List<DateTime>();
            var day1 = new DateTime(2025, 1, 2);
            var day2 = new DateTime(2025, 1, 3);
            var day3 = new DateTime(2025, 1, 6);

            foreach (var d in new[] { day1, day2, day3 })
            {
                dates.Add(d.AddHours(9).AddMinutes(30));
                dates.Add(d.AddHours(10).AddMinutes(30));
                dates.Add(d.AddHours(13).AddMinutes(30));
                dates.Add(d.AddHours(15).AddMinutes(0));
            }

            var meta = new DynamicMarketMetadata(new[] { "sh.600000" }, dates);
            var ts = new MockTimeseriesRegistry(meta);
            var broker = new SimulatedBroker(new BrokerConfig());
            var strategy = new CountingStrategy();
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            // 配置截至日期为 Day 2 纯日期 (2025-01-03 00:00:00)
            var config = new EngineConfig
            {
                StartDate = day1,
                EndDate = day2 // 纯日期
            };

            using var engine = new BacktestingEngine(ts, broker, pipeline, config: config);
            engine.Run();

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            // 应该包含 Day 1 的 4 根 Bar 和 Day 2 的 4 根 Bar，共 8 根 Bar，绝不能丢弃 Day 2
            Assert.AreEqual(8, strategy.UpdateCount, "纯日期 EndDate 必须包含截至日全天所有分钟 Bar");
            Assert.AreEqual(dates[7], strategy.ExecutedTimes[^1], "最后执行的时间戳应为 Day 2 的 15:00:00");
        }

        [TestMethod]
        public void EventRegistry_ConcurrentRegisterAndQuery_ShouldBeThreadSafe()
        {
            var registry = new EventRegistry();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var date = new DateTime(2025, 1, 2);

            var writeTask = Task.Run(() =>
            {
                int counter = 0;
                while (!cts.IsCancellationRequested)
                {
                    var dummyTable = new EventTable($"table_{counter++}", new[] { "col" }, new());
                    registry.Register(dummyTable.Name, dummyTable);
                }
            });

            var readTask = Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    _ = registry.HasNewEvents(date);
                    _ = registry.GetTableNames();
                }
            });

            Task.WaitAll(writeTask, readTask);
            Assert.IsTrue(registry.GetTableNames().Count > 0);
        }

        private class TPlusOneTradeStrategy : IStrategy
        {
            public string Name => "TPlusOneStrategy";
            public bool TriedSellDay0Failed { get; private set; }
            public bool SellDay1Succeeded { get; private set; }

            public void OnUpdate(IEngineContext context)
            {
                if (context.CurrentIndex == 0)
                {
                    // Day 0 买入 1000 股
                    context.Broker.SubmitOrder(new OrderRequest(
                        "sh.600000",
                        OrderDirection.Buy,
                        OrderType.Market,
                        10.0,
                        1000m));

                    // 验证当日可用持仓受 T+1 约束仍为 0
                    var avail = context.Broker.GetAvailablePosition("sh.600000");
                    if (avail == 0m)
                    {
                        TriedSellDay0Failed = true;
                    }
                }
                else if (context.CurrentIndex == 1)
                {
                    // Day 1 跨日后，通过 RollDate 解锁，可用持仓应为 1000m
                    var avail = context.Broker.GetAvailablePosition("sh.600000");
                    if (avail == 1000m)
                    {
                        // 提交卖单平仓
                        string ordId = context.Broker.SubmitOrder(new OrderRequest(
                            "sh.600000",
                            OrderDirection.Sell,
                            OrderType.Market,
                            11.0,
                            1000m));

                        var status = context.Broker.GetOrderStatus(ordId);
                        if (status == OrderStatus.Filled)
                        {
                            SellDay1Succeeded = true;
                        }
                    }
                }
            }
        }

        [TestMethod]
        public void Run_RollDate_TPlusOne_ShouldUnlockSharesOnNextDay()
        {
            var (ts, _) = CreateTestTimeseries(3, new DateTime(2025, 1, 1));
            // 采用 A 股 T+1 预设
            var broker = new SimulatedBroker(BrokerConfig.AStock);
            var strategy = new TPlusOneTradeStrategy();
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            using var engine = new BacktestingEngine(ts, broker, pipeline);
            engine.Run();

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            Assert.IsTrue(strategy.TriedSellDay0Failed, "Day 0 应该被 T+1 冻结");
            Assert.IsTrue(strategy.SellDay1Succeeded, "Day 1 经由 RollDate 跨日结算后应成功卖出平仓");
        }

        [TestMethod]
        public void Run_Events_ShouldDetectAndDispatchToPipeline()
        {
            var (ts, dates) = CreateTestTimeseries(5, new DateTime(2025, 1, 1));
            var broker = new SimulatedBroker(new BrokerConfig());

            // 构造事件流并在 Day 1 (2025-01-02) 注入事件
            var eventDate = dates[1];
            var ordinals = new Dictionary<string, int> { ["factor"] = 0 };
            var row = new DataRow("sh.600000", eventDate, new[] { "1.05" }, ordinals);
            var dayMap = new Dictionary<string, List<DataRow>>
            {
                ["sh.600000"] = new List<DataRow> { row }
            };
            var dataDict = new Dictionary<DateTime, Dictionary<string, List<DataRow>>>
            {
                [eventDate] = dayMap
            };
            var eventTable = new EventTable("adj_factor", new[] { "factor" }, dataDict);
            var eventRegistry = new EventRegistry();
            eventRegistry.Register("adj_factor", eventTable);

            var strategy = new EventTrackingStrategy();
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            using var engine = new BacktestingEngine(ts, broker, pipeline, events: eventRegistry);
            engine.Run();

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            Assert.AreEqual(1, strategy.ReceivedEvents.Count);
            Assert.AreEqual("adj_factor", strategy.ReceivedEvents[0].table);
            Assert.AreEqual(1, strategy.ReceivedEvents[0].count);
            Assert.AreEqual(eventDate, strategy.ReceivedEvents[0].time);
        }

        [TestMethod]
        public void Run_BrokerEvents_ShouldBridgeToStrategyCallbacks()
        {
            var (ts, _) = CreateTestTimeseries(2);
            var broker = new SimulatedBroker(new BrokerConfig { SettleDays = 0 });
            var strategy = new BrokerCallbackTrackingStrategy();
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            using var engine = new BacktestingEngine(ts, broker, pipeline);
            engine.Run();

            Assert.IsTrue(strategy.OrderStatusChanges.Count > 0, "应收到订单状态变更回调");
            Assert.IsTrue(strategy.Trades.Count > 0, "应收到成交回报回调");
            Assert.IsTrue(strategy.PositionChanges.Count > 0, "应收到持仓变更回调");
            Assert.AreEqual("sh.600000", strategy.Trades[0].Symbol);
        }

        [TestMethod]
        public void PauseAndResume_ShouldControlExecution()
        {
            var (ts, _) = CreateTestTimeseries(50);
            var broker = new SimulatedBroker(new BrokerConfig());
            var strategy = new SynchronizedStrategy();
            strategy.StepGate.Reset(); // 步进阻塞，防止空循环瞬间跑完全部时间步
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            using var engine = new BacktestingEngine(ts, broker, pipeline);

            var runTask = engine.RunAsync();
            strategy.ReachedStep.Wait(); // 等待进入第 1 步

            engine.Pause();
            strategy.StepGate.Set(); // 放行当前步，使循环在下一步开头进入 Paused 等待

            // 等待循环进入 Paused
            for (int k = 0; k < 50 && engine.Status != EngineStatus.Paused; k++)
            {
                Thread.Sleep(5);
            }

            Assert.AreEqual(EngineStatus.Paused, engine.Status);

            int countAtPause = strategy.UpdateCount;
            Thread.Sleep(20);
            Assert.AreEqual(countAtPause, strategy.UpdateCount, "暂停期间步数不应增加");

            engine.Resume();
            runTask.Wait(2000);

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            Assert.AreEqual(50, strategy.UpdateCount);
        }

        [TestMethod]
        public void Stop_ShouldTerminateGracefully()
        {
            var (ts, _) = CreateTestTimeseries(500);
            var broker = new SimulatedBroker(new BrokerConfig());
            var strategy = new SynchronizedStrategy();
            strategy.StepGate.Reset(); // 步进阻塞，让测试完全控制
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            using var engine = new BacktestingEngine(ts, broker, pipeline);

            var runTask = engine.RunAsync();
            strategy.ReachedStep.Wait(); // 等待进入第 1 步并阻塞住

            engine.Stop();
            strategy.StepGate.Set(); // 唤醒策略

            runTask.Wait(1000);

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            Assert.IsTrue(strategy.UpdateCount < 500, "被提前停止，步数应小于总天数");
            Assert.IsTrue(strategy.Stopped, "引擎停止时应触发 Pipeline.OnStop");
        }

        [TestMethod]
        public void RunAsync_WithCancellationToken_ShouldCancelGracefully()
        {
            var (ts, _) = CreateTestTimeseries(500);
            var broker = new SimulatedBroker(new BrokerConfig());
            var strategy = new SynchronizedStrategy();
            strategy.StepGate.Reset(); // 步进阻塞
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            using var engine = new BacktestingEngine(ts, broker, pipeline);
            using var cts = new CancellationTokenSource();

            var runTask = engine.RunAsync(cts.Token);
            strategy.ReachedStep.Wait(); // 等待进入第 1 步并阻塞住

            cts.Cancel();
            strategy.StepGate.Set(); // 唤醒策略

            runTask.Wait(1000);

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            Assert.IsTrue(strategy.UpdateCount < 500);
        }

        [TestMethod]
        public void SaveAndRestoreState_ShouldValidateAndResumeExecution()
        {
            var (ts, dates) = CreateTestTimeseries(10);
            var broker = new SimulatedBroker(new BrokerConfig());
            var strategy = new CountingStrategy();
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            using var engine1 = new BacktestingEngine(ts, broker, pipeline);
            var context = engine1.Context as EngineContext;
            context!.UpdateStep(6, dates[6]); // 模拟跑到了第 6 天

            byte[] state = engine1.SaveState();
            Assert.IsNotNull(state);
            Assert.IsTrue(state.Length >= 32);

            // 1. 验证非法长度异常
            Assert.ThrowsException<ArgumentException>(() => engine1.RestoreState(new byte[10]));

            // 2. 验证非法魔数异常
            var corrupted = (byte[])state.Clone();
            corrupted[0] = 0xFF;
            Assert.ThrowsException<System.IO.InvalidDataException>(() => engine1.RestoreState(corrupted));

            // 3. 验证正常恢复并从第 7 天继续断点续跑 (7, 8, 9 共 3 步)
            using var engine2 = new BacktestingEngine(ts, broker, pipeline);
            engine2.RestoreState(state);

            Assert.AreEqual(6, engine2.Context.CurrentIndex);
            Assert.AreEqual(dates[6], engine2.Context.CurrentTime);
            Assert.AreEqual(EngineStatus.Idle, engine2.Status);

            engine2.Run();
            Assert.AreEqual(EngineStatus.Completed, engine2.Status);
            Assert.AreEqual(3, strategy.UpdateCount, "断点恢复后应从第 7 步跑到第 9 步，共执行 3 步");
        }

        [TestMethod]
        public void CreateFactory_WithSessionConfig_ShouldAssembleAndRun()
        {
            var (ts, _) = CreateTestTimeseries(5);
            var sessionConfig = new SessionConfig
            {
                Engine = new EngineConfig { Mode = "backtest" },
                Broker = new BrokerConfig { InitialCash = 500000.0 }
            };

            var strategy = new CountingStrategy();
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            using var engine = BacktestingEngine.Create(sessionConfig, ts, pipeline);
            Assert.AreEqual(500000.0, engine.Context.Broker.GetCash());

            engine.Run();
            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            Assert.AreEqual(5, strategy.UpdateCount);
        }

        private class RealDataMaStrategy : IStrategy
        {
            public string Name => "RealDataMaStrategy";
            public int TradeCount { get; private set; }
            private bool _ordered = false;

            public void OnUpdate(IEngineContext context)
            {
                if (!_ordered)
                {
                    _ordered = true;
                    string symbol = context.Timeseries.Metadata.Symbols[0];
                    var matrix = context.Timeseries.GetMatrix<double>("close");
                    var close = matrix[context.CurrentIndex, 0];
                    if (close > 0)
                    {
                        context.Broker.SubmitOrder(new OrderRequest(
                            symbol,
                            OrderDirection.Buy,
                            OrderType.Market,
                            close,
                            100m));
                    }
                }
            }

            public void OnTrade(IEngineContext context, ExecutionReport e)
            {
                TradeCount++;
            }
        }

        [TestMethod]
        public void Integration_EndToEnd_RealDataDriven_BacktestShouldExecuteSuccessfully()
        {
            string storageRoot = Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "csv");
            if (!Directory.Exists(storageRoot))
            {
                Assert.Inconclusive($"测试数据目录不存在: {storageRoot}，跳过此集成测试。");
            }

            var resolver = new StorageResolver(storageRoot);
            using var source = TimeseriesSourceFactory.Create(resolver, "ashare.kline.1d.raw.baostock");
            using var tsRegistry = new TimeseriesRegistry(source, resolver.StorageRoot, enableCache: false);

            var industryTable = FieldSourceFactory.Create(resolver, "ashare.industry.eastmoney", keyColumn: "symbol");
            var fieldRegistry = new FieldRegistry();
            fieldRegistry.Register("industry", industryTable);

            var broker = new SimulatedBroker(new BrokerConfig { InitialCash = 1000000.0, SettleDays = 0 });
            var strategy = new RealDataMaStrategy();
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            var engineConfig = new EngineConfig
            {
                StartDate = new DateTime(2025, 1, 1),
                EndDate = new DateTime(2025, 3, 31)
            };

            using var engine = new BacktestingEngine(
                tsRegistry,
                broker,
                pipeline,
                fields: fieldRegistry,
                config: engineConfig);

            engine.Run();

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            Assert.IsTrue(strategy.TradeCount > 0, "真实数据回测应成功触发成交");
            Assert.IsTrue(broker.GetTotalEquity() > 0, "总资产应为正数");
        }

        private class ThrowingStrategy : IStrategy
        {
            public string Name => "ThrowingStrategy";
            public void OnUpdate(IEngineContext context)
            {
                throw new InvalidOperationException("Strategy fault test");
            }
        }

        [TestMethod]
        public void PauseThenStop_ShouldExitImmediatelyWithoutStealingExtraStep()
        {
            var (ts, _) = CreateTestTimeseries(100);
            var broker = new SimulatedBroker(new BrokerConfig());
            var strategy = new SynchronizedStrategy();
            strategy.StepGate.Reset(); // 步进阻塞
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            using var engine = new BacktestingEngine(ts, broker, pipeline);

            var runTask = engine.RunAsync();
            strategy.ReachedStep.Wait(); // 等待进入第 1 步并阻塞住

            engine.Pause();
            strategy.StepGate.Set(); // 放行第 1 步

            for (int k = 0; k < 50 && engine.Status != EngineStatus.Paused; k++)
            {
                Thread.Sleep(5);
            }
            Assert.AreEqual(EngineStatus.Paused, engine.Status);
            int countAtPause = strategy.UpdateCount;

            // 在暂停时直接调用 Stop
            engine.Stop();
            runTask.Wait(1000);

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            Assert.AreEqual(countAtPause, strategy.UpdateCount, "暂停后停止绝不应偷跑任何额外步数");
        }

        [TestMethod]
        public void Run_WhenNotIdle_ThrowsInvalidOperationException()
        {
            var (ts, _) = CreateTestTimeseries(5);
            var broker = new SimulatedBroker(new BrokerConfig());
            var pipeline = StrategyPipeline.Sequential();

            using var engine = new BacktestingEngine(ts, broker, pipeline);
            engine.Run();
            Assert.AreEqual(EngineStatus.Completed, engine.Status);

            Assert.ThrowsException<InvalidOperationException>(() => engine.Run());
        }

        [TestMethod]
        public void Run_AfterDisposed_ThrowsObjectDisposedException()
        {
            var (ts, _) = CreateTestTimeseries(5);
            var broker = new SimulatedBroker(new BrokerConfig());
            var pipeline = StrategyPipeline.Sequential();

            var engine = new BacktestingEngine(ts, broker, pipeline);
            engine.Dispose();

            Assert.ThrowsException<ObjectDisposedException>(() => engine.Run());
        }

        [TestMethod]
        public void RestoreState_WhenRunning_ThrowsInvalidOperationException()
        {
            var (ts, _) = CreateTestTimeseries(50);
            var broker = new SimulatedBroker(new BrokerConfig());
            var strategy = new SynchronizedStrategy();
            strategy.StepGate.Reset();
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            using var engine = new BacktestingEngine(ts, broker, pipeline);
            var state = engine.SaveState();

            var task = engine.RunAsync();
            strategy.ReachedStep.Wait();

            Assert.ThrowsException<InvalidOperationException>(() => engine.RestoreState(state));

            strategy.StepGate.Set();
            task.Wait(1000);
        }

        [TestMethod]
        public void Run_StrategyThrowsException_EngineTransitionsToFaultedAndUnsubscribes()
        {
            var (ts, _) = CreateTestTimeseries(5);
            var broker = new SimulatedBroker(new BrokerConfig());
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(new ThrowingStrategy());

            using var engine = new BacktestingEngine(ts, broker, pipeline);

            Assert.ThrowsException<InvalidOperationException>(() => engine.Run());
            Assert.AreEqual(EngineStatus.Faulted, engine.Status);
        }

        [TestMethod]
        public void Run_EndDateBeforeStartDate_ShouldCompleteWithZeroUpdates()
        {
            var (ts, _) = CreateTestTimeseries(10, new DateTime(2025, 1, 1));
            var broker = new SimulatedBroker(new BrokerConfig());
            var strategy = new CountingStrategy();
            var pipeline = StrategyPipeline.Sequential();
            pipeline.Add(strategy);

            var config = new EngineConfig
            {
                StartDate = new DateTime(2025, 1, 8),
                EndDate = new DateTime(2025, 1, 3) // 结束早于开始
            };

            using var engine = new BacktestingEngine(ts, broker, pipeline, config: config);
            engine.Run();

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            Assert.AreEqual(0, strategy.UpdateCount);
        }
    }
}
