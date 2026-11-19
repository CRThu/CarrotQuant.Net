using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data;

/// <summary>
/// 事件注册中心（EventRegistry）与装配工厂（EventRegistryFactory）单元测试。
/// 验证事件表注册、检索、索引器访问、缺失键抛错以及多表批量装配。
/// </summary>
[TestClass]
public class EventRegistryTests
{
    private static string GetTestDataRoot()
    {
        return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root");
    }

    [TestMethod]
    public void RegisterAndRetrieve_ShouldWork()
    {
        var registry = new EventRegistry();
        var emptySymbolData = new Dictionary<MarketTime, Dictionary<string, List<DataRow>>>();
        var table = new EventTable("adjustments", new[] { "back_adj_factor" }, emptySymbolData);

        registry.Register("adjustments", table);

        Assert.IsTrue(registry.HasTable("adjustments"));
        var retrieved = registry.GetTable("adjustments");
        Assert.AreSame(table, retrieved);

        var viaIndexer = registry["adjustments"];
        Assert.AreSame(table, viaIndexer);

        var names = registry.GetTableNames();
        Assert.AreEqual(1, names.Count);
        Assert.AreEqual("adjustments", names[0]);
    }

    [TestMethod]
    [ExpectedException(typeof(KeyNotFoundException))]
    public void GetTable_NonExistent_ShouldThrow()
    {
        var registry = new EventRegistry();
        registry.GetTable("non_existent");
    }

    [TestMethod]
    public void HasTable_ShouldReturnCorrectly()
    {
        var registry = new EventRegistry();
        Assert.IsFalse(registry.HasTable("adjustments"));

        var emptySymbolData = new Dictionary<MarketTime, Dictionary<string, List<DataRow>>>();
        var table = new EventTable("adjustments", new[] { "back_adj_factor" }, emptySymbolData);
        registry.Register("adjustments", table);

        Assert.IsTrue(registry.HasTable("adjustments"));
        Assert.IsFalse(registry.HasTable("other"));
    }

    [TestMethod]
    public void EventRegistryFactory_BatchAssemble_ShouldWork()
    {
        string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
        var resolver = new StorageResolver(csvRoot);

        var configs = new[]
        {
            new CarrotBacktesting.NET.Config.EventStreamConfig
            {
                Name = "adj",
                TableId = "ashare.adj_factor.baostock"
            }
        };

        var registry = EventRegistryFactory.Create(resolver, configs);
        Assert.IsTrue(registry.HasTable("adj"));
        var table = registry["adj"];
        Assert.IsNotNull(table);
        Assert.IsTrue(table.Count > 0);
    }
}
