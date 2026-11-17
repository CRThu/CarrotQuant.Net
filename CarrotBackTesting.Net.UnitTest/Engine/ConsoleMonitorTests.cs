using System;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Engine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

[TestClass]
public class ConsoleMonitorTests
{
    [TestMethod]
    public void Log_LevelFiltering_ShouldRespectMinLevel()
    {
        var sw = new StringWriter();
        var monitor = new ConsoleMonitor(minLevel: LogLevel.Warning, showProgress: false, writer: sw);

        monitor.Log("This info should be filtered", LogLevel.Info);
        monitor.Log("This warning should appear", LogLevel.Warning);
        monitor.Log("This error should appear", LogLevel.Error);

        string output = sw.ToString();
        Assert.IsFalse(output.Contains("This info should be filtered"));
        Assert.IsTrue(output.Contains("This warning should appear"));
        Assert.IsTrue(output.Contains("This error should appear"));
        Assert.IsTrue(output.Contains("WARNING"));
        Assert.IsTrue(output.Contains("ERROR"));
    }

    [TestMethod]
    public void Alert_ShouldOutputAlertPrefix()
    {
        var sw = new StringWriter();
        var monitor = new ConsoleMonitor(writer: sw);

        monitor.Alert("Risk limit breached!");

        string output = sw.ToString();
        Assert.IsTrue(output.Contains("[ALERT] Risk limit breached!"));
    }

    [TestMethod]
    public void ReportMetric_ShouldLogMetric()
    {
        var sw = new StringWriter();
        var monitor = new ConsoleMonitor(minLevel: LogLevel.Info, writer: sw);

        monitor.ReportMetric("BarsPerSecond", 2500000.5);

        string output = sw.ToString();
        Assert.IsTrue(output.Contains("Metric [BarsPerSecond] = 2500000.5000"));
    }

    [TestMethod]
    public void ReportProgress_Throttled_TerminalShouldAlwaysPrint()
    {
        var sw = new StringWriter();
        var monitor = new ConsoleMonitor(showProgress: true, writer: sw);

        // 中间非掩码非超时步进应被节流
        monitor.ReportProgress(1, 1000, new DateTime(2024, 1, 1), 100000);

        // 终止步进必须打印并输出换行
        monitor.ReportProgress(1000, 1000, new DateTime(2024, 1, 1), 100000);

        string output = sw.ToString();
        Assert.IsTrue(output.Contains("[Progress: 100.0%]"));
        Assert.IsTrue(output.Contains("Step: 1000/1000"));
    }

    [TestMethod]
    public void IProgress_Report_ShouldDelegateToReportProgress()
    {
        var sw = new StringWriter();
        IProgress<EngineProgress> monitor = new ConsoleMonitor(showProgress: true, writer: sw);

        monitor.Report(new EngineProgress(1000, 1000, new DateTime(2024, 1, 1), TimeSpan.FromSeconds(1)));

        string output = sw.ToString();
        Assert.IsTrue(output.Contains("[Progress: 100.0%]"));
        Assert.IsTrue(output.Contains("Step: 1000/1000"));
    }
}
