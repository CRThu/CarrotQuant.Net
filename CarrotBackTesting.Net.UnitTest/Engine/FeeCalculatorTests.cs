using System;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Engine.Broker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

[TestClass]
public class FeeCalculatorTests
{
    [TestMethod]
    public void CalculateFee_ZeroOrNegativeTurnover_ReturnsZero()
    {
        var config = new BrokerConfig { FeeRate = 0.001, MinFee = 5.0 };
        Assert.AreEqual(0.0, FeeCalculator.CalculateFee(0.0, null, config));
        Assert.AreEqual(0.0, FeeCalculator.CalculateFee(-100.0, null, config));
    }

    [TestMethod]
    public void CalculateFee_NullConfig_ReturnsZero()
    {
        Assert.AreEqual(0.0, FeeCalculator.CalculateFee(10000.0, null, null));
    }

    [TestMethod]
    public void CalculateFee_NormalTurnover_RespectsMinFeeAndRate()
    {
        var config = new BrokerConfig { FeeRate = 0.0003, MinFee = 5.0 };
        // 1000 * 0.0003 = 0.3 < 5.0 => 5.0
        Assert.AreEqual(5.0, FeeCalculator.CalculateFee(1000.0, null, config), 1e-6);

        // 100000 * 0.0003 = 30.0 > 5.0 => 30.0
        Assert.AreEqual(30.0, FeeCalculator.CalculateFee(100000.0, null, config), 1e-6);
    }

    [TestMethod]
    public void CalculateFee_CustomHook_InvokesHook()
    {
        var request = new OrderRequest("SH.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 1000m);
        var config = new BrokerConfig
        {
            FeeRate = 0.0003,
            MinFee = 5.0,
            OnCalculateFee = (req, turnover) =>
            {
                Assert.AreSame(request, req);
                return turnover * 0.001 + 2.0;
            }
        };

        double fee = FeeCalculator.CalculateFee(10000.0, request, config);
        Assert.AreEqual(12.0, fee);
    }
}
