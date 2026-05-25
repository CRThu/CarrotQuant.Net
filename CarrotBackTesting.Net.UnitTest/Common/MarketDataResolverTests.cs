using System;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Common
{
    /// <summary>
    /// 针对 StorageResolver 的单元测试，测试路径调度、元数据字段显式读取以及年份剪枝的正确性。
    /// </summary>
    [TestClass]
    public class StorageResolverTests
    {
        private static string GetTestDataRoot()
        {
            string current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                string target = Path.Combine(current, "CarrotBackTesting.NET.TestData", "test_storage_root");
                if (Directory.Exists(target))
                {
                    return target;
                }
                
                string parent = Path.GetDirectoryName(current)!;
                if (parent == current) break;
                current = parent;
            }
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\CarrotBackTesting.NET.TestData\test_storage_root"));
        }

        [TestMethod]
        public void TestResolverInitializationAndPaths()
        {
            string testDataRoot = GetTestDataRoot();
            Assert.IsTrue(Directory.Exists(testDataRoot), $"测试数据根目录未找到: {testDataRoot}");

            string csvRoot = Path.Combine(testDataRoot, "csv");
            
            // 初始化 StorageResolver，定位到 csv 根目录
            var resolver = new StorageResolver(csvRoot);

            // 1. 验证 HasTable 检测
            string tableId = "aindex.kline.1d.raw.baostock";
            Assert.IsTrue(resolver.HasTable(tableId), $"未检测到目标测试表: {tableId}");

            // 2. 验证 Format 识别
            Assert.AreEqual("csv", resolver.GetFormat(tableId));

            // 3. 验证 Layout 布局模式
            Assert.AreEqual(StorageLayout.Hive, resolver.GetLayout(tableId));
            
            // 4. 验证 Category 数据分类
            Assert.AreEqual("timeseries", resolver.GetCategory(tableId));

            // 5. 验证 Partition 分区模式
            Assert.AreEqual("symbol", resolver.GetPartition(tableId));

            // 6. 验证 StartTimestamp / EndTimestamp
            Assert.AreEqual(1262534400000L, resolver.GetStartTimestamp(tableId));
            Assert.AreEqual(1767110400000L, resolver.GetEndTimestamp(tableId));

            // 7. 验证 Field Schema 字段名及类型映射
            var fieldNames = resolver.GetFieldNames(tableId);
            Assert.IsTrue(fieldNames.Contains("close"));
            Assert.IsTrue(fieldNames.Contains("volume"));
            Assert.AreEqual(typeof(double), resolver.GetFieldType(tableId, "close"));
            Assert.AreEqual(typeof(long), resolver.GetFieldType(tableId, "timestamp"));

            // 8. 验证终极物理路径调度与年份分区剪枝
            // 请求 2024 年度的 sh.000001 数据文件
            var files = resolver.ResolvePhysicalFiles(tableId, "sh.000001", new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));
            Assert.AreEqual(1, files.Count);
            Assert.IsTrue(files[0].Contains("year=2024"));
            Assert.IsTrue(files[0].Contains("sh.000001.csv"));
            Assert.IsTrue(File.Exists(files[0]), $"解析出的文件实际不存在: {files[0]}");
        }
    }
}
