using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Indicators;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Indicators;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Indicators
{
    [TestClass]
    public class IndicatorPoolRegistryTests
    {
        private class SimpleMockMetadata : IMarketMetadata
        {
            public IReadOnlyList<MarketTime> TradeDates { get; } = new List<MarketTime> { MarketTime.UnixEpoch };
            public IReadOnlyList<string> Symbols { get; } = new List<string> { "sh.600000", "sz.000001", "sh.600519" };
            public IReadOnlyList<string> FieldNames { get; } = new List<string> { "close", "volume", "open", "high", "low" };

            public Type GetFieldType(string fieldName) => typeof(double);
            public int GetDateIndex(MarketTime date) => 0;

            public int GetSymbolIndex(string symbol)
            {
                for (int i = 0; i < Symbols.Count; i++)
                {
                    if (Symbols[i].Equals(symbol, StringComparison.OrdinalIgnoreCase)) return i;
                }
                return -1;
            }
        }

        [TestMethod]
        public void IndicatorPool_IndexAndSymbolAccess_ShouldBeConsistent()
        {
            var meta = new SimpleMockMetadata();
            var pool = new IndicatorPool<Sma>("sma3", () => new Sma(3), meta);

            Assert.AreEqual("sma3", pool.Name);
            Assert.AreEqual(PriceMode.Post, pool.Mode);
            Assert.AreEqual(3, pool.Count);

            // 通过代码与通过索引器访问指向同一实例
            var instance0 = pool["sh.600000"];
            var instanceByIdx0 = pool[0];
            Assert.AreSame(instance0, instanceByIdx0);

            var instance1 = pool["sz.000001"];
            Assert.AreSame(instance1, pool[1]);

            // 越界与未知标的防御
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = pool[10]);
            Assert.ThrowsException<ArgumentException>(() => _ = pool["unknown.symbol"]);
        }

        [TestMethod]
        public void IndicatorRegistry_AddAndGet_ShouldShareAcrossStrategies()
        {
            var meta = new SimpleMockMetadata();
            var registry = new IndicatorRegistry(meta);

            // 策略 A 声明并注册指标，获取 Pool 实例
            var poolA = registry.GetOrAdd("sma5", () => new Sma(5));
            Assert.IsTrue(registry.Has("sma5"));

            // 策略 B 重复注册同名同类型指标，幂等返回同一 Pool 实例
            var poolB = registry.GetOrAdd("sma5", () => new Sma(5));
            Assert.AreSame(poolA, poolB);
            Assert.AreSame(poolA, registry.GetPool<Sma>("sma5"));

            // 获取指标实例验证单例共享
            var instA = registry.Get<Sma>("sma5", "sh.600000");
            var instB = registry.Get<Sma>("sma5", 0);
            Assert.IsNotNull(instA);
            Assert.AreSame(instA, instB);
            Assert.AreSame(poolA[0], instA);

            // 类型不匹配时防御抛错
            Assert.ThrowsException<InvalidOperationException>(() => registry.GetOrAdd("sma5", () => new Ema(5)));
            Assert.ThrowsException<InvalidOperationException>(() => _ = registry.Get<Ema>("sma5", 0));

            // 未注册指标防御抛出 KeyNotFoundException
            Assert.ThrowsException<KeyNotFoundException>(() => _ = registry.Get<Sma>("unregistered", 0));
        }

        [TestMethod]
        public void IndicatorRegistry_ConcurrentGetOrAdd_ShouldBeThreadSafeAndNoGhostPools()
        {
            var meta = new SimpleMockMetadata();
            var registry = new IndicatorRegistry(meta);

            // 50 个线程高并发竞争注册同名指标
            const int threadCount = 50;
            var tasks = new System.Threading.Tasks.Task[threadCount];

            for (int i = 0; i < threadCount; i++)
            {
                tasks[i] = System.Threading.Tasks.Task.Run(() =>
                {
                    registry.Add("concurrent_sma", () => new Sma(5));
                });
            }

            System.Threading.Tasks.Task.WaitAll(tasks);

            // 验证内部注册表唯一，且活跃列表仅包含 1 个实例（彻底杜绝幽灵指标池泄漏）
            Assert.AreEqual(1, registry.Names.Count);
            Assert.IsNotNull(registry.Get<Sma>("concurrent_sma", 0));
        }

        [TestMethod]
        public void IndicatorRegistry_StepAndReset_ShouldDriveAllPools()
        {
            var meta = new SimpleMockMetadata();
            var registry = new IndicatorRegistry(meta);

            registry.Add("sma3", () => new Sma(3));

            // 构建一个简单的 Mock 引擎上下文验证 Step
            var mockBroker = new FakeBroker();
            var mockTimeseries = new FakeTimeseries(meta);
            var context = new EngineContext(mockTimeseries, mockBroker, indicators: registry);

            // 步进 3 次
            for (int i = 0; i < 3; i++)
            {
                context.UpdateStep(i, DateTime.UtcNow);
                registry.Step(context);
            }

            // 验证全部实例已就绪
            for (int s = 0; s < meta.Symbols.Count; s++)
            {
                Assert.IsTrue(registry.Get<Sma>("sma3", s).IsReady);
            }

            // 验证 Reset 状态机守恒
            registry.Reset();
            for (int s = 0; s < meta.Symbols.Count; s++)
            {
                Assert.IsFalse(registry.Get<Sma>("sma3", s).IsReady);
            }
        }

        [TestMethod]
        public void IndicatorPool_Step_HotPathZeroAllocation()
        {
            var meta = new SimpleMockMetadata();
            var pool = new IndicatorPool<Sma>("sma3", () => new Sma(3), meta);

            var mockBroker = new FakeBroker();
            var mockTimeseries = new FakeTimeseries(meta);
            var context = new EngineContext(mockTimeseries, mockBroker);

            // Warmup (确保 Tier 1 JIT 编译稳定完成)
            for (int w = 0; w < 50; w++)
            {
                context.UpdateStep(w, DateTime.UtcNow);
                pool.Step(context);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                context.UpdateStep(i, DateTime.UtcNow);
                pool.Step(context);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, $"IndicatorPool Step allocated {allocated} bytes!");
        }

        [TestMethod]
        public void IndicatorRegistry_Step_HotPathZeroAllocation()
        {
            var meta = new SimpleMockMetadata();
            var registry = new IndicatorRegistry(meta);
            registry.Add("sma3", () => new Sma(3));

            var mockBroker = new FakeBroker();
            var mockTimeseries = new FakeTimeseries(meta);
            var context = new EngineContext(mockTimeseries, mockBroker, indicators: registry);

            // Warmup (确保 Tier 1 JIT 编译稳定完成)
            for (int w = 0; w < 50; w++)
            {
                context.UpdateStep(w, DateTime.UtcNow);
                registry.Step(context);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                context.UpdateStep(i, DateTime.UtcNow);
                registry.Step(context);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, $"Hot path step allocated {allocated} bytes!");
        }

        [TestMethod]
        public void EngineContext_Facades_HotPathZeroAllocation()
        {
            var meta = new SimpleMockMetadata();
            var mockBroker = new FakeBroker();
            var mockTimeseries = new FakeTimeseries(meta);
            var context = new EngineContext(mockTimeseries, mockBroker);

            context.UpdateStep(0, DateTime.UtcNow);
            // Warmup ResolveMatrix cache
            _ = context.GetClose(0);
            _ = context.GetOpen(0);
            _ = context.GetHigh(0);
            _ = context.GetLow(0);
            _ = context.GetVolume(0);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                _ = context.GetClose(0);
                _ = context.GetOpen(0);
                _ = context.GetHigh(0);
                _ = context.GetLow(0);
                _ = context.GetVolume(0);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, $"Context facades allocated {allocated} bytes!");
        }

        [TestMethod]
        public void IndicatorPool_MultiSymbolAsymmetricListing_ShouldSmoothNaNAndWarmUpCorrectly()
        {
            // 标的 A (sh.600000): Day 0 上市，价格序列 10, 11, 12, 13, 14, 15
            // 标的 B (sz.300001): 新股 IPO，Day 0~2 均未上市 (NaN)，Day 3 上市 (20)，Day 4 (22)，Day 5 (24)
            var symbols = new[] { "sh.600000", "sz.300001" };
            var dates = new[]
            {
                (MarketTime)new DateTime(2025, 1, 2),
                (MarketTime)new DateTime(2025, 1, 3),
                (MarketTime)new DateTime(2025, 1, 4),
                (MarketTime)new DateTime(2025, 1, 5),
                (MarketTime)new DateTime(2025, 1, 6),
                (MarketTime)new DateTime(2025, 1, 7)
            };

            var metadata = new DynamicMarketMetadata(symbols, dates);
            var closeMat = new ChunkMatrix2D<double>(width: 2, pageSize: 4096, initialRowCount: 6);

            // Day 0: 老股票 10.0，新股未上市 NaN
            closeMat[0, 0] = 10.0; closeMat[0, 1] = double.NaN;
            // Day 1: 老股票 11.0，新股未上市 NaN
            closeMat[1, 0] = 11.0; closeMat[1, 1] = double.NaN;
            // Day 2: 老股票 12.0，新股未上市 NaN
            closeMat[2, 0] = 12.0; closeMat[2, 1] = double.NaN;
            // Day 3: 老股票 13.0，新股上市首日 20.0
            closeMat[3, 0] = 13.0; closeMat[3, 1] = 20.0;
            // Day 4: 老股票 14.0，新股 22.0
            closeMat[4, 0] = 14.0; closeMat[4, 1] = 22.0;
            // Day 5: 老股票 15.0，新股 24.0
            closeMat[5, 0] = 15.0; closeMat[5, 1] = 24.0;

            var matrices = new Dictionary<string, IReadOnlyMatrix2D<double>>(StringComparer.OrdinalIgnoreCase)
            {
                [StandardFields.Close] = closeMat
            };
            var timeseries = new ArrayMatrixTimeseries(metadata, matrices);
            var registry = new IndicatorRegistry(metadata);
            var pool = registry.GetOrAdd("sma3", () => new Sma(3));

            var mockBroker = new FakeBroker();
            var context = new EngineContext(timeseries, mockBroker, indicators: registry);

            // 步进 Day 0 ~ Day 2
            for (int step = 0; step < 3; step++)
            {
                context.UpdateStep(step, dates[step]);
                registry.Step(context);

                // 新股 sz.300001 必须保持未就绪且值为 NaN，绝不抛出任何异常
                Assert.IsFalse(pool[1].IsReady, $"新股在未上市 Day {step} 必须未就绪");
                Assert.IsTrue(double.IsNaN(pool[1].Current), $"新股在未上市 Day {step} 值必须为 NaN");
            }

            // Day 2 结束时，老股票 sh.600000 已经累计 3 天样本 (10, 11, 12)，必须就绪且均值为 11.0
            Assert.IsTrue(pool[0].IsReady);
            Assert.AreEqual(11.0, pool[0].Current, 1e-4);

            // 步进 Day 3 (新股上市首日: 20.0)
            context.UpdateStep(3, dates[3]);
            registry.Step(context);
            Assert.IsFalse(pool[1].IsReady, "新股仅有 1 个样本，SMA(3) 必须未就绪");

            // 步进 Day 4 (新股上市次日: 22.0)
            context.UpdateStep(4, dates[4]);
            registry.Step(context);
            Assert.IsFalse(pool[1].IsReady, "新股仅有 2 个样本，SMA(3) 必须未就绪");

            // 步进 Day 5 (新股上市第 3 日: 24.0)
            context.UpdateStep(5, dates[5]);
            registry.Step(context);

            // 验证：
            // 1. 新股在 Day 5 样本达到 3，成功就绪，均值无偏差等于 (20.0 + 22.0 + 24.0) / 3 = 22.0
            Assert.IsTrue(pool[1].IsReady, "新股样本达到 3，SMA(3) 必须就绪");
            Assert.AreEqual(22.0, pool[1].Current, 1e-4);

            // 2. 老股票在 Day 5 的 SMA(3) = (13.0 + 14.0 + 15.0) / 3 = 14.0
            Assert.IsTrue(pool[0].IsReady);
            Assert.AreEqual(14.0, pool[0].Current, 1e-4);
        }

        #region Helper Fakes

        private class ArrayMatrixTimeseries : ITimeseriesRegistry
        {
            private readonly Dictionary<string, IReadOnlyMatrix2D<double>> _matrices;
            public IMarketMetadata Metadata { get; }

            public ArrayMatrixTimeseries(IMarketMetadata metadata, Dictionary<string, IReadOnlyMatrix2D<double>> matrices)
            {
                Metadata = metadata;
                _matrices = matrices;
            }

            public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged
            {
                if (_matrices.TryGetValue(fieldName, out var mat))
                {
                    return (IReadOnlyMatrix2D<T>)mat;
                }
                throw new KeyNotFoundException($"Field '{fieldName}' not found.");
            }

            public bool HasField(string fieldName) => _matrices.ContainsKey(fieldName);
            public IReadOnlyList<T> GetSeries<T>(string fieldName) where T : unmanaged => throw new NotImplementedException();
            public void StepTo(int index) { }
            public bool IsUpdated(int tableIndex) => false;
            public bool IsUpdated(string tableName) => false;
            public int GetTableIndex(string tableName) => 0;
        }

        private class FakeTimeseries : ITimeseriesRegistry
        {
            public IMarketMetadata Metadata { get; }
            public FakeTimeseries(IMarketMetadata metadata) => Metadata = metadata;

            public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged
            {
                return (IReadOnlyMatrix2D<T>)(object)new FakeMatrix();
            }

            public bool HasField(string fieldName) => true;
            public bool IsUpdated(int tableIndex) => true;
            public bool IsUpdated(string tableName) => true;
            public int GetTableIndex(string tableName) => 0;
            public void StepTo(int rowIndex) { }
            public void Commit() { }
            public void Dispose() { }
        }

        private class FakeMatrix : IReadOnlyMatrix2D<double>
        {
            public int RowCount => 10;
            public int Width => 3;
            private static readonly double _fixedPrice = 100.0;
            public ref readonly double this[int r, int c] => ref _fixedPrice;

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

        #endregion
    }
}
