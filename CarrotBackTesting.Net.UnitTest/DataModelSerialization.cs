using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Utility;
using CarrotBacktesting.NET.Utility.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CarrotBacktesting.NET.UnitTest
{
    [TestClass]
    public sealed class DataModelSerialization
    {
        #region Test Data Factory Methods

        /// <summary>
        /// 创建一个用于测试的、确定的 StockFrame 实例。
        /// </summary>
        private StockFrame CreateTestStockFrame()
        {
            return new StockFrame(1.1, 2.2, 3.3, 4.4, 5000, TradeStatus.Active);
        }

        /// <summary>
        /// 创建一个用于测试的、确定的 MarketFrame 实例。
        /// </summary>
        private MarketFrame CreateTestMarketFrame()
        {
            var stockFrame = CreateTestStockFrame();
            // 使用一个固定的日期，确保测试的确定性
            var testDate = new DateTime(2025, 10, 17, 15, 0, 0, DateTimeKind.Utc);
            return new MarketFrame(testDate, new StockFrame?[] { stockFrame, null }, new(), new());
        }

        /// <summary>
        /// 创建一个用于测试的、确定的 MarketStorage 实例。
        /// </summary>
        private MarketStorage CreateTestMarketStorage()
        {
            var marketFrame = CreateTestMarketFrame();
            var testDate = marketFrame.Time.Date;
            var symbolsMap = new Dictionary<string, int> { { "AAPL", 0 }, { "GOOG", 1 } };
            // 注意：MarketFrame中的数组长度应与SymbolsMap匹配
            marketFrame.PrimaryData[1] = CreateTestStockFrame(); // 填充之前为null的第二个元素

            var marketFrames = new Dictionary<DateTime, MarketFrame> { { testDate, marketFrame } };
            return new MarketStorage(marketFrames, symbolsMap);
        }

        #endregion

        #region StockFrame Tests

        [TestMethod]
        public void StockFrame_JsonSerialization_ShouldBeEqual()
        {
            // Arrange
            var original = CreateTestStockFrame();

            // Act
            string jsonString = JsonSerializationHelper.SerializeToString(original);
            StockFrame deserialized = JsonSerializationHelper.DeserializeFromString<StockFrame>(jsonString);

            // Assert
            Assert.AreEqual(original, deserialized, "JSON反序列化后的StockFrame应与原始对象相等。");
        }

        [TestMethod]
        public void StockFrame_MessagePackSerialization_ShouldBeEqual()
        {
            // Arrange
            var original = CreateTestStockFrame();

            // Act
            byte[] bytes = MessagePackSerializationHelper.SerializeToBytes(original);
            StockFrame deserialized = MessagePackSerializationHelper.DeserializeFromBytes<StockFrame>(bytes);

            // Assert
            Assert.AreEqual(original, deserialized, "MessagePack反序列化后的StockFrame应与原始对象相等。");
        }

        #endregion

        #region MarketFrame Tests

        [TestMethod]
        public void MarketFrame_JsonSerialization_ShouldBeEqual()
        {
            // Arrange
            var original = CreateTestMarketFrame();

            // Act
            string jsonString = JsonSerializationHelper.SerializeToString(original);
            MarketFrame? deserialized = JsonSerializationHelper.DeserializeFromString<MarketFrame>(jsonString);

            // Assert
            Assert.IsNotNull(deserialized);
            Assert.AreEqual(original.Time, deserialized.Time);
            CollectionAssert.AreEqual(original.PrimaryData, deserialized.PrimaryData, "JSON反序列化后的PrimaryData数组应相等。");
        }

        [TestMethod]
        public void MarketFrame_MessagePackSerialization_ShouldBeEqual()
        {
            // Arrange
            var original = CreateTestMarketFrame();

            // Act
            byte[] bytes = MessagePackSerializationHelper.SerializeToBytes(original);
            MarketFrame? deserialized = MessagePackSerializationHelper.DeserializeFromBytes<MarketFrame>(bytes);

            // Assert
            Assert.IsNotNull(deserialized);
            Assert.AreEqual(original.Time, deserialized.Time);
            CollectionAssert.AreEqual(original.PrimaryData, deserialized.PrimaryData, "MessagePack反序列化后的PrimaryData数组应相等。");
        }

        #endregion

        #region MarketStorage Tests

        [TestMethod]
        public void MarketStorage_JsonSerialization_ShouldBeEqual()
        {
            // Arrange
            var original = CreateTestMarketStorage();

            // Act
            string jsonString = JsonSerializationHelper.SerializeToString(original);
            MarketStorage? deserialized = JsonSerializationHelper.DeserializeFromString<MarketStorage>(jsonString);

            // Assert
            AssertMarketStorageAreEqual(original, deserialized, "JSON");
        }

        [TestMethod]
        public void MarketStorage_MessagePackSerialization_ShouldBeEqual()
        {
            // Arrange
            var original = CreateTestMarketStorage();

            // Act
            byte[] bytes = MessagePackSerializationHelper.SerializeToBytes(original);
            MarketStorage? deserialized = MessagePackSerializationHelper.DeserializeFromBytes<MarketStorage>(bytes);

            // Assert
            AssertMarketStorageAreEqual(original, deserialized, "MessagePack");
        }

        /// <summary>
        /// 自定义的断言方法，用于深度比较两个 MarketStorage 对象。
        /// </summary>
        private void AssertMarketStorageAreEqual(MarketStorage? expected, MarketStorage? actual, string context)
        {
            Assert.IsNotNull(actual, $"[{context}] 反序列化后的 MarketStorage不应为null。");

            // 验证 SymbolsMap 和 Symbols 列表
            CollectionAssert.AreEqual(expected.SymbolsMap, actual.SymbolsMap, $"[{context}] SymbolsMap 应相等。");
            CollectionAssert.AreEqual(expected.Symbols.ToList(), actual.Symbols.ToList(), $"[{context}] Symbols 列表应被正确重建。");

            // 验证 MarketFrames 字典
            Assert.AreEqual(expected.MarketFrames.Count, actual.MarketFrames.Count, $"[{context}] MarketFrames 数量应相等。");
            var expectedFrame = expected.MarketFrames.First().Value;
            var actualFrame = actual.MarketFrames.First().Value;
            Assert.AreEqual(expectedFrame.Time, actualFrame.Time, $"[{context}] MarketFrame 的时间应相等。");
            CollectionAssert.AreEqual(expectedFrame.PrimaryData, actualFrame.PrimaryData, $"[{context}] MarketFrame 内部的 PrimaryData 应相等。");
        }

        #endregion
    }
}