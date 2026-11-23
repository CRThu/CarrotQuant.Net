using System;
using System.IO;
using CarrotBacktesting.NET.CLI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.CLI;

/// <summary>
/// CLI 命令行工具单元测试。
/// 验证命令行解析、帮助信息、容错机制、显式/隐式 run 动词以及参数动态覆盖。
/// </summary>
[TestClass]
public class CliRunnerTests
{
    private string _csvRoot = "";

    [TestInitialize]
    public void Setup()
    {
        _csvRoot = Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "csv").Replace("\\", "/");
    }

    [TestMethod]
    public void Help_WithNoArgs_PrintsUsageAndReturnsSuccess()
    {
        using var swOut = new StringWriter();
        using var swErr = new StringWriter();

        int code = CliRunner.Run([], swOut, swErr);

        Assert.AreEqual(CliRunner.ExitSuccess, code);
        string output = swOut.ToString();
        Assert.IsTrue(output.Contains("CarrotQuant .NET 回测命令行工具"));
        Assert.IsTrue(output.Contains("用法:"));
        Assert.AreEqual(0, swErr.ToString().Length);
    }

    [TestMethod]
    [DataRow("-h")]
    [DataRow("--help")]
    [DataRow("help")]
    public void Help_WithHelpFlags_PrintsUsageAndReturnsSuccess(string helpFlag)
    {
        using var swOut = new StringWriter();
        using var swErr = new StringWriter();

        int code = CliRunner.Run([helpFlag], swOut, swErr);

        Assert.AreEqual(CliRunner.ExitSuccess, code);
        Assert.IsTrue(swOut.ToString().Contains("用法:"));
    }

    [TestMethod]
    public void Run_NonExistentConfigFile_ReturnsExitInvalidArgs()
    {
        using var swOut = new StringWriter();
        using var swErr = new StringWriter();

        int code = CliRunner.Run(["non_existent_config.yaml"], swOut, swErr);

        Assert.AreEqual(CliRunner.ExitInvalidArgs, code);
        Assert.IsTrue(swErr.ToString().Contains("指定的配置文件不存在"));
    }

    [TestMethod]
    public void Run_MissingConfigFileAfterRunSubcommand_ReturnsExitInvalidArgs()
    {
        using var swOut = new StringWriter();
        using var swErr = new StringWriter();

        int code = CliRunner.Run(["run"], swOut, swErr);

        Assert.AreEqual(CliRunner.ExitInvalidArgs, code);
        Assert.IsTrue(swErr.ToString().Contains("缺少回测配置文件路径"));
    }

    [TestMethod]
    public void Run_InvalidOverrideOptions_ReturnsExitInvalidArgs()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            using var swOut = new StringWriter();
            using var swErr = new StringWriter();

            // 1. 无效的 start-date 格式
            int code1 = CliRunner.Run([tempFile, "--start-date", "invalid_date"], swOut, swErr);
            Assert.AreEqual(CliRunner.ExitInvalidArgs, code1);
            Assert.IsTrue(swErr.ToString().Contains("缺少有效日期值"));

            // 2. 未知参数（包括已移除的 --cash）
            using var swErr2 = new StringWriter();
            int code2 = CliRunner.Run([tempFile, "--unknown-flag"], swOut, swErr2);
            Assert.AreEqual(CliRunner.ExitInvalidArgs, code2);
            Assert.IsTrue(swErr2.ToString().Contains("未知参数"));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [TestMethod]
    public void Run_ValidYaml_DirectPath_ExecutesSuccessfully()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "CarrotQuant_CliTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var (yamlPath, _) = CreateTestYamlAndStrategy(tempDir);

            using var swOut = new StringWriter();
            using var swErr = new StringWriter();

            // 直接传配置文件路径：carrot-backtest <config.yaml>
            int exitCode = CliRunner.Run([yamlPath, "--no-export"], swOut, swErr);

            Assert.AreEqual(CliRunner.ExitSuccess, exitCode, $"执行失败，错误信息: {swErr}");
            string output = swOut.ToString();
            Assert.IsTrue(output.Contains("回测执行与分析完成"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Run_ValidYaml_WithRunSubcommandAndOverrides_ExecutesSuccessfully()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "CarrotQuant_CliTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var (yamlPath, _) = CreateTestYamlAndStrategy(tempDir);

            using var swOut = new StringWriter();
            using var swErr = new StringWriter();

            // 显式 run 动词并动态覆盖起止时间
            string[] args =
            [
                "run",
                yamlPath,
                "--start-date", "2024-01-02",
                "--end-date", "2024-01-10",
                "--no-export"
            ];

            int exitCode = CliRunner.Run(args, swOut, swErr);

            Assert.AreEqual(CliRunner.ExitSuccess, exitCode, $"执行失败，错误信息: {swErr}");
            string output = swOut.ToString();
            Assert.IsTrue(output.Contains("覆盖起始日期: 2024-01-02"));
            Assert.IsTrue(output.Contains("覆盖结束日期: 2024-01-10"));
            Assert.IsTrue(output.Contains("回测执行与分析完成"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private (string YamlPath, string StrategyPath) CreateTestYamlAndStrategy(string targetDir)
    {
        string strategyCodePath = Path.Combine(targetDir, "CliTestStrategy.cs");
        File.WriteAllText(strategyCodePath, """
        using System;
        using System.Collections.Generic;
        using CarrotBacktesting.NET.Abstraction.Data;
        using CarrotBacktesting.NET.Abstraction.Engine;
        using CarrotBacktesting.NET.Abstraction.Strategy;

        namespace CustomCliStrategies
        {
            public class CliTestStrategy : IStrategy
            {
                public string Name => "CliTestStrategy";
                public int Lookback => 5;
                private bool _bought = false;

                public void OnStart(IEngineContext context) { }
                public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
                public void OnStop(IEngineContext context) { }

                public void OnUpdate(IEngineContext context)
                {
                    if (!_bought && context.Timeseries.Primary.Metadata.Symbols.Count > 0)
                    {
                        string symbol = context.Timeseries.Primary.Metadata.Symbols[0];
                        double close = context.GetClose(0);
                        if (close > 0.0 && !double.IsNaN(close))
                        {
                            context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Buy, close, 100m));
                            _bought = true;
                        }
                    }
                }
            }
        }
        """);

        string yamlPath = Path.Combine(targetDir, "cli_test_session.yaml");
        string strategyNormalized = strategyCodePath.Replace("\\", "/");

        File.WriteAllText(yamlPath, $"""
        engine:
          mode: backtest
          start_date: "2024-01-02 00:00:00"
          end_date: "2024-01-15 00:00:00"
        broker:
          initial_cash: 500000.0
          fee_rate: 0.0003
        data:
          root: "{_csvRoot}"
          timeseries:
            - table_id: ashare.kline.1d.raw.baostock
              fields:
                - close
                - volume
              cache: false
        pipeline:
          mode: sequential
          pipeline:
            - source: "{strategyNormalized}"
        analysis:
          analyzers:
            - type: TradeAnalyzer
        """);

        return (yamlPath, strategyCodePath);
    }

    [TestMethod]
    public void Program_Main_WithHelpFlag_ReturnsSuccessExitCode()
    {
        var programType = typeof(CliRunner).Assembly.GetType("CarrotBacktesting.NET.CLI.Program");
        Assert.IsNotNull(programType);
        var mainMethod = programType.GetMethod("Main", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(mainMethod);

        int exitCode = (int)mainMethod.Invoke(null, new object[] { new string[] { "--help" } })!;
        Assert.AreEqual(CliRunner.ExitSuccess, exitCode);
    }
}
