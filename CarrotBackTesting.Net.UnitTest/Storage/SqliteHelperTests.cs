using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using CarrotBacktesting.Net.Storage;
using CarrotBacktesting.Net.DataModel;

namespace CarrotBackTesting.Net.UnitTest.Storage
{
    [TestClass()]
    public class SqliteHelperTests
    {
        public const string SqliteDatabasePath = "../../../../Data/sz.000400-sz.000499_1d_baostock.db";

        [TestMethod()]
        public void QueryTest()
        {
            SqliteHelper sqliteHelper = new();
            sqliteHelper.Open(SqliteDatabasePath);
            int d = sqliteHelper.Query<int>("SELECT COUNT(*) FROM 'sz.000400';");
            Console.WriteLine($"Call: \"SELECT COUNT(*) FROM 'sz.000400';\"\nReturn: {d}.");
            Assert.IsTrue(d == 5954);
        }

        [TestMethod()]
        public void QueryAsDataTableTest()
        {
            SqliteHelper sqliteHelper = new();
            sqliteHelper.Open(SqliteDatabasePath);
            DataTable dt = sqliteHelper.QueryAsDataTable("SELECT * FROM 'sz.000400';");
            Console.WriteLine($"Call: \"SELECT * FROM 'sz.000400';\"\nReturn: {dt.Rows.Count} rows DataTable.");
            Assert.IsTrue(dt.Rows.Count == 5954);
        }

        [TestMethod()]
        public void QueryAsDictionaryListTest()
        {
            SqliteHelper sqliteHelper = new();
            sqliteHelper.Open(SqliteDatabasePath);
            var rows = sqliteHelper.QueryAsDictionaryList("SELECT * FROM 'sz.000400';");
            Assert.IsTrue(rows.Count() == 5954);
            var types = rows.First().Select(kv => kv.Key + "|" + kv.Value.GetType().FullName);
            Console.WriteLine(string.Join('\n', types));
        }

        [TestMethod()]
        public void GetTableNamesTest()
        {
            SqliteHelper sqliteHelper = new();
            sqliteHelper.Open(SqliteDatabasePath);
            string[] names = sqliteHelper.GetTableNames().ToArray();
            Console.WriteLine($"Return: {names.Length} table names.");
            Assert.IsTrue(names.Length == 30);
            Console.WriteLine(string.Join('|', names.Take(5)));
        }

        [TestMethod()]
        public void GetTableTest()
        {
            SqliteHelper sqliteHelper = new();
            sqliteHelper.Open(SqliteDatabasePath);
            var dt1 = sqliteHelper.GetTable("sz.000400");
            var dt2 = sqliteHelper.GetTable("sz.000400",
                new string[] { "[index]", "交易日期", "收盘价" });
            var dt3 = sqliteHelper.GetTable("sz.000400",
                new string[] { "[index]", "交易日期", "收盘价" },
                ("交易日期", "2020-01-01", "2022-12-31", FilterCondition.BigEqualAndSmallEqual));
            var dt4 = sqliteHelper.GetTable("sz.000400",
                new string[] { "[index]", "交易日期", "收盘价" },
                shareFrameMapper: new ShareFrameMapper()
                {
                    ["[index]"] = "[index]",
                    ["交易日期"] = "DateTime",
                    ["收盘价"] = "close"
                });

            // SELECT * FROM 'sz.000400';
            // SELECT [index],交易日期,收盘价 FROM 'sz.000400';
            // SELECT [index],交易日期,收盘价 FROM 'sz.000400' WHERE 交易日期 >= '2020-01-01' AND 交易日期 <= '2022-12-31';
            Assert.IsTrue(dt1.Count() == 5954);
            Assert.IsTrue(dt2.Count() == 5954 && dt2.First().Count == 3);
            Assert.IsTrue(dt3.Count() == 446);

            List<string> colNames = dt2.First().Keys.ToList();
            Console.WriteLine("Column Names: " + string.Join('|', colNames));
            Console.WriteLine("Rows: " + dt3.Count());

            Assert.IsTrue(colNames.Count == 3);


            List<string> colNames2 = dt4.First().Keys.ToList();
            Console.WriteLine("Column Names: " + string.Join('|', colNames2));
        }
    }
}