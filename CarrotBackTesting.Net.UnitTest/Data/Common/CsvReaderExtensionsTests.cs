using System;
using System.IO;
using System.Text;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sylvan.Data.Csv;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class CsvReaderExtensionsTests
    {
        private static CsvDataReader CreateReader(string csvContent)
        {
            var bytes = Encoding.UTF8.GetBytes(csvContent);
            var stream = new MemoryStream(bytes);
            return CsvDataReader.Create(new StreamReader(stream));
        }

        [TestMethod]
        public void FindColumn_SingleName_MatchesCaseInsensitively()
        {
            string csv = "Symbol,DateTime,Close\nsh.600000,2024-01-02,10.5\n";
            using var reader = CreateReader(csv);

            Assert.AreEqual(0, reader.FindColumn("symbol"));
            Assert.AreEqual(0, reader.FindColumn("SYMBOL"));
            Assert.AreEqual(1, reader.FindColumn("datetime"));
            Assert.AreEqual(2, reader.FindColumn("CLOSE"));
            Assert.AreEqual(-1, reader.FindColumn("not_found"));
        }

        [TestMethod]
        public void FindColumn_CandidateNames_ReturnsFirstMatchingOrdinal()
        {
            string csv1 = "symbol,datetime,close\nsh.600000,2024-01-02,10.5\n";
            using (var reader1 = CreateReader(csv1))
            {
                int idx = reader1.FindColumn("timestamp", "datetime");
                Assert.AreEqual(1, idx);
            }

            string csv2 = "symbol,timestamp,close\nsh.600000,1704153600000,10.5\n";
            using (var reader2 = CreateReader(csv2))
            {
                int idx = reader2.FindColumn("datetime", "timestamp");
                Assert.AreEqual(1, idx);
            }

            string csv3 = "symbol,volume,close\nsh.600000,1000,10.5\n";
            using (var reader3 = CreateReader(csv3))
            {
                int idx = reader3.FindColumn("datetime", "timestamp");
                Assert.AreEqual(-1, idx);
            }
        }

        [TestMethod]
        public void GetOrdinalMap_ReturnsCaseInsensitiveDictionary()
        {
            string csv = "Symbol,Close,Volume\nsh.600000,10.5,1000\n";
            using var reader = CreateReader(csv);

            var map = reader.GetOrdinalMap();
            Assert.AreEqual(3, map.Count);
            Assert.AreEqual(0, map["symbol"]);
            Assert.AreEqual(0, map["SYMBOL"]);
            Assert.AreEqual(1, map["close"]);
            Assert.AreEqual(2, map["volume"]);
            Assert.IsFalse(map.ContainsKey("unknown"));
        }

        [TestMethod]
        public void ParseTime_ParsesDateTimeAndTimestampCorrectly()
        {
            string csv = "dt_col,ts_col\n2024-01-02 09:30:00,1704153600000\n";
            using var reader = CreateReader(csv);
            Assert.IsTrue(reader.Read());

            MarketTime dtFromDate = reader.ParseTime(0);
            Assert.AreEqual(new MarketTime(2024, 1, 2, 9, 30, 0), dtFromDate);

            MarketTime dtFromTs = reader.ParseTime(1);
            Assert.AreEqual(new MarketTime(2024, 1, 2, 0, 0, 0), dtFromTs);

            MarketTime dtInvalid = reader.ParseTime(-1);
            Assert.AreEqual(MarketTime.MinValue, dtInvalid);
        }

        [TestMethod]
        public void ParseTime_DualColumns_PrefersDateTimeWithTimestampFallback()
        {
            string csv = "datetime,timestamp\n2024-01-02,1704240000000\n,1704153600000\n";
            using var reader = CreateReader(csv);

            // Row 1: Both exist, datetime takes precedence
            Assert.IsTrue(reader.Read());
            MarketTime dt1 = reader.ParseTime(0, 1);
            Assert.AreEqual(new MarketTime(2024, 1, 2), dt1);

            // Row 2: datetime is empty, falls back to timestamp
            Assert.IsTrue(reader.Read());
            MarketTime dt2 = reader.ParseTime(0, 1);
            Assert.AreEqual(new MarketTime(2024, 1, 2), dt2);
        }

        [TestMethod]
        public void ReadValue_UnmanagedTypes_ConvertsCorrectlyWithoutBoxing()
        {
            string csv = "d,f,l,i,b,by\n10.5,2.5,123456789,42,true,255\n";
            using var reader = CreateReader(csv);
            Assert.IsTrue(reader.Read());

            Assert.AreEqual(10.5, reader.ReadValue<double>(0), 1e-6);
            Assert.AreEqual(2.5f, reader.ReadValue<float>(1), 1e-6f);
            Assert.AreEqual(123456789L, reader.ReadValue<long>(2));
            Assert.AreEqual(42, reader.ReadValue<int>(3));
            Assert.AreEqual(true, reader.ReadValue<bool>(4));
            Assert.AreEqual((byte)255, reader.ReadValue<byte>(5));
        }

        [TestMethod]
        public void ReadValue_NullEmptyAndWhitespace_ReturnsNaNOrSentinel()
        {
            // Row 1: 连续逗号表示的空字段 ,,
            // Row 2: 纯空格表示的空字段 ,   ,
            string csv = "d,f,l,i,s\n,,,,\n   ,   ,   ,   ,   \n";
            using var reader = CreateReader(csv);

            // 1. 测试负索引越界安全防护
            Assert.IsTrue(double.IsNaN(reader.ReadValue<double>(-1)));
            Assert.IsTrue(float.IsNaN(reader.ReadValue<float>(-1)));
            Assert.AreEqual(long.MinValue, reader.ReadValue<long>(-1));
            Assert.AreEqual(int.MinValue, reader.ReadValue<int>(-1));
            Assert.AreEqual(short.MinValue, reader.ReadValue<short>(-1));

            // 2. 测试首行空字符串字段（IsEmpty 拦截）
            Assert.IsTrue(reader.Read());
            Assert.IsTrue(double.IsNaN(reader.ReadValue<double>(0)));
            Assert.IsTrue(float.IsNaN(reader.ReadValue<float>(1)));
            Assert.AreEqual(long.MinValue, reader.ReadValue<long>(2));
            Assert.AreEqual(int.MinValue, reader.ReadValue<int>(3));
            Assert.AreEqual(short.MinValue, reader.ReadValue<short>(4));

            // 3. 测试次行纯空格空白字段（IsWhiteSpace 拦截）
            Assert.IsTrue(reader.Read());
            Assert.IsTrue(double.IsNaN(reader.ReadValue<double>(0)));
            Assert.IsTrue(float.IsNaN(reader.ReadValue<float>(1)));
            Assert.AreEqual(long.MinValue, reader.ReadValue<long>(2));
            Assert.AreEqual(int.MinValue, reader.ReadValue<int>(3));
            Assert.AreEqual(short.MinValue, reader.ReadValue<short>(4));
        }

        [TestMethod]
        public void ParseTime_WithTimezoneOffsets_ShouldPreserveWallClockTimeAndBeHostTimezoneAgnostic()
        {
            // 测试包含各种时区偏移 (+08:00, -05:00, Z) 的 ISO 8601 文本
            string csv = "cst_time,est_time,utc_time\n" +
                         "2024-01-02T15:00:00.000+08:00,2024-01-02T09:30:00.000-05:00,2024-01-02T07:00:00.000Z\n";
            using var reader = CreateReader(csv);
            Assert.IsTrue(reader.Read());

            // 1. 未指定 sourceTimeZone 时：保真保留原始钟表时间，绝不受宿主机本地时区污染
            MarketTime cst = reader.ParseTime(0);
            Assert.AreEqual(new MarketTime(2024, 1, 2, 15, 0, 0), cst);
            Assert.AreEqual(new TimeSpan(15, 0, 0), cst.TimeOfDay);

            MarketTime est = reader.ParseTime(1);
            Assert.AreEqual(new MarketTime(2024, 1, 2, 9, 30, 0), est);
            Assert.AreEqual(new TimeSpan(9, 30, 0), est.TimeOfDay);

            MarketTime utc = reader.ParseTime(2);
            Assert.AreEqual(new MarketTime(2024, 1, 2, 7, 0, 0), utc);

            // 2. 指定 sourceTimeZone 时：数据源时间被权威归一化为 UTC 市场时间 (07:00:00 UTC)
            var tzShanghai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
            MarketTime cstShanghai = reader.ParseTime(0, tzShanghai);
            Assert.AreEqual(new MarketTime(2024, 1, 2, 7, 0, 0), cstShanghai);

            MarketTime cstUtc = reader.ParseTime(0, TimeZoneInfo.Utc);
            Assert.AreEqual(new MarketTime(2024, 1, 2, 7, 0, 0), cstUtc);
        }

        [TestMethod]
        public void ParseTime_CrossTypes_WithSourceTimeZone_ShouldAlignToIdenticalUtcInstant()
        {
            // 第一列为 long (Unix 毫秒戳 1704178800000 = 2024-01-02 07:00:00 UTC)
            // 第二列为 string (2024-01-02 15:00:00，无时区文本，源时区为上海)
            // 第三列为 string (2024-01-02T15:00:00.000+08:00，自带时区)
            string csv = "ts_col,str_local_col,str_iso_col\n" +
                         "1704178800000,2024-01-02 15:00:00,2024-01-02T15:00:00.000+08:00\n";
            using var reader = CreateReader(csv);
            Assert.IsTrue(reader.Read());

            var tzShanghai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
            MarketTime dtTs = reader.ParseTime(0, tzShanghai);
            MarketTime dtStrLocal = reader.ParseTime(1, tzShanghai);
            MarketTime dtStrIso = reader.ParseTime(2, tzShanghai);

            MarketTime expectedUtc = new MarketTime(2024, 1, 2, 7, 0, 0);
            Assert.AreEqual(expectedUtc, dtTs);
            Assert.AreEqual(expectedUtc, dtStrLocal);
            Assert.AreEqual(expectedUtc, dtStrIso);

            Assert.AreEqual(dtTs.Ticks, dtStrLocal.Ticks);
            Assert.AreEqual(dtTs.Ticks, dtStrIso.Ticks);
        }
    }
}
