using CarrotBacktesting.NET.Result;
using CarrotBacktesting.NET.Utility.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBackTesting.Net.UnitTest
{
    [TestClass]
    public class BacktestingResultSerialization
    {
        #region Test Data Factory (测试数据工厂)

        /// <summary>
        /// 创建一个包含乱序信号的、用于测试的 BacktestingResult 实例。
        /// </summary>
        /// <returns>一个包含3个测试信号的BacktestingResult对象。</returns>
        private BacktestingResult CreateTestBacktestingResultWithUnorderedSignals()
        {
            var result = new BacktestingResult();
            // 以乱序方式添加信号，以严格测试序列化时的排序逻辑
            result.SignalsResult.Store("GOOG", new DateTime(2025, 11, 01, 0, 0, 0, DateTimeKind.Utc));
            result.SignalsResult.Store("AAPL", new DateTime(2025, 11, 02, 0, 0, 0, DateTimeKind.Utc));
            result.SignalsResult.Store("AAPL", new DateTime(2025, 11, 01, 0, 0, 0, DateTimeKind.Utc));
            return result;
        }

        #endregion

        #region JSON Serialization Tests (JSON 序列化测试)

        [TestMethod]
        public void BacktestingResult_JsonSerialization_ShouldPreserveDataAndOrder()
        {
            // --- 1. Arrange (准备) ---
            var originalResult = CreateTestBacktestingResultWithUnorderedSignals();
            // 获取一份原始的、排好序的信号列表，作为我们期望的“正确答案”
            var expectedSignals = originalResult.SignalsResult.GetSignals().ToList();
            Console.WriteLine("--- Testing JSON Serialization ---");

            // --- 2. Act (操作) ---
            string jsonString = JsonSerializationHelper.SerializeToString(originalResult);
            Console.WriteLine("Serialized JSON:\n" + jsonString);
            var deserializedResult = JsonSerializationHelper.DeserializeFromString<BacktestingResult>(jsonString);

            // --- 3. Assert (断言) ---
            AssertBacktestResultIsValid(deserializedResult, expectedSignals, "JSON");
        }

        #endregion

        #region MessagePack Serialization Tests (MessagePack 序列化测试)

        [TestMethod]
        public void BacktestingResult_MessagePackSerialization_ShouldPreserveDataAndOrder()
        {
            // --- 1. Arrange (准备) ---
            var originalResult = CreateTestBacktestingResultWithUnorderedSignals();
            var expectedSignals = originalResult.SignalsResult.GetSignals().ToList();
            Console.WriteLine("\n--- Testing MessagePack Serialization ---");

            // --- 2. Act (操作) ---
            byte[] bytes = MessagePackSerializationHelper.SerializeToBytes(originalResult);
            var deserializedResult = MessagePackSerializationHelper.DeserializeFromBytes<BacktestingResult>(bytes);

            // --- 3. Assert (断言) ---
            AssertBacktestResultIsValid(deserializedResult, expectedSignals, "MessagePack");
        }

        #endregion

        #region Custom Assertions (自定义断言)

        /// <summary>
        /// 用于深度比较反序列化后的 BacktestingResult 是否与预期一致。
        /// </summary>
        /// <param name="actualResult">实际反序列化得到的结果</param>
        /// <param name="expectedSignals">期望的、有序的信号列表</param>
        /// <param name="context">上下文信息（如 "JSON" 或 "MessagePack"），用于生成更清晰的错误报告</param>
        private void AssertBacktestResultIsValid(BacktestingResult? actualResult, List<SignalInfo> expectedSignals, string context)
        {
            // 验证对象本身和其内部的SignalSet不为null
            Assert.IsNotNull(actualResult, $"[{context}] 反序列化后的结果不应为null。");
            Assert.IsNotNull(actualResult.SignalsResult, $"[{context}] 反序列化后的SignalSet不应为null。");

            // 验证信号数量是否正确
            Assert.AreEqual(expectedSignals.Count, actualResult.SignalsResult.Count, $"[{context}] 反序列化后的信号数量应与原始数量一致。");

            // 关键验证：获取反序列化后的有序列表，并与“正确答案”进行逐一比较
            var actualSignals = actualResult.SignalsResult.GetSignals().ToList();
            CollectionAssert.AreEqual(expectedSignals, actualSignals, $"[{context}] 反序列化后的信号列表内容和顺序应与原始有序列表完全一致。");
        }

        #endregion
    }
}
