using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Config
{
    /// <summary>
    /// 纯代码配置（Code-First Configuration）端到端集成测试。
    /// 验证不依赖任何 YAML 文件、完全通过 C# 强类型对象或工厂 API 进行装配的完整可用性。
    /// </summary>
    [TestClass]
    public class CodeFirstConfigurationTests
    {
        private static string GetTestDataRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "csv");
        }

        public record AdjFactorItem(double BackAdjFactor);
        public record IndustryItem(string BoardCode, string BoardName, string Symbol, string StockName);

        [TestMethod]
        public void CodeConfig_FullEngineConfig_Assembly_ShouldSucceed()
        {
            // 【用法一：强类型顶层对象配置法】
            // 直接通过 C# 对象声明完整回测与数据配置，不创建任何物理文件
            string storageRoot = GetTestDataRoot();

            var config = new EngineConfig
            {
                Engine = new EngineSettings
                {
                    Mode = "backtest",
                    StartDate = new DateTime(2025, 1, 1),
                    EndDate = new DateTime(2025, 12, 31),
                    InitialCash = 1000000.0
                },
                Data = new DataConfig
                {
                    Root = storageRoot,
                    Timeseries = new List<TimeseriesConfig>
                    {
                        new()
                        {
                            TableId = "ashare.kline.1d.raw.baostock",
                            Cache = false,
                            PageSize = 4096,
                            MaxPages = 4,
                            Prefetch = false
                        }
                    },
                    Events = new List<EventStreamConfig>
                    {
                        new() { Name = "adjustments", TableId = "ashare.adj_factor.baostock" }
                    },
                    Fields = new List<FieldTableConfig>
                    {
                        new() { Name = "industry", TableId = "ashare.industry.eastmoney", KeyColumn = "symbol" }
                    }
                }
            };

            // 1. 基于配置中的 Root 构建物理路径寻址器
            var resolver = new StorageResolver(config.Data.Root);

            // 2. 利用配置中的起止日期与单表参数装配时序
            using var timeseriesRegistry = (IDisposable)TimeseriesRegistryFactory.Create(
                resolver,
                config.Data.Timeseries[0],
                startDate: config.Engine.StartDate,
                endDate: config.Engine.EndDate);
            var ts = (ITimeseriesRegistry)timeseriesRegistry;

            // 3. 批量装配事件流（传入回测时间区间，触发物理年份分区剪枝）
            var events = EventRegistryFactory.Create(
                resolver,
                config.Data.Events,
                startDate: config.Engine.StartDate,
                endDate: config.Engine.EndDate);

            // 4. 批量装配维表
            var fields = FieldRegistryFactory.Create(
                resolver,
                config.Data.Fields);

            // 5. 验证纯代码装配结果
            Assert.IsNotNull(ts);
            Assert.IsTrue(ts.HasField("close"));
            Assert.IsTrue(ts.Metadata.Symbols.Count > 0);

            var closeMatrix = ts.GetMatrix<double>("close");
            Assert.IsNotNull(closeMatrix);
            Assert.IsTrue(closeMatrix.RowCount > 0);

            Assert.IsNotNull(events);
            Assert.IsTrue(events.HasTable("adjustments"));
            var adjTable = events["adjustments"];
            Assert.IsTrue(adjTable.Count > 0);

            Assert.IsNotNull(fields);
            Assert.IsTrue(fields.HasTable("industry"));
            var indTable = fields["industry"];
            var molyStocks = indTable.GetKeys("钼");
            Assert.IsTrue(molyStocks.Count > 0);
        }

        [TestMethod]
        public void CodeConfig_DirectFactory_FluentAssembly_ShouldSucceed()
        {
            // 【用法二：工厂直连装配法】
            // 零中间配置模型对象，直接面向数据区根目录与具名参数快速装配
            string storageRoot = GetTestDataRoot();

            // 1. 时序子系统（自动识别 CSV / Parquet）
            using var source = TimeseriesSourceFactory.Create(storageRoot, "ashare.kline.1d.raw.baostock");
            using var timeseries = (IDisposable)TimeseriesRegistryFactory.Create(
                storageRoot: storageRoot,
                source: source,
                pageSize: 4096,
                maxPages: 4,
                enableCache: false,
                prefetch: false);
            var ts = (ITimeseriesRegistry)timeseries;

            // 2. 事件子系统（直接传入轻量列表）
            var events = EventRegistryFactory.Create(storageRoot, new[]
            {
                new EventStreamConfig { Name = "adjustments", TableId = "ashare.adj_factor.baostock" }
            });

            // 3. 维表子系统（直接传入轻量列表）
            var fields = FieldRegistryFactory.Create(storageRoot, new[]
            {
                new FieldTableConfig { Name = "industry", TableId = "ashare.industry.eastmoney", KeyColumn = "symbol" }
            });

            // 验证可用性
            Assert.IsTrue(ts.HasField("close"));
            Assert.IsTrue(events.HasTable("adjustments"));
            Assert.IsTrue(fields.HasTable("industry"));
        }

        [TestMethod]
        public void CodeConfig_FirstPrinciples_DirectSourceAssembly_ShouldSucceed()
        {
            // 【用法三：底层物理驱动组合装配】
            // 适合底层定制开发与单表操作
            string storageRoot = GetTestDataRoot();
            var resolver = new StorageResolver(storageRoot);

            // 直接获取独立维表并进行双向倒排检索
            var industryTable = FieldSourceFactory.Create(resolver, "ashare.industry.eastmoney", keyColumn: "symbol");
            var molyStocks = industryTable.GetKeys("钼");
            Assert.IsTrue(molyStocks.Count > 0);

            // 直接获取单表事件流并自动执行年份分区剪枝
            var adjTable = EventSourceFactory.Create(
                resolver,
                "ashare.adj_factor.baostock",
                startDate: new DateTime(2025, 1, 1),
                endDate: new DateTime(2025, 12, 31));
            Assert.IsTrue(adjTable.Count > 0);

            // 直接构建单字段时序矩阵
            using var source = TimeseriesSourceFactory.Create(resolver, "ashare.kline.1d.raw.baostock");
            using var tsRegistry = new TimeseriesRegistry(source, resolver.StorageRoot, enableCache: false);
            var closeMatrix = tsRegistry.GetMatrix<double>("close");
            Assert.IsNotNull(closeMatrix);
            Assert.IsTrue(closeMatrix.RowCount > 0);
        }
    }
}
