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

        #region 1. 元数据解析测试

        [TestMethod]
        [DataRow("timeseries", "hive", "symbol", "csv")]
        [DataRow("event", "flat", "none", "parquet")]
        [DataRow("field", "flat", "none", "parquet")]
        [DataRow("fields", "flat", "none", "csv")]
        [DataRow("timeseries", "flat", "symbol", "parquet")]
        [DataRow("event", "hive", "none", "csv")]
        public void MetadataParsing_ShouldMapCorrectly(string cat, string lay, string part, string fmt)
        {
            string tableId = $"{cat}_{lay}_{part}_{fmt}";
            CreateTable(tableId, new { category = cat, layout = lay, partition = part, format = fmt, schema = new { id = "int64" } });

            var resolver = new StorageResolver(_tempRoot);
            var meta = resolver.GetMetadata(tableId);

            TableCategory expectedCategory = cat switch
            {
                "field" or "fields" => TableCategory.Field,
                "event" or "events" => TableCategory.Event,
                _ => TableCategory.Timeseries
            };
            
            Assert.AreEqual(expectedCategory, meta.Category);
            Assert.AreEqual(lay == "hive" ? StorageLayout.Hive : StorageLayout.Flat, meta.Layout);
            Assert.AreEqual(part == "symbol" ? PartitionMode.Symbol : PartitionMode.None, meta.Partition);
            Assert.AreEqual(fmt == "parquet" ? StorageFormat.Parquet : StorageFormat.Csv, meta.Format);
        }

        #endregion

        #region 2. 物理文件调度测试

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
        public void Resolve_Event_FlatLayout_SymbolPartition_ShouldRouteToSpecificFile()
        {
            string tableId = "event_flat_symbol";
            CreateTable(tableId, 
                new { category = "event", layout = "flat", format = "csv", partition = "symbol", schema = new { id = "int64" } },
                new List<string> { "sh.600000.csv" });

            var resolver = new StorageResolver(_tempRoot);
            
            Assert.AreEqual(TableCategory.Event, resolver.GetMetadata(tableId).Category);
            var files = resolver.ResolvePhysicalFiles(tableId, "sh.600000");
            Assert.AreEqual(1, files.Count);
            Assert.IsTrue(files[0].EndsWith("sh.600000.csv"));
        }

        [TestMethod]
        public void Resolve_HiveLayout_SymbolPartition_ShouldPruneYearsAndRouteToSymbol()
        {
            string tableId = "hive_symbol";
            CreateTable(tableId,
                new { layout = "hive", format = "csv", partition = "symbol", schema = new { c = "int64" } },
                new List<string> { "year=2023/sh.600000.csv", "year=2024/sh.600000.csv" });

            var resolver = new StorageResolver(_tempRoot);
            
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
        public void Resolve_HiveLayout_MultiLevelMonthPartition_ShouldPruneMonthsCorrectly()
        {
            string tableId = "hive_multi_level_month";
            CreateTable(tableId,
                new { layout = "hive", format = "parquet", partition = "none", schema = new { c = "int64" } },
                new List<string>
                {
                    "year=2025/month=01/data.parquet",
                    "year=2025/month=02/data.parquet",
                    "year=2025/month=03/data.parquet",
                    "year=2024/month=12/data.parquet"
                });

            var resolver = new StorageResolver(_tempRoot);

            // 1. 无时间过滤：应返回全部 4 个文件
            var allFiles = resolver.ResolvePhysicalFiles(tableId);
            Assert.AreEqual(4, allFiles.Count);

            // 2. 年份与月份过滤：2025年2月到2025年2月 -> 仅 month=02
            var febOnly = resolver.ResolvePhysicalFiles(tableId, null, new DateTime(2025, 2, 1), new DateTime(2025, 2, 28));
            Assert.AreEqual(1, febOnly.Count);
            Assert.IsTrue(febOnly[0].Replace('\\', '/').Contains("year=2025/month=02/data.parquet"));

            // 3. 起始时间剪枝：2025年2月及以后 -> month=02, month=03
            var fromFeb = resolver.ResolvePhysicalFiles(tableId, null, new DateTime(2025, 2, 1), null);
            Assert.AreEqual(2, fromFeb.Count);
            Assert.IsTrue(fromFeb.Any(f => f.Replace('\\', '/').Contains("year=2025/month=02")));
            Assert.IsTrue(fromFeb.Any(f => f.Replace('\\', '/').Contains("year=2025/month=03")));

            // 4. 跨年起止时间剪枝：2024年12月到2025年1月 -> 2024-12 和 2025-01
            var crossYear = resolver.ResolvePhysicalFiles(tableId, null, new DateTime(2024, 12, 1), new DateTime(2025, 1, 31));
            Assert.AreEqual(2, crossYear.Count);
            Assert.IsTrue(crossYear.Any(f => f.Replace('\\', '/').Contains("year=2024/month=12")));
            Assert.IsTrue(crossYear.Any(f => f.Replace('\\', '/').Contains("year=2025/month=01")));
        }

        [TestMethod]
        public void Resolve_HiveLayout_CachedTopology_ShouldAvoidDiskReEnumeration()
        {
            string tableId = "hive_cached_topology";
            CreateTable(tableId,
                new { layout = "hive", format = "csv", partition = "symbol", schema = new { c = "int64" } },
                new List<string>
                {
                    "year=2023/sh.600000.csv",
                    "year=2023/sh.600001.csv",
                    "year=2024/sh.600000.csv",
                    "year=2024/sh.600001.csv"
                });

            var resolver = new StorageResolver(_tempRoot);

            // 首次解析触发元数据与目录拓扑缓存
            var files600000 = resolver.ResolvePhysicalFiles(tableId, "sh.600000");
            Assert.AreEqual(2, files600000.Count);

            // 后续解析不同标的直接复用已缓存的年份目录结构，准确路由
            var files600001 = resolver.ResolvePhysicalFiles(tableId, "sh.600001", new DateTime(2024, 1, 1), null);
            Assert.AreEqual(1, files600001.Count);
            Assert.IsTrue(files600001[0].Contains("year=2024"));
            Assert.IsTrue(files600001[0].EndsWith("sh.600001.csv"));
        }

        #endregion

        #region 3. 边界与异常测试

        [TestMethod]
        public void Resolve_WithoutMetadataJson_Parquet_ShouldAutoInferMetadata()
        {
            // 复制一个无 metadata.json 的真实 Parquet Hive 表到临时目录
            string tableId = "parquet_no_metadata";
            string srcParquet = Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "parquet", "ashare.kline.1m.raw.custom", "year=2025", "month=01", "data.parquet");
            
            string destDir = Path.Combine(_tempRoot, tableId, "year=2025", "month=01");
            Directory.CreateDirectory(destDir);
            File.Copy(srcParquet, Path.Combine(destDir, "data.parquet"));

            var resolver = new StorageResolver(_tempRoot);

            Assert.IsTrue(resolver.HasTable(tableId));
            var meta = resolver.GetMetadata(tableId);
            Assert.AreEqual(StorageFormat.Parquet, meta.Format);
            Assert.AreEqual(StorageLayout.Hive, meta.Layout);
            Assert.AreEqual(PartitionMode.None, meta.Partition);
            Assert.AreEqual(TableCategory.Timeseries, meta.Category);

            var fields = meta.FieldNames;
            Assert.IsTrue(fields.Contains("symbol"));
            Assert.IsTrue(fields.Contains("close"));
            Assert.AreEqual(typeof(double), meta.GetFieldType("close"));
            Assert.AreEqual(typeof(string), meta.GetFieldType("symbol"));

            var files = resolver.ResolvePhysicalFiles(tableId);
            Assert.AreEqual(1, files.Count);
        }

        [TestMethod]
        public void Resolve_WithoutMetadataJson_Csv_ShouldAutoInferMetadata()
        {
            // 创建一个无 metadata.json 的 CSV 表
            string tableId = "csv_no_metadata";
            string dir = Path.Combine(_tempRoot, tableId, "year=2024");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "sh.600000.csv"), "symbol,datetime,close,volume\nsh.600000,2024-01-02,10.5,1000\n");

            var resolver = new StorageResolver(_tempRoot);

            Assert.IsTrue(resolver.HasTable(tableId));
            var meta = resolver.GetMetadata(tableId);
            Assert.AreEqual(StorageFormat.Csv, meta.Format);
            Assert.AreEqual(StorageLayout.Hive, meta.Layout);
            Assert.AreEqual(PartitionMode.Symbol, meta.Partition);

            var fields = meta.FieldNames;
            Assert.IsTrue(fields.Contains("symbol"));
            Assert.IsTrue(fields.Contains("close"));
            Assert.AreEqual(typeof(double), meta.GetFieldType("close"));
            Assert.AreEqual(typeof(string), meta.GetFieldType("symbol"));
        }

        [TestMethod]
        [ExpectedException(typeof(DirectoryNotFoundException))]
        public void Resolve_NonExistentTable_ShouldThrowException()
        {
            var resolver = new StorageResolver(_tempRoot);
            resolver.ResolvePhysicalFiles("unknown_table");
        }

        [TestMethod]
        public void ResolvePhysicalFiles_PassMetadataDirectly_ShouldBypassCatalogQuery()
        {
            string tableId = "direct_meta_test";
            CreateTable(tableId,
                new { layout = "flat", partition = "symbol", format = "csv", schema = new { id = "int64" } },
                new List<string> { "sh.600000.csv", "sz.000001.csv" });

            var resolver = new StorageResolver(_tempRoot);
            var meta = resolver.GetMetadata(tableId);
            var files = resolver.ResolvePhysicalFiles(meta, "sh.600000");

            Assert.AreEqual(1, files.Count);
            Assert.IsTrue(files[0].EndsWith("sh.600000.csv"));
        }

        [TestMethod]
        public void ClearCache_ShouldInvalidateCachedMetadataAndTopology()
        {
            string tableId = "clear_cache_test";
            CreateTable(tableId, new { format = "csv", schema = new { price = "float64" } });

            var resolver = new StorageResolver(_tempRoot);
            var m1 = resolver.GetMetadata(tableId);
            Assert.IsNotNull(m1);

            resolver.ClearCache(tableId);
            var m2 = resolver.GetMetadata(tableId);
            Assert.AreNotSame(m1, m2);
        }

        #endregion
    }
}
