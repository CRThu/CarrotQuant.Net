using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Utility;

namespace CarrotBackTesting.Net.UnitTest
{
    [TestClass]
    public sealed class DataModelSerialization
    {
        [TestMethod]
        public void StockFrame_Serialization_ShouldBeEqual()
        {
            // Arrange: 准备原始数据
            var originalFrame = new StockFrame(1.1, 2.2, 3.3, 4.4, 5000, TradeStatus.Active);

            // Act: 执行序列化和反序列化
            string jsonString = SerializationHelper.SerializeToString(originalFrame);
            Console.WriteLine("StockFrame JSON:\n" + jsonString);
            StockFrame deserializedFrame = SerializationHelper.DeserializeFromString<StockFrame>(jsonString);

            // Assert: 验证结果
            Assert.AreEqual(originalFrame, deserializedFrame, "反序列化后的 StockFrame 应该与原始对象完全相等。");
        }

        [TestMethod]
        public void MarketFrame_Serialization_ShouldBeEqual()
        {
            // Arrange: 准备原始数据
            // 1. 创建一个用于测试的 StockFrame
            var stockFrame = new StockFrame(10, 12, 9, 11, 10000, TradeStatus.Active);
            // 2. 使用一个固定的日期，而不是 DateTime.Now，确保测试的确定性
            var testDate = new DateTime(2025, 10, 17, 15, 0, 0);
            var originalFrame = new MarketFrame(testDate, new StockFrame?[] { stockFrame, null }, new(), new());

            // Act: 执行序列化和反序列化
            string jsonString = SerializationHelper.SerializeToString(originalFrame);
            Console.WriteLine("\nMarketFrame JSON:\n" + jsonString);
            MarketFrame? deserializedFrame = SerializationHelper.DeserializeFromString<MarketFrame>(jsonString);

            // Assert: 验证结果
            Assert.IsNotNull(deserializedFrame);
            Assert.AreEqual(originalFrame.Time, deserializedFrame.Time, "日期应该相等。");
            Assert.AreEqual(originalFrame.PrimaryData.Length, deserializedFrame.PrimaryData.Length, "数据数组长度应该相等。");
            Assert.AreEqual(originalFrame.PrimaryData[0], deserializedFrame.PrimaryData[0], "数组中的StockFrame应该相等。");
            Assert.IsNull(deserializedFrame.PrimaryData[1], "数组中的null值应该保持为null。");
        }

        [TestMethod]
        public void MarketStorage_Serialization_ShouldBeEqual()
        {
            // Arrange: 准备一个完整的、自包含的测试数据
            // 1. 创建底层数据
            var stockFrame = new StockFrame(150.5, 152, 149.8, 151.2, 5000000, TradeStatus.Active);
            var testDate = DateTime.Today; // 使用 Today 可以避免时间部分带来的微小差异
            var marketFrame = new MarketFrame(testDate, new StockFrame?[] { stockFrame }, new(), new());

            // 2. 创建符号和市场帧的字典
            var symbolsMap = new Dictionary<string, int> { { "AAPL", 0 } };
            var marketFrames = new Dictionary<DateTime, MarketFrame> { { testDate, marketFrame } };

            // 3. 创建原始的 MarketStorage 对象
            var originalStorage = new MarketStorage(symbolsMap, marketFrames);

            // Act: 执行序列化和反序列化
            string jsonString = SerializationHelper.SerializeToString(originalStorage);
            Console.WriteLine("\nMarketStorage JSON:\n" + jsonString);
            MarketStorage? deserializedStorage = SerializationHelper.DeserializeFromString<MarketStorage>(jsonString);

            // Assert: 验证结果
            Assert.IsNotNull(deserializedStorage);
            // 验证 SymbolsMap 和 Symbols 列表是否正确恢复
            Assert.AreEqual(1, deserializedStorage.SymbolsMap.Count, "SymbolsMap 数量应该为1。");
            Assert.AreEqual(0, deserializedStorage.SymbolsMap["AAPL"], "AAPL的索引应该为0。");
            Assert.AreEqual("AAPL", deserializedStorage.Symbols.First(), "Symbols列表应该被正确重建。");

            // 验证 MarketFrames 字典是否正确恢复
            Assert.AreEqual(1, deserializedStorage.MarketFrames.Count, "MarketFrames 数量应该为1。");
            Assert.IsTrue(deserializedStorage.MarketFrames.ContainsKey(testDate), "应该包含正确的日期键。");

            // 验证字典中的 MarketFrame 内容是否正确
            var deserializedMarketFrame = deserializedStorage.MarketFrames[testDate];
            Assert.AreEqual(marketFrame.Time, deserializedMarketFrame.Time);
            Assert.AreEqual(stockFrame, deserializedMarketFrame.PrimaryData[0]);
        }
    }
}
