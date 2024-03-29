﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.Net.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBacktesting.Net.Common;

namespace CarrotBacktesting.Net.DataModel.Tests
{
    /*
    [TestClass()]
    public class MarketDataBuilderTests
    {
        List<ShareFrame> frames = new();

        public MarketDataBuilderTests()
        {
            frames.Add(new(new Dictionary<string, object>() { { "Time", "2022-01-01 09:00:00" }, { "Close", "123.456" }, { "Status", "false" } }, "code.001"));
            frames.Add(new(new Dictionary<string, object>() { { "Time", "2022-01-02 09:00:00" }, { "Close", "234.456" }, { "Status", "true" } }, "code.001"));
            frames.Add(new(new Dictionary<string, object>() { { "Time", "2022-01-03 09:00:00" }, { "Close", "345.456" }, { "Status", "true" } }, "code.001"));
            frames.Add(new(new Dictionary<string, object>() { { "Time", "2022-01-03 09:00:00" }, { "Close", "456.567" }, { "Status", "true" } }, "code.002"));
            frames.Add(new(new Dictionary<string, object>() { { "Time", "2022-01-03 09:00:00" }, { "Close", "999.999" }, { "Status", "false" } }, "code.003"));
            frames.Add(new(new Dictionary<string, object>() { { "Time", "2022-01-04" }, { "Close", "999.999" }, { "Status", "false" } }, "code.003"));
            frames.Add(new(new Dictionary<string, object>() { { "Time", "2022-01-05" }, { "Close", "999.999" }, { "Status", "false" } }, "code.003"));
            frames.Add(new(new Dictionary<string, object>() { { "Time", "2022-01-04" }, { "Close", "888.888" }, { "Status", "true" } }, "code.004"));
            frames.Add(new(new Dictionary<string, object>() { { "Time", "2022-01-05" }, { "Close", "888.888" }, { "Status", "true" } }, "code.004"));
        }

        [TestMethod()]
        public void AddRangeTest()
        {
            MarketDataBuilder builder = new();
            builder.AddRange(frames);

            MarketData data = builder.ToMarketData();

            Console.WriteLine("Times:");
            Console.WriteLine(string.Join('\n', data.Times.Select(t => t.FormatDateTime())));
            Assert.IsTrue(data.Times.Length == 5);
            Assert.IsTrue(data.Times[0] == new DateTime(2022, 01, 01, 09, 00, 00));
            Assert.IsTrue(data.Times[^1] == new DateTime(2022, 01, 05, 00, 00, 00));

            Console.WriteLine("Stocks in frames:");
            Console.WriteLine($"frames[2]:" + string.Join('|', data[2].Codes));
            Console.WriteLine($"frames[3]:" + string.Join('|', data[3].Codes));
            Console.WriteLine($"frames[4]:" + string.Join('|', data[4].Codes));
            Assert.IsTrue(data[0].Count == 1 && data[0].Codes.First() == "code.001");
            Assert.IsTrue(data[1].Count == 1 && data[1].Codes.First() == "code.001");
            Assert.IsTrue(data[2].Count == 3);
            //Assert.IsTrue(data[2].StockCodes[0] == "code.001");
            //Assert.IsTrue(data[2].StockCodes[1] == "code.002");
            //Assert.IsTrue(data[2].StockCodes[2] == "code.003");
            //Assert.IsTrue(data[3].StockCodes.Length == 2);
            //Assert.IsTrue(data[3].StockCodes[0] == "code.003");
            //Assert.IsTrue(data[3].StockCodes[1] == "code.004");
            //Assert.IsTrue(data[4].StockCodes.Length == 2);
            //Assert.IsTrue(data[4].StockCodes[0] == "code.003");
            //Assert.IsTrue(data[4].StockCodes[1] == "code.004");
        }
    }
    */
}