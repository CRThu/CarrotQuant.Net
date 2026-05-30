using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class StorageResolverTests
    {
        private string _tempRoot = "";

        [TestInitialize]
        public void Setup() => _tempRoot = Path.Combine(Path.GetTempPath(), "CarrotResolverTest_" + Guid.NewGuid());

        [TestCleanup]
        public void Cleanup() { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }

        private void CreateTable(string tableId, object metadata, List<string>? files = null)
        {
            string dir = Path.Combine(_tempRoot, tableId);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "metadata.json"), JsonSerializer.Serialize(metadata));
            
            if (files != null)
            {
                foreach (var f in files)
                {
                    string path = Path.Combine(dir, f);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, "");
                }
            }
        }

        #region 1. 元数据解析测试 (元数据定义是否正确)

        [TestMethod]
        [DataRow("timeseries", "hive", "symbol", "csv")]
        [DataRow("events", "flat", "none", "parquet")]
        [DataRow("events", "hive", "date", "csv")]
        [DataRow("timeseries", "flat", "symbol", "parquet")]
        public void MetadataParsing_ShouldMapCorrectly(string cat, string lay, string part, string fmt)
        {
            string tableId = $"{cat}_{lay}_{part}_{fmt}";
            CreateTable(tableId, new { category = cat, layout = lay, partition = part, format = fmt, schema = new { id = "int64" } });

            var resolver = new StorageResolver(_tempRoot);
            
            // 验证 Category 严格映射 (只允许 timeseries / events)
            Assert.AreEqual(cat, resolver.GetCategory(tableId));
            
            // 验证 Layout 逻辑
            Assert.AreEqual(lay == "hive" ? StorageLayout.Hive : StorageLayout.Flat, resolver.GetLayout(tableId));
            
            // 验证 Partition 逻辑
            Assert.AreEqual(part, resolver.GetPartition(tableId));
            
            // 验证 Format
            Assert.AreEqual(fmt, resolver.GetFormat(tableId));
        }

        #endregion

        #region 2. 物理文件调度测试 (调度行为逻辑)

        [TestMethod]
        public void Resolve_FlatLayout_SymbolPartition_ShouldRouteToSpecificFile()
        {
            string tableId = "flat_symbol";
            CreateTable(tableId, 
                new { layout = "flat", format = "csv", partition = "symbol", schema = new { c = "int64" } },
                new List<string> { "sh.600000.csv", "sh.600001.csv" });

            var resolver = new StorageResolver(_tempRoot);
            var files = resolver.ResolvePhysicalFiles(tableId, "sh.600000");
            
            Assert.AreEqual(1, files.Count);
            Assert.IsTrue(files[0].EndsWith("sh.600000.csv"));
        }

        [TestMethod]
        public void Resolve_Events_FlatLayout_SymbolPartition_ShouldRouteToSpecificFile()
        {
            string tableId = "events_flat_symbol";
            // 确保 Category 为 events
            CreateTable(tableId, 
                new { category = "events", layout = "flat", format = "csv", partition = "symbol", schema = new { id = "int64" } },
                new List<string> { "sh.600000.csv" });

            var resolver = new StorageResolver(_tempRoot);
            
            // 验证 category
            Assert.AreEqual("events", resolver.GetCategory(tableId));
            
            // 验证文件调度
            var files = resolver.ResolvePhysicalFiles(tableId, "sh.600000");
            Assert.AreEqual(1, files.Count);
            Assert.IsTrue(files[0].EndsWith("sh.600000.csv"));
        }

        [TestMethod]
        public void Resolve_FlatLayout_NoPartition_ShouldReturnAllFiles()
        {
            string tableId = "flat_none";
            CreateTable(tableId, 
                new { layout = "flat", format = "csv", partition = "none", schema = new { c = "int64" } },
                new List<string> { "data1.csv", "data2.csv" });

            var resolver = new StorageResolver(_tempRoot);
            var files = resolver.ResolvePhysicalFiles(tableId);
            
            Assert.AreEqual(2, files.Count);
        }

        [TestMethod]
        public void Resolve_HiveLayout_SymbolPartition_ShouldPruneYearsAndRouteToSymbol()
        {
            string tableId = "hive_symbol";
            CreateTable(tableId,
                new { layout = "hive", format = "csv", partition = "symbol", schema = new { c = "int64" } },
                new List<string> { "year=2023/sh.600000.csv", "year=2024/sh.600000.csv" });

            var resolver = new StorageResolver(_tempRoot);
            
            // 剪枝 2023，仅取 2024
            var files = resolver.ResolvePhysicalFiles(tableId, "sh.600000", new DateTime(2024, 1, 1), null);
            
            Assert.AreEqual(1, files.Count);
            Assert.IsTrue(files[0].Contains("year=2024"));
            Assert.IsTrue(files[0].EndsWith("sh.600000.csv"));
        }

        [TestMethod]
        public void Resolve_HiveLayout_NoPartition_ShouldPruneYearsAndReturnAllFiles()
        {
            string tableId = "hive_none";
            CreateTable(tableId,
                new { layout = "hive", format = "csv", partition = "none", schema = new { c = "int64" } },
                new List<string> { "year=2023/data.csv", "year=2024/data.csv" });

            var resolver = new StorageResolver(_tempRoot);
            
            // 剪枝 2023，仅取 2024
            var files = resolver.ResolvePhysicalFiles(tableId, null, new DateTime(2024, 1, 1), null);
            
            Assert.AreEqual(1, files.Count);
            Assert.IsTrue(files[0].Contains("year=2024"));
        }

        [TestMethod]
        public void Resolve_FlatLayout_Parquet_ShouldUseCorrectExtension()
        {
            string tableId = "flat_parquet";
            CreateTable(tableId, 
                new { layout = "flat", format = "parquet", partition = "symbol", schema = new { c = "int64" } },
                new List<string> { "sh.600000.parquet" });

            var resolver = new StorageResolver(_tempRoot);
            var files = resolver.ResolvePhysicalFiles(tableId, "sh.600000");
            
            Assert.AreEqual(1, files.Count);
            Assert.IsTrue(files[0].EndsWith(".parquet"));
        }

        [TestMethod]
        public void Resolve_HiveLayout_Parquet_ShouldUseCorrectExtension()
        {
            string tableId = "hive_parquet";
            CreateTable(tableId,
                new { layout = "hive", format = "parquet", partition = "symbol", schema = new { c = "int64" } },
                new List<string> { "year=2024/sh.600000.parquet" });

            var resolver = new StorageResolver(_tempRoot);
            
            var files = resolver.ResolvePhysicalFiles(tableId, "sh.600000", new DateTime(2024, 1, 1), null);
            
            Assert.AreEqual(1, files.Count);
            Assert.IsTrue(files[0].EndsWith(".parquet"));
        }

        #endregion

        #region 3. 边界与异常测试

        [TestMethod]
        [ExpectedException(typeof(DirectoryNotFoundException))]
        public void Resolve_NonExistentTable_ShouldThrowException()
        {
            var resolver = new StorageResolver(_tempRoot);
            resolver.ResolvePhysicalFiles("unknown_table");
        }

        #endregion
    }
}
