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
        public void ParseDate_ParsesDateTimeAndTimestampCorrectly()
        {
            string csv = "dt_col,ts_col\n2024-01-02 09:30:00,1704153600000\n";
            using var reader = CreateReader(csv);
            Assert.IsTrue(reader.Read());

            DateTime dtFromDate = reader.ParseDate(0);
            Assert.AreEqual(new DateTime(2024, 1, 2), dtFromDate);

            DateTime dtFromTs = reader.ParseDate(1);
            Assert.AreEqual(new DateTime(2024, 1, 2), dtFromTs);

            DateTime dtInvalid = reader.ParseDate(-1);
            Assert.AreEqual(DateTime.MinValue, dtInvalid);
        }

        [TestMethod]
        public void ParseDate_DualColumns_PrefersDateTimeWithTimestampFallback()
        {
            string csv = "datetime,timestamp\n2024-01-02,1704240000000\n,1704153600000\n";
            using var reader = CreateReader(csv);

            // Row 1: Both exist, datetime takes precedence
            Assert.IsTrue(reader.Read());
            DateTime dt1 = reader.ParseDate(0, 1);
            Assert.AreEqual(new DateTime(2024, 1, 2), dt1);

            // Row 2: datetime is empty, falls back to timestamp
            Assert.IsTrue(reader.Read());
            DateTime dt2 = reader.ParseDate(0, 1);
            Assert.AreEqual(new DateTime(2024, 1, 2), dt2);
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
    }
}
