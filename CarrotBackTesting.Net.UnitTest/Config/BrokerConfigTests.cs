using System;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Config
{
    [TestClass]
    public class BrokerConfigTests
    {
        [TestMethod]
        public void BrokerConfig_DefaultValues_ShouldMatchQuantBaseline()
        {
            var config = new BrokerConfig();

            Assert.AreEqual(1000000.0, config.InitialCash);
            Assert.AreEqual(0, config.SettleDays);
            Assert.IsFalse(config.AllowShort);
            Assert.AreEqual(0.001, config.FeeRate);
            Assert.AreEqual(0.0, config.MinFee);
            Assert.AreEqual(0.001, config.Slippage);
            Assert.AreEqual(0.05, config.VolumeLimit);
            Assert.IsFalse(config.CheckLimit);
            Assert.AreEqual(1.0, config.Multiplier);
            Assert.AreEqual(1.0, config.MarginRate);
            Assert.AreEqual("UTC", config.TimeZoneId);
            Assert.AreEqual(TimeZoneInfo.Utc, config.TimeZone);
            Assert.IsNull(config.OnCheckVolume);
            Assert.IsNull(config.OnGetSettleDays);
            Assert.IsNull(config.OnGetMultiplier);
            Assert.IsNull(config.OnGetMarginRate);
            Assert.IsNull(config.OnCalculateFee);
            Assert.IsNull(config.OnCalculateSlippage);
            Assert.IsNull(config.OnCheckLimit);
        }

        [TestMethod]
        public void BrokerConfig_TimeZone_PropertyAndId_RoundTripAndFallback()
        {
            var config = new BrokerConfig();

            // 1. 显式给 TimeZone 属性赋值有效时区
            var shanghai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
            config.TimeZone = shanghai;
            Assert.AreEqual("Asia/Shanghai", config.TimeZoneId);
            Assert.AreEqual(shanghai, config.TimeZone);

            // 2. 给 TimeZone 属性赋 null，自动安全回退为 UTC
            config.TimeZone = null!;
            Assert.AreEqual(TimeZoneInfo.Utc, config.TimeZone);
            Assert.AreEqual(TimeZoneInfo.Utc.Id, config.TimeZoneId);

            // 3. 显式给 TimeZoneId 赋值
            config.TimeZoneId = "UTC";
            Assert.AreEqual("UTC", config.TimeZoneId);
            Assert.AreEqual(TimeZoneInfo.Utc, config.TimeZone);
        }

        [TestMethod]
        public void BrokerConfig_TimeZoneId_NullOrWhitespace_ResetsToUtc()
        {
            var config = new BrokerConfig();
            config.TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

            // 空字符串重置为 UTC
            config.TimeZoneId = "";
            Assert.AreEqual("UTC", config.TimeZoneId);
            Assert.AreEqual(TimeZoneInfo.Utc, config.TimeZone);

            // 纯空白字符重置为 UTC
            config.TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
            config.TimeZoneId = "   ";
            Assert.AreEqual("UTC", config.TimeZoneId);
            Assert.AreEqual(TimeZoneInfo.Utc, config.TimeZone);

            // null 重置为 UTC
            config.TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
            config.TimeZoneId = null!;
            Assert.AreEqual("UTC", config.TimeZoneId);
            Assert.AreEqual(TimeZoneInfo.Utc, config.TimeZone);
        }

        [TestMethod]
        public void BrokerConfig_TimeZoneId_InvalidId_ThrowsTimeZoneNotFoundException()
        {
            var config = new BrokerConfig();

            // 依据红线规范第 8 条与第 12 条：非法时区必须显式抛出强类型异常
            Assert.ThrowsException<TimeZoneNotFoundException>(() =>
            {
                config.TimeZoneId = "NonExistent_Invalid_TimeZone_XYZ_12345";
            });
        }

        [TestMethod]
        public void BrokerConfig_Hooks_ShouldStoreAndInvokeCorrectly()
        {
            var config = new BrokerConfig
            {
                OnCheckVolume = (req, bar) => req.Quantity <= 100m,
                OnGetSettleDays = sym => sym.StartsWith("sh") ? 1 : 0,
                OnGetMultiplier = (sym, fields) => sym.StartsWith("IF") ? 300.0 : 1.0,
                OnGetMarginRate = (sym, fields) => sym.StartsWith("IF") ? 0.12 : 1.0,
                OnCalculateFee = (req, turnover) => req.IsBuy ? turnover * 0.0001 : turnover * 0.0006,
                OnCalculateSlippage = (req, basePrice, bar) => req.IsBuy ? basePrice * 1.001 : basePrice * 0.999,
                OnCheckLimit = (req, bar) => true
            };

            var dummyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, 50m);
            var dummyRow = new DataRow("sh.600000", DateTime.Today, new[] { "10.0" }, new Dictionary<string, int> { ["close"] = 0 });

            Assert.IsTrue(config.OnCheckVolume!(dummyOrder, dummyRow));
            Assert.AreEqual(1, config.OnGetSettleDays!("sh.600000"));
            Assert.AreEqual(0, config.OnGetSettleDays!("sz.000001"));
            Assert.AreEqual(300.0, config.OnGetMultiplier!("IF2501", null));
            Assert.AreEqual(0.12, config.OnGetMarginRate!("IF2501", null));
            Assert.AreEqual(100.0 * 0.0001, config.OnCalculateFee!(dummyOrder, 100.0), 1e-6);
            Assert.AreEqual(10.0 * 1.001, config.OnCalculateSlippage!(dummyOrder, 10.0, dummyRow), 1e-6);
            Assert.IsTrue(config.OnCheckLimit!(dummyOrder, dummyRow));
        }
    }
}
