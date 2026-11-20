using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data;

[TestClass]
public class DataLayerFactoryTests
{
    private string _csvRoot = "";
    private string _parquetRoot = "";

    [TestInitialize]
    public void Setup()
    {
        _csvRoot = Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "csv");
        _parquetRoot = Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "parquet");
    }

    [TestMethod]
    public void Create_NullConfig_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() => DataLayerFactory.Create(null!));
    }

    [TestMethod]
    public void Create_EmptyTimeseries_ThrowsInvalidOperationException()
    {
        var config = new DataConfig
        {
            Root = _csvRoot,
            Timeseries = new List<TimeseriesConfig>()
        };

        var ex = Assert.ThrowsException<InvalidOperationException>(() => DataLayerFactory.Create(config));
        StringAssert.Contains(ex.Message, "未配置任何 timeseries 表项");
    }

    [TestMethod]
    public void Create_NullTimeseriesList_ThrowsInvalidOperationException()
    {
        var config = new DataConfig
        {
            Root = _csvRoot,
            Timeseries = null!
        };

        var ex = Assert.ThrowsException<InvalidOperationException>(() => DataLayerFactory.Create(config));
        StringAssert.Contains(ex.Message, "未配置任何 timeseries 表项");
    }

    [TestMethod]
    public void Create_WithValidCsvConfig_InitializesAllRegistries()
    {
        var config = new DataConfig
        {
            Root = _csvRoot,
            Timeseries = new List<TimeseriesConfig>
            {
                new()
                {
                    TableId = "ashare.kline.1d.raw.baostock",
                    Fields = new List<string> { "close", "volume" },
                    Cache = false
                }
            },
            Events = new List<EventStreamConfig>
            {
                new()
                {
                    Name = "adj_factor",
                    TableId = "ashare.adj_factor.baostock"
                }
            },
            Fields = new List<FieldTableConfig>
            {
                new()
                {
                    Name = "industry",
                    TableId = "ashare.industry.eastmoney"
                }
            }
        };

        var (ts, ev, fl) = DataLayerFactory.Create(config);

        try
        {
            Assert.IsNotNull(ts, "TimeseriesRegistry 应该被正确创建");
            Assert.IsNotNull(ev, "EventRegistry 应该被正确创建");
            Assert.IsNotNull(fl, "FieldRegistry 应该被正确创建");

            Assert.IsTrue(ts.Metadata.Symbols.Count > 0);
            Assert.IsTrue(ts.Metadata.TradeDates.Count > 0);
            Assert.IsTrue(ev.HasTable("adj_factor"));
            Assert.IsTrue(fl.HasTable("industry"));
        }
        finally
        {
            (ts as IDisposable)?.Dispose();
        }
    }

    [TestMethod]
    public void Create_WithDateRangeAndLiveMode_PassesParametersProperly()
    {
        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 12, 31);

        var config = new DataConfig
        {
            Root = _parquetRoot,
            Timeseries = new List<TimeseriesConfig>
            {
                new()
                {
                    TableId = "ashare.kline.1d.raw.baostock",
                    Cache = false
                }
            }
        };

        var (ts, ev, fl) = DataLayerFactory.Create(config, startDate, endDate, isLive: true);

        try
        {
            Assert.IsNotNull(ts);
            Assert.IsNotNull(ev);
            Assert.IsNotNull(fl);

            // 验证时间剪枝正确传递给元数据
            Assert.IsTrue(ts.Metadata.TradeDates.Count > 0);
            Assert.IsTrue(ts.Metadata.TradeDates[0] >= startDate);
            Assert.IsTrue(ts.Metadata.TradeDates[^1] <= endDate.AddDays(1));
        }
        finally
        {
            (ts as IDisposable)?.Dispose();
        }
    }

    [TestMethod]
    public void Create_MultipleTimeseries_ThrowsNotSupportedException()
    {
        var config = new DataConfig
        {
            Root = _csvRoot,
            Timeseries = new List<TimeseriesConfig>
            {
                new() { TableId = "ashare.kline.1d.raw.baostock", Cache = false },
                new() { TableId = "ashare.kline.5m.raw.baostock", Cache = false }
            }
        };

        var ex = Assert.ThrowsException<NotSupportedException>(() => DataLayerFactory.Create(config));
        StringAssert.Contains(ex.Message, "仅支持单张主时序行情表");
    }

    [TestMethod]
    public void Create_ZeroEventsAndZeroFields_ReturnsEmptyRegistries()
    {
        var config = new DataConfig
        {
            Root = _csvRoot,
            Timeseries = new List<TimeseriesConfig>
            {
                new() { TableId = "ashare.kline.1d.raw.baostock", Cache = false }
            },
            Events = new List<EventStreamConfig>(),
            Fields = new List<FieldTableConfig>()
        };

        var (ts, ev, fl) = DataLayerFactory.Create(config);

        try
        {
            Assert.IsNotNull(ts);
            Assert.IsNotNull(ev);
            Assert.IsNotNull(fl);
            Assert.IsFalse(ev.HasTable("non_existent"));
            Assert.IsFalse(fl.HasTable("non_existent"));
        }
        finally
        {
            (ts as IDisposable)?.Dispose();
        }
    }

    [TestMethod]
    public void Create_MultipleEventsAndMultipleFields_RegistersAllCorrectly()
    {
        var config = new DataConfig
        {
            Root = _csvRoot,
            Timeseries = new List<TimeseriesConfig>
            {
                new() { TableId = "ashare.kline.1d.raw.baostock", Cache = false }
            },
            Events = new List<EventStreamConfig>
            {
                new() { Name = "adj_factor", TableId = "ashare.adj_factor.baostock" },
                new() { Name = "dragon_tiger", TableId = "ashare.dragon_tiger.eastmoney" }
            },
            Fields = new List<FieldTableConfig>
            {
                new() { Name = "industry", TableId = "ashare.industry.eastmoney" },
                new() { Name = "concept", TableId = "ashare.concept.eastmoney" }
            }
        };

        var (ts, ev, fl) = DataLayerFactory.Create(config);

        try
        {
            Assert.IsNotNull(ts);
            Assert.IsNotNull(ev);
            Assert.IsNotNull(fl);

            // 验证 N 个事件表均被注册
            Assert.IsTrue(ev.HasTable("adj_factor"));
            Assert.IsTrue(ev.HasTable("dragon_tiger"));

            // 验证 N 个维表均被注册
            Assert.IsTrue(fl.HasTable("industry"));
            Assert.IsTrue(fl.HasTable("concept"));
        }
        finally
        {
            (ts as IDisposable)?.Dispose();
        }
    }

    [TestMethod]
    public void SessionConfig_AdjustmentModeAndTableId_YamlSerializationRoundtrip()
    {
        string yaml = """
            engine:
              mode: backtest
            data:
              root: "./data"
              timeseries:
                - table_id: ashare.kline.1d.raw.baostock
                  refresh: auto
                  compression: none
              adjust:
                mode: post
                table_id: my_custom_adjustments
                factor_field: factor
                cache: false
            """;

        var config = SessionConfig.FromYaml(yaml);

        Assert.IsNotNull(config.Data.Adjust);
        Assert.AreEqual(AdjustmentMode.Post, config.Data.Adjust.Mode);
        Assert.AreEqual("my_custom_adjustments", config.Data.Adjust.TableId);
        Assert.AreEqual("factor", config.Data.Adjust.FactorField);
        Assert.IsFalse(config.Data.Adjust.Cache);

        Assert.AreEqual(CacheRefreshMode.Auto, config.Data.Timeseries[0].Refresh);
        Assert.AreEqual(CacheCompression.None, config.Data.Timeseries[0].Compression);

        // 重新序列化后必须包含 table_id 与强类型枚举
        string serialized = config.ToYaml();
        StringAssert.Contains(serialized, "table_id: my_custom_adjustments");
        StringAssert.Contains(serialized, "mode: Post");
    }

    [TestMethod]
    public void SessionConfig_InvalidAdjustmentMode_ThrowsYamlException()
    {
        string invalidYaml = """
            data:
              adjust:
                mode: invalid_mode_typo
            """;

        Assert.ThrowsException<YamlDotNet.Core.YamlException>(() => SessionConfig.FromYaml(invalidYaml));
    }
}
