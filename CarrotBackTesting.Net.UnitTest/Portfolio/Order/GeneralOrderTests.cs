using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.Net.Portfolio.Order;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Order.Tests
{
    [TestClass()]
    public class GeneralOrderTests
    {
        [TestMethod()]
        public void TradeTest()
        {
            GeneralOrder order1 = new("sz.000444", OrderType.LimitOrder, OrderDirection.Buy, 1000, 100);
            GeneralOrder order2 = new("sz.000444", OrderType.LimitOrder, OrderDirection.Sell, 1000, 100);
            GeneralOrder order3 = new("sz.000444", OrderType.MarketOrder, OrderDirection.Buy, 1000);
            GeneralOrder order4 = new("sz.000444", OrderType.MarketOrder, OrderDirection.Sell, 1000);

            Assert.AreEqual(0, order1.OrderId);
            Assert.AreEqual(1, order2.OrderId);
            Assert.AreEqual(2, order3.OrderId);
            Assert.AreEqual(3, order4.OrderId);

            (int size, int price)[] order1TradeData = new (int, int)[] {
                (200, 100),
                (400, 98),
                (400, 90) };
            (int size, int price)[] order2TradeData = new (int, int)[] {
                (300, 120),
                (200, 150),
                (200, 200) };
            (int size, int price)[] order3TradeData = new (int, int)[] {
                (800, 105)};
            (int size, int price)[] order4TradeData = new (int, int)[] {
                (200, 99),
                (800, 98) };

            foreach (var (size, price) in order1TradeData)
                order1.Trade(size, price);
            foreach (var (size, price) in order2TradeData)
                order2.Trade(size, price);
            foreach (var (size, price) in order3TradeData)
                order3.Trade(size, price);
            foreach (var (size, price) in order4TradeData)
                order4.Trade(size, price);

            Console.WriteLine($"Order1:");
            Console.WriteLine($"Status: {order1.Status}, PendingSize: {order1.PendingSize}, "
                + $"Deal AvgPrice and Size: {order1.DealAveragePrice} * {order1.DealSize}.");
            Assert.IsTrue(order1.Status == ((order1TradeData.Sum(d => d.size) == 1000) ? GeneralOrderStatus.Executed : GeneralOrderStatus.Pending)
                && order1.PendingSize == (1000 - order1TradeData.Sum(d => d.size))
                && order1.DealSize == order1TradeData.Sum(d => d.size)
                && order1.DealAveragePrice == (double)order1TradeData.Sum(d => d.size * d.price) / order1TradeData.Sum(d => d.size));
            Console.WriteLine($"Order2:");
            Console.WriteLine($"Status: {order2.Status}, PendingSize: {order2.PendingSize}, "
                + $"Deal AvgPrice and Size: {order2.DealAveragePrice} * {order2.DealSize}.");
            Assert.IsTrue(order2.Status == ((order2TradeData.Sum(d => d.size) == 1000) ? GeneralOrderStatus.Executed : GeneralOrderStatus.Pending)
                && order2.PendingSize == (1000 - order2TradeData.Sum(d => d.size))
                && order2.DealSize == order2TradeData.Sum(d => d.size)
                && order2.DealAveragePrice == (double)order2TradeData.Sum(d => d.size * d.price) / order2TradeData.Sum(d => d.size));
            Console.WriteLine($"Order3:");
            Console.WriteLine($"Status: {order3.Status}, PendingSize: {order3.PendingSize}, "
                + $"Deal AvgPrice and Size: {order3.DealAveragePrice} * {order3.DealSize}.");
            Assert.IsTrue(order3.Status == ((order3TradeData.Sum(d => d.size) == 1000) ? GeneralOrderStatus.Executed : GeneralOrderStatus.Pending)
                && order3.PendingSize == (1000 - order3TradeData.Sum(d => d.size))
                && order3.DealSize == order3TradeData.Sum(d => d.size)
                && order3.DealAveragePrice == (double)order3TradeData.Sum(d => d.size * d.price) / order3TradeData.Sum(d => d.size));
            Console.WriteLine($"Order4:");
            Console.WriteLine($"Status: {order4.Status}, PendingSize: {order4.PendingSize}, "
                + $"Deal AvgPrice and Size: {order4.DealAveragePrice} * {order4.DealSize}.");
            Assert.IsTrue(order4.Status == ((order4TradeData.Sum(d => d.size) == 1000) ? GeneralOrderStatus.Executed : GeneralOrderStatus.Pending)
                && order4.PendingSize == (1000 - order4TradeData.Sum(d => d.size))
                && order4.DealSize == order4TradeData.Sum(d => d.size)
                && order4.DealAveragePrice == (double)order4TradeData.Sum(d => d.size * d.price) / order4TradeData.Sum(d => d.size));

            order3.Cancel();
            Console.WriteLine($"Order3: Status: {order3.Status}");
            Assert.IsTrue(order3.Status == GeneralOrderStatus.Cancelled);
        }
    }
}