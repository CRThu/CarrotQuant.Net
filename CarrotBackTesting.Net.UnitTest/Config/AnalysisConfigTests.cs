using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Analysis;
using CarrotBacktesting.NET.Analysis.Analyzers;
using CarrotBacktesting.NET.Analysis.Exporters;
using CarrotBacktesting.NET.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Config;

[TestClass]
public class AnalysisConfigTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CarrotQuant_AnalysisConfigTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void FromYaml_StandardInlineAnalysisYaml_ParsesOptionsAndNodesCorrectly()
    {
        string yaml = @"
options:
  risk_free_rate: 0.03
  annual_trading_days: 250
  exit_timing_steps: 15
  signal_horizon_steps: 25
  benchmark_symbol: ""sh.000300""
  price_field_name: ""close""

analyzers:
  - type: ""PortfolioAnalyzer""
  - type: ""TradeAnalyzer""
  - type: ""CostAnalyzer""
    enabled: false

exporters:
  - type: ""ConsoleExporter""
    params:
      show_capital: false
      save_html: true
      html_output_path: ""output/summary.html""
  - type: ""JsonReportExporter""
    params:
      output_path: ""output/report.json""
      indented: false
";

        var config = AnalysisConfig.FromYaml(yaml, _tempDir);

        Assert.IsNotNull(config);
        Assert.AreEqual(0.03, config.Options.RiskFreeRate, 1e-6);
        Assert.AreEqual(250, config.Options.AnnualTradingDays);
        Assert.AreEqual(15, config.Options.ExitTimingSteps);
        Assert.AreEqual(25, config.Options.SignalHorizonSteps);
        Assert.AreEqual("sh.000300", config.Options.BenchmarkSymbol);
        Assert.AreEqual("close", config.Options.PriceFieldName);

        var opts = config.Options.ToOptions();
        Assert.AreEqual(0.03, opts.RiskFreeRate, 1e-6);
        Assert.AreEqual(250, opts.AnnualTradingDays);
        Assert.AreEqual("sh.000300", opts.BenchmarkSymbol);

        Assert.AreEqual(3, config.Analyzers.Count);
        Assert.AreEqual("PortfolioAnalyzer", config.Analyzers[0].Type);
        Assert.IsTrue(config.Analyzers[0].Enabled);
        Assert.AreEqual("CostAnalyzer", config.Analyzers[2].Type);
        Assert.IsFalse(config.Analyzers[2].Enabled);

        Assert.AreEqual(2, config.Exporters.Count);
        Assert.AreEqual("ConsoleExporter", config.Exporters[0].Type);
        Assert.AreEqual("JsonReportExporter", config.Exporters[1].Type);
    }

    [TestMethod]
    public void SessionConfig_WithInlineAnalysis_LoadsProperly()
    {
        string sessionYaml = @"
engine:
  mode: ""backtest""
  start_date: ""2025-01-01""
  end_date: ""2025-03-31""

broker:
  initial_cash: 500000.0

analysis:
  options:
    risk_free_rate: 0.025
    benchmark_symbol: ""sz.399300""
  analyzers:
    - type: ""portfolio""
    - type: ""trades""
  exporters:
    - type: ""console""
";

        var session = SessionConfig.FromYaml(sessionYaml, _tempDir);

        Assert.IsNotNull(session.Analysis);
        Assert.AreEqual(0.025, session.Analysis.Options.RiskFreeRate, 1e-6);
        Assert.AreEqual("sz.399300", session.Analysis.Options.BenchmarkSymbol);
        Assert.AreEqual(2, session.Analysis.Analyzers.Count);
        Assert.AreEqual(1, session.Analysis.Exporters.Count);
    }

    [TestMethod]
    public void SessionConfig_WithExternalAnalysisYaml_ResolvesAndLoadsPolymorphically()
    {
        // 1. 在子目录写入独立的 analysis.yaml
        string sharedDir = Path.Combine(_tempDir, "configs", "shared");
        Directory.CreateDirectory(sharedDir);
        string externalAnalysisFile = Path.Combine(sharedDir, "analysis.yaml");

        string externalContent = @"
options:
  risk_free_rate: 0.015
  benchmark_symbol: ""sh.000001""
analyzers:
  - type: ""portfolio""
";
        File.WriteAllText(externalAnalysisFile, externalContent);

        // 2. 主 session 引用相对路径
        string mainConfigDir = Path.Combine(_tempDir, "configs");
        string sessionYaml = @"
engine:
  mode: ""backtest""

broker:
  initial_cash: 1000000.0

analysis: ""shared/analysis.yaml""
";

        var session = SessionConfig.FromYaml(sessionYaml, mainConfigDir);

        Assert.IsNotNull(session.Analysis);
        Assert.AreEqual(0.015, session.Analysis.Options.RiskFreeRate, 1e-6);
        Assert.AreEqual("sh.000001", session.Analysis.Options.BenchmarkSymbol);
        Assert.AreEqual(1, session.Analysis.Analyzers.Count);
        Assert.AreEqual("portfolio", session.Analysis.Analyzers[0].Type);
    }

    [TestMethod]
    public void AnalysisEngineFactory_DefaultAndCustomAssembly_WorksAsExpected()
    {
        // 1. 缺省配置 -> 装配官方全部五大分析器
        var defaultEngine = AnalysisEngineFactory.Create(null);
        Assert.AreEqual(5, defaultEngine.Analyzers.Count);
        Assert.IsTrue(defaultEngine.Analyzers.Any(a => a is PortfolioAnalyzer));
        Assert.IsTrue(defaultEngine.Analyzers.Any(a => a is CapitalAnalyzer));
        Assert.IsTrue(defaultEngine.Analyzers.Any(a => a is TradeAnalyzer));
        Assert.IsTrue(defaultEngine.Analyzers.Any(a => a is CostAnalyzer));
        Assert.IsTrue(defaultEngine.Analyzers.Any(a => a is SignalAnalyzer));

        // 2. 定制配置 -> 仅装配开启的分析器
        var config = new AnalysisConfig
        {
            Analyzers = new List<AnalyzerNodeConfig>
            {
                new() { Type = "portfolio", Enabled = true },
                new() { Type = "trades", Enabled = true },
                new() { Type = "cost", Enabled = false }
            }
        };

        var customEngine = AnalysisEngineFactory.Create(config);
        Assert.AreEqual(2, customEngine.Analyzers.Count);
        Assert.IsTrue(customEngine.Analyzers.Any(a => a is PortfolioAnalyzer));
        Assert.IsTrue(customEngine.Analyzers.Any(a => a is TradeAnalyzer));
        Assert.IsFalse(customEngine.Analyzers.Any(a => a is CostAnalyzer));
    }

    [TestMethod]
    public void CreateExporters_AssemblesExportersAndNormalizesPaths()
    {
        var config = new AnalysisConfig
        {
            Exporters = new List<ExporterNodeConfig>
            {
                new()
                {
                    Type = "console",
                    Params = new Dictionary<string, object>
                    {
                        { "save_html", true },
                        { "html_output_path", "out/summary.html" }
                    }
                },
                new()
                {
                    Type = "json",
                    Params = new Dictionary<string, object>
                    {
                        { "output_path", "out/report.json" },
                        { "indented", false }
                    }
                }
            }
        };

        var exporters = config.CreateExporters(_tempDir);

        Assert.AreEqual(2, exporters.Count);
        Assert.IsInstanceOfType(exporters[0], typeof(ConsoleExporter));
        Assert.IsInstanceOfType(exporters[1], typeof(JsonReportExporter));

        var jsonExp = (JsonReportExporter)exporters[1];
        Assert.IsFalse(jsonExp.Indented);
        Assert.IsTrue(Path.IsPathRooted(jsonExp.OutputPath));
        Assert.IsTrue(jsonExp.OutputPath.EndsWith(Path.Combine("out", "report.json")));

        // 验证 AnalysisEngineFactory.CreateExporters 静态对等方法
        var directExporters = AnalysisEngineFactory.CreateExporters(config, _tempDir);
        Assert.AreEqual(2, directExporters.Count);
    }

    [TestMethod]
    public void EndToEnd_ConfigDrivenAnalysisAndExport_SavesReportFileSuccessfully()
    {
        string yaml = $@"
options:
  risk_free_rate: 0.02
analyzers:
  - type: ""portfolio""
  - type: ""trades""
exporters:
  - type: ""json""
    params:
      output_path: ""final_result.json""
";

        var config = AnalysisConfig.FromYaml(yaml, _tempDir);
        var engine = AnalysisEngineFactory.Create(config);

        // 构造最小交易事实并执行分析
        var t0 = new DateTime(2025, 1, 1);
        var equity = new List<EquityPoint>
        {
            new(t0, 100000.0, 100000.0, 0.0, 0.0, 0.0, 0.0),
            new(t0.AddDays(1), 102000.0, 102000.0, 0.0, 0.0, 0.0, 0.0)
        };
        var facts = new TradingResult(Array.Empty<SettlementRecord>(), Array.Empty<Trade>(), Array.Empty<PositionSnapshot>(), equity, Array.Empty<DailySnapshot>(), Array.Empty<SignalRecord>());

        var report = engine.Analyze(facts, config);

        Assert.IsNotNull(report.Portfolio);
        Assert.IsNotNull(report.Trades);
        Assert.IsNull(report.Capital); // 未启用 CapitalAnalyzer

        // 执行内聚导出门面：一键导出所有已配置渠道
        config.Export(report, _tempDir);

        string expectedJsonPath = Path.Combine(_tempDir, "final_result.json");
        Assert.IsTrue(File.Exists(expectedJsonPath), "JsonReportExporter 必须根据配置成功生成文件。");

        var restored = JsonReportExporter.Load(expectedJsonPath);
        Assert.IsNotNull(restored.Portfolio);
        Assert.AreEqual(report.Portfolio.TotalReturn, restored.Portfolio.TotalReturn, 1e-6);

        // 测试一站式 AnalyzeAndExport
        string oneStopPath = Path.Combine(_tempDir, "one_stop.json");
        var oneStopConfig = AnalysisConfig.FromYaml($@"
analyzers:
  - type: ""portfolio""
exporters:
  - type: ""json""
    params:
      output_path: ""one_stop.json""
", _tempDir);

        var oneStopReport = engine.AnalyzeAndExport(facts, oneStopConfig, baseDirectory: _tempDir);
        Assert.IsNotNull(oneStopReport);
        Assert.IsTrue(File.Exists(oneStopPath), "AnalyzeAndExport 必须一站式成功产出并导出报告。");
    }

    [TestMethod]
    public void AnalysisConfig_SerializationRoundTrip_SavesAndRestoresIdenticalConfig()
    {
        var original = new AnalysisConfig
        {
            Options = new AnalysisOptionsConfig
            {
                RiskFreeRate = 0.025,
                AnnualTradingDays = 245,
                BenchmarkSymbol = "sh.000300"
            },
            Analyzers = new List<AnalyzerNodeConfig>
            {
                new() { Type = "portfolio", Enabled = true },
                new() { Type = "cost", Enabled = false }
            },
            Exporters = new List<ExporterNodeConfig>
            {
                new()
                {
                    Type = "console",
                    Params = new Dictionary<string, object>
                    {
                        { "save_html", true },
                        { "html_output_path", "out/dash.html" }
                    }
                }
            }
        };

        string filePath = Path.Combine(_tempDir, "roundtrip.yaml");
        original.Save(filePath);

        Assert.IsTrue(File.Exists(filePath));

        var restored = AnalysisConfig.Load(filePath);
        Assert.IsNotNull(restored);
        Assert.AreEqual(original.Options.RiskFreeRate, restored.Options.RiskFreeRate, 1e-6);
        Assert.AreEqual(original.Options.AnnualTradingDays, restored.Options.AnnualTradingDays);
        Assert.AreEqual(original.Options.BenchmarkSymbol, restored.Options.BenchmarkSymbol);
        Assert.AreEqual(2, restored.Analyzers.Count);
        Assert.AreEqual("portfolio", restored.Analyzers[0].Type);
        Assert.IsTrue(restored.Analyzers[0].Enabled);
        Assert.AreEqual("cost", restored.Analyzers[1].Type);
        Assert.IsFalse(restored.Analyzers[1].Enabled);
        Assert.AreEqual(1, restored.Exporters.Count);
    }

    [TestMethod]
    public void ConfigPropertyInjector_MultiCaseAndRichTypes_InjectsSuccessfully()
    {
        var dummy = new TestTarget();
        var parameters = new Dictionary<string, object>
        {
            { "int_value", 42 },
            { "doubleValue", 3.14159 },
            { "BoolValue", true },
            { "mode", "Aggressive" },
            { "timeout", 10.5 },
            { "tags", new List<object> { "alpha", "trend" } },
            { "weights", new List<object> { 0.6, 0.4 } },
            { "scores", new List<object> { 100, 200 } }
        };

        ConfigPropertyInjector.Inject(dummy, parameters);

        Assert.AreEqual(42, dummy.IntValue);
        Assert.AreEqual(3.14159, dummy.DoubleValue, 1e-5);
        Assert.IsTrue(dummy.BoolValue);
        Assert.AreEqual(TestEnum.Aggressive, dummy.Mode);
        Assert.AreEqual(TimeSpan.FromSeconds(10.5), dummy.Timeout);
        Assert.IsNotNull(dummy.Tags);
        CollectionAssert.AreEqual(new[] { "alpha", "trend" }, dummy.Tags);
        Assert.IsNotNull(dummy.Weights);
        CollectionAssert.AreEqual(new[] { 0.6, 0.4 }, (System.Collections.ICollection)dummy.Weights);
        Assert.IsNotNull(dummy.Scores);
        CollectionAssert.AreEqual(new[] { 100, 200 }, (System.Collections.ICollection)dummy.Scores);
    }

    [TestMethod]
    public void ConfigPropertyInjector_UnknownOrPrivateProperty_ThrowsInformativeException()
    {
        var dummy = new TestTarget();

        // 1. 未知属性报错并提示可用属性列表
        var ex1 = Assert.ThrowsException<ArgumentException>(() =>
            ConfigPropertyInjector.Inject(dummy, new Dictionary<string, object> { { "non_existent_key", 123 } }));
        StringAssert.Contains(ex1.Message, "不存在可写入的公共属性 'non_existent_key'");
        StringAssert.Contains(ex1.Message, "可用属性:");

        // 2. private set 属性应被正确判定为不可写入
        var ex2 = Assert.ThrowsException<ArgumentException>(() =>
            ConfigPropertyInjector.Inject(dummy, new Dictionary<string, object> { { "read_only_prop", 999 } }));
        StringAssert.Contains(ex2.Message, "不存在可写入的公共属性 'read_only_prop'");
    }

    [TestMethod]
    public void AnalysisEngineFactory_InvalidAnalyzerConfiguration_ThrowsSafeExceptions()
    {
        // 缺少必需的 type 字段
        var configMissingType = new AnalysisConfig
        {
            Analyzers = new List<AnalyzerNodeConfig> { new() { Type = "" } }
        };
        Assert.ThrowsException<InvalidOperationException>(() => AnalysisEngineFactory.Create(configMissingType));

        // 无法解析的分析器类名
        var configUnknownType = new AnalysisConfig
        {
            Analyzers = new List<AnalyzerNodeConfig> { new() { Type = "NonExistentAnalyzerClass" } }
        };
        Assert.ThrowsException<TypeLoadException>(() => AnalysisEngineFactory.Create(configUnknownType));
    }

    [TestMethod]
    public void AnalysisConfigLoader_InvalidFilePathOrContent_ThrowsExpectedExceptions()
    {
        // 1. 空路径参数防御
        Assert.ThrowsException<ArgumentException>(() => AnalysisConfigLoader.Load(null!));
        Assert.ThrowsException<ArgumentException>(() => AnalysisConfigLoader.Load("   "));

        // 2. 文件不存在防御
        Assert.ThrowsException<FileNotFoundException>(() => AnalysisConfigLoader.Load("non_existent_config_file_999.yaml"));

        // 3. 空 YAML 内容防御
        Assert.ThrowsException<ArgumentException>(() => AnalysisConfigLoader.FromYaml(null!));
        Assert.ThrowsException<ArgumentException>(() => AnalysisConfigLoader.FromYaml("   "));
    }

    [TestMethod]
    public void AnalysisConfigLoader_WrappedInAnalysisRoot_ShouldDeserializeCorrectly()
    {
        string wrappedYaml = """
        analysis:
          options:
            benchmark_symbol: "sh.000300"
            risk_free_rate: 0.025
          analyzers:
            - type: "PortfolioAnalyzer"
          exporters:
            - type: "ConsoleExporter"
        """;

        var config = AnalysisConfigLoader.FromYaml(wrappedYaml, _tempDir);
        Assert.IsNotNull(config);
        Assert.AreEqual("sh.000300", config.Options.BenchmarkSymbol);
        Assert.AreEqual(0.025, config.Options.RiskFreeRate);
        Assert.AreEqual(1, config.Analyzers.Count);
        Assert.AreEqual("PortfolioAnalyzer", config.Analyzers[0].Type);
        Assert.AreEqual(1, config.Exporters.Count);
        Assert.AreEqual("ConsoleExporter", config.Exporters[0].Type);
    }

    [TestMethod]
    public void AnalysisEngineFactory_InvalidExporterConfiguration_ThrowsSafeExceptions()
    {
        // 1. 导出器节点缺少 type 字段
        var configMissingType = new AnalysisConfig
        {
            Exporters = new List<ExporterNodeConfig> { new() { Type = "" } }
        };
        Assert.ThrowsException<InvalidOperationException>(() => AnalysisEngineFactory.CreateExporters(configMissingType));

        // 2. 导出器类名无法解析
        var configUnknownType = new AnalysisConfig
        {
            Exporters = new List<ExporterNodeConfig> { new() { Type = "NonExistentExporterClass" } }
        };
        Assert.ThrowsException<TypeLoadException>(() => AnalysisEngineFactory.CreateExporters(configUnknownType));

        // 3. 目标类型未实现 IReportExporter
        var configNotExporter = new AnalysisConfig
        {
            Exporters = new List<ExporterNodeConfig> { new() { Type = typeof(string).FullName! } }
        };
        Assert.ThrowsException<InvalidOperationException>(() => AnalysisEngineFactory.CreateExporters(configNotExporter));
    }

    private enum TestEnum { Conservative, Aggressive }

    private class TestTarget
    {
        public int IntValue { get; set; }
        public double DoubleValue { get; set; }
        public bool BoolValue { get; set; }
        public TestEnum Mode { get; set; }
        public TimeSpan Timeout { get; set; }
        public string[]? Tags { get; set; }
        public List<double>? Weights { get; set; }
        public IReadOnlyList<int>? Scores { get; set; }
        public int ReadOnlyProp { get; private set; } = 100;
    }
}
