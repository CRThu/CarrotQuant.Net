using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class MarketDataAlignerTests
    {
        [TestMethod]
        public void TestAlignAndFill_NormalRecords_MatchesDates()
        {
            var globalDates = new List<DateTime>
            {
                new(2025, 1, 6),
                new(2025, 1, 7),
                new(2025, 1, 8),
            };

            var recordDates = new[]
            {
                new DateTime(2025, 1, 6),
                new DateTime(2025, 1, 7),
                new DateTime(2025, 1, 8),
            };

            var recordValues = new[] { 10.0, 11.0, 12.0 };
            double[] destination = new double[globalDates.Count];

            MarketDataAligner.AlignAndFill(
                globalDates,
                0,
                globalDates.Count,
                recordDates,
                recordValues,
                destination);

            Assert.AreEqual(10.0, destination[0], 1e-4);
            Assert.AreEqual(11.0, destination[1], 1e-4);
            Assert.AreEqual(12.0, destination[2], 1e-4);
        }

        [TestMethod]
        public void TestAlignAndFill_MissingDates_StrictlyFillsNaN()
        {
            // 物理源数据在 1月8日 与 1月9日 缺失（停牌/无交易），按第一性原理严格保留为 NaN
            var globalDates = new List<DateTime>
            {
                new(2025, 1, 6),
                new(2025, 1, 7),
                new(2025, 1, 8),  // 缺失
                new(2025, 1, 9),  // 缺失
                new(2025, 1, 10),
            };

            var recordDates = new[]
            {
                new DateTime(2025, 1, 6),
                new DateTime(2025, 1, 7),
                new DateTime(2025, 1, 10),
            };

            var recordValues = new[] { 10.0, 11.0, 15.0 };
            double[] destination = new double[globalDates.Count];

            MarketDataAligner.AlignAndFill(
                globalDates,
                0,
                globalDates.Count,
                recordDates,
                recordValues,
                destination);

            Assert.AreEqual(10.0, destination[0], 1e-4);
            Assert.AreEqual(11.0, destination[1], 1e-4);
            Assert.IsTrue(double.IsNaN(destination[2]), "缺失交易日必须严格保持为 NaN");
            Assert.IsTrue(double.IsNaN(destination[3]), "缺失交易日必须严格保持为 NaN");
            Assert.AreEqual(15.0, destination[4], 1e-4);
        }

        [TestMethod]
        public void TestAlignAndFill_EmptyRecords_StrictlyFillsNaN()
        {
            var globalDates = new List<DateTime> { new(2025, 1, 6), new(2025, 1, 7) };
            double[] priceDest = new double[2];

            MarketDataAligner.AlignAndFill<double>(
                globalDates,
                0,
                2,
                ReadOnlySpan<DateTime>.Empty,
                ReadOnlySpan<double>.Empty,
                priceDest);

            Assert.IsTrue(double.IsNaN(priceDest[0]), "空记录必须全部为 NaN");
            Assert.IsTrue(double.IsNaN(priceDest[1]), "空记录必须全部为 NaN");
        }

        [TestMethod]
        public void TestAlignAndFill_PreIpoAndPostDelist_StrictlyFillsNaN()
        {
            // 未上市与退市交易日（物理无数据），全时段自动保持为 NaN
            var globalDates = new List<DateTime>
            {
                new(2025, 1, 6), // 未上市
                new(2025, 1, 7), // 上市首日
                new(2025, 1, 8), // 正常交易
                new(2025, 1, 9), // 退市后
            };

            var recordDates = new[] { new DateTime(2025, 1, 7), new DateTime(2025, 1, 8) };
            var recordValues = new[] { 20.0, 21.0 };
            double[] dest = new double[4];

            MarketDataAligner.AlignAndFill(
                globalDates,
                0,
                4,
                recordDates,
                recordValues,
                dest);

            Assert.IsTrue(double.IsNaN(dest[0]), "未上市必须为 NaN");
            Assert.AreEqual(20.0, dest[1], 1e-4);
            Assert.AreEqual(21.0, dest[2], 1e-4);
            Assert.IsTrue(double.IsNaN(dest[3]), "退市后必须为 NaN");
        }

        [TestMethod]
        public void TestAlignAndFill_NonTradingCalendarDirtyDates_NeverStallsCursor()
        {
            // 验证当物理源数据中包含非交易日记录（如周末公告点、重复点）时，游标绝不挂死，正常对齐
            var globalDates = new List<DateTime>
            {
                new(2025, 1, 6), // 周一
                new(2025, 1, 7), // 周二 (缺失)
                new(2025, 1, 8), // 周三
            };

            // 原始记录包含了非交易日的周末脏记录 (2025-01-05) 以及周一的重复记录
            var rawDates = new[]
            {
                new DateTime(2025, 1, 5), // 周日 (非交易日)
                new DateTime(2025, 1, 6), // 周一
                new DateTime(2025, 1, 6), // 周一重复记录
                new DateTime(2025, 1, 8), // 周三
            };
            var rawValues = new[] { 9.0, 10.0, 10.5, 12.0 };
            double[] dest = new double[3];

            MarketDataAligner.AlignAndFill(
                globalDates,
                0,
                3,
                rawDates,
                rawValues,
                dest);

            // 周一匹配成功 (10.0)
            Assert.AreEqual(10.0, dest[0], 1e-4);
            // 周二无记录 (保持 NaN)
            Assert.IsTrue(double.IsNaN(dest[1]));
            // 周三匹配成功 (12.0)，证明游标未因周一重复记录或周日脏数据而挂死！
            Assert.AreEqual(12.0, dest[2], 1e-4);
        }

        [TestMethod]
        public void TestAlignAndFill_WindowSlice_SkipsPriorRecords()
        {
            var globalDates = new List<DateTime>
            {
                new(2025, 1, 6),
                new(2025, 1, 7),
                new(2025, 1, 8),
                new(2025, 1, 9),
            };

            var recordDates = new[]
            {
                new DateTime(2025, 1, 6),
                new DateTime(2025, 1, 7),
                new DateTime(2025, 1, 8),
                new DateTime(2025, 1, 9),
            };
            var recordValues = new[] { 1.0, 2.0, 3.0, 4.0 };

            // 截取索引 2..3 (1月8日 到 1月9日)
            double[] dest = new double[2];

            MarketDataAligner.AlignAndFill(
                globalDates,
                2,
                2,
                recordDates,
                recordValues,
                dest);

            Assert.AreEqual(3.0, dest[0], 1e-4);
            Assert.AreEqual(4.0, dest[1], 1e-4);
        }

        [TestMethod]
        public void TestGetNaNValue_AllSupportedTypes_ValidSentinelNaN()
        {
            // 浮点类型：IEEE 754 硬件 NaN
            double dNaN = MarketDataAligner.GetNaNValue<double>();
            Assert.IsTrue(double.IsNaN(dNaN));
            Assert.IsTrue(MarketDataAligner.IsValueNaN(dNaN));

            float fNaN = MarketDataAligner.GetNaNValue<float>();
            Assert.IsTrue(float.IsNaN(fNaN));
            Assert.IsTrue(MarketDataAligner.IsValueNaN(fNaN));

            // 整型：DolphinDB/Kdb+ 标准极值哨兵 NaN（杜绝以 0 充当缺失引起的业务歧义）
            long lNaN = MarketDataAligner.GetNaNValue<long>();
            Assert.AreEqual(long.MinValue, lNaN);
            Assert.IsTrue(MarketDataAligner.IsValueNaN(lNaN));

            int iNaN = MarketDataAligner.GetNaNValue<int>();
            Assert.AreEqual(int.MinValue, iNaN);
            Assert.IsTrue(MarketDataAligner.IsValueNaN(iNaN));

            short sNaN = MarketDataAligner.GetNaNValue<short>();
            Assert.AreEqual(short.MinValue, sNaN);
            Assert.IsTrue(MarketDataAligner.IsValueNaN(sNaN));

            sbyte sbNaN = MarketDataAligner.GetNaNValue<sbyte>();
            Assert.AreEqual(sbyte.MinValue, sbNaN);
            Assert.IsTrue(MarketDataAligner.IsValueNaN(sbNaN));

            byte bNaN = MarketDataAligner.GetNaNValue<byte>();
            Assert.AreEqual(byte.MaxValue, bNaN);
            Assert.IsTrue(MarketDataAligner.IsValueNaN(bNaN));

            // 回退未特化类型
            Assert.AreEqual(default(char), MarketDataAligner.GetNaNValue<char>());
            Assert.IsFalse(MarketDataAligner.IsValueNaN('a'));

            // 正常有效数值断言不是 NaN
            Assert.IsFalse(MarketDataAligner.IsValueNaN(0.0));
            Assert.IsFalse(MarketDataAligner.IsValueNaN(0L));
            Assert.IsFalse(MarketDataAligner.IsValueNaN(0));
            Assert.IsFalse(MarketDataAligner.IsValueNaN(100));
            Assert.IsFalse(MarketDataAligner.IsValueNaN((sbyte)10));
            Assert.IsFalse(MarketDataAligner.IsValueNaN((byte)10));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void TestAlignAndFill_StartIndexNegative_ThrowsException()
        {
            var dates = new List<DateTime> { DateTime.Today };
            MarketDataAligner.AlignAndFill(dates, -1, 1, Array.Empty<DateTime>(), Array.Empty<double>(), new double[1]);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void TestAlignAndFill_WindowExceedsGlobalDates_ThrowsException()
        {
            var dates = new List<DateTime> { DateTime.Today };
            MarketDataAligner.AlignAndFill(dates, 0, 2, Array.Empty<DateTime>(), Array.Empty<double>(), new double[2]);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TestAlignAndFill_DestinationLengthMismatch_ThrowsException()
        {
            var dates = new List<DateTime> { DateTime.Today };
            MarketDataAligner.AlignAndFill(dates, 0, 1, Array.Empty<DateTime>(), Array.Empty<double>(), new double[2]);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TestAlignAndFill_RecordDatesValuesMismatch_ThrowsException()
        {
            var dates = new List<DateTime> { DateTime.Today };
            MarketDataAligner.AlignAndFill(dates, 0, 1, new[] { DateTime.Today }, Array.Empty<double>(), new double[1]);
        }

        [TestMethod]
        public void TestBufferExtension_IsNaN()
        {
            using var buffer = new ChunkBuffer2D<double>(2, 1024, 2);
            buffer[0, 0] = 10.5;
            buffer[0, 1] = double.NaN;
            buffer[1, 0] = double.NaN;
            buffer[1, 1] = 20.0;

            Assert.IsFalse(buffer.IsNaN(0, 0));
            Assert.IsTrue(buffer.IsNaN(0, 1));
            Assert.IsTrue(buffer.IsNaN(1, 0));
            Assert.IsFalse(buffer.IsNaN(1, 1));
        }
    }
}
