using CarrotBacktesting.NET.Result;
using CarrotBacktesting.NET.Utility.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.UnitTest
{
    [TestClass]
    public class BacktestingResultSerialization
    {
        #region Test Data Factory (测试数据工厂)

        /// <summary>
        /// 创建一个包含乱序信号的、用于测试的 BacktestingResult 实例。
        /// </summary>
        /// <returns>一个包含3个测试信号的BacktestingResult对象。</returns>
        private BacktestingResult CreateTestBacktestingResultWithTrades()
        {
            var result = new BacktestingResult();
           
            // 创建一笔已平仓的交易
            var trade1 = new Trade("AAPL", "Default", "Entry", new DateTime(2025, 11, 01), 150.0);
            trade1.Close("Default", "Exit", new DateTime(2025, 11, 05), 160.0);

            // 创建另一笔已平仓的交易
            var trade2 = new Trade("GOOG", "Default", "Entry", new DateTime(2025, 10, 30), 2800.0);
            trade2.Close("Default", "Exit", new DateTime(2025, 11, 10), 2750.0);

            // 创建一笔未平仓的交易
            var trade3 = new Trade("MSFT", "Default", "Entry", new DateTime(2025, 11, 03), 300.0);

            // 以任意顺序添加到结果中
            result.Trades.Add(trade1);
            result.Trades.Add(trade3);
            result.Trades.Add(trade2);
            
            return result;
        }

        #endregion

        #region JSON Serialization Tests (JSON 序列化测试)

        [TestMethod]
        public void BacktestingResult_JsonSerialization_ShouldPreserveDataAndOrder()
        {
            // --- 1. Arrange (准备) ---
            var originalResult = CreateTestBacktestingResultWithTrades();
            // 获取一份原始的、按入场日期排好序的交易列表，作为我们期望的“正确答案”
            var expectedTrades = originalResult.Trades.OrderBy(t => t.EntryDate).ToList();
            Console.WriteLine("--- Testing JSON Serialization ---");

            // --- 2. Act (操作) ---
            string jsonString = JsonSerializationHelper.SerializeToString(originalResult);
            Console.WriteLine("Serialized JSON:\n" + jsonString);
            var deserializedResult = JsonSerializationHelper.DeserializeFromString<BacktestingResult>(jsonString);

            // --- 3. Assert (断言) ---
            AssertBacktestResultIsValid(deserializedResult, expectedTrades, "JSON");
        }

        #endregion

        #region MessagePack Serialization Tests (MessagePack 序列化测试)

        [TestMethod]
        public void BacktestingResult_MessagePackSerialization_ShouldPreserveTradeData()
        {
            // --- 1. Arrange (准备) ---
            var originalResult = CreateTestBacktestingResultWithTrades();
            var expectedTrades = originalResult.Trades.OrderBy(t => t.EntryDate).ToList();
            Console.WriteLine("\n--- Testing MessagePack Serialization ---");

            // --- 2. Act (操作) ---
            byte[] bytes = MessagePackSerializationHelper.SerializeToBytes(originalResult);
            var deserializedResult = MessagePackSerializationHelper.DeserializeFromBytes<BacktestingResult>(bytes);

            // --- 3. Assert (断言) ---
            AssertBacktestResultIsValid(deserializedResult, expectedTrades, "MessagePack");
        }

        #endregion

        #region Custom Assertions (自定义断言)

        /// <summary>
        /// 用于深度比较反序列化后的 BacktestingResult 是否与预期一致。
        /// </summary>
        /// <param name="actualResult">实际反序列化得到的结果</param>
        /// <param name="expectedTrades">期望的、有序的交易列表</param>
        /// <param name="context">上下文信息（如 "JSON" 或 "MessagePack"），用于生成更清晰的错误报告</param>
        private void AssertBacktestResultIsValid(BacktestingResult? actualResult, List<Trade> expectedTrades, string context)
        {
            // 验证对象本身不为null
            Assert.IsNotNull(actualResult, $"[{context}] 反序列化后的结果不应为null。");

            // 验证交易数量是否正确
            Assert.AreEqual(expectedTrades.Count, actualResult.Trades.Count, $"[{context}] 反序列化后的交易数量应与原始数量一致。");

            // 关键验证：将反序列化后的列表也按入场日期排序，然后与“正确答案”进行逐一比较
            var actualTrades = actualResult.Trades.OrderBy(t => t.EntryDate).ToList();

            for (int i = 0; i < expectedTrades.Count; i++)
            {
                var expected = expectedTrades[i];
                var actual = actualTrades[i];

                Assert.AreEqual(expected.StockCode, actual.StockCode, $"[{context}] Trade #{i} StockCode不匹配。");
                Assert.AreEqual(expected.EntryDate, actual.EntryDate, $"[{context}] Trade #{i} EntryDate不匹配。");
                Assert.AreEqual(expected.EntryPrice, actual.EntryPrice, $"[{context}] Trade #{i} EntryPrice不匹配。");
                Assert.AreEqual(expected.IsClosed, actual.IsClosed, $"[{context}] Trade #{i} IsClosed状态不匹配。");

                // 只对已平仓的交易验证平仓信息
                if (expected.IsClosed)
                {
                    Assert.AreEqual(expected.ExitDate, actual.ExitDate, $"[{context}] Trade #{i} ExitDate不匹配。");
                    Assert.AreEqual(expected.ExitPrice, actual.ExitPrice, $"[{context}] Trade #{i} ExitPrice不匹配。");
                    Assert.AreEqual(expected.Return, actual.Return, $"[{context}] Trade #{i} Return不匹配。");
                }
            }
        }

        #endregion
    }
}
