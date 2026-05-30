using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sylvan.Data.Csv;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    /// <summary>
    /// 针对 CsvMarketSeriesSource 的单元测试。
    /// 物理提取 CSV 行与数据进行对照校验，以确保加载出来的日期及价格与底层物理数据完全对齐。
    /// </summary>
    [TestClass]
    public class CsvMarketSeriesSourceTests
    {
        private static string GetTestDataRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root");
        }

        [TestMethod]
        public void TestCsvMarketSeriesSourceReadAndPhysicalVerification()
        {
            string testDataRoot = GetTestDataRoot();
            string csvRoot = Path.Combine(testDataRoot, "csv");
            string tableId = "ashare.kline.1d.raw.baostock";
            string testSymbol = "sh.600000";

            // 1. 物理读取 CSV 数据，建立物理基准字典
            var physicalData = new Dictionary<DateTime, double>();
            var years = new[] { 2024, 2025 };
            foreach (var year in years)
            {
                string csvFile = Path.Combine(csvRoot, tableId, $"year={year}", $"{testSymbol}.csv");
                if (!File.Exists(csvFile)) continue;

                using var csvReader = CsvDataReader.Create(csvFile);
                int dateCol = csvReader.GetOrdinal("datetime");
                int closeCol = csvReader.GetOrdinal("close");

                while (csvReader.Read())
                {
                    DateTime date = DateTime.MinValue;
                    if (csvReader.GetFieldType(dateCol) == typeof(DateTime))
                    {
                        date = csvReader.GetDateTime(dateCol).Date;
                    }
                    else
                    {
                        string dtStr = csvReader.GetString(dateCol);
                        if (DateTime.TryParse(dtStr, out var parsedDate))
                        {
                            date = parsedDate.Date;
                        }
                    }

                    double close = csvReader.GetDouble(closeCol);
                    if (date != DateTime.MinValue)
                    {
                        physicalData[date] = close;
                    }
                }
            }

            Assert.IsTrue(physicalData.Count > 0, "Physical data should contain rows.");

            // 2. 通过 CsvMarketSeriesSource 载入序列
            using var source = new CsvMarketSeriesSource(csvRoot, tableId);
            Assert.IsTrue(source.Symbols.Contains(testSymbol));
            
            int length = source.TradeDates.Count;
            double[] destination = new double[length];
            source.ReadSymbolSeries(testSymbol, "close", 0, length, destination);

            // 3. 逐交易日对比行（交易日）和物理数值
            for (int i = 0; i < length; i++)
            {
                DateTime globalDate = source.TradeDates[i];
                double loadedVal = destination[i];

                if (physicalData.TryGetValue(globalDate, out double physicalVal))
                {
                    // 在物理数据中存在该交易日，值必须完全一致
                    if (double.IsNaN(physicalVal))
                    {
                        Assert.IsTrue(double.IsNaN(loadedVal), $"At {globalDate:yyyy-MM-dd}, loaded Close should be NaN.");
                    }
                    else
                    {
                        Assert.AreEqual(physicalVal, loadedVal, 1e-6, $"Close value mismatch at {globalDate:yyyy-MM-dd}.");
                    }
                }
                else
                {
                    // 物理数据中不存在此日的记录（例如本股票尚未上市或停牌），值应为默认值 0.0 或 NaN
                    Assert.IsTrue(loadedVal == 0.0 || double.IsNaN(loadedVal), 
                        $"For non-listed date {globalDate:yyyy-MM-dd}, value should be 0.0 or NaN, but got {loadedVal}");
                }
            }
        }
    }
}
