using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using CarrotBacktesting.NET;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class MarketTimeTests
    {
        [TestMethod]
        public void MarketTime_PhysicalSize_ShouldBeExactlyEightBytes()
        {
            // 结构体内存对齐：尺寸严格为 64 位（8 字节），支持寄存器传值与零堆分配
            int size = Unsafe.SizeOf<MarketTime>();
            Assert.AreEqual(8, size, "MarketTime struct must be exactly 8 bytes (64-bit).");
        }

        [TestMethod]
        public void Constants_ShouldMatchDateTimeBoundaries()
        {
            Assert.AreEqual(DateTime.MinValue.Ticks, MarketTime.MinValue.Ticks);
            Assert.AreEqual(DateTime.MaxValue.Ticks, MarketTime.MaxValue.Ticks);
            Assert.AreEqual(DateTime.UnixEpoch.Ticks, MarketTime.UnixEpoch.Ticks);
        }

        [TestMethod]
        public void Constructors_And_Factories_ShouldConstructAccurateTicks()
        {
            long ticks = 638400000000000000L;
            var mt = new MarketTime(ticks);
            Assert.AreEqual(ticks, mt.Ticks);

            // 构造函数：纯年月日
            var mtYmd = new MarketTime(2024, 1, 2);
            Assert.AreEqual(new DateTime(2024, 1, 2, 0, 0, 0).Ticks, mtYmd.Ticks);

            // 构造函数：年月日时分秒毫秒
            var mtFull = new MarketTime(2024, 1, 2, 9, 30, 15, 500);
            Assert.AreEqual(new DateTime(2024, 1, 2, 9, 30, 15, 500).Ticks, mtFull.Ticks);

            // 构造函数：带 offsetHours（例如传入东八区 08:30:00，转换为 UTC 00:30:00）
            var mtOffset = new MarketTime(2024, 1, 2, 8, 30, 0, 0, offsetHours: 8);
            Assert.AreEqual(new DateTime(2024, 1, 2, 0, 30, 0).Ticks, mtOffset.Ticks);

            // 构造函数：带 TimeZoneInfo
            var tzShanghai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
            var mtTz = new MarketTime(2024, 1, 2, 8, 30, 0, 0, tzShanghai);
            Assert.AreEqual(new DateTime(2024, 1, 2, 0, 30, 0).Ticks, mtTz.Ticks);

            var utcDt = new DateTime(2024, 1, 2, 15, 0, 0, DateTimeKind.Utc);
            var mtUtc = MarketTime.FromUtc(utcDt);
            Assert.AreEqual(utcDt.Ticks, mtUtc.Ticks);

            long ms = 1704182400000L; // 2024-01-02 08:00:00 UTC
            var mtMs = MarketTime.FromUnixTimeMilliseconds(ms);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcTicks, mtMs.Ticks);

            long sec = 1704182400L;
            var mtSec = MarketTime.FromUnixTimeSeconds(sec);
            Assert.AreEqual(mtMs.Ticks, mtSec.Ticks);
        }

        [TestMethod]
        public void OutOfRangeTicks_ShouldThrowArgumentOutOfRangeException()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new MarketTime(DateTime.MinValue.Ticks - 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new MarketTime(DateTime.MaxValue.Ticks + 1));

            // offsetHours 越界防御
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new MarketTime(2024, 1, 1, 0, 0, 0, 0, offsetHours: 25));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new MarketTime(2024, 1, 1, 0, 0, 0, 0, offsetHours: -25));

            // 年月日时分秒越界防御
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new MarketTime(0, 1, 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new MarketTime(2024, 13, 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new MarketTime(2024, 1, 32));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new MarketTime(2024, 1, 1, 24, 0, 0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new MarketTime(2024, 1, 1, 0, 60, 0));
        }

        [TestMethod]
        public void Operators_Comparison_And_Arithmetic()
        {
            var t1 = new MarketTime(1000);
            var t2 = new MarketTime(2000);
            var t3 = new MarketTime(1000);

            Assert.IsTrue(t1 < t2);
            Assert.IsTrue(t1 <= t2);
            Assert.IsTrue(t1 <= t3);
            Assert.IsTrue(t2 > t1);
            Assert.IsTrue(t2 >= t1);
            Assert.IsTrue(t1 == t3);
            Assert.IsTrue(t1 != t2);

            var span = TimeSpan.FromTicks(500);
            Assert.AreEqual(new MarketTime(1500), t1 + span);
            Assert.AreEqual(new MarketTime(500), t1 - span);
            Assert.AreEqual(TimeSpan.FromTicks(1000), t2 - t1);
        }

        [TestMethod]
        public void ImplicitConversions_Between_DateTime_And_MarketTime()
        {
            var raw = new DateTime(2024, 1, 2, 15, 0, 0, DateTimeKind.Unspecified);
            MarketTime mt = raw; // 隐式转换
            Assert.AreEqual(raw.Ticks, mt.Ticks);

            DateTime dtBack = (DateTime)mt; // 显式转换回 DateTime
            Assert.AreEqual(raw.Ticks, dtBack.Ticks);
            Assert.AreEqual(raw.TimeOfDay, dtBack.TimeOfDay);

            // UTC DateTime 隐式转换
            var utc = new DateTime(2024, 1, 2, 7, 0, 0, DateTimeKind.Utc);
            MarketTime mtFromUtc = utc;
            Assert.AreEqual(utc.Ticks, mtFromUtc.Ticks);
        }

        [TestMethod]
        public void Parse_WithExplicitTimezones_ShouldBePhysicalInstantIdentical()
        {
            // 时间刻度对齐：2024-01-02 15:00:00+08:00、2024-01-02 07:00:00Z 与 2024-01-02 02:00:00-05:00 对应同一物理 UTC 时间点
            string sCst = "2024-01-02T15:00:00.000+08:00";
            string sCstNoColon = "2024-01-02T15:00:00.000+0800";
            string sUtc = "2024-01-02T07:00:00.000Z";
            string sEst = "2024-01-02T02:00:00.000-05:00";

            var tzShanghai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
            var tzEst = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

            // 1. 文本自带显式时区，未传 sourceTimeZone 时：保真保留原始钟表时间（全球环境绝对一致，绝不依赖本地时区）
            var mtCst = MarketTime.Parse(sCst);
            var mtCstNoColon = MarketTime.Parse(sCstNoColon);
            var mtEst = MarketTime.Parse(sEst);

            Assert.AreEqual(new DateTime(2024, 1, 2, 15, 0, 0), mtCst.ToDateTime());
            Assert.AreEqual(new DateTime(2024, 1, 2, 15, 0, 0), mtCstNoColon.ToDateTime());
            Assert.AreEqual(new DateTime(2024, 1, 2, 2, 0, 0), mtEst.ToDateTime());

            // 2. 指定目标时区为 UTC 时：权威归一化为绝对 UTC 刻度
            var mtCstUtc = MarketTime.Parse(sCst, TimeZoneInfo.Utc);
            var mtUtc = MarketTime.Parse(sUtc);
            var mtEstUtc = MarketTime.Parse(sEst, TimeZoneInfo.Utc);

            Assert.AreEqual(mtUtc, mtCstUtc);
            Assert.AreEqual(mtUtc, mtEstUtc);
            Assert.AreEqual(new DateTime(2024, 1, 2, 7, 0, 0, DateTimeKind.Utc), mtCstUtc.ToUtcDateTime());

            // 3. 文本自带显式时区，传入一致的业务源时区时：正常校验通过并归一化为 UTC
            var mtCstMatched = MarketTime.Parse(sCst, tzShanghai);
            Assert.AreEqual(mtUtc, mtCstMatched);

            // 4. 文本自带显式时区，传入冲突的业务源时区时：严格显式抛出 ArgumentException 阻断
            Assert.ThrowsException<ArgumentException>(() => MarketTime.Parse(sCst, tzEst));
        }

        [TestMethod]
        public void HasTimezone_ShouldDetectExplicitTimezoneAccurately()
        {
            // 带时区文本
            Assert.IsTrue(MarketTime.HasTimezone("2024-01-02T15:00:00Z"));
            Assert.IsTrue(MarketTime.HasTimezone("2024-01-02T15:00:00z"));
            Assert.IsTrue(MarketTime.HasTimezone("2024-01-02T15:00:00+08:00"));
            Assert.IsTrue(MarketTime.HasTimezone("2024-01-02T15:00:00+0800"));
            Assert.IsTrue(MarketTime.HasTimezone("2024-01-02 15:00:00 -05:00"));
            Assert.IsTrue(MarketTime.HasTimezone("2024-01-02 15:00:00-0500".AsSpan()));

            // 无时区文本
            Assert.IsFalse(MarketTime.HasTimezone("2024-01-02 15:00:00"));
            Assert.IsFalse(MarketTime.HasTimezone("2024-01-02T15:00:00"));
            Assert.IsFalse(MarketTime.HasTimezone("2024-01-02"));
            Assert.IsFalse(MarketTime.HasTimezone("20240102093000"));
            Assert.IsFalse(MarketTime.HasTimezone("1704182400000"));
            Assert.IsFalse(MarketTime.HasTimezone(string.Empty));
        }

        [TestMethod]
        public void Parse_CompactNumericFormats_ShouldNotBeMisinterpretedAsTimestamps()
        {
            // 20 位紧凑微秒格式 yyyyMMddHHmmssffffff
            string s20 = "20240102093000123456";
            var mt20 = MarketTime.Parse(s20);
            Assert.AreEqual(2024, mt20.Year);
            Assert.AreEqual(1, mt20.Month);
            Assert.AreEqual(2, mt20.Day);
            Assert.AreEqual(9, mt20.Hour);
            Assert.AreEqual(30, mt20.Minute);
            Assert.AreEqual(0, mt20.Second);
            Assert.AreEqual(123, mt20.Millisecond);
            Assert.AreEqual(456, mt20.Microsecond);

            // 17 位紧凑毫秒格式 yyyyMMddHHmmssfff
            string s17 = "20240102093000123";
            var mt17 = MarketTime.Parse(s17);
            Assert.AreEqual(new DateTime(2024, 1, 2, 9, 30, 0, 123).Ticks, mt17.Ticks);

            // 14 位紧凑日期时间 yyyyMMddHHmmss
            string s14 = "20240102093000";
            var mt14 = MarketTime.Parse(s14);
            Assert.AreEqual(new DateTime(2024, 1, 2, 9, 30, 0).Ticks, mt14.Ticks);

            // 8 位紧凑日期 yyyyMMdd
            string s8 = "20240102";
            var mt8 = MarketTime.Parse(s8);
            Assert.AreEqual(new DateTime(2024, 1, 2, 0, 0, 0).Ticks, mt8.Ticks);
        }

        [TestMethod]
        public void Parse_Iso8601WithTAndZ_ShouldParseAccurately()
        {
            // 标准 ISO 8601 UTC 时间 (带 T 和 Z)
            string sUtc = "2024-01-02T09:30:00Z";
            var mtUtc = MarketTime.Parse(sUtc);
            Assert.AreEqual(new DateTime(2024, 1, 2, 9, 30, 0).Ticks, mtUtc.Ticks);

            // 标准 ISO 8601 带微秒与 Z
            string sUtcMicro = "2024-01-02T09:30:00.123456Z";
            var mtUtcMicro = MarketTime.Parse(sUtcMicro);
            Assert.AreEqual(2024, mtUtcMicro.Year);
            Assert.AreEqual(1, mtUtcMicro.Month);
            Assert.AreEqual(2, mtUtcMicro.Day);
            Assert.AreEqual(9, mtUtcMicro.Hour);
            Assert.AreEqual(30, mtUtcMicro.Minute);
            Assert.AreEqual(0, mtUtcMicro.Second);
            Assert.AreEqual(123, mtUtcMicro.Millisecond);
            Assert.AreEqual(456, mtUtcMicro.Microsecond);

            // 带 T 但无时区
            string sNoTz = "2024-01-02T09:30:00";
            var mtNoTz = MarketTime.Parse(sNoTz);
            Assert.AreEqual(new DateTime(2024, 1, 2, 9, 30, 0).Ticks, mtNoTz.Ticks);
        }

        [TestMethod]
        public void Parse_PureNumericTimestamps_MillisecondsAndSeconds()
        {
            // 13 位毫秒戳
            string sMs = "1704182400000";
            var mtMs = MarketTime.Parse(sMs);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1704182400000L).UtcTicks, mtMs.Ticks);

            // 10 位秒戳
            string sSec = "1704182400";
            var mtSec = MarketTime.Parse(sSec);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1704182400L).UtcTicks, mtSec.Ticks);
        }

        [TestMethod]
        public void Parse_WithoutTimezone_ShouldPreserveLiteralTicks()
        {
            string s = "2024-01-02 15:00:00";
            var mt = MarketTime.Parse(s);
            Assert.AreEqual(new DateTime(2024, 1, 2, 15, 0, 0).Ticks, mt.Ticks);

            // 指定 sourceTimeZone 时转换为绝对 UTC
            var tzShanghai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
            var mtUtc = MarketTime.Parse(s, tzShanghai);
            Assert.AreEqual(new DateTime(2024, 1, 2, 7, 0, 0, DateTimeKind.Utc), mtUtc.ToUtcDateTime());
        }

        [TestMethod]
        public void TryParse_InvalidStrings_ShouldReturnFalse()
        {
            Assert.IsFalse(MarketTime.TryParse(string.Empty, null, out _));
            Assert.IsFalse(MarketTime.TryParse("   ", null, out _));
            Assert.IsFalse(MarketTime.TryParse("not_a_time", null, out _));
            Assert.ThrowsException<FormatException>(() => MarketTime.Parse("invalid_time"));
        }

        [TestMethod]
        public void Formatting_And_SpanFormatting()
        {
            var dt = new DateTime(2024, 1, 2, 15, 30, 45);
            var mt = new MarketTime(dt.Ticks);

            Assert.AreEqual("2024-01-02 15:30:45", mt.ToString());
            Assert.AreEqual("20240102", mt.ToString("yyyyMMdd"));

            Span<char> buffer = stackalloc char[32];
            Assert.IsTrue(mt.TryFormat(buffer, out int charsWritten, "yyyy-MM-dd", CultureInfo.InvariantCulture));
            Assert.AreEqual("2024-01-02", buffer[..charsWritten].ToString());
        }

        [TestMethod]
        public void ViewProjections_NegativeOffsetDefense_ShouldAlwaysBeNonNegative()
        {
            // UTC 时刻：2024-01-02 02:00:00 UTC
            var utcDt = new DateTime(2024, 1, 2, 2, 0, 0, DateTimeKind.Utc);
            var mt = MarketTime.FromUtc(utcDt);

            // 西五区负偏移 (-05:00) -> 当地时间为前一日 21:00
            TimeSpan estOffset = TimeSpan.FromHours(-5);
            int hour = mt.GetHour(estOffset);
            int minute = mt.GetMinute(estOffset);

            Assert.AreEqual(21, hour, "Hour must be in [0, 23] even under negative offset.");
            Assert.AreEqual(0, minute, "Minute must be in [0, 59] even under negative offset.");
            Assert.AreEqual(new DateTime(2024, 1, 1, 21, 0, 0), mt.ToExchangeTime(estOffset));
        }

        [TestMethod]
        public void Equals_ShouldConformToHashEquivalenceContract()
        {
            var mt1 = new MarketTime(1000);
            var mt2 = new MarketTime(1000);
            var mt3 = new MarketTime(2000);

            Assert.IsTrue(mt1.Equals((object)mt2));
            Assert.IsFalse(mt1.Equals((object)mt3));

            // 对非 MarketTime 类型严格返回 false，杜绝破坏哈希公理
            var dt = new DateTime(1000);
            Assert.IsFalse(mt1.Equals((object)dt));
            Assert.IsFalse(mt1.Equals(null));
        }

        [TestMethod]
        public void Sorting_And_BinarySearch()
        {
            var arr = new MarketTime[]
            {
                new(3000),
                new(1000),
                new(2000)
            };

            Array.Sort(arr);
            Assert.AreEqual(new MarketTime(1000), arr[0]);
            Assert.AreEqual(new MarketTime(2000), arr[1]);
            Assert.AreEqual(new MarketTime(3000), arr[2]);

            int idx = Array.BinarySearch(arr, new MarketTime(2000));
            Assert.AreEqual(1, idx);
        }

        [TestMethod]
        public void CalendarComponents_And_ArithmeticOperations()
        {
            var mt = new MarketTime(2025, 3, 15, 14, 30, 45, 123);
            Assert.AreEqual(2025, mt.Year);
            Assert.AreEqual(3, mt.Month);
            Assert.AreEqual(15, mt.Day);
            Assert.AreEqual(14, mt.Hour);
            Assert.AreEqual(30, mt.Minute);
            Assert.AreEqual(45, mt.Second);
            Assert.AreEqual(123, mt.Millisecond);
            Assert.AreEqual(DayOfWeek.Saturday, mt.DayOfWeek);
            Assert.AreEqual(new TimeSpan(0, 14, 30, 45, 123), mt.TimeOfDay);

            var mtDate = mt.Date;
            Assert.AreEqual(new MarketTime(2025, 3, 15), mtDate);
            Assert.AreEqual(TimeSpan.Zero, mtDate.TimeOfDay);

            // 算术运算
            Assert.AreEqual(new MarketTime(2025, 3, 16, 14, 30, 45, 123), mt.AddDays(1));
            Assert.AreEqual(new MarketTime(2025, 3, 15, 16, 30, 45, 123), mt.AddHours(2));
            Assert.AreEqual(new MarketTime(2025, 3, 15, 14, 35, 45, 123), mt.AddMinutes(5));
            Assert.AreEqual(new MarketTime(2025, 3, 15, 14, 30, 55, 123), mt.AddSeconds(10));
            Assert.AreEqual(new MarketTime(2025, 3, 15, 14, 30, 45, 623), mt.AddMilliseconds(500));
            Assert.AreEqual(mt.Ticks + 1000, mt.AddTicks(1000).Ticks);

            var mt2 = mt.AddDays(2);
            Assert.AreEqual(TimeSpan.FromDays(2), mt2.Subtract(mt));
            Assert.AreEqual(mt, mt2.Subtract(TimeSpan.FromDays(2)));
        }

        [TestMethod]
        public void GetDate_And_CalendarMath_ConsistencyWithDateTime()
        {
            // 验证核心特殊年份及临界点
            int[] testYears = { 1, 4, 100, 400, 1600, 1900, 2000, 2004, 2024, 2025, 2096, 2100, 2400, 9999 };

            foreach (int year in testYears)
            {
                Assert.AreEqual(DateTime.IsLeapYear(year), MarketTime.IsLeapYear(year), $"Leap year mismatch for year {year}");

                // 测年初、平闰二月末、年末
                var checkDates = new[]
                {
                    (Month: 1, Day: 1),
                    (Month: 2, Day: 28),
                    (Month: 2, Day: DateTime.IsLeapYear(year) ? 29 : 28),
                    (Month: 12, Day: 31)
                };

                foreach (var (m, d) in checkDates)
                {
                    var dt = new DateTime(year, m, d, 14, 30, 45, 123, DateTimeKind.Unspecified);
                    var mt = new MarketTime(dt.Ticks);

                    Assert.AreEqual(dt.Year, mt.Year);
                    Assert.AreEqual(dt.Month, mt.Month);
                    Assert.AreEqual(dt.Day, mt.Day);
                    Assert.AreEqual(dt.Hour, mt.Hour);
                    Assert.AreEqual(dt.Minute, mt.Minute);
                    Assert.AreEqual(dt.Second, mt.Second);
                    Assert.AreEqual(dt.Millisecond, mt.Millisecond);
                    Assert.AreEqual(dt.Microsecond, mt.Microsecond);
                    Assert.AreEqual(dt.DayOfWeek, mt.DayOfWeek);
                }
            }

            // 针对 2000 到 2050 年间连续每日抽样比对
            var start = new DateTime(2000, 1, 1);
            var end = new DateTime(2050, 1, 1);
            for (DateTime curr = start; curr < end; curr = curr.AddDays(37))
            {
                var mt = new MarketTime(curr.Ticks);
                Assert.AreEqual(curr.Year, mt.Year);
                Assert.AreEqual(curr.Month, mt.Month);
                Assert.AreEqual(curr.Day, mt.Day);
                Assert.AreEqual(curr.DayOfWeek, mt.DayOfWeek);
            }
        }

        [TestMethod]
        public void SystemTextJson_SerializationAndDeserialization_RoundTrip()
        {
            var original = new MarketTime(2025, 1, 2, 9, 30, 0);
            string json = System.Text.Json.JsonSerializer.Serialize(original);
            Assert.AreEqual("\"2025-01-02T09:30:00Z\"", json);

            var restored = System.Text.Json.JsonSerializer.Deserialize<MarketTime>(json);
            Assert.AreEqual(original, restored);

            // 带微秒/毫秒
            var subSec = new MarketTime(2025, 1, 2, 9, 30, 0, 500);
            string jsonSubSec = System.Text.Json.JsonSerializer.Serialize(subSec);
            Assert.AreEqual("\"2025-01-02T09:30:00.500000Z\"", jsonSubSec);
            var restoredSubSec = System.Text.Json.JsonSerializer.Deserialize<MarketTime>(jsonSubSec);
            Assert.AreEqual(subSec, restoredSubSec);

            // 可空类型 MarketTime?
            MarketTime? nullableTime = original;
            string jsonNullable = System.Text.Json.JsonSerializer.Serialize(nullableTime);
            var restoredNullable = System.Text.Json.JsonSerializer.Deserialize<MarketTime?>(jsonNullable);
            Assert.AreEqual(nullableTime, restoredNullable);

            MarketTime? nullValue = null;
            string jsonNull = System.Text.Json.JsonSerializer.Serialize(nullValue);
            var restoredNull = System.Text.Json.JsonSerializer.Deserialize<MarketTime?>(jsonNull);
            Assert.IsNull(restoredNull);
        }
    }
}
