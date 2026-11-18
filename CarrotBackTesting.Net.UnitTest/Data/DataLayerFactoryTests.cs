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
}
