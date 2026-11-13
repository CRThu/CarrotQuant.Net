using System;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Scripting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Scripting;

[TestClass]
public class RoslynStrategyCompilerTests
{
    private class FakeBroker : IBroker
    {
        public double GetCash() => 100000.0;
        public decimal GetPosition(string symbol) => 0m;
        public string SubmitOrder(OrderRequest request) => "ord-1";
        public void CancelOrder(string orderId) { }
        public OrderStatus GetOrderStatus(string orderId) => OrderStatus.Filled;

#pragma warning disable CS0067
        public event Action<string, OrderStatus>? OnOrderStatusChanged;
        public event Action<ExecutionReport>? OnTrade;
        public event Action<PositionEvent>? OnPositionChanged;
#pragma warning restore CS0067
    }

    private class FakeEngineContext : IEngineContext
    {
        public DateTime CurrentTime { get; set; } = DateTime.UtcNow;
        public int CurrentIndex { get; set; } = 0;
        public CarrotBacktesting.NET.Abstraction.Data.ITimeseriesRegistry Timeseries => throw new NotImplementedException();
        public CarrotBacktesting.NET.Abstraction.Data.IEventRegistry Events => throw new NotImplementedException();
        public CarrotBacktesting.NET.Abstraction.Data.IFieldRegistry Fields => throw new NotImplementedException();
        public IBroker Broker { get; }
        public IMarketState Market { get; }

        public FakeEngineContext(IBroker broker, IMarketState market)
        {
            Broker = broker;
            Market = market;
        }

        public T? GetExtension<T>() where T : class => null;
    }

    [TestMethod]
    public void CompileSource_ValidStrategy_ShouldInstantiateAndRun()
    {
        const string code = @"
using System;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace MyDynamicNamespace;

public class DynamicMovingAverageStrategy : IStrategy
{
    public string Name => ""DynamicMA"";
    public int Lookback => 60;

    public void OnUpdate(IEngineContext context)
    {
        context.Market.SetState(""Signal_Fired"", key: ""Status"");
    }
}
";
        var (strategy, alc) = RoslynStrategyCompiler.CompileSource(code);
        try
        {
            Assert.IsNotNull(strategy);
            Assert.AreEqual("DynamicMA", strategy.Name);
            Assert.AreEqual(60, strategy.Lookback);

            var market = new MarketState();
            var context = new FakeEngineContext(new FakeBroker(), market);

            strategy.OnUpdate(context);

            var status = market.GetState<string>("Status");
            Assert.AreEqual("Signal_Fired", status);
        }
        finally
        {
            alc.Unload();
        }
    }

    [TestMethod]
    public void CompileSource_WithConstructorParameters_ShouldPassArgs()
    {
        const string code = @"
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Abstraction.Engine;

public class ParamStrategy : IStrategy
{
    public string Name { get; }
    public int Lookback { get; }

    public ParamStrategy(string name, int lookback)
    {
        Name = name;
        Lookback = lookback;
    }

    public void OnUpdate(IEngineContext context) { }
}
";
        var (strategy, alc) = RoslynStrategyCompiler.CompileSource(code, constructorArgs: new object[] { "CustomParamMA", 120 });
        try
        {
            Assert.AreEqual("CustomParamMA", strategy.Name);
            Assert.AreEqual(120, strategy.Lookback);
        }
        finally
        {
            alc.Unload();
        }
    }

    [TestMethod]
    public void CompileSource_SyntaxError_ShouldThrowWithFormattedDiagnostics()
    {
        const string invalidCode = @"
using System;
using CarrotBacktesting.NET.Abstraction.Strategy;

public class BrokenStrategy : IStrategy
{
    public string Name => ""Broken"";
    // 语法错误：缺少分号与类型
    this is a syntax error
}
";
        var ex = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            RoslynStrategyCompiler.CompileSource(invalidCode);
        });

        StringAssert.Contains(ex.Message, "动态策略源码编译失败");
        StringAssert.Contains(ex.Message, "[Line");
    }

    [TestMethod]
    public void CompileSource_NoStrategyType_ShouldThrowTypeLoadException()
    {
        const string code = @"
public class NotAStrategy
{
    public int Value => 42;
}
";
        Assert.ThrowsException<TypeLoadException>(() =>
        {
            RoslynStrategyCompiler.CompileSource(code);
        });
    }

    [TestMethod]
    public void CompileFile_ValidFile_ShouldCompileSuccessfully()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"Strategy_{Guid.NewGuid():N}.cs");
        const string code = @"
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Abstraction.Engine;

public class FileBasedStrategy : IStrategy
{
    public string Name => ""FromFile"";
    public void OnUpdate(IEngineContext context) { }
}
";
        File.WriteAllText(tempFile, code);

        try
        {
            var (strategy, alc) = RoslynStrategyCompiler.CompileFile(tempFile);
            try
            {
                Assert.AreEqual("FromFile", strategy.Name);
            }
            finally
            {
                alc.Unload();
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void CompileSource_NullOrWhitespace_ShouldThrowArgumentException()
    {
        Assert.ThrowsException<ArgumentException>(() =>
        {
            RoslynStrategyCompiler.CompileSource("   ");
        });
    }

    [TestMethod]
    public void CompileFile_FileNotFound_ShouldThrowFileNotFoundException()
    {
        Assert.ThrowsException<FileNotFoundException>(() =>
        {
            RoslynStrategyCompiler.CompileFile("non_existent_file.cs");
        });
    }

    [TestMethod]
    public void CompileSource_WithAbstractAndConcreteClasses_ShouldFindConcreteStrategy()
    {
        const string code = @"
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Abstraction.Engine;

public abstract class BaseAlpha : IStrategy
{
    public abstract string Name { get; }
    public abstract void OnUpdate(IEngineContext context);
}

public class ConcreteAlpha : BaseAlpha
{
    public override string Name => ""ConcreteAlpha"";
    public override void OnUpdate(IEngineContext context) { }
}
";
        var (strategy, alc) = RoslynStrategyCompiler.CompileSource(code);
        try
        {
            Assert.AreEqual("ConcreteAlpha", strategy.Name);
        }
        finally
        {
            alc.Unload();
        }
    }

    [TestMethod]
    public void CompileSource_ExplicitTypeName_MatchesSpecifiedClass()
    {
        const string code = @"
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Abstraction.Engine;

public class StrategyOne : IStrategy
{
    public string Name => ""One"";
    public void OnUpdate(IEngineContext context) { }
}

public class StrategyTwo : IStrategy
{
    public string Name => ""Two"";
    public void OnUpdate(IEngineContext context) { }
}
";
        var (strategy, alc) = RoslynStrategyCompiler.CompileSource(code, typeName: "StrategyTwo");
        try
        {
            Assert.AreEqual("Two", strategy.Name);
        }
        finally
        {
            alc.Unload();
        }
    }

    [TestMethod]
    public void CompileSource_ExplicitTypeName_IsAbstract_ShouldThrowTypeLoadException()
    {
        const string code = @"
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Abstraction.Engine;

public abstract class AbstractStrategy : IStrategy
{
    public string Name => ""Abstract"";
    public abstract void OnUpdate(IEngineContext context);
}
";
        Assert.ThrowsException<TypeLoadException>(() =>
        {
            RoslynStrategyCompiler.CompileSource(code, typeName: "AbstractStrategy");
        });
    }

    [TestMethod]
    public void CompileSource_ConstructorThrows_ShouldThrowInvalidOperationException()
    {
        const string code = @"
using System;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Abstraction.Engine;

public class CrashingStrategy : IStrategy
{
    public string Name => ""Crashing"";
    public CrashingStrategy()
    {
        throw new InvalidOperationException(""Boom from ctor"");
    }
    public void OnUpdate(IEngineContext context) { }
}
";
        var ex = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            RoslynStrategyCompiler.CompileSource(code);
        });

        StringAssert.Contains(ex.Message, "实例化失败");
    }

    [TestMethod]
    public void CompileSource_InternalClassStrategy_ShouldBeLoadedSuccessfully()
    {
        const string code = @"
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Abstraction.Engine;

// 未加 public 关键字，C# 默认为 internal
class InternalScriptStrategy : IStrategy
{
    public string Name => ""InternalMA"";
    public int Lookback => 45;
    public void OnUpdate(IEngineContext context) { }
}
";
        var (strategy, alc) = RoslynStrategyCompiler.CompileSource(code);
        try
        {
            Assert.IsNotNull(strategy);
            Assert.AreEqual("InternalMA", strategy.Name);
            Assert.AreEqual(45, strategy.Lookback);
        }
        finally
        {
            alc.Unload();
        }
    }

    [TestMethod]
    public void CompileSource_UnsafeCodeStrategy_ShouldCompileAndExecute()
    {
        const string code = @"
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Abstraction.Engine;

public class UnsafeFastStrategy : IStrategy
{
    public string Name => ""UnsafeFast"";
    public unsafe void OnUpdate(IEngineContext context)
    {
        double value = 123.456;
        double* ptr = &value;
        context.Market.SetState(new double[] { *ptr }, key: ""UnsafeResult"");
    }
}
";
        var (strategy, alc) = RoslynStrategyCompiler.CompileSource(code);
        try
        {
            Assert.IsNotNull(strategy);
            var market = new MarketState();
            var context = new FakeEngineContext(new FakeBroker(), market);
            strategy.OnUpdate(context);
            var res = market.GetState<double[]>("UnsafeResult");
            Assert.IsNotNull(res);
            Assert.AreEqual(123.456, res[0]);
        }
        finally
        {
            alc.Unload();
        }
    }
}
