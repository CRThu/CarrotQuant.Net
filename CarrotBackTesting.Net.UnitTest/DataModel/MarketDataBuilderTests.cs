using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.Net.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBacktesting.Net.Common;

namespace CarrotBacktesting.Net.DataModel.Tests
{
    [TestClass()]
    public class MarketDataBuilderTests
    {
        Dictionary<string, List<Dictionary<string, object>>> stockDict = new();

        public MarketDataBuilderTests()
        {
            List<Dictionary<string, object>> c1 = new();
            List<Dictionary<string, object>> c2 = new();
            List<Dictionary<string, object>> c3 = new();
            List<Dictionary<string, object>> c4 = new();
            c1.Add(new() { { "DateTime", "2022-01-01 09:00:00" }, { "ClosePrice", "123.456" }, { "IsTrading", "false" } });
            c1.Add(new() { { "DateTime", "2022-01-02 09:00:00" }, { "ClosePrice", "234.456" }, { "IsTrading", "true" } });
            c1.Add(new() { { "DateTime", "2022-01-03 09:00:00" }, { "ClosePrice", "345.456" }, { "IsTrading", "true" } });
            stockDict.Add("code.001", c1);
            c2.Add(new() { { "DateTime", "2022-01-03 09:00:00" }, { "ClosePrice", "456.567" }, { "IsTrading", "true" } });
            stockDict.Add("code.002", c2);
            c3.Add(new() { { "DateTime", "2022-01-03 09:00:00" }, { "ClosePrice", "999.999" }, { "IsTrading", "false" } });
            c3.Add(new() { { "DateTime", "2022-01-04" }, { "ClosePrice", "999.999" }, { "IsTrading", "false" } });
            c3.Add(new() { { "DateTime", "2022-01-05" }, { "ClosePrice", "999.999" }, { "IsTrading", "false" } });
            stockDict.Add("code.003", c3);
            c4.Add(new() { { "DateTime", "2022-01-04" }, { "ClosePrice", "888.888" }, { "IsTrading", "true" } });
            c4.Add(new() { { "DateTime", "2022-01-05" }, { "ClosePrice", "888.888" }, { "IsTrading", "true" } });
            stockDict.Add("code.004", c4);
        }

        [TestMethod()]
        public void AddRangeTest()
        {
            MarketDataBuilder builder = new();
            foreach (var stock in stockDict)
                builder.AddRange(stock.Key, stock.Value);

            MarketData data = builder.ToMarketData();

            Console.WriteLine("Times:");
            Console.WriteLine(string.Join('\n', data.Times.Select(t => t.FormatDateTime())));
            Assert.IsTrue(data.Times.Length == 5);
            Assert.IsTrue(data.Times[0] == new DateTime(2022, 01, 01, 09, 00, 00));
            Assert.IsTrue(data.Times[^1] == new DateTime(2022, 01, 05, 00, 00, 00));

            Console.WriteLine("Stocks in frames:");
            Console.WriteLine($"frames[2]:" + string.Join('|', data[2].StockCodes));
            Console.WriteLine($"frames[3]:" + string.Join('|', data[3].StockCodes));
            Console.WriteLine($"frames[4]:" + string.Join('|', data[4].StockCodes));
            Assert.IsTrue(data[0].StockCodes.Length == 1 && data[0].StockCodes.First() == "code.001");
            Assert.IsTrue(data[1].StockCodes.Length == 1 && data[1].StockCodes.First() == "code.001");
            Assert.IsTrue(data[2].StockCodes.Length == 3);
            Assert.IsTrue(data[2].StockCodes[0] == "code.001");
            Assert.IsTrue(data[2].StockCodes[1] == "code.002");
            Assert.IsTrue(data[2].StockCodes[2] == "code.003");
            Assert.IsTrue(data[3].StockCodes.Length == 2);
            Assert.IsTrue(data[3].StockCodes[0] == "code.003");
            Assert.IsTrue(data[3].StockCodes[1] == "code.004");
            Assert.IsTrue(data[4].StockCodes.Length == 2);
            Assert.IsTrue(data[4].StockCodes[0] == "code.003");
            Assert.IsTrue(data[4].StockCodes[1] == "code.004");
        }
    }
}