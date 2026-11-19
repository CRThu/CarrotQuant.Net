using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Indicators;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Analysis;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBacktesting.NET.Indicators;
using CarrotBacktesting.NET.Strategy;
using Spectre.Console;

namespace CarrotBacktesting.NET.Benchmark;

/// <summary>
/// CarrotQuant .NET 核心基准测试套件执行内核。
/// </summary>
public static class BenchmarkSuite
{
    private static bool _isJitWarmedUp;

    /// <summary>
    /// 全局 JIT 预热（JIT Warmup）：彻底消除首次执行时的 RyuJIT 动态编译与分层 PGO 抖动。
    /// 确保回测主时钟、切片读取、指标递推、两阶段撮合和策略生命周期全路径均完成原生机器码编译。
    /// </summary>
    public static void EnsureWarmup()
    {
        if (_isJitWarmedUp) return;

        var dummySource = new SyntheticMarketGenerator(symbolCount: 5, timeSteps: 240);
        var dummyConfig = new SessionConfig
        {
            Engine = new EngineConfig
            {
                Mode = "backtest",
                StartDate = dummySource.TradeDates[0],
                EndDate = dummySource.TradeDates[^1]
            },
            Broker = new BrokerConfig { InitialCash = 1000000.0 },
            Data = new DataConfig
            {
                Timeseries = new List<TimeseriesConfig>
                {
                    new()
                    {
                        TableId = "ashare.kline.1m",
                        Fields = new List<string> { "close", "open", "high", "low", "volume" }
                    }
                }
            }
        };

        using var dummyTs = new TimeseriesRegistry(dummySource, "", pageSize: 4096, enableCache: false);
        _ = dummyTs.GetMatrix<double>("close");
        _ = dummyTs.GetMatrix<double>("open");
        _ = dummyTs.GetMatrix<double>("high");
        _ = dummyTs.GetMatrix<double>("low");
        _ = dummyTs.GetMatrix<double>("volume");

        IStrategy[] warmupStrategies =
        {
            new PureEmptyBenchmarkStrategy(),
            new ReadPriceBenchmarkStrategy(),
            new SingleIndicatorBenchmarkStrategy(),
            new SingleTradeBenchmarkStrategy(),
            new DualMaBenchmarkStrategy()
        };

        foreach (var strat in warmupStrategies)
        {
            using var s = CreateBenchmarkSession(dummyConfig, strat, dummyTs);
            s.Run();
        }

        _isJitWarmedUp = true;
    }

    /// <summary>
    /// 压测会话定制装配辅助方法：规避对顶层通用门面 FromConfig 的反向污染，显式组合引擎与会话。
    /// </summary>
    private static BacktestSession CreateBenchmarkSession(
        SessionConfig config,
        IStrategy strategy,
        ITimeseriesRegistry ts,
        ITradingRecorder? recorder = null)
    {
        var pipeline = strategy is IStrategyPipeline p
            ? p
            : StrategyPipeline.Sequential().Add(strategy);

        var engine = BacktestingEngine.Create(
            config,
            timeseries: ts,
            pipeline: pipeline,
            recorder: recorder);

        return new BacktestSession(config, engine);
    }

    /// <summary>
    /// 基准 1：二维非托管连续矩阵内存访问吞吐测试（2000 万次读取）。
    /// </summary>
    public static unsafe void RunMatrixBenchmark()
    {
        Console.WriteLine("\n[1/4] 正在执行二维连续矩阵内存访问吞吐压测 (20,000,000 次读取)...");

        const int Rows = 4096;
        const int Cols = 5000;
        using var matrix = new ChunkMatrix2D<double>(width: Cols, pageSize: 4096, initialRowCount: Rows);

        var pageSpanInit = matrix.GetPageSpan(0, Rows);
        for (int r = 0; r < Rows; r++)
        {
            var span = pageSpanInit.Slice(r * Cols, Cols);
            for (int c = 0; c < Cols; c++) span[c] = (r + 1) * 100.0 + c;
        }

        const int rounds = 1;
        long totalReads = (long)Rows * Cols * rounds;

        // 1. 跳跃跨列随机访问 (受制于 DRAM 寻址延迟)
        var swRandom = Stopwatch.StartNew();
        double sumRandom = 0;
        for (int c = 0; c < Cols; c++)
        {
            for (int r = 0; r < Rows; r++) sumRandom += matrix[r, c];
        }
        swRandom.Stop();
        double randomPerSec = totalReads / swRandom.Elapsed.TotalSeconds;

        // 2. 连续截面切片访问 (L1/L2 Cache 命中)
        var swCache = Stopwatch.StartNew();
        double sumCache = 0;
        var pageSpanRead = matrix.GetReadOnlyPageSpan(0, Rows);
        for (int r = 0; r < Rows; r++)
        {
            var rowSpan = pageSpanRead.Slice(r * Cols, Cols);
            for (int c = 0; c < Cols; c++) sumCache += rowSpan[c];
        }
        swCache.Stop();
        double cachePerSec = totalReads / swCache.Elapsed.TotalSeconds;

        // 3. 非托管指针直通流式读取 (Cache 物理极限)
        var swPtr = Stopwatch.StartNew();
        double sumPtr = 0;
        var fullSpan = matrix.GetReadOnlyPageSpan(0, Rows);
        fixed (double* pBase = fullSpan)
        {
            double* pCur = pBase;
            double* pEnd = pBase + (Rows * Cols);
            while (pCur < pEnd) sumPtr += *pCur++;
        }
        swPtr.Stop();
        double ptrPerSec = totalReads / swPtr.Elapsed.TotalSeconds;

        Console.WriteLine($"""
        ---------------------------------------------------------------------------------------------
        【二维连续矩阵内存访问实测 (总计 {totalReads:N0} 次单元格读取)】
          - 模式 1 (跳跃跨列 / DRAM 寻址下限) : 耗时 {swRandom.ElapsedMilliseconds,6} ms | 吞吐: {randomPerSec:N0} 读取/秒
          - 模式 2 (连续截面 / L1/L2 Cache 命中): 耗时 {swCache.ElapsedMilliseconds,6} ms | 吞吐: {cachePerSec:N0} 读取/秒
          - 模式 3 (非托管裸指针 / Cache 极限) : 耗时 {swPtr.ElapsedMilliseconds,6} ms | 吞吐: {ptrPerSec:N0} 读取/秒 (约 {(1.0 / ptrPerSec) * 1e9:F2} ns/次)
        ---------------------------------------------------------------------------------------------
        """);
    }

    /// <summary>
    /// 基准 2：流式增量指标递推计算吞吐测试（500 万次指标更新）。
    /// </summary>
    public static void RunIndicatorsBenchmark()
    {
        Console.WriteLine("\n[2/4] 正在执行增量流式指标计算吞吐压测 (5,000,000 次指标更新)...");

        const int Points = 1_000_000;
        IStreamIndicator[] indicators =
        {
            new Sma(20),
            new Ema(20),
            new Macd(12, 26, 9),
            new BollingerBands(20, 2.0),
            new Rsi(14)
        };

        // 预热
        foreach (var ind in indicators) ind.Update(10.0, 10.5, 9.5, 10.0, 1000.0);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < Points; i++)
        {
            double p = 100.0 + Math.Sin(i * 0.05) * 10.0;
            double h = p + 0.5;
            double l = p - 0.5;
            double v = 1000.0 + (i % 100);

            for (int k = 0; k < indicators.Length; k++)
            {
                indicators[k].Update(p, h, l, p, v);
            }
        }
        sw.Stop();

        long totalCalculations = (long)Points * indicators.Length;
        double throughput = totalCalculations / sw.Elapsed.TotalSeconds;

        Console.WriteLine($"""
        ---------------------------------------------------------------------------------------------
        【流式增量指标池吞吐实测 (SMA, EMA, MACD, 布林带, RSI 共 5 大核心指标)】
          - 样本数据点数: {Points:N0} 点
          - 指标累计计算: {totalCalculations:N0} 次状态机递推
          - 计算总耗时  : {sw.ElapsedMilliseconds:N0} ms ({sw.Elapsed.TotalSeconds:F2} 秒)
          - 指标计算吞吐: {throughput:N0} 次指标递推/秒 ({throughput / 10000.0:F1} 万/秒)
        ---------------------------------------------------------------------------------------------
        """);
    }

    /// <summary>
    /// 基准 3：虚拟撮合与资金账本极限吞吐测试（100,000 笔交割流水）。
    /// </summary>
    public static void RunBrokerBenchmark()
    {
        Console.WriteLine("\n[3/4] 正在执行虚拟撮合引擎与两阶段资金账本吞吐压测 (100,000 笔连续报单)...");

        const int OrderCount = 100_000;
        var brokerConfig = new BrokerConfig
        {
            InitialCash = 100_000_000.0,
            FeeRate = 0.0003,
            Slippage = 0.0005,
            SettleDays = 0
        };

        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        // 预热 JIT
        broker.SubmitOrder(new OrderRequest("TEST", OrderDirection.Buy, OrderType.Market, 10.0, 10m));

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < OrderCount; i++)
        {
            double price = 10.0 + (i % 50) * 0.1;
            var direction = (i % 2 == 0) ? OrderDirection.Buy : OrderDirection.Sell;
            decimal volume = 100m;

            if (direction == OrderDirection.Sell && broker.GetAvailablePosition("TEST") < volume)
            {
                direction = OrderDirection.Buy;
            }

            broker.SubmitOrder(new OrderRequest("TEST", direction, OrderType.Market, price, volume));
        }
        sw.Stop();

        double ordersPerSec = OrderCount / sw.Elapsed.TotalSeconds;

        Console.WriteLine($"""
        ---------------------------------------------------------------------------------------------
        【虚拟撮合与两阶段资金预冻结/解冻吞吐实测】
          - 报单交割总量: {OrderCount:N0} 笔市价单 (两阶段冻结、滑点佣金结算、持仓均价更新)
          - 撮合总耗时  : {sw.ElapsedMilliseconds:N0} ms ({sw.Elapsed.TotalSeconds:F3} 秒)
          - 撮合成交吞吐: {ordersPerSec:N0} 笔交割/秒 ({ordersPerSec / 10000.0:F1} 万/秒)
        ---------------------------------------------------------------------------------------------
        """);
    }

    /// <summary>
    /// 基准测试度量结果数据结构。
    /// </summary>
    public sealed record BenchmarkResult(
        int Symbols,
        int TimeSteps,
        string Mode,
        double ElapsedMs,
        double StepsPerSec,
        double BarsPerSec,
        long TotalBars,
        int Trades,
        double MemoryDeltaMb);

    /// <summary>
    /// 基准 4：基于真实 BacktestSession 门面的端到端回测吞吐测试（自包含纯内存生成，零外部文件）。
    /// </summary>
    /// <param name="symbols">标的股票数量（默认 5000）</param>
    /// <param name="timeSteps">时间步数（默认 240 分钟步）</param>
    /// <param name="mode">模式：full（双均线+撮合）、empty（纯空跑）、read（纯遍历价格）</param>
    public static BenchmarkResult RunBacktestBenchmark(int symbols = 5000, int timeSteps = 240, string mode = "full")
    {
        EnsureWarmup();
        long totalDataPoints = (long)symbols * timeSteps;

        using var mockSource = new SyntheticMarketGenerator(symbols, timeSteps, isMinute: true);
        using var ts = new TimeseriesRegistry(mockSource, AppContext.BaseDirectory, pageSize: 4096, enableCache: false);

        var sessionConfig = new SessionConfig
        {
            Engine = new EngineConfig
            {
                Mode = "backtest",
                StartDate = mockSource.TradeDates[0],
                EndDate = mockSource.TradeDates[^1]
            },
            Broker = new BrokerConfig
            {
                InitialCash = 1_000_000_000.0,
                FeeRate = 0.0003,
                Slippage = 0.0002,
                SettleDays = 0
            }
        };

        IStrategy strategy = mode.ToLowerInvariant() switch
        {
            "empty" => new PureEmptyBenchmarkStrategy(),
            "read" => new ReadPriceBenchmarkStrategy(),
            _ => new DualMaBenchmarkStrategy()
        };

        var recorder = new TradingRecorder();
        using var session = CreateBenchmarkSession(sessionConfig, strategy, ts, recorder);

        long memBefore = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();

        session.Run();

        sw.Stop();
        long memAfter = GC.GetTotalMemory(false);

        double elapsedMs = sw.Elapsed.TotalMilliseconds;
        double stepsPerSec = timeSteps / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.001);
        double pointsPerSec = totalDataPoints / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.001);
        int trades = recorder.GetResult(Array.Empty<PositionSnapshot>()).Settlements.Count;
        double memDelta = (memAfter - memBefore) / 1024.0 / 1024.0;

        string modeDesc = mode.ToLowerInvariant() switch
        {
            "empty" => "纯空策略 (仅测会话推进与主时钟)",
            "read" => "纯行情遍历 (仅读取最新收盘价，无指标无撮合)",
            _ => "完整业务策略 (含双均线增量递推 + 金叉死叉撮合)"
        };

        Console.WriteLine($"""
        ---------------------------------------------------------------------------------------------
        【BacktestSession 实操压力测试 ({modeDesc})】
          * 标的数量 (Symbols)          : {symbols:N0} 只股票
          * 高频时间步 (Time Steps)     : {timeSteps:N0} 根分钟 Bar (单日 4 小时高频交易)
          * 全市场总 Bar 数 (Total Bars): {totalDataPoints:N0} 根数据点 (数据点 = 时间步 × 标的数)
          * 实际成交交割笔数 (Trades)   : {trades:N0} 笔撮合流水
          -------------------------------------------------------------------------------------------
          * 会话执行耗时                : {elapsedMs:F1} ms ({sw.Elapsed.TotalSeconds:F3} 秒)
          * 时间步推进速率              : {stepsPerSec:N0} 步/秒
          * 【全市场总 Bar 吞吐量】      : {pointsPerSec:N0} 总Bar/秒 ({pointsPerSec / 10000.0:F1} 万/秒)
          * 内存变动                    : {memDelta:F2} MB
        ---------------------------------------------------------------------------------------------
        """);

        return new BenchmarkResult(symbols, timeSteps, mode, elapsedMs, stepsPerSec, pointsPerSec, totalDataPoints, trades, memDelta);
    }

    /// <summary>
    /// 执行 3×3 矩阵实测：[5标的, 500标的, 5000标的] × [空策略, 读行情策略, 全量指标与撮合策略]。
    /// 全程严格由系统原生 BacktestSession 驱动。
    /// </summary>
    /// <param name="timeSteps">时间步数（默认 240 分钟步）</param>
    public static void RunMatrix3x3SessionBenchmark(int timeSteps = 240)
    {
        Console.WriteLine("""
        =============================================================================================
               CarrotQuant .NET 原生 BacktestSession 3×3 全景性能矩阵压测 (240 分钟高频步)
               标的维度: [5 标的, 500 标的, 5000 标的]
               策略维度: [空策略, 仅读行情策略, 带全量指标与撮合策略]
        =============================================================================================
        """);

        int[] symbolList = { 5, 500, 5000 };
        string[] modeList = { "empty", "read", "full" };

        var results = new List<BenchmarkResult>();

        foreach (int sym in symbolList)
        {
            foreach (string m in modeList)
            {
                var res = RunBacktestBenchmark(symbols: sym, timeSteps: timeSteps, mode: m);
                results.Add(res);
            }
        }

        // 输出 3×3 吞吐量矩阵表格 (万Bar/秒)
        Console.WriteLine("\n=============================================================================================");
        Console.WriteLine("                        【3×3 全景性能矩阵：全市场总 Bar 吞吐量 (万Bar/秒)】");
        Console.WriteLine("---------------------------------------------------------------------------------------------");
        Console.WriteLine($"| {"策略类型",-24} | {"5 标的",14} | {"500 标的",14} | {"5,000 标的",14} |");
        Console.WriteLine("|--------------------------|----------------|----------------|----------------|");

        foreach (string m in modeList)
        {
            string label = m switch
            {
                "empty" => "空策略 (Empty)",
                "read" => "仅读行情 (Read Only)",
                _ => "全量指标+撮合 (Full)"
            };

            var r5 = results.First(x => x.Symbols == 5 && x.Mode == m);
            var r500 = results.First(x => x.Symbols == 500 && x.Mode == m);
            var r5000 = results.First(x => x.Symbols == 5000 && x.Mode == m);

            Console.WriteLine($"| {label,-22} | {r5.BarsPerSec / 10000.0,11:F1} 万/s | {r500.BarsPerSec / 10000.0,11:F1} 万/s | {r5000.BarsPerSec / 10000.0,11:F1} 万/s |");
        }
        Console.WriteLine("---------------------------------------------------------------------------------------------");

        // 输出 3×3 耗时矩阵表格 (ms)
        Console.WriteLine("\n=============================================================================================");
        Console.WriteLine("                           【3×3 全景性能矩阵：会话总耗时 (毫秒 ms)】");
        Console.WriteLine("---------------------------------------------------------------------------------------------");
        Console.WriteLine($"| {"策略类型",-24} | {"5 标的",14} | {"500 标的",14} | {"5,000 标的",14} |");
        Console.WriteLine("|--------------------------|----------------|----------------|----------------|");

        foreach (string m in modeList)
        {
            string label = m switch
            {
                "empty" => "空策略 (Empty)",
                "read" => "仅读行情 (Read Only)",
                _ => "全量指标+撮合 (Full)"
            };

            var r5 = results.First(x => x.Symbols == 5 && x.Mode == m);
            var r500 = results.First(x => x.Symbols == 500 && x.Mode == m);
            var r5000 = results.First(x => x.Symbols == 5000 && x.Mode == m);

            Console.WriteLine($"| {label,-22} | {r5.ElapsedMs,11:F1} ms | {r500.ElapsedMs,11:F1} ms | {r5000.ElapsedMs,11:F1} ms |");
        }
        Console.WriteLine("---------------------------------------------------------------------------------------------");

        // 输出 9 组压测完整事实明细表
        Console.WriteLine("\n=============================================================================================");
        Console.WriteLine("                                 【9 组压测详细指标清单】");
        Console.WriteLine("---------------------------------------------------------------------------------------------");
        Console.WriteLine($"| {"标的数",6} | {"策略模式",-10} | {"总步数",6} | {"总Bar数",10} | {"耗时(ms)",9} | {"时间步/秒",9} | {"吞吐(Bar/s)",12} | {"成交笔数",8} |");
        Console.WriteLine("|--------|------------|--------|------------|-----------|-----------|--------------|----------|");

        foreach (var r in results)
        {
            Console.WriteLine($"| {r.Symbols,6} | {r.Mode,-10} | {r.TimeSteps,6} | {r.TotalBars,10:N0} | {r.ElapsedMs,9:F1} | {r.StepsPerSec,9:N0} | {r.BarsPerSec,12:N0} | {r.Trades,8:N0} |");
        }
        Console.WriteLine("=============================================================================================\n");
    }

    /// <summary>
    /// 执行 3×3×3 全景真实物理 Parquet 端到端会话矩阵压测。
    /// 维度 1: 数据生命周期 [冷启动 Cold, 温启动 Warm, 热启动 Hot]
    /// 维度 2: 标的股票规模 [5 标的, 500 标的, 5,000 标的]
    /// 维度 3: 策略业务负载 [空策略 Empty, 读行情 Read, 全量指标与撮合 Full]
    /// 数据源: 真实物理写盘的 Parquet 文件（Snappy 压缩，经由官方 ParquetTimeseriesSource 真实读取）。
    /// 回测门面: 100% 官方原生 BacktestSession。
    /// </summary>
    public static async Task RunMatrix3x3x3ParquetSessionBenchmark(int timeSteps = 240)
    {
        Console.WriteLine("""
        =============================================================================================
             CarrotQuant .NET 原生 BacktestSession 3×3×3 真实物理 Parquet 全景矩阵实测
             [维度 1] 数据生命周期 : [冷启动 Cold (无Cache), 温启动 Warm (磁盘Cache), 热启动 Hot (内存常驻)]
             [维度 2] 标的股票规模 : [5 标的, 500 标的, 5,000 标的]
             [维度 3] 策略业务负载 : [空策略 Empty, 读行情 Read, 全量指标+真实撮合 Full]
             [真实物理数据源]     : 本地临时目录真实写盘的 Snappy Parquet 物理文件与真实磁盘 I/O
        =============================================================================================
        """);

        int[] symbolCounts = { 5, 500, 5000 };
        string[] modes = { "empty", "read", "full" };

        string fixtureRoot = Path.Combine(Path.GetTempPath(), "CarrotBenchmark_3x3x3_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);

        try
        {
            EnsureWarmup();
            // 存储 27 组实测结果: Key = (DataState, Symbols, Mode) -> BenchmarkResult
            var matrixResults = new Dictionary<(string State, int Symbols, string Mode), BenchmarkResult>();

            foreach (int symCount in symbolCounts)
            {
                Console.WriteLine($"\n>>> 正在准备 {symCount} 标的真实物理 Parquet 测试数据...");
                string symDir = Path.Combine(fixtureRoot, $"sym_{symCount}");
                Directory.CreateDirectory(symDir);

                var symbols = new List<string>(symCount);
                for (int i = 0; i < symCount; i++) symbols.Add("sh.6" + i.ToString("D5"));

                string tableId = "ashare.kline.1m";
                await RealParquetFixture.GenerateAsync(symDir, tableId, symbols, timeSteps);
                Console.WriteLine($"    [完成] {symCount} 标的物理 Parquet 文件写盘就绪 (共 {symCount * timeSteps:N0} 行物理数据)。");

                string cacheRoot = Path.Combine(symDir, ".cache_root");
                Directory.CreateDirectory(cacheRoot);

                var startDate = new DateTime(2025, 1, 2, 9, 30, 0);
                var endDate = startDate.AddMinutes(timeSteps - 1);

                // --- 1. 冷启动 (Cold): 首次从真实物理 Parquet 读取，并流式构建本地 LZ4 分块 Cache ---
                foreach (string mode in modes)
                {
                    // 确保每次冷启动前没有旧缓存
                    string coldCacheDir = Path.Combine(cacheRoot, $"cold_{mode}");
                    if (Directory.Exists(coldCacheDir)) Directory.Delete(coldCacheDir, true);
                    Directory.CreateDirectory(coldCacheDir);

                    // 使用官方底层驱动加载物理 Parquet
                    using var source = TimeseriesSourceFactory.Create(symDir, tableId, startDate, endDate);
                    using var tsCold = new TimeseriesRegistry(
                        source,
                        coldCacheDir,
                        pageSize: 4096,
                        enableCache: true,
                        tableId: tableId,
                        compression: "lz4");

                    var sessionConfig = new SessionConfig
                    {
                        Engine = new EngineConfig { Mode = "backtest", StartDate = startDate, EndDate = endDate },
                        Broker = new BrokerConfig { InitialCash = 1_000_000_000.0, FeeRate = 0.0003, Slippage = 0.0002 },
                        Data = new DataConfig
                        {
                            Timeseries = new List<TimeseriesConfig>
                            {
                                new()
                                {
                                    TableId = tableId,
                                    Fields = mode == "empty" ? null : new List<string> { "close", "open", "high", "low", "volume" }
                                }
                            }
                        }
                    };

                    IStrategy strategy = mode switch
                    {
                        "empty" => new PureEmptyBenchmarkStrategy(),
                        "read" => new ReadPriceBenchmarkStrategy(),
                        _ => new DualMaBenchmarkStrategy()
                    };

                    var recorder = new TradingRecorder();
                    using var session = CreateBenchmarkSession(sessionConfig, strategy, tsCold, recorder);

                    var sw = Stopwatch.StartNew();
                    session.Run();
                    sw.Stop();

                    double ms = sw.Elapsed.TotalMilliseconds;
                    double stepsSec = timeSteps / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.001);
                    long totalBars = (long)symCount * timeSteps;
                    double barsSec = totalBars / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.001);
                    int trades = recorder.GetResult(Array.Empty<PositionSnapshot>()).Settlements.Count;

                    matrixResults[("Cold", symCount, mode)] = new BenchmarkResult(symCount, timeSteps, mode, ms, stepsSec, barsSec, totalBars, trades, 0);
                    Console.WriteLine($"    [冷启动 (Cold)] 标的: {symCount,5} | 策略: {mode,-6} | 耗时: {ms,8:F1} ms | 吞吐: {barsSec / 10000.0,8:F1} 万Bar/s | 成交: {trades,6:N0} 笔");
                }

                // --- 2. 温启动 (Warm): 磁盘二进制 LZ4 Cache 已生成，从磁盘直接读取分块并解压注入内存 ---
                foreach (string mode in modes)
                {
                    string warmCacheDir = Path.Combine(cacheRoot, $"cold_{mode}"); // 复用刚才冷启动生成的本地 Cache

                    using var source = TimeseriesSourceFactory.Create(symDir, tableId, startDate, endDate);
                    using var tsWarm = new TimeseriesRegistry(
                        source,
                        warmCacheDir,
                        pageSize: 4096,
                        enableCache: true,
                        tableId: tableId,
                        compression: "lz4");

                    var sessionConfig = new SessionConfig
                    {
                        Engine = new EngineConfig { Mode = "backtest", StartDate = startDate, EndDate = endDate },
                        Broker = new BrokerConfig { InitialCash = 1_000_000_000.0, FeeRate = 0.0003, Slippage = 0.0002 },
                        Data = new DataConfig
                        {
                            Timeseries = new List<TimeseriesConfig>
                            {
                                new()
                                {
                                    TableId = tableId,
                                    Fields = mode == "empty" ? null : new List<string> { "close", "open", "high", "low", "volume" }
                                }
                            }
                        }
                    };

                    IStrategy strategy = mode switch
                    {
                        "empty" => new PureEmptyBenchmarkStrategy(),
                        "read" => new ReadPriceBenchmarkStrategy(),
                        _ => new DualMaBenchmarkStrategy()
                    };

                    var recorder = new TradingRecorder();
                    using var session = CreateBenchmarkSession(sessionConfig, strategy, tsWarm, recorder);

                    var sw = Stopwatch.StartNew();
                    session.Run();
                    sw.Stop();

                    double ms = sw.Elapsed.TotalMilliseconds;
                    double stepsSec = timeSteps / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.001);
                    long totalBars = (long)symCount * timeSteps;
                    double barsSec = totalBars / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.001);
                    int trades = recorder.GetResult(Array.Empty<PositionSnapshot>()).Settlements.Count;

                    matrixResults[("Warm", symCount, mode)] = new BenchmarkResult(symCount, timeSteps, mode, ms, stepsSec, barsSec, totalBars, trades, 0);
                    Console.WriteLine($"    [温启动 (Warm)] 标的: {symCount,5} | 策略: {mode,-6} | 耗时: {ms,8:F1} ms | 吞吐: {barsSec / 10000.0,8:F1} 万Bar/s | 成交: {trades,6:N0} 笔");
                }

                // --- 3. 热启动 (Hot): 内存矩阵全量常驻，零磁盘 I/O 纯内存会话回测 ---
                {
                    using var source = TimeseriesSourceFactory.Create(symDir, tableId, startDate, endDate);
                    using var tsHot = new TimeseriesRegistry(source, cacheRoot, pageSize: 4096, enableCache: false);

                    // 预热矩阵进常驻内存
                    _ = tsHot.GetMatrix<double>("close");
                    _ = tsHot.GetMatrix<double>("open");
                    _ = tsHot.GetMatrix<double>("high");
                    _ = tsHot.GetMatrix<double>("low");
                    _ = tsHot.GetMatrix<double>("volume");

                    foreach (string mode in modes)
                    {
                        var sessionConfig = new SessionConfig
                        {
                            Engine = new EngineConfig { Mode = "backtest", StartDate = startDate, EndDate = endDate },
                            Broker = new BrokerConfig { InitialCash = 1_000_000_000.0, FeeRate = 0.0003, Slippage = 0.0002 }
                        };

                        IStrategy strategy = mode switch
                        {
                            "empty" => new PureEmptyBenchmarkStrategy(),
                            "read" => new ReadPriceBenchmarkStrategy(),
                            _ => new DualMaBenchmarkStrategy()
                        };

                        var recorder = new TradingRecorder();
                        using var session = CreateBenchmarkSession(sessionConfig, strategy, tsHot, recorder);

                        var sw = Stopwatch.StartNew();
                        session.Run();
                        sw.Stop();

                        double ms = sw.Elapsed.TotalMilliseconds;
                        double stepsSec = timeSteps / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.001);
                        long totalBars = (long)symCount * timeSteps;
                        double barsSec = totalBars / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.001);
                        int trades = recorder.GetResult(Array.Empty<PositionSnapshot>()).Settlements.Count;

                        matrixResults[("Hot", symCount, mode)] = new BenchmarkResult(symCount, timeSteps, mode, ms, stepsSec, barsSec, totalBars, trades, 0);
                        Console.WriteLine($"    [热启动 (Hot) ] 标的: {symCount,5} | 策略: {mode,-6} | 耗时: {ms,8:F1} ms | 吞吐: {barsSec / 10000.0,8:F1} 万Bar/s | 成交: {trades,6:N0} 笔");
                    }
                }
            }

            // ========================= 打印 3×3×3 全景报表 =========================
            Console.WriteLine("\n\n=============================================================================================");
            Console.WriteLine("                CarrotQuant .NET 3×3×3 真实物理 Parquet 会话实测全景榜单 (27 组)");
            Console.WriteLine("=============================================================================================");

            string[] states = { "Cold", "Warm", "Hot" };
            foreach (string state in states)
            {
                string stateDesc = state switch
                {
                    "Cold" => "【1. 冷启动 (Cold)：无Cache，物理磁盘Parquet读取 + 构建LZ4 Cache + 回测】",
                    "Warm" => "【2. 温启动 (Warm)：命中磁盘LZ4分块Cache，直接反序列化进内存 + 回测】",
                    _ => "【3. 热启动 (Hot) ：内存矩阵全量常驻，零磁盘I/O纯内存执行】"
                };

                Console.WriteLine($"\n{stateDesc}");
                Console.WriteLine("---------------------------------------------------------------------------------------------");
                Console.WriteLine($"| {"策略业务类型",-24} | {"5 标的 (耗时 / 吞吐)",22} | {"500 标的 (耗时 / 吞吐)",24} | {"5,000 标的 (耗时 / 吞吐)",26} |");
                Console.WriteLine("|--------------------------|------------------------|--------------------------|----------------------------|");

                foreach (string m in modes)
                {
                    string mLabel = m switch
                    {
                        "empty" => "空策略 (Empty)",
                        "read" => "仅读行情 (Read Only)",
                        _ => "全量指标+撮合 (Full)"
                    };

                    var r5 = matrixResults[(state, 5, m)];
                    var r500 = matrixResults[(state, 500, m)];
                    var r5000 = matrixResults[(state, 5000, m)];

                    string cell5 = $"{r5.ElapsedMs,6:F1}ms / {r5.BarsPerSec / 10000.0,6:F1}万/s";
                    string cell500 = $"{r500.ElapsedMs,6:F1}ms / {r500.BarsPerSec / 10000.0,7:F1}万/s";
                    string cell5000 = $"{r5000.ElapsedMs,6:F1}ms / {r5000.BarsPerSec / 10000.0,7:F1}万/s";

                    Console.WriteLine($"| {mLabel,-22} | {cell5,22} | {cell500,24} | {cell5000,26} |");
                }
                Console.WriteLine("---------------------------------------------------------------------------------------------");
            }

            Console.WriteLine("""
            =============================================================================================
                                     3×3×3 真实物理 Parquet 实测全部顺利完成！
            =============================================================================================
            """);
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                try { Directory.Delete(fixtureRoot, true); } catch { }
            }
        }
    }

    /// <summary>
    /// 冷启动、温启动、热启动全链路性能对比测试（全程严格基于系统原生 BacktestSession）。
    /// - 冷启动 (Cold)：无本地 Cache，首次流式解析 + 构建落盘 LZ4 分块 Cache + 会话回测
    /// - 温启动 (Warm)：命中本地磁盘 LZ4 分块 Cache，快速读取反序列化载入内存 + 会话回测
    /// - 热启动 (Hot) ：内存矩阵已常驻（In-Memory），零磁盘 I/O 纯内存会话回测
    /// </summary>
    public static void RunColdWarmHotBenchmark(int timeSteps = 240)
    {
        Console.WriteLine("""
        =============================================================================================
               CarrotQuant .NET 原生 BacktestSession [冷启动 / 温启动 / 热启动] 全景对比
               标的跨度: [5 标的, 500 标的, 5,000 标的]
               业务负载: 完整全量指标计算 + 金叉死叉交易撮合 (Full 策略)
        =============================================================================================
        """);

        int[] symbolList = { 5, 500, 5000 };
        string baseTemp = Path.Combine(Path.GetTempPath(), "CarrotBenchmark_CWH_" + Guid.NewGuid().ToString("N"));

        try
        {
            EnsureWarmup();
            var coldResults = new Dictionary<int, (double ms, double barsPerSec)>();
            var warmResults = new Dictionary<int, (double ms, double barsPerSec)>();
            var hotResults = new Dictionary<int, (double ms, double barsPerSec)>();

            foreach (int sym in symbolList)
            {
                string symCacheDir = Path.Combine(baseTemp, $"sym_{sym}");
                if (Directory.Exists(symCacheDir)) Directory.Delete(symCacheDir, true);
                Directory.CreateDirectory(symCacheDir);

                long totalBars = (long)sym * timeSteps;

                // 1. 冷启动 (Cold): 首次生成 Cache
                {
                    using var source = new SyntheticMarketGenerator(sym, timeSteps, isMinute: true);
                    using var tsCold = new TimeseriesRegistry(
                        source, 
                        symCacheDir, 
                        pageSize: 4096, 
                        enableCache: true, 
                        tableId: "ashare.kline.1m", 
                        compression: "lz4");

                    var config = new SessionConfig
                    {
                        Engine = new EngineConfig
                        {
                            Mode = "backtest",
                            StartDate = source.TradeDates[0],
                            EndDate = source.TradeDates[^1]
                        },
                        Broker = new BrokerConfig { InitialCash = 1_000_000_000.0 }
                    };

                    var recorder = new TradingRecorder();
                    using var sessionCold = CreateBenchmarkSession(config, new DualMaBenchmarkStrategy(), tsCold, recorder);

                    var swCold = Stopwatch.StartNew();
                    sessionCold.Run();
                    swCold.Stop();

                    double ms = swCold.Elapsed.TotalMilliseconds;
                    double tps = totalBars / (swCold.Elapsed.TotalSeconds > 0 ? swCold.Elapsed.TotalSeconds : 0.001);
                    coldResults[sym] = (ms, tps);
                }

                // 2. 温启动 (Warm): 磁盘 Cache 已存在，新会话从磁盘 LZ4 加载
                {
                    using var source = new SyntheticMarketGenerator(sym, timeSteps, isMinute: true);
                    using var tsWarm = new TimeseriesRegistry(
                        source, 
                        symCacheDir, 
                        pageSize: 4096, 
                        enableCache: true, 
                        tableId: "ashare.kline.1m", 
                        compression: "lz4");

                    var config = new SessionConfig
                    {
                        Engine = new EngineConfig
                        {
                            Mode = "backtest",
                            StartDate = source.TradeDates[0],
                            EndDate = source.TradeDates[^1]
                        },
                        Broker = new BrokerConfig { InitialCash = 1_000_000_000.0 }
                    };

                    var recorder = new TradingRecorder();
                    using var sessionWarm = CreateBenchmarkSession(config, new DualMaBenchmarkStrategy(), tsWarm, recorder);

                    var swWarm = Stopwatch.StartNew();
                    sessionWarm.Run();
                    swWarm.Stop();

                    double ms = swWarm.Elapsed.TotalMilliseconds;
                    double tps = totalBars / (swWarm.Elapsed.TotalSeconds > 0 ? swWarm.Elapsed.TotalSeconds : 0.001);
                    warmResults[sym] = (ms, tps);
                }

                // 3. 热启动 (Hot): 内存 Matrix 常驻，零磁盘 I/O 纯内存在线回测
                {
                    using var source = new SyntheticMarketGenerator(sym, timeSteps, isMinute: true);
                    using var tsHot = new TimeseriesRegistry(
                        source, 
                        symCacheDir, 
                        pageSize: 4096, 
                        enableCache: false);

                    // 预热加载矩阵至内存
                    _ = tsHot.GetMatrix<double>("close");
                    _ = tsHot.GetMatrix<double>("open");
                    _ = tsHot.GetMatrix<double>("high");
                    _ = tsHot.GetMatrix<double>("low");
                    _ = tsHot.GetMatrix<double>("volume");

                    var config = new SessionConfig
                    {
                        Engine = new EngineConfig
                        {
                            Mode = "backtest",
                            StartDate = source.TradeDates[0],
                            EndDate = source.TradeDates[^1]
                        },
                        Broker = new BrokerConfig { InitialCash = 1_000_000_000.0 }
                    };

                    var recorder = new TradingRecorder();
                    using var sessionHot = CreateBenchmarkSession(config, new DualMaBenchmarkStrategy(), tsHot, recorder);

                    var swHot = Stopwatch.StartNew();
                    sessionHot.Run();
                    swHot.Stop();

                    double ms = swHot.Elapsed.TotalMilliseconds;
                    double tps = totalBars / (swHot.Elapsed.TotalSeconds > 0 ? swHot.Elapsed.TotalSeconds : 0.001);
                    hotResults[sym] = (ms, tps);
                }
            }

            // 打印冷温热对比报表
            Console.WriteLine("\n=============================================================================================");
            Console.WriteLine("                【冷启动 vs 温启动 vs 热启动：全市场总 Bar 吞吐量 (万Bar/秒)】");
            Console.WriteLine("---------------------------------------------------------------------------------------------");
            Console.WriteLine($"| {"启动状态",-22} | {"5 标的",14} | {"500 标的",14} | {"5,000 标的",14} |");
            Console.WriteLine("|------------------------|----------------|----------------|----------------|");
            Console.WriteLine($"| {"冷启动 (首次生成Cache)",-18} | {coldResults[5].barsPerSec / 10000.0,11:F1} 万/s | {coldResults[500].barsPerSec / 10000.0,11:F1} 万/s | {coldResults[5000].barsPerSec / 10000.0,11:F1} 万/s |");
            Console.WriteLine($"| {"温启动 (命中磁盘Cache)",-18} | {warmResults[5].barsPerSec / 10000.0,11:F1} 万/s | {warmResults[500].barsPerSec / 10000.0,11:F1} 万/s | {warmResults[5000].barsPerSec / 10000.0,11:F1} 万/s |");
            Console.WriteLine($"| {"热启动 (内存常驻零IO)",-18}  | {hotResults[5].barsPerSec / 10000.0,11:F1} 万/s | {hotResults[500].barsPerSec / 10000.0,11:F1} 万/s | {hotResults[5000].barsPerSec / 10000.0,11:F1} 万/s |");
            Console.WriteLine("---------------------------------------------------------------------------------------------");

            Console.WriteLine("\n=============================================================================================");
            Console.WriteLine("                   【冷启动 vs 温启动 vs 热启动：会话执行总耗时 (毫秒 ms)】");
            Console.WriteLine("---------------------------------------------------------------------------------------------");
            Console.WriteLine($"| {"启动状态",-22} | {"5 标的",14} | {"500 标的",14} | {"5,000 标的",14} |");
            Console.WriteLine("|------------------------|----------------|----------------|----------------|");
            Console.WriteLine($"| {"冷启动 (首次生成Cache)",-18} | {coldResults[5].ms,11:F1} ms | {coldResults[500].ms,11:F1} ms | {coldResults[5000].ms,11:F1} ms |");
            Console.WriteLine($"| {"温启动 (命中磁盘Cache)",-18} | {warmResults[5].ms,11:F1} ms | {warmResults[500].ms,11:F1} ms | {warmResults[5000].ms,11:F1} ms |");
            Console.WriteLine($"| {"热启动 (内存常驻零IO)",-18}  | {hotResults[5].ms,11:F1} ms | {hotResults[500].ms,11:F1} ms | {hotResults[5000].ms,11:F1} ms |");
            Console.WriteLine("=============================================================================================\n");
        }
        finally
        {
            if (Directory.Exists(baseTemp))
            {
                try { Directory.Delete(baseTemp, true); } catch { }
            }
        }
    }

    /// <summary>
    /// 一键执行全套基准测试并打印综合汇总。
    /// </summary>
    public static void RunAll()
    {
        Console.WriteLine("""
        =============================================================================================
                          CarrotQuant .NET 工业级量化引擎全景性能基准套件
                          (自包含合成行情，100% 内存驱动，零外部磁盘依赖)
        =============================================================================================
        """);

        RunMatrixBenchmark();
        RunIndicatorsBenchmark();
        RunBrokerBenchmark();
        RunBacktestBenchmark(symbols: 5000, timeSteps: 240, mode: "empty");
        RunBacktestBenchmark(symbols: 5000, timeSteps: 240, mode: "read");
        RunBacktestBenchmark(symbols: 5000, timeSteps: 240, mode: "full");

        Console.WriteLine("""
        =============================================================================================
                                     全景性能跑分测试全部顺利完成！
        =============================================================================================
        """);
    }

    /// <summary>
    /// 执行正交解耦的分层性能归因基准测试（5 标的 vs 5000 标的 × 7 大核心分层项）。
    /// </summary>
    public static async Task RunLayeredBenchmark(int[]? symbolScales = null, int steps = 240)
    {
        symbolScales ??= new[] { 5, 5000 };
        string baseTemp = Path.Combine(Path.GetTempPath(), $"carrot_layered_{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseTemp);

        AnsiConsole.MarkupLine("""
        [bold cyan]=============================================================================================[/]
        [bold yellow]            CarrotQuant .NET 分层性能归因基准测试 (Layered Benchmark)[/]
        [bold gray]            标的规模: 5 支 (微型极限) vs 5000 支 (全市场极限) | 回测时间步: 240 Steps[/]
        [bold cyan]=============================================================================================[/]
        """);

        // 存储各项测试在各个规模下的结果: (testIndex, scale) => (ms, throughput)
        var results = new Dictionary<(int testIdx, int scale), (double ms, string throughput)>();
        const string tableId = "ashare.kline.1m";

        try
        {
            // --- 0. 全局 JIT 预热（JIT Warmup）：彻底消除首次执行时的 RyuJIT 动态编译与分层 PGO 抖动 ---
            EnsureWarmup();

            foreach (int symCount in symbolScales)
            {
                AnsiConsole.MarkupLine($"\n[bold white]>>> 正在执行标的规模:[/] [bold yellow]{symCount}[/] [bold white]标的...[/]");
                string symDir = Path.Combine(baseTemp, $"sym_{symCount}");
                string cacheDir = Path.Combine(symDir, ".cache");

                // 1. 生成物理 Parquet 真实夹具
                var symbols = new List<string>(symCount);
                for (int i = 0; i < symCount; i++) symbols.Add("sh.6" + i.ToString("D5"));
                await RealParquetFixture.GenerateAsync(symDir, tableId, symbols, steps);
                var startDate = new DateTime(2025, 1, 2, 9, 30, 0);
                var endDate = startDate.AddMinutes(steps - 1);

                // --- 测试 1: 读取 Parquet (冷态 / 物理磁盘) ---
                {
                    using var source = TimeseriesSourceFactory.Create(symDir, tableId, startDate, endDate);
                    double[] dest = new double[steps];
                    var sw = Stopwatch.StartNew();
                    for (int i = 0; i < symCount; i++)
                    {
                        source.ReadSymbolSeries<double>(source.Symbols[i], "close", 0, steps, dest);
                    }
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    long totalBars = (long)symCount * steps;
                    double barsSec = totalBars / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.0001);
                    double mbSec = (totalBars * sizeof(double)) / (1024.0 * 1024.0 * (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.0001));
                    results[(1, symCount)] = (ms, $"{barsSec / 10000.0:F1} 万/s ({mbSec:F1} MB/s)");
                    AnsiConsole.MarkupLine($"  [green]1. 读取 Parquet (冷态)[/]: 耗时 [bold]{ms,7:F1} ms[/] | 吞吐: [bold]{barsSec / 10000.0,6:F1} 万/s[/] ({mbSec:F1} MB/s)");
                }

                // --- 测试 2: 读取 Cache (LZ4) 性能 ---
                {
                    // 先开启 Cache 加载一次生成本地缓存文件
                    using (var sourceWarm = TimeseriesSourceFactory.Create(symDir, tableId, startDate, endDate))
                    using (var tsGen = new TimeseriesRegistry(sourceWarm, cacheDir, pageSize: 4096, enableCache: true))
                    {
                        _ = tsGen.GetMatrix<double>("close");
                    }

                    // 再次读取命中 LZ4 本地分块缓存
                    using var source = TimeseriesSourceFactory.Create(symDir, tableId, startDate, endDate);
                    using var tsCache = new TimeseriesRegistry(source, cacheDir, pageSize: 4096, enableCache: true);
                    var sw = Stopwatch.StartNew();
                    _ = tsCache.GetMatrix<double>("close");
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    long totalBars = (long)symCount * steps;
                    double barsSec = totalBars / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.0001);
                    double mbSec = (totalBars * sizeof(double)) / (1024.0 * 1024.0 * (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.0001));
                    results[(2, symCount)] = (ms, $"{barsSec / 10000.0:F1} 万/s ({mbSec:F1} MB/s)");
                    AnsiConsole.MarkupLine($"  [green]2. 读取 Cache (LZ4)[/]:  耗时 [bold]{ms,7:F1} ms[/] | 吞吐: [bold]{barsSec / 10000.0,6:F1} 万/s[/] ({mbSec:F1} MB/s)");
                }

                // --- 准备热态环境 (消除磁盘与解压干扰，后续测试在纯内存中运行) ---
                using var sourceHot = TimeseriesSourceFactory.Create(symDir, tableId, startDate, endDate);
                using var tsHot = new TimeseriesRegistry(sourceHot, cacheDir, pageSize: 4096, enableCache: false);
                _ = tsHot.GetMatrix<double>("close");
                _ = tsHot.GetMatrix<double>("open");
                _ = tsHot.GetMatrix<double>("high");
                _ = tsHot.GetMatrix<double>("low");
                _ = tsHot.GetMatrix<double>("volume");

                var sessionConfig = new SessionConfig
                {
                    Engine = new EngineConfig { Mode = "backtest", StartDate = startDate, EndDate = endDate },
                    Broker = new BrokerConfig { InitialCash = 1_000_000_000.0, FeeRate = 0.0003, Slippage = 0.0002 },
                    Data = new DataConfig
                    {
                        Timeseries = new List<TimeseriesConfig>
                        {
                            new()
                            {
                                TableId = tableId,
                                Fields = new List<string> { "close", "open", "high", "low", "volume" }
                            }
                        }
                    }
                };

                // --- 测试 3: 空策略空循环 (纯时钟推进) ---
                {
                    using var session = CreateBenchmarkSession(sessionConfig, new PureEmptyBenchmarkStrategy(), tsHot);
                    var sw = Stopwatch.StartNew();
                    session.Run();
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    double stepsSec = steps / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.0001);
                    long totalBars = (long)symCount * steps;
                    double barsSec = totalBars / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.0001);
                    results[(3, symCount)] = (ms, $"{barsSec / 10000.0:F1} 万/s ({stepsSec / 10000.0:F1} 万步/s)");
                    AnsiConsole.MarkupLine($"  [green]3. 空策略空循环[/]:     耗时 [bold]{ms,7:F1} ms[/] | 调度上限: [bold]{barsSec / 10000.0,6:F1} 万Bar/s[/] ({stepsSec / 10000.0:F1} 万步/s)");
                }

                // --- 测试 4: 连续截面单读取性能 (按 0..N-1 连续行切片遍历) ---
                {
                    using var session = CreateBenchmarkSession(sessionConfig, new ReadPriceBenchmarkStrategy(), tsHot);
                    var sw = Stopwatch.StartNew();
                    session.Run();
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    long totalBars = (long)symCount * steps;
                    double barsSec = totalBars / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.0001);
                    results[(4, symCount)] = (ms, $"{barsSec / 10000.0:F1} 万Bar/s");
                    AnsiConsole.MarkupLine($"  [green]4. 连续截面单读取[/]:   耗时 [bold]{ms,7:F1} ms[/] | 吞吐: [bold]{barsSec / 10000.0,6:F1} 万Bar/s[/] (顺序切片)");
                }

                // --- 测试 5: 随机打乱单读取性能 (本时间步内随机索引点查) ---
                {
                    using var session = CreateBenchmarkSession(sessionConfig, new RandomReadPriceBenchmarkStrategy(), tsHot);
                    var sw = Stopwatch.StartNew();
                    session.Run();
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    long totalBars = (long)symCount * steps;
                    double barsSec = totalBars / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.0001);
                    results[(5, symCount)] = (ms, $"{barsSec / 10000.0:F1} 万Bar/s");
                    AnsiConsole.MarkupLine($"  [green]5. 随机打乱单读取[/]:   耗时 [bold]{ms,7:F1} ms[/] | 吞吐: [bold]{barsSec / 10000.0,6:F1} 万Bar/s[/] (打乱点查)");
                }

                // --- 测试 6: 单指标计算 (全市场 SMA20 增量递推) ---
                {
                    using var session = CreateBenchmarkSession(sessionConfig, new SingleIndicatorBenchmarkStrategy(), tsHot);
                    var sw = Stopwatch.StartNew();
                    session.Run();
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    long totalCalcs = (long)symCount * steps;
                    double calcsSec = totalCalcs / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.0001);
                    results[(6, symCount)] = (ms, $"{calcsSec / 10000.0:F1} 万次/s");
                    AnsiConsole.MarkupLine($"  [green]6. 单指标计算 (SMA)[/]: 耗时 [bold]{ms,7:F1} ms[/] | 算力: [bold]{calcsSec / 10000.0,6:F1} 万次/s[/]");
                }

                // --- 测试 7: 单买卖撮合性能 (资金冻结 + 撮合 + 成交回报) ---
                {
                    var recorder = new TradingRecorder();
                    using var session = CreateBenchmarkSession(sessionConfig, new SingleTradeBenchmarkStrategy(), tsHot, recorder);
                    var sw = Stopwatch.StartNew();
                    session.Run();
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    int trades = recorder.GetResult(Array.Empty<PositionSnapshot>()).Settlements.Count;
                    double ordersSec = trades / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.0001);
                    results[(7, symCount)] = (ms, $"{ordersSec / 10000.0:F1} 万笔/s ({trades} 笔)");
                    AnsiConsole.MarkupLine($"  [green]7. 单买卖撮合[/]:       耗时 [bold]{ms,7:F1} ms[/] | 撮合: [bold]{ordersSec / 10000.0,6:F1} 万笔/s[/] ({trades} 笔成交)");
                }

                // --- 测试 8: 典型全流程综合回测 (Dual SMA + 信号 + 挂单撮合 + 成交记账) ---
                {
                    var recorder = new TradingRecorder();
                    using var session = CreateBenchmarkSession(sessionConfig, new DualMaBenchmarkStrategy(), tsHot, recorder);
                    var sw = Stopwatch.StartNew();
                    session.Run();
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    long totalBars = (long)symCount * steps;
                    double barsSec = totalBars / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 0.0001);
                    int trades = recorder.GetResult(Array.Empty<PositionSnapshot>()).Settlements.Count;
                    results[(8, symCount)] = (ms, $"{barsSec / 10000.0:F1} 万Bar/s ({trades} 笔)");
                    AnsiConsole.MarkupLine($"  [green]8. 典型双SMA回测[/]:    耗时 [bold]{ms,7:F1} ms[/] | 综合: [bold]{barsSec / 10000.0,6:F1} 万Bar/s[/] ({trades} 笔成交)");
                }
            }

            // --- 打印 Spectre.Console 汇总表格 ---
            var table = new Table().Border(TableBorder.Rounded).Title("[bold yellow]CarrotQuant .NET 分层性能归因汇总[/]");
            table.AddColumn(new TableColumn("[bold yellow]测试分层[/]"));
            table.AddColumn(new TableColumn("[bold cyan]测试项目[/]"));
            table.AddColumn(new TableColumn("[bold green]5 标的耗时[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold green]5 标的吞吐[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold magenta]5000 标的耗时[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold magenta]5000 标的吞吐[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold white]性能特征与瓶颈归因[/]"));

            var metadataList = new[]
            {
                (1, "存储 I/O", "1. 读取 Parquet (冷态)", "磁盘 I/O + Snappy 解压 + 列存解析"),
                (2, "存储 I/O", "2. 读取 Cache (LZ4)", "本地二进制分块解压至堆外连续内存"),
                (3, "引擎调度", "3. 空策略空循环", "理论调度天花板：全市场时钟推进等效 Bar 吞吐"),
                (4, "内存访问", "4. 连续截面单读取", "ChunkMatrix2D 连续行切片 (Cache 命中)"),
                (5, "内存访问", "5. 随机打乱单读取", "本时刻随机打乱标的寻址 (Cache 穿透)"),
                (6, "算力计算", "6. 单指标计算 (SMA)", "流式增量数学递推 (倒数乘法优化)"),
                (7, "交易执行", "7. 单买卖撮合", "两阶段资金冻结 + 撮合引擎 + 成交记账"),
                (8, "全流程综合", "8. 典型双SMA回测", "行情+指标池+信号决策+交易风控全链路闭环")
            };

            foreach (var item in metadataList)
            {
                var r5 = results.GetValueOrDefault((item.Item1, 5), (0, "-"));
                var r5000 = results.GetValueOrDefault((item.Item1, 5000), (0, "-"));
                table.AddRow(
                    $"[yellow]{item.Item2}[/]",
                    $"[cyan]{item.Item3}[/]",
                    $"{r5.ms:F1} ms",
                    $"{r5.throughput}",
                    $"{r5000.ms:F1} ms",
                    $"{r5000.throughput}",
                    $"[gray]{item.Item4}[/]"
                );
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }
        finally
        {
            if (Directory.Exists(baseTemp))
            {
                try { Directory.Delete(baseTemp, true); } catch { }
            }
        }
    }
}
