using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Analysis;

[TestClass]
public class PortfolioStatisticsTests
{
    [TestMethod]
    public void Create_EmptyEquityCurve_ReturnsEmptyMetrics()
    {
        var options = new AnalysisOptions();
        var portfolio = PortfolioStatistics.Create(
            Array.Empty<EquityPoint>(),
            Array.Empty<DailySnapshot>(),
            options
        );
        var capital = CapitalStatistics.Create(
            Array.Empty<DailySnapshot>(),
            Array.Empty<EquityPoint>(),
            Array.Empty<SettlementRecord>(),
            options
        );

        Assert.AreEqual(0.0, portfolio.TotalReturn);
        Assert.AreEqual(0.0, portfolio.AnnualizedReturn);
        Assert.AreEqual(0.0, portfolio.AnnualizedVolatility);
        Assert.AreEqual(0.0, portfolio.MaxDrawdown);
        Assert.AreEqual(0.0, portfolio.SharpeRatio);
        Assert.AreEqual(0.0, capital.AveragePositionRatio);
        Assert.AreEqual(0.0, capital.AnnualizedTurnover);
    }

    [TestMethod]
    public void Create_DeterministicEquityCurve_ComputesExactReturnAndDrawdown()
    {
        var t0 = new DateTime(2024, 1, 1);
        var points = new List<EquityPoint>
        {
            new(t0, 100000.0, 50000.0, 0.0, 10000.0, 50000.0, 0.0),
            new(t0.AddDays(1), 120000.0, 60000.0, 0.0, 12000.0, 60000.0, 10000.0),
            new(t0.AddDays(2), 96000.0, 48000.0, 0.0, 9600.0, 48000.0, -14000.0),
            new(t0.AddDays(3), 108000.0, 54000.0, 0.0, 10800.0, 54000.0, -2000.0),
            new(t0.AddDays(4), 120000.0, 60000.0, 0.0, 12000.0, 60000.0, 10000.0),
            new(t0.AddDays(5), 150000.0, 75000.0, 0.0, 15000.0, 75000.0, 40000.0)
        };

        var options = new AnalysisOptions
        {
            RiskFreeRate = 0.0,
            AnnualTradingDays = 252
        };

        var portfolio = PortfolioStatistics.Create(
            points,
            Array.Empty<DailySnapshot>(),
            options
        );
        var capital = CapitalStatistics.Create(
            Array.Empty<DailySnapshot>(),
            points,
            Array.Empty<SettlementRecord>(),
            options
        );

        // 1. 总收益 50%
        Assert.AreEqual(0.50, portfolio.TotalReturn, 1e-4);

        // 2. 最大回撤 20%
        Assert.AreEqual(0.20, portfolio.MaxDrawdown, 1e-4);
        Assert.AreEqual(t0.AddDays(1), portfolio.MaxDrawdownPeakTime);
        Assert.AreEqual(t0.AddDays(2), portfolio.MaxDrawdownTroughTime);
        Assert.AreEqual(t0.AddDays(4), portfolio.MaxDrawdownRecoveryTime);
        Assert.AreEqual(3, portfolio.MaxDrawdownDurationSteps);

        // 3. 卡玛比率
        Assert.IsTrue(portfolio.CalmarRatio > 0.0);
        Assert.AreEqual(portfolio.AnnualizedReturn / 0.20, portfolio.CalmarRatio, 1e-4);

        // 4. 夏普与索提诺
        Assert.IsTrue(portfolio.SharpeRatio > 0.0);
        Assert.IsTrue(portfolio.SortinoRatio > 0.0);
        Assert.IsTrue(portfolio.AnnualizedVolatility > 0.0);
        Assert.IsTrue(portfolio.DownsideVolatility > 0.0);

        // 5. 资金与敞口统计
        Assert.AreEqual(0.50, capital.AveragePositionRatio, 1e-4);
        Assert.AreEqual(0.50, capital.MaxPositionRatio, 1e-4);
        Assert.AreEqual(0.50, capital.CashDrag, 1e-4);
        Assert.AreEqual(0.10, capital.AverageMarginRatio, 1e-4);
        Assert.AreEqual(0.10, capital.MaxMarginRatio, 1e-4);
    }

    [TestMethod]
    public void Create_WithSettlements_ComputesAnnualizedTurnover()
    {
        var t0 = new DateTime(2024, 1, 1);
        var points = new List<EquityPoint>
        {
            new(t0, 100000.0, 100000.0, 0.0, 0.0, 0.0, 0.0),
            new(t0.AddDays(365.25), 100000.0, 100000.0, 0.0, 0.0, 0.0, 0.0)
        };

        var settlements = new List<SettlementRecord>
        {
            new(t0.AddDays(10), "sh.600000", "ord-1", OrderDirection.Buy, 10.0, 10000m, 100000.0, 50.0, 0.0, 0.0, ""),
            new(t0.AddDays(20), "sh.600000", "ord-2", OrderDirection.Sell, 10.0, 10000m, 100000.0, 50.0, 0.0, 100000.0, "")
        };

        var options = new AnalysisOptions();
        var capital = CapitalStatistics.Create(
            Array.Empty<DailySnapshot>(),
            points,
            settlements,
            options
        );

        // 单向换手率 = 200,000 / 2 / 100,000 / 1.0 = 1.0x (100% 换手)
        Assert.AreEqual(1.0, capital.AnnualizedTurnover, 0.05);
    }

    [TestMethod]
    public void Create_WithBenchmarkReturns_ComputesAlphaBetaAndInfoRatio()
    {
        var t0 = new DateTime(2024, 1, 1);
        var snapshots = new List<DailySnapshot>
        {
            new(t0, 100.0, 100.0, 0.0, 0.0, 0.0, 0.0, Array.Empty<PositionSnapshot>()),
            new(t0.AddDays(1), 102.0, 102.0, 0.0, 0.0, 0.0, 0.0, Array.Empty<PositionSnapshot>()),
            new(t0.AddDays(2), 100.98, 100.98, 0.0, 0.0, 0.0, 0.0, Array.Empty<PositionSnapshot>()),
            new(t0.AddDays(3), 104.0094, 104.0094, 0.0, 0.0, 0.0, 0.0, Array.Empty<PositionSnapshot>())
        };

        var benchmarkReturns = new List<double> { 0.01, -0.005, 0.015 };

        var options = new AnalysisOptions { RiskFreeRate = 0.0 };
        var portfolio = PortfolioStatistics.Create(
            Array.Empty<EquityPoint>(),
            snapshots,
            options,
            benchmarkReturns
        );

        Assert.IsNotNull(portfolio.Beta);
        Assert.IsNotNull(portfolio.Alpha);
        Assert.IsNotNull(portfolio.InformationRatio);
        Assert.AreEqual(2.0, portfolio.Beta.Value, 1e-4);
        Assert.IsTrue(portfolio.InformationRatio.Value > 0.0);
    }

    [TestMethod]
    public void Create_MonotonicIncreasingEquity_HasZeroDrawdownAndZeroDuration()
    {
        var t0 = new DateTime(2024, 1, 1);
        var points = new List<EquityPoint>
        {
            new(t0, 100.0, 100.0, 0, 0, 0, 0),
            new(t0.AddDays(1), 110.0, 110.0, 0, 0, 0, 0),
            new(t0.AddDays(2), 120.0, 120.0, 0, 0, 0, 0),
            new(t0.AddDays(3), 130.0, 130.0, 0, 0, 0, 0)
        };

        var options = new AnalysisOptions();
        var portfolio = PortfolioStatistics.Create(points, Array.Empty<DailySnapshot>(), options);

        Assert.AreEqual(0.0, portfolio.MaxDrawdown);
        Assert.AreEqual(0, portfolio.MaxDrawdownDurationSteps);
    }

    [TestMethod]
    public void Create_FlatEquityCurve_ZeroVolatilityAndZeroRatios()
    {
        var t0 = new DateTime(2024, 1, 1);
        var points = new List<EquityPoint>
        {
            new(t0, 100.0, 100.0, 0, 0, 0, 0),
            new(t0.AddDays(1), 100.0, 100.0, 0, 0, 0, 0),
            new(t0.AddDays(2), 100.0, 100.0, 0, 0, 0, 0)
        };

        var options = new AnalysisOptions();
        var portfolio = PortfolioStatistics.Create(points, Array.Empty<DailySnapshot>(), options);

        Assert.AreEqual(0.0, portfolio.TotalReturn);
        Assert.AreEqual(0.0, portfolio.AnnualizedVolatility);
        Assert.AreEqual(0.0, portfolio.MaxDrawdown);
        Assert.AreEqual(0.0, portfolio.SharpeRatio);
        Assert.AreEqual(0, portfolio.MaxDrawdownDurationSteps);
    }

    [TestMethod]
    public void Create_SinglePointEquity_DoesNotThrow()
    {
        var points = new[] { new EquityPoint(DateTime.Now, 100.0, 100.0, 0, 0, 0, 0) };
        var options = new AnalysisOptions();
        var portfolio = PortfolioStatistics.Create(points, Array.Empty<DailySnapshot>(), options);

        Assert.AreEqual(0.0, portfolio.TotalReturn);
        Assert.AreEqual(0.0, portfolio.AnnualizedVolatility);
        Assert.AreEqual(0.0, portfolio.MaxDrawdown);
    }

    [TestMethod]
    public void Create_BankruptNegativeEquity_HandlesGracefully()
    {
        var t0 = new DateTime(2024, 1, 1);
        var points = new List<EquityPoint>
        {
            new(t0, 100.0, 100.0, 0, 0, 0, 0),
            new(t0.AddDays(1), 50.0, 50.0, 0, 0, 0, 0),
            new(t0.AddDays(2), -20.0, -20.0, 0, 0, 0, 0) // 穿仓破产
        };

        var options = new AnalysisOptions();
        var portfolio = PortfolioStatistics.Create(points, Array.Empty<DailySnapshot>(), options);
        var capital = CapitalStatistics.Create(Array.Empty<DailySnapshot>(), points, Array.Empty<SettlementRecord>(), options);

        // 最终净值 <= 0 时年化收益置为 -100%
        Assert.AreEqual(-1.0, portfolio.AnnualizedReturn);
        // 回撤率 (100 - (-20)) / 100 = 120%
        Assert.AreEqual(1.20, portfolio.MaxDrawdown, 1e-4);
        Assert.IsFalse(double.IsNaN(capital.CashDrag));
    }

    [TestMethod]
    public void Create_ComprehensiveVerification_AllPropertiesAsserted()
    {
        var t0 = new DateTime(2024, 1, 1);
        var r = new[]
        {
            -0.05, -0.04, -0.03, -0.02, -0.01,
             0.00,  0.01,  0.01,  0.01,  0.02,
             0.02,  0.02,  0.03,  0.03,  0.03,
             0.04,  0.04,  0.05,  0.05,  0.06
        };

        var equityCurve = new List<EquityPoint>
        {
            new(t0, 100000.0, 50000.0, 0.0, 10000.0, 50000.0, 0.0)
        };

        for (int i = 0; i < r.Length; i++)
        {
            double prevEq = equityCurve[^1].TotalEquity;
            double nextEq = prevEq * (1.0 + r[i]);
            equityCurve.Add(new EquityPoint(t0.AddDays(i + 1), nextEq, nextEq * 0.5, 0.0, nextEq * 0.1, nextEq * 0.5, 0.0));
        }

        var benchmarkReturns = new List<double>(r.Length);
        for (int i = 0; i < r.Length; i++)
        {
            benchmarkReturns.Add(r[i] * 0.5);
        }

        var options = new AnalysisOptions
        {
            RiskFreeRate = 0.02,
            AnnualTradingDays = 252
        };

        var portfolio = PortfolioStatistics.Create(
            equityCurve,
            Array.Empty<DailySnapshot>(),
            options,
            benchmarkReturns
        );

        // 1. TotalReturn & AnnualizedReturn
        Assert.AreEqual(0.29615, portfolio.TotalReturn, 1e-4);
        Assert.AreEqual(113.1215, portfolio.AnnualizedReturn, 1e-2);

        // 2. Volatilities
        Assert.AreEqual(0.4866, portfolio.AnnualizedVolatility, 1e-4);
        Assert.AreEqual(0.2638, portfolio.DownsideVolatility, 1e-4);

        // 3. Drawdown properties
        Assert.AreEqual(0.1417, portfolio.MaxDrawdown, 1e-4);
        Assert.AreEqual(t0, portfolio.MaxDrawdownPeakTime);
        Assert.AreEqual(t0.AddDays(5), portfolio.MaxDrawdownTroughTime);
        Assert.AreEqual(t0.AddDays(15), portfolio.MaxDrawdownRecoveryTime);
        Assert.AreEqual(15, portfolio.MaxDrawdownDurationSteps);

        // 4. Ratios (Sharpe, Sortino, Calmar)
        double expectedSharpe = (portfolio.AnnualizedReturn - options.RiskFreeRate) / portfolio.AnnualizedVolatility;
        double expectedSortino = (portfolio.AnnualizedReturn - options.RiskFreeRate) / portfolio.DownsideVolatility;
        double expectedCalmar = portfolio.AnnualizedReturn / portfolio.MaxDrawdown;
        Assert.AreEqual(expectedSharpe, portfolio.SharpeRatio, 1e-4);
        Assert.AreEqual(expectedSortino, portfolio.SortinoRatio, 1e-4);
        Assert.AreEqual(expectedCalmar, portfolio.CalmarRatio, 1e-4);

        // 5. VaR & CVaR
        Assert.AreEqual(0.04, portfolio.DailyVaR95, 1e-4);
        Assert.AreEqual(0.045, portfolio.DailyCVaR95, 1e-4);

        // 6. Benchmark metrics
        Assert.IsNotNull(portfolio.BenchmarkReturn);
        Assert.AreEqual(0.141487, portfolio.BenchmarkReturn.Value, 1e-4);
        Assert.IsNotNull(portfolio.Beta);
        Assert.AreEqual(2.0, portfolio.Beta.Value, 1e-4);
        Assert.IsNotNull(portfolio.Alpha);
        Assert.AreEqual(92.7237, portfolio.Alpha.Value, 1e-2);
        Assert.IsNotNull(portfolio.InformationRatio);
        Assert.AreEqual(422.9558, portfolio.InformationRatio.Value, 1e-1);

        // 7. MonthlyReturns
        Assert.IsNotNull(portfolio.MonthlyReturns);
        Assert.AreEqual(1, portfolio.MonthlyReturns.Count);
        Assert.AreEqual(2024, portfolio.MonthlyReturns[0].Year);
        Assert.AreEqual(1, portfolio.MonthlyReturns[0].Month);
        Assert.AreEqual(portfolio.TotalReturn, portfolio.MonthlyReturns[0].ReturnRate, 1e-4);
    }

    [TestMethod]
    public void Create_UnrecoveredDrawdown_RecoveryTimeIsNull_DurationCalculatedToEnd()
    {
        var t0 = new DateTime(2024, 1, 1);
        var points = new List<EquityPoint>
        {
            new(t0, 100000.0, 100000.0, 0, 0, 0, 0),
            new(t0.AddDays(1), 90000.0, 90000.0, 0, 0, 0, 0),
            new(t0.AddDays(2), 80000.0, 80000.0, 0, 0, 0, 0),
            new(t0.AddDays(3), 85000.0, 85000.0, 0, 0, 0, 0)
        };

        var portfolio = PortfolioStatistics.Create(points, Array.Empty<DailySnapshot>(), new AnalysisOptions());

        Assert.AreEqual(0.20, portfolio.MaxDrawdown, 1e-4);
        Assert.AreEqual(t0, portfolio.MaxDrawdownPeakTime);
        Assert.AreEqual(t0.AddDays(2), portfolio.MaxDrawdownTroughTime);
        Assert.IsNull(portfolio.MaxDrawdownRecoveryTime);
        // 未修复时，跨度计算从 Peak (0) 到结尾 (3): 3 - 0 = 3
        Assert.AreEqual(3, portfolio.MaxDrawdownDurationSteps);
    }

    [TestMethod]
    public void Create_MonthlyReturns_MultiMonthSnapshots_ComputesExactMonthlyReturns()
    {
        var snapshots = new List<DailySnapshot>
        {
            new(new DateTime(2024, 1, 1), 100000.0, 100000.0, 0, 0, 0, 0, Array.Empty<PositionSnapshot>()),
            new(new DateTime(2024, 1, 31), 110000.0, 110000.0, 0, 0, 0, 0, Array.Empty<PositionSnapshot>()),
            new(new DateTime(2024, 2, 15), 105000.0, 105000.0, 0, 0, 0, 0, Array.Empty<PositionSnapshot>()),
            new(new DateTime(2024, 2, 29), 99000.0, 99000.0, 0, 0, 0, 0, Array.Empty<PositionSnapshot>()),
            new(new DateTime(2024, 3, 31), 118800.0, 118800.0, 0, 0, 0, 0, Array.Empty<PositionSnapshot>())
        };

        var portfolio = PortfolioStatistics.Create(Array.Empty<EquityPoint>(), snapshots, new AnalysisOptions());

        Assert.AreEqual(3, portfolio.MonthlyReturns.Count);

        // 1 月: 110,000 / 100,000 - 1 = +10.0%
        Assert.AreEqual(2024, portfolio.MonthlyReturns[0].Year);
        Assert.AreEqual(1, portfolio.MonthlyReturns[0].Month);
        Assert.AreEqual(0.10, portfolio.MonthlyReturns[0].ReturnRate, 1e-4);

        // 2 月: 99,000 / 110,000 - 1 = -10.0%
        Assert.AreEqual(2024, portfolio.MonthlyReturns[1].Year);
        Assert.AreEqual(2, portfolio.MonthlyReturns[1].Month);
        Assert.AreEqual(-0.10, portfolio.MonthlyReturns[1].ReturnRate, 1e-4);

        // 3 月: 118,800 / 99,000 - 1 = +20.0%
        Assert.AreEqual(2024, portfolio.MonthlyReturns[2].Year);
        Assert.AreEqual(3, portfolio.MonthlyReturns[2].Month);
        Assert.AreEqual(0.20, portfolio.MonthlyReturns[2].ReturnRate, 1e-4);
    }

    [TestMethod]
    public void Create_NullDailySnapshots_FallbackGracefully()
    {
        var t0 = new DateTime(2024, 1, 1);
        var points = new List<EquityPoint>
        {
            new(t0, 100000.0, 100000.0, 0, 0, 0, 0),
            new(t0.AddDays(1), 105000.0, 105000.0, 0, 0, 0, 0)
        };

        // 传入 null 作为 dailySnapshots，验证不会触发 NullReferenceException 并降级使用 equityCurve
        var portfolio = PortfolioStatistics.Create(points, null!, new AnalysisOptions());

        Assert.AreEqual(0.05, portfolio.TotalReturn, 1e-4);
        Assert.AreEqual(1, portfolio.MonthlyReturns.Count);
        Assert.AreEqual(0.05, portfolio.MonthlyReturns[0].ReturnRate, 1e-4);
    }

    [TestMethod]
    public void Create_InvalidAnnualTradingDays_DoesNotDivideByZero()
    {
        var t0 = new DateTime(2024, 1, 1);
        var points = new List<EquityPoint>
        {
            new(t0, 100000.0, 100000.0, 0, 0, 0, 0),
            new(t0.AddDays(1), 105000.0, 105000.0, 0, 0, 0, 0)
        };

        var badOptions = new AnalysisOptions
        {
            AnnualTradingDays = 0 // 极端无效值，测试除以零防御
        };

        var portfolio = PortfolioStatistics.Create(points, Array.Empty<DailySnapshot>(), badOptions);
        var capital = CapitalStatistics.Create(Array.Empty<DailySnapshot>(), points, Array.Empty<SettlementRecord>(), badOptions);

        Assert.IsFalse(double.IsNaN(portfolio.AnnualizedReturn));
        Assert.IsFalse(double.IsInfinity(portfolio.AnnualizedReturn));
        Assert.IsFalse(double.IsNaN(portfolio.AnnualizedVolatility));
        Assert.IsFalse(double.IsNaN(capital.AnnualizedTurnover));
    }
}
