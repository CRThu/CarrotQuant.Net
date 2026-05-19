using System;
using System.IO;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Common
{
    /// <summary>
    /// 针对 MarketDataResolver 的单元测试，测试路径拼接、类型推导以及年份扫描的正确性。
    /// </summary>
    [TestClass]
    public class MarketDataResolverTests
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
            Assert.IsTrue(Directory.Exists(testDataRoot), $"Test data root directory not found: {testDataRoot}");

            string csvRoot = Path.Combine(testDataRoot, "csv");
            string tableId = "ashare.kline.1d.raw.baostock";

            var resolver = new MarketDataResolver(csvRoot, tableId);

            // 验证 Format 识别
            Assert.AreEqual("csv", resolver.Format);
            
            // 验证 Field Schema
            Assert.IsTrue(resolver.FieldNames.Contains("close"));
            Assert.IsTrue(resolver.FieldNames.Contains("volume"));

            // 验证可用年份扫描
            var years = resolver.GetAvailableYears();
            Assert.IsTrue(years.Contains(2024));
            Assert.IsTrue(years.Contains(2025));

            // 验证类型反序列化解析映射
            Assert.AreEqual(typeof(double), resolver.GetFieldType("close"));
            Assert.AreEqual(typeof(long), resolver.GetFieldType("timestamp"));

            // 验证物理路径拼接
            string expectedPath = Path.Combine(resolver.TableDir, "year=2024", "sh.600000.csv");
            Assert.AreEqual(expectedPath, resolver.GetCsvFilePath(2024, "sh.600000"));
        }
    }
}
