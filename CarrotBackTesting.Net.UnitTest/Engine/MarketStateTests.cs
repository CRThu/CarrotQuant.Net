using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Engine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

[TestClass]
public class MarketStateTests
{
    private record SampleRegime(bool IsRiskOff, double Volatility);
    private record PortfolioBudget(decimal CashLimit);

    [TestMethod]
    public void GetAndSetState_DefaultKey_ShouldWorkAsSingletonPerType()
    {
        IMarketState blackboard = new MarketState();

        var regime = new SampleRegime(true, 0.25);
        blackboard.SetState(regime);

        Assert.IsTrue(blackboard.HasState<SampleRegime>());
        var retrieved = blackboard.GetState<SampleRegime>();
        Assert.IsNotNull(retrieved);
        Assert.AreEqual(true, retrieved.IsRiskOff);
        Assert.AreEqual(0.25, retrieved.Volatility);

        // 未存入的类型应返回 null 与 false
        Assert.IsFalse(blackboard.HasState<PortfolioBudget>());
        Assert.IsNull(blackboard.GetState<PortfolioBudget>());
    }

    [TestMethod]
    public void GetAndSetState_WithCustomKey_ShouldIsolateSameType()
    {
        IMarketState blackboard = new MarketState();

        var scoresA = new Dictionary<string, double> { ["sh.600000"] = 0.85 };
        var scoresB = new Dictionary<string, double> { ["sh.600000"] = -0.32 };

        blackboard.SetState(scoresA, key: "Alpha_Momentum");
        blackboard.SetState(scoresB, key: "Alpha_Reversion");

        Assert.IsTrue(blackboard.HasState<Dictionary<string, double>>("Alpha_Momentum"));
        Assert.IsTrue(blackboard.HasState<Dictionary<string, double>>("Alpha_Reversion"));

        var resultA = blackboard.GetState<Dictionary<string, double>>("Alpha_Momentum");
        var resultB = blackboard.GetState<Dictionary<string, double>>("Alpha_Reversion");

        Assert.IsNotNull(resultA);
        Assert.IsNotNull(resultB);
        Assert.AreEqual(0.85, resultA["sh.600000"]);
        Assert.AreEqual(-0.32, resultB["sh.600000"]);
    }

    [TestMethod]
    public void Clear_ShouldRemoveAllStates()
    {
        IMarketState blackboard = new MarketState();
        blackboard.SetState(new SampleRegime(false, 0.1));
        blackboard.SetState(new PortfolioBudget(1000m), key: "Budget");

        Assert.IsTrue(blackboard.HasState<SampleRegime>());
        Assert.IsTrue(blackboard.HasState<PortfolioBudget>("Budget"));

        blackboard.Clear();

        Assert.IsFalse(blackboard.HasState<SampleRegime>());
        Assert.IsFalse(blackboard.HasState<PortfolioBudget>("Budget"));
        Assert.IsNull(blackboard.GetState<SampleRegime>());
    }

    [TestMethod]
    public void SetState_NullArgument_ShouldThrowArgumentNullException()
    {
        IMarketState blackboard = new MarketState();
        Assert.ThrowsException<ArgumentNullException>(() =>
        {
            blackboard.SetState<SampleRegime>(null!);
        });
    }

    [TestMethod]
    public void ConcurrentOperations_ShouldBeThreadSafe()
    {
        IMarketState blackboard = new MarketState();
        const int taskCount = 16;
        const int iterations = 1000;

        Parallel.For(0, taskCount, taskId =>
        {
            var key = $"Worker_{taskId}";
            for (int i = 0; i < iterations; i++)
            {
                blackboard.SetState(new SampleRegime(i % 2 == 0, i * 0.01), key);
                var retrieved = blackboard.GetState<SampleRegime>(key);
                Assert.IsNotNull(retrieved);
            }
        });

        // 验证所有 key 均存在
        for (int taskId = 0; taskId < taskCount; taskId++)
        {
            Assert.IsTrue(blackboard.HasState<SampleRegime>($"Worker_{taskId}"));
        }
    }

    [TestMethod]
    public void GetState_TypeMismatch_ShouldReturnNullSafely()
    {
        IMarketState blackboard = new MarketState();
        blackboard.SetState("StringValue", key: "MyKey");

        // 用同一个 key 尝试获取不相符的类型 SampleRegime，应返回 null 且不抛异常
        var result = blackboard.GetState<SampleRegime>("MyKey");
        Assert.IsNull(result);
        Assert.IsFalse(blackboard.HasState<SampleRegime>("MyKey"));
    }
}
