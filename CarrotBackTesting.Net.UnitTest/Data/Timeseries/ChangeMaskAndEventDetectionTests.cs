using System;
using System.Collections;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data;

[TestClass]
public class ChangeMaskAndEventDetectionTests
{
    private TimeseriesRegistry CreateTestRegistry()
    {
        var symbols = new[] { "sh.600000" };
        var dates = new[] { DateTime.Today };
        var fieldTypes = new Dictionary<string, Type> { { "close", typeof(double) } };
        var metadata = new DynamicMarketMetadata(symbols, dates, fieldTypes);
        return new TimeseriesRegistry(metadata, (fieldName, type) => null!);
    }

    [TestMethod]
    public void TimeseriesRegistry_ChangeMask_DefaultAllTrue()
    {
        using var registry = CreateTestRegistry();

        // 默认状态下位掩码为全 1，单表或缺省配置下任何序号均判定为已更新（零开销兼容）
        Assert.IsTrue(registry.IsUpdated(0));
        Assert.IsTrue(registry.IsUpdated(1));
        Assert.IsTrue(registry.IsUpdated(63));
        Assert.IsFalse(registry.IsUpdated(64)); // 超出 64 范围直接返回 false

        // 未注册表名抛出 KeyNotFoundException，防止拼写错误隐式静默漏算
        Assert.ThrowsException<KeyNotFoundException>(() => registry.IsUpdated("unregistered_table"));
        Assert.ThrowsException<ArgumentException>(() => registry.IsUpdated(""));
        Assert.ThrowsException<ArgumentNullException>(() => registry.IsUpdated(null!));
    }

    [TestMethod]
    public void TimeseriesRegistry_RegisterTable_SequentialIndicesAndDuplicateHandling()
    {
        using var registry = CreateTestRegistry();

        int idx1m = registry.RegisterTable("kline.1m");
        int idx5m = registry.RegisterTable("kline.5m");
        int idx1d = registry.RegisterTable("kline.1d");

        Assert.AreEqual(0, idx1m);
        Assert.AreEqual(1, idx5m);
        Assert.AreEqual(2, idx1d);

        // 重复注册返回已有序号
        Assert.AreEqual(1, registry.RegisterTable("kline.5m"));
        // 大小写不敏感
        Assert.AreEqual(0, registry.RegisterTable("KLINE.1M"));

        // GetTableIndex 验证
        Assert.AreEqual(0, registry.GetTableIndex("kline.1m"));
        Assert.AreEqual(1, registry.GetTableIndex("kline.5m"));
        Assert.AreEqual(2, registry.GetTableIndex("kline.1d"));
        Assert.AreEqual(-1, registry.GetTableIndex("non_existent"));
        Assert.AreEqual(-1, registry.GetTableIndex(""));
        Assert.AreEqual(-1, registry.GetTableIndex(null!));
    }

    [TestMethod]
    public void TimeseriesRegistry_RegisterTable_Exceeding64_ThrowsNotSupportedException()
    {
        using var registry = CreateTestRegistry();

        for (int i = 0; i < 64; i++)
        {
            int idx = registry.RegisterTable($"table_{i}");
            Assert.AreEqual(i, idx);
        }

        // 第 65 张表必须抛出强类型异常防御
        var ex = Assert.ThrowsException<NotSupportedException>(() =>
        {
            registry.RegisterTable("table_64");
        });
        StringAssert.Contains(ex.Message, "时序表数量超过上限");
    }

    [TestMethod]
    public void TimeseriesRegistry_SetUpdatedMask_BitmaskEvaluation()
    {
        using var registry = CreateTestRegistry();

        int idx0 = registry.RegisterTable("t0");
        int idx1 = registry.RegisterTable("t1");
        int idx2 = registry.RegisterTable("t2");

        // 设置掩码为二进制 101 (第 0 位与第 2 位为 1，第 1 位为 0)
        registry.SetUpdatedMask(0b101UL);

        Assert.IsTrue(registry.IsUpdated(idx0));
        Assert.IsFalse(registry.IsUpdated(idx1));
        Assert.IsTrue(registry.IsUpdated(idx2));

        // 通过表名查询
        Assert.IsTrue(registry.IsUpdated("t0"));
        Assert.IsFalse(registry.IsUpdated("t1"));
        Assert.IsTrue(registry.IsUpdated("t2"));
    }

    [TestMethod]
    public void TimeseriesRegistry_SetUpdated_BitToggleAndAutoRegister()
    {
        using var registry = CreateTestRegistry();

        int idx = registry.RegisterTable("kline.daily");

        // 初始为全 1
        Assert.IsTrue(registry.IsUpdated(idx));

        // 翻转为 false
        registry.SetUpdated(idx, false);
        Assert.IsFalse(registry.IsUpdated(idx));
        Assert.IsFalse(registry.IsUpdated("kline.daily"));

        // 翻转为 true
        registry.SetUpdated(idx, true);
        Assert.IsTrue(registry.IsUpdated(idx));
        Assert.IsTrue(registry.IsUpdated("kline.daily"));

        // 通过表名调用 SetUpdated（自动注册并设置）
        registry.SetUpdated("kline.auto", false);
        Assert.AreEqual(1, registry.GetTableIndex("kline.auto"));
        Assert.IsFalse(registry.IsUpdated("kline.auto"));

        registry.SetUpdated("kline.auto", true);
        Assert.IsTrue(registry.IsUpdated("kline.auto"));

        // 越界保护
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
        {
            registry.SetUpdated(64, true);
        });
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
        {
            registry.SetUpdated(-1, true);
        });
    }

    private class MockEventTable : IEventTable
    {
        private readonly HashSet<DateTime> _dates;

        public MockEventTable(string name, IEnumerable<DateTime> dates)
        {
            Name = name;
            _dates = new HashSet<DateTime>(dates);
        }

        public string Name { get; }
        public int Count => _dates.Count;
        public IEnumerable<DateTime> Dates => _dates;
        public bool ContainsDate(DateTime date) => _dates.Contains(date.Date);

        public IReadOnlyDictionary<string, IReadOnlyList<IDataRow>> this[DateTime date] => throw new NotImplementedException();
        public IDataRow this[DateTime date, string key] => throw new NotImplementedException();
        public bool TryGetDay(DateTime date, out IReadOnlyDictionary<string, IReadOnlyList<IDataRow>>? dayMap) => throw new NotImplementedException();
        public IReadOnlyList<IDataRow> GetRows(DateTime timestamp, string key) => throw new NotImplementedException();
        public bool TryGet(DateTime timestamp, string key, out IReadOnlyList<IDataRow> rows) => throw new NotImplementedException();
        public bool TryGetFirst(DateTime timestamp, string key, out IDataRow? row) => throw new NotImplementedException();
        public bool TryGetGlobal(DateTime timestamp, out IReadOnlyList<IDataRow> globalRows) => throw new NotImplementedException();
        public T Get<T>(DateTime timestamp, string key) where T : class => throw new NotImplementedException();
        public bool TryGet<T>(DateTime timestamp, string key, out T? value) where T : class => throw new NotImplementedException();
        public IEnumerable<T> As<T>(DateTime timestamp) where T : class => throw new NotImplementedException();
        public IEnumerable<T> As<T>(string key) where T : class => throw new NotImplementedException();
        public IEnumerable<T> As<T>() where T : class => throw new NotImplementedException();
        public IEnumerator<KeyValuePair<DateTime, IReadOnlyDictionary<string, IReadOnlyList<IDataRow>>>> GetEnumerator() => throw new NotImplementedException();
        IEnumerator IEnumerable.GetEnumerator() => throw new NotImplementedException();
    }

    [TestMethod]
    public void EventRegistry_HasNewEvents_Detection()
    {
        var registry = new EventRegistry();
        var date1 = new DateTime(2025, 5, 20);
        var date2 = new DateTime(2025, 5, 21);

        // 空注册中心
        Assert.IsFalse(registry.HasNewEvents(date1));

        // 注册停牌表
        var suspensionTable = new MockEventTable("suspension", new[] { date1 });
        registry.Register("suspension", suspensionTable);

        // 注册分红表
        var dividendTable = new MockEventTable("dividend", new[] { date2 });
        registry.Register("dividend", dividendTable);

        // 全局探测
        Assert.IsTrue(registry.HasNewEvents(date1));
        Assert.IsTrue(registry.HasNewEvents(date2));
        Assert.IsFalse(registry.HasNewEvents(new DateTime(2025, 5, 22)));

        // 单表过滤探测
        Assert.IsTrue(registry.HasNewEvents(date1, "suspension"));
        Assert.IsFalse(registry.HasNewEvents(date1, "dividend"));
        Assert.IsTrue(registry.HasNewEvents(date2, "dividend"));
        Assert.IsFalse(registry.HasNewEvents(date2, "suspension"));

        // 未注册表名返回 false
        Assert.IsFalse(registry.HasNewEvents(date1, "non_existent_table"));
    }

    [TestMethod]
    public void TimeseriesRegistry_ConcurrentRegisterAndSetUpdated_ShouldBeThreadSafe()
    {
        using var registry = CreateTestRegistry();
        const int threadCount = 8;
        const int tablesPerThread = 6;

        System.Threading.Tasks.Parallel.For(0, threadCount, t =>
        {
            for (int i = 0; i < tablesPerThread; i++)
            {
                var name = $"table_{t}_{i}";
                int idx = registry.RegisterTable(name);
                Assert.IsTrue(idx >= 0 && idx < 64);
                registry.SetUpdated(name, i % 2 == 0);
                Assert.AreEqual(i % 2 == 0, registry.IsUpdated(name));
            }
        });
    }

    [TestMethod]
    public void TimeseriesRegistry_NegativeIndex_ShouldReturnFalseSafely()
    {
        using var registry = CreateTestRegistry();
        Assert.IsFalse(registry.IsUpdated(-1));
        Assert.IsFalse(registry.IsUpdated(-100));
    }
}
