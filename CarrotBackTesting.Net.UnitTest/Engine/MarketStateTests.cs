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
    private sealed record SampleSignal(string Action, int Score);

    [TestMethod]
    public void GetAndSetState_WithExplicitKey_ShouldStoreAndRetrieveAccurately()
    {
        IMarketState blackboard = new MarketState();

        var regime = new SampleRegime(true, 0.25);
        blackboard.SetState("macro_regime", regime);

        Assert.IsTrue(blackboard.HasState<SampleRegime>("macro_regime"));
        var retrieved = blackboard.GetState<SampleRegime>("macro_regime");
        Assert.IsNotNull(retrieved);
        Assert.AreEqual(true, retrieved.IsRiskOff);
        Assert.AreEqual(0.25, retrieved.Volatility);

        // 未存入的 key 应返回 null 与 false
        Assert.IsFalse(blackboard.HasState<PortfolioBudget>("unregistered_key"));
        Assert.IsNull(blackboard.GetState<PortfolioBudget>("unregistered_key"));
    }

    [TestMethod]
    public void GetAndSetState_MultipleKeys_ShouldIsolateDifferentValues()
    {
        IMarketState blackboard = new MarketState();

        var scoresA = new Dictionary<string, double> { ["sh.600000"] = 0.85 };
        var scoresB = new Dictionary<string, double> { ["sh.600000"] = -0.32 };

        blackboard.SetState("Alpha_Momentum", scoresA);
        blackboard.SetState("Alpha_Reversion", scoresB);

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
        blackboard.SetState("regime", new SampleRegime(false, 0.1));
        blackboard.SetState("Budget", new PortfolioBudget(1000m));

        Assert.IsTrue(blackboard.HasState<SampleRegime>("regime"));
        Assert.IsTrue(blackboard.HasState<PortfolioBudget>("Budget"));

        blackboard.Clear();

        Assert.IsFalse(blackboard.HasState<SampleRegime>("regime"));
        Assert.IsFalse(blackboard.HasState<PortfolioBudget>("Budget"));
        Assert.IsNull(blackboard.GetState<SampleRegime>("regime"));
    }

    [TestMethod]
    public void SetState_NullArgument_ShouldThrowArgumentNullException()
    {
        IMarketState blackboard = new MarketState();
        Assert.ThrowsException<ArgumentNullException>(() =>
        {
            blackboard.SetState<SampleRegime>("regime", null!);
        });

        Assert.ThrowsException<ArgumentNullException>(() =>
        {
            blackboard.SetState(null!, new SampleRegime(true, 1.0));
        });

        Assert.ThrowsException<ArgumentException>(() =>
        {
            blackboard.SetState("   ", new SampleRegime(true, 1.0));
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
                blackboard.SetState(key, new SampleRegime(i % 2 == 0, i * 0.01));
                var retrieved = blackboard.GetState<SampleRegime>(key);
                Assert.IsNotNull(retrieved);
            }
        });

        for (int taskId = 0; taskId < taskCount; taskId++)
        {
            Assert.IsTrue(blackboard.HasState<SampleRegime>($"Worker_{taskId}"));
        }
    }

    [TestMethod]
    public void GetState_TypeMismatch_ShouldReturnNullSafely()
    {
        IMarketState blackboard = new MarketState();
        blackboard.SetState("MyKey", "StringValue");

        // 用同一个 key 尝试获取不相符的类型 SampleRegime，应返回 null 且不抛异常
        var result = blackboard.GetState<SampleRegime>("MyKey");
        Assert.IsNull(result);
        Assert.IsFalse(blackboard.HasState<SampleRegime>("MyKey"));
        Assert.IsTrue(blackboard.HasState<string>("MyKey"));
    }

    [TestMethod]
    public void SetState_ShouldPreserveSimulationTimestampAccurately()
    {
        var defaultBlackboard = new MarketState();
        defaultBlackboard.SetState("sig1", "SignalValue");
        Assert.IsTrue(defaultBlackboard.TryGetState<string>("sig1", out var val1, out var time1));
        Assert.IsNull(time1);

        var simTime = new MarketTime(2021, 5, 20);
        defaultBlackboard.SetState("sig2", "SignalValue", simTime);
        Assert.IsTrue(defaultBlackboard.TryGetState<string>("sig2", out var val2, out var time2));
        Assert.AreEqual(simTime, time2);
    }

    [TestMethod]
    public void ClearState_ShouldRemoveSpecificStateAccurately()
    {
        IMarketState blackboard = new MarketState();
        blackboard.SetState("regime", new SampleRegime(true, 1.0));
        blackboard.SetState("signal", "PulseSignal");

        Assert.IsTrue(blackboard.HasState<SampleRegime>("regime"));
        Assert.IsTrue(blackboard.HasState<string>("signal"));

        // 精确移除指定类型为 string 的 signal
        bool removed = blackboard.ClearState<string>("signal");
        Assert.IsTrue(removed);
        Assert.IsFalse(blackboard.HasState<string>("signal"));
        Assert.IsNull(blackboard.GetState<string>("signal"));

        // regime 依然完好保留
        Assert.IsTrue(blackboard.HasState<SampleRegime>("regime"));
        Assert.AreEqual(true, blackboard.GetState<SampleRegime>("regime")?.IsRiskOff);

        // 重复移除返回 false
        Assert.IsFalse(blackboard.ClearState<string>("signal"));
    }

    [TestMethod]
    public void TryTakeState_ShouldAtomicallyRetrieveAndRemove_PreventingGhostSignals()
    {
        IMarketState blackboard = new MarketState();
        var signal = new SampleSignal("Buy", 100);
        var expectedTime = new MarketTime(2025, 5, 20, 9, 30, 0);
        blackboard.SetState("worker_alpha", signal, expectedTime);

        // 第一次原子消费：成功获取并就地移除，同时验证输出的时间戳
        bool firstTake = blackboard.TryTakeState<SampleSignal>("worker_alpha", out var takenSignal, out var takenTime);
        Assert.IsTrue(firstTake);
        Assert.IsNotNull(takenSignal);
        Assert.AreEqual("Buy", takenSignal.Action);
        Assert.AreEqual(100, takenSignal.Score);
        Assert.AreEqual(expectedTime, takenTime);

        // 第二次获取：已被取走，返回 false 且为 null，彻底杜绝幽灵交易
        bool secondTake = blackboard.TryTakeState<SampleSignal>("worker_alpha", out var ghostSignal, out var ghostTime);
        Assert.IsFalse(secondTake);
        Assert.IsNull(ghostSignal);
        Assert.IsNull(ghostTime);
        Assert.IsNull(blackboard.GetState<SampleSignal>("worker_alpha"));
    }

    [TestMethod]
    public void TryTakeState_ConcurrentCompetition_OnlyOneThreadSucceeds()
    {
        IMarketState blackboard = new MarketState();
        var originalSignal = new SampleSignal("PulseBuy", 999);
        var simTime = new MarketTime(2025, 5, 20);
        blackboard.SetState("pulse_signal", originalSignal, simTime);

        const int threadCount = 16;
        int successCount = 0;
        SampleSignal? consumedSignal = null;
        MarketTime? consumedTime = null;

        Parallel.For(0, threadCount, _ =>
        {
            if (blackboard.TryTakeState<SampleSignal>("pulse_signal", out var taken, out var time))
            {
                System.Threading.Interlocked.Increment(ref successCount);
                consumedSignal = taken;
                consumedTime = time;
            }
        });

        Assert.AreEqual(1, successCount);
        Assert.IsNotNull(consumedSignal);
        Assert.AreEqual("PulseBuy", consumedSignal.Action);
        Assert.AreEqual(999, consumedSignal.Score);
        Assert.AreEqual(simTime, consumedTime);

        Assert.IsFalse(blackboard.HasState<SampleSignal>("pulse_signal"));
    }

    [TestMethod]
    public void ScalarAndDynamicTypes_ShouldWorkFluentlyWithoutAnyInheritance()
    {
        IMarketState blackboard = new MarketState();

        // 原生标量存储与读取（double, int, bool, string）
        blackboard.SetState("stop_price", 15.8);
        blackboard.SetState("is_risk_off", true);
        blackboard.SetState("bars", 5);
        blackboard.SetState("pattern", "DOUBLE_BOTTOM");

        Assert.AreEqual(15.8, blackboard.GetState<double>("stop_price"));
        Assert.AreEqual(true, blackboard.GetState<bool>("is_risk_off"));
        Assert.AreEqual(5, blackboard.GetState<int>("bars"));
        Assert.AreEqual("DOUBLE_BOTTOM", blackboard.GetState<string>("pattern"));

        // 普通自定义 POCO/Record 对象无需继承任何接口或基类
        var customObj = new SampleSignal("TakeProfit", 88);
        blackboard.SetState("custom_sig", customObj);

        var retrieved = blackboard.GetState<SampleSignal>("custom_sig");
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("TakeProfit", retrieved.Action);
        Assert.AreEqual(88, retrieved.Score);
    }

    public record CustomStrategyRecord(string Symbol, double Weight, int Rank, bool IsActive);

    public readonly record struct CustomRecordStruct(double StopLoss, double TakeProfit);

    public struct CustomPlainStruct
    {
        public int Lookback { get; set; }
        public double Alpha { get; set; }
    }

    public class CustomPocoModel
    {
        public string ModelName { get; set; } = string.Empty;
        public List<string> Targets { get; set; } = new();
        public Dictionary<string, double> Scores { get; set; } = new();
    }

    [TestMethod]
    public void SaveAndRestoreState_NativeScalars_ShouldPreserveExactTypesAndPrecision()
    {
        var original = new MarketState();
        var t0 = new MarketTime(2025, 6, 18, 14, 30, 0);

        original.SetState("val_float", 12.345f, t0);
        original.SetState("val_double", 98.7654321, t0);
        original.SetState("val_int", 42, t0);
        original.SetState("val_long", 9876543210L, t0);
        original.SetState("val_short", (short)123, t0);
        original.SetState("val_byte", (byte)255, t0);
        original.SetState("val_decimal", 123456789.987654321m, t0);
        original.SetState("val_bool", true, t0);
        original.SetState("val_string", "CarrotQuant_FirstPrinciple", t0);

        var snapshot = original.SaveState();
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(9, snapshot.Entries.Count);

        var restored = new MarketState();
        restored.RestoreState(snapshot);

        // 验证精确类型保真（绝不发生隐式类型提升或截断）
        Assert.IsTrue(restored.TryGetState<float>("val_float", out var fVal, out var fTime));
        Assert.AreEqual(12.345f, fVal);
        Assert.AreEqual(t0, fTime);
        Assert.AreEqual(0.0, restored.GetState<double>("val_float")); // float 不应被错误读取为 double

        Assert.IsTrue(restored.TryGetState<double>("val_double", out var dVal, out _));
        Assert.AreEqual(98.7654321, dVal, 1e-7);

        Assert.IsTrue(restored.TryGetState<int>("val_int", out var iVal, out _));
        Assert.AreEqual(42, iVal);
        Assert.AreEqual(0L, restored.GetState<long>("val_int")); // int 不应被错误提升为 long

        Assert.IsTrue(restored.TryGetState<long>("val_long", out var lVal, out _));
        Assert.AreEqual(9876543210L, lVal);

        Assert.IsTrue(restored.TryGetState<short>("val_short", out var sVal, out _));
        Assert.AreEqual((short)123, sVal);

        Assert.IsTrue(restored.TryGetState<byte>("val_byte", out var bVal, out _));
        Assert.AreEqual((byte)255, bVal);

        // 验证 decimal 精度完全无损
        Assert.IsTrue(restored.TryGetState<decimal>("val_decimal", out var mVal, out _));
        Assert.AreEqual(123456789.987654321m, mVal);

        Assert.IsTrue(restored.TryGetState<bool>("val_bool", out var boolVal, out _));
        Assert.IsTrue(boolVal);

        Assert.IsTrue(restored.TryGetState<string>("val_string", out var strVal, out _));
        Assert.AreEqual("CarrotQuant_FirstPrinciple", strVal);
    }

    [TestMethod]
    public void SaveAndRestoreState_CustomRecordAndStructs_ShouldRoundtripAccurately()
    {
        var original = new MarketState();
        var t0 = new MarketTime(2025, 8, 1);

        var recordObj = new CustomStrategyRecord("sh.600000", 0.35, 1, true);
        var recordStructObj = new CustomRecordStruct(10.5, 15.0);
        var plainStructObj = new CustomPlainStruct { Lookback = 60, Alpha = 1.25 };

        original.SetState("record_item", recordObj, t0);
        original.SetState("record_struct_item", recordStructObj, t0);
        original.SetState("plain_struct_item", plainStructObj, t0);

        var snapshot = original.SaveState();
        var restored = new MarketState();
        restored.RestoreState(snapshot);

        // 1. 验证自建 record
        var rRecord = restored.GetState<CustomStrategyRecord>("record_item");
        Assert.IsNotNull(rRecord);
        Assert.AreEqual("sh.600000", rRecord.Symbol);
        Assert.AreEqual(0.35, rRecord.Weight, 1e-6);
        Assert.AreEqual(1, rRecord.Rank);
        Assert.IsTrue(rRecord.IsActive);

        // 2. 验证自建 readonly record struct
        var rRecordStruct = restored.GetState<CustomRecordStruct>("record_struct_item");
        Assert.AreEqual(10.5, rRecordStruct.StopLoss, 1e-6);
        Assert.AreEqual(15.0, rRecordStruct.TakeProfit, 1e-6);

        // 3. 验证自建普通 struct
        var rPlainStruct = restored.GetState<CustomPlainStruct>("plain_struct_item");
        Assert.AreEqual(60, rPlainStruct.Lookback);
        Assert.AreEqual(1.25, rPlainStruct.Alpha, 1e-6);
    }

    [TestMethod]
    public void SaveAndRestoreState_CustomClassAndComplexCollections_ShouldRoundtripAccurately()
    {
        var original = new MarketState();
        var t0 = new MarketTime(2025, 10, 1);

        // 1. 自建复杂 class/POCO
        var poco = new CustomPocoModel
        {
            ModelName = "MultiFactorRanker",
            Targets = new List<string> { "sh.600000", "sz.000001", "sh.688001" },
            Scores = new Dictionary<string, double>
            {
                ["sh.600000"] = 0.95,
                ["sz.000001"] = 0.88,
                ["sh.688001"] = 0.72
            }
        };
        original.SetState("model_poco", poco, t0);

        // 2. 数组类型
        double[] arrayData = new[] { 1.1, 2.2, 3.3, 4.4 };
        original.SetState("double_array", arrayData, t0);

        // 3. 复杂泛型嵌套集合
        var nestedDict = new Dictionary<string, List<int>>
        {
            ["cluster_A"] = new List<int> { 1, 2, 3 },
            ["cluster_B"] = new List<int> { 4, 5, 6 }
        };
        original.SetState("nested_collection", nestedDict, t0);

        var snapshot = original.SaveState();
        var restored = new MarketState();
        restored.RestoreState(snapshot);

        // 验证 POCO
        var rPoco = restored.GetState<CustomPocoModel>("model_poco");
        Assert.IsNotNull(rPoco);
        Assert.AreEqual("MultiFactorRanker", rPoco.ModelName);
        CollectionAssert.AreEqual(new List<string> { "sh.600000", "sz.000001", "sh.688001" }, rPoco.Targets);
        Assert.AreEqual(0.95, rPoco.Scores["sh.600000"], 1e-6);
        Assert.AreEqual(0.88, rPoco.Scores["sz.000001"], 1e-6);
        Assert.AreEqual(0.72, rPoco.Scores["sh.688001"], 1e-6);

        // 验证数组
        var rArray = restored.GetState<double[]>("double_array");
        Assert.IsNotNull(rArray);
        CollectionAssert.AreEqual(arrayData, rArray);

        // 验证嵌套泛型集合
        var rNested = restored.GetState<Dictionary<string, List<int>>>("nested_collection");
        Assert.IsNotNull(rNested);
        Assert.AreEqual(2, rNested.Count);
        CollectionAssert.AreEqual(new List<int> { 1, 2, 3 }, rNested["cluster_A"]);
        CollectionAssert.AreEqual(new List<int> { 4, 5, 6 }, rNested["cluster_B"]);
    }

    [TestMethod]
    public void SaveAndRestoreState_EmptyAndInvalidState_ShouldHandleDefensively()
    {
        var emptyMarket = new MarketState();
        var emptySnapshot = emptyMarket.SaveState();
        Assert.IsNotNull(emptySnapshot);
        Assert.AreEqual(0, emptySnapshot.Entries.Count);

        var newMarket = new MarketState();
        newMarket.RestoreState(emptySnapshot);
        Assert.IsFalse(newMarket.HasState<string>("any_key"));

        // 未知或不可解析类型必须显式抛出强类型异常，严禁静默吞掉
        var invalidSnapshot = new MarketStateSnapshot(new[]
        {
            new MarketStateEntrySnapshot("corrupted_key", "NonExistentNamespace.FakeType, NonExistentAssembly", "{}", MarketTime.MinValue)
        });

        var targetMarket = new MarketState();
        Assert.ThrowsException<InvalidOperationException>(() =>
        {
            targetMarket.RestoreState(invalidSnapshot);
        });
    }
}
