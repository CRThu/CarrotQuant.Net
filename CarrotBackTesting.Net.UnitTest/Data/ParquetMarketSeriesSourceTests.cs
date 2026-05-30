using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Parquet;
using Parquet.Data;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    /// <summary>
    /// 针对 ParquetMarketSeriesSource 的单元测试。
    /// 物理提取 Parquet 行与数据进行对照校验，以确保加载出来的日期及价格与底层物理数据完全对齐。
    /// </summary>
    [TestClass]
    public class ParquetMarketSeriesSourceTests
    {
        private static string GetTestDataRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root");
        }

        [TestMethod]
        public void TestParquetMarketSeriesSourceReadAndPhysicalVerification()
        {
            string testDataRoot = GetTestDataRoot();
            string parquetRoot = Path.Combine(testDataRoot, "parquet");
            string tableId = "ashare.kline.1d.raw.baostock";
            string testSymbol = "sh.600000";

            // 1. 直接用底层的 ParquetReader 物理读取当月数据，建立物理基准
            var physicalData = new Dictionary<DateTime, double>();
            string parquetFile = Path.Combine(parquetRoot, tableId, "year=2024", "2024-01.parquet");
            Assert.IsTrue(File.Exists(parquetFile));

            using var fs = File.OpenRead(parquetFile);
            var reader = ParquetReader.CreateAsync(fs).GetAwaiter().GetResult();
            try
            {
                var dataFields = reader.Schema.GetDataFields();

                var symField = dataFields.First(f => f.Name.Equals("symbol", StringComparison.OrdinalIgnoreCase));
                var dtField = dataFields.FirstOrDefault(f => f.Name.Equals("datetime", StringComparison.OrdinalIgnoreCase))
                           ?? dataFields.FirstOrDefault(f => f.Name.Equals("timestamp", StringComparison.OrdinalIgnoreCase));
                var closeField = dataFields.First(f => f.Name.Equals("close", StringComparison.OrdinalIgnoreCase));

                for (int i = 0; i < reader.RowGroupCount; i++)
                {
                    var groupReader = reader.OpenRowGroupReader(i);
                    int rowCount = (int)groupReader.RowCount;
                    
                    var symArr = new string[rowCount];
                    groupReader.ReadAsync(symField, symArr).GetAwaiter().GetResult();

                    var closeArr = new double[rowCount];
                    var defLevels = new int[rowCount];
                    groupReader.ReadRawAsync<double>(closeField, closeArr, defLevels, null, default).GetAwaiter().GetResult();

                    var dtArr = new string[rowCount];
                    groupReader.ReadAsync(dtField, dtArr).GetAwaiter().GetResult();

                    for (int idx = 0; idx < rowCount; idx++)
                    {
                        if (symArr[idx].Equals(testSymbol, StringComparison.OrdinalIgnoreCase))
                        {
                            if (DateTime.TryParse(dtArr[idx], out var rowDate))
                            {
                                physicalData[rowDate.Date] = closeArr[idx];
                            }
                        }
                    }
                }
            }
            finally
            {
                reader.DisposeAsync().GetAwaiter().GetResult();
            }

            Assert.IsTrue(physicalData.Count > 0, "Physical Parquet data should contain rows.");

            // 2. 使用 ParquetMarketSeriesSource 加载序列
            using var source = new ParquetMarketSeriesSource(parquetRoot, tableId);
            Assert.IsTrue(source.Symbols.Contains(testSymbol));

            int length = source.TradeDates.Count;
            double[] destination = new double[length];
            source.ReadSymbolSeries(testSymbol, "close", 0, length, destination);

            // 3. 逐个交易日对比数据值
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
                    // 物理数据中不存在此日的记录（例如跨越了其他月份或停牌），值应为默认值 0.0 或 NaN
                    Assert.IsTrue(loadedVal == 0.0 || double.IsNaN(loadedVal), 
                        $"For non-listed date {globalDate:yyyy-MM-dd}, value should be 0.0 or NaN, but got {loadedVal}");
                }
            }
        }
    }
}
