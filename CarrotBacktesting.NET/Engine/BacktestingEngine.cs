using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Result;
using CarrotBacktesting.NET.Strategy;
using CarrotBacktesting.NET.Utility.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Engine
{
    /// <summary>
    /// 交易模拟引擎，负责执行策略并生成完整的交易列表。
    /// </summary>
    public class BacktestingEngine
    {
        private readonly EnvConfig _config;

        /// <summary>
        /// 引擎内部统一使用纵向的StockHistory列表进行计算
        /// </summary>
        private readonly List<StockHistory> _stockHistories;
        private readonly IReadOnlyDictionary<string, StockHistory> _stockHistoryDict;

        /// <summary>
        /// 市场策略缓存 (日期 -> 市场决策结果)
        /// </summary>
        private readonly Dictionary<DateTime, MarketResult> _marketCache = new();

        /// <summary>
        /// 对齐后的个股历史数据字典 (v4.7 高性能缓存)
        /// </summary>
        public IReadOnlyDictionary<string, StockHistory> StockHistories => _stockHistoryDict;

        private readonly IDataStorage _data;

        /// <summary>
        /// 构造回测引擎
        /// </summary>
        /// <param name="data">加载完毕的市场数据，跨越多只股票</param>
        public BacktestingEngine(IDataStorage data, EnvConfig config)
        {
            _config = config;
            _data = data;

            Console.WriteLine("Initializing backtesting engine...");

            _stockHistories = StorageConverter.ToStockHistories(data);
            _stockHistoryDict = _stockHistories.ToDictionary(h => h.StockCode);

            Console.WriteLine("Engine ready.");
        }

        /// <summary>
        /// 运行交易模拟
        /// </summary>
        /// <returns>回测结果</returns>
        public BacktestingResult Run(IStrategy strategy)
        {
            // 1. 管线首站：如果是市场决策策略，执行市场计算并注入缓存
            if (strategy is IMarketStrategy marketStrategy)
            {
                PreScanMarket(marketStrategy);
            }

            // 2. 管线分支：根据策略核心职责执行对应的回测循环
            return strategy switch
            {
                ITradeStrategy tradeStrategy => RunTrade(tradeStrategy),
                ISignalStrategy signalStrategy => RunSignal(signalStrategy),
                IMarketStrategy => HandleMarketOnlyRun(),
                _ => throw new ArgumentException($"策略 '{strategy.Name}' 未实现任何支持的执行接口 (ITradeStrategy/ISignalStrategy)。")
            };
        }

        private BacktestingResult HandleMarketOnlyRun()
        {
            Console.WriteLine("Strategy is strictly Market-based. Pipeline execution finished.");
            return new BacktestingResult();
        }

        /// <summary>
        /// 预执行市场扫描，并缓存每日市场结果
        /// </summary>
        private void PreScanMarket(IMarketStrategy marketStrategy)
        {
            _marketCache.Clear();
            Console.WriteLine($"[Pipeline] 正在计算市场宏观环境: {marketStrategy.Name}");
            var stopwatch = Stopwatch.StartNew();

            foreach (var date in _data.TradeDates)
            {
                MarketFrame? frame = null;
                if (_data is MarketStorage storage)
                {
                    storage.TryGetFrame(date, out frame);
                }

                var context = new MarketStrategyContext(date, frame, _data);
                
                // 直接多态调用，无需任何反射
                var result = marketStrategy.CheckMarket(context);
                if (result != null)
                {
                    _marketCache[date] = result;
                }
            }
            stopwatch.Stop();
            Console.WriteLine($"[Pipeline] 市场决策缓存构建完成，处理日期: {_marketCache.Count}，耗时: {stopwatch.Elapsed.TotalSeconds:F3}s.");
        }

        /// <summary>
        /// 信号回测策略
        /// </summary>
        private BacktestingResult RunSignal(ISignalStrategy strategy)
        {
            Console.WriteLine($"信号回测开始，策略名: '{strategy.Name}'。");
            var stopwatch = Stopwatch.StartNew();
            var result = new BacktestingResult();
            var signals = new ConcurrentBag<Trade>();

            var contexts = _stockHistories.Select(h => new SignalStrategyContext(h)).ToList();
            int totalDays = _data.TradeDates.Count;

            if (_config.Data.Mode == StorageMode.TimeSeries)
            {
                // 纵向路径 (先股后天)
                Parallel.ForEach(contexts, ctx =>
                {
                    for (int i = 0; i < totalDays; i++)
                    {
                        DateTime date = ctx.Series.Dates[i];
                        _marketCache.TryGetValue(date, out var market);
                        ctx.Market = market;

                        if (market?.SkipAlpha == true) continue;

                        ExecuteSignalLogic(ctx, i, strategy, signals);
                    }
                });
            }
            else
            {
                // 横向路径 (先天后股)
                for (int i = 0; i < totalDays; i++)
                {
                    int dayIndex = i;
                    DateTime date = _data.TradeDates[dayIndex];
                    _marketCache.TryGetValue(date, out var market);

                    if (market?.SkipAlpha == true) continue;

                    Parallel.ForEach(contexts, ctx =>
                    {
                        ctx.Market = market;
                        ExecuteSignalLogic(ctx, dayIndex, strategy, signals);
                    });
                }
            }

            result.Trades.AddRange(signals.OrderBy(t => t.EntryDate).ThenBy(t => t.StockCode));
            stopwatch.Stop();
            Console.WriteLine($"信号生成结束，共产生 {result.Trades.Count} 个买入信号，耗时: {stopwatch.Elapsed.TotalSeconds:F3} 秒。");
            return result;
        }

        private void ExecuteSignalLogic(SignalStrategyContext ctx, int i, ISignalStrategy strategy, ConcurrentBag<Trade> signals)
        {
            ctx.CurrentIndex = i;
            if (ctx.GetFrame(0)?.Status == TradeStatus.Halted) return;

            // 调用策略判断当前是否触发
            var entryResult = strategy.CheckSignal(ctx);

            bool currentSignalState = (entryResult != null);
            if (currentSignalState && !ctx.LastSignalState)
            {
                double price = ctx.GetClose(0) ?? 0;
                if (price > 0)
                {
                    // 只有在脉冲点才记录信号
                    signals.Add(new Trade(ctx.Series.StockCode, entryResult!.Value.Group, entryResult!.Value.Reason, ctx.CurrentDate, price));
                }
            }

            // 更新上次触发状态
            ctx.LastSignalState = currentSignalState;
        }

        private BacktestingResult RunTrade(ITradeStrategy strategy)
        {
            Console.WriteLine($"交易回测开始，策略名: '{strategy.Name}'。");
            var stopwatch = Stopwatch.StartNew();
            var result = new BacktestingResult();
            var completedTrades = new ConcurrentBag<Trade>();

            var contexts = _stockHistories.Select(h => new SignalStrategyContext(h)).ToList();
            int totalDays = _data.TradeDates.Count;

            if (_config.Data.Mode == StorageMode.TimeSeries)
            {
                // 纵向路径 (先股后天)
                Parallel.ForEach(contexts, ctx =>
                {
                    for (int i = 0; i < totalDays; i++)
                    {
                        DateTime date = ctx.Series.Dates[i];
                        _marketCache.TryGetValue(date, out var market);
                        ctx.Market = market;

                        if (market?.SkipAlpha == true) continue;

                        ExecuteTradeLogic(ctx, i, strategy, completedTrades);
                    }
                    if (ctx.CurrentTrade != null) completedTrades.Add(ctx.CurrentTrade);
                });
            }
            else
            {
                // 横向路径 (先天后股)
                for (int i = 0; i < totalDays; i++)
                {
                    int dayIndex = i;
                    DateTime date = _data.TradeDates[dayIndex];
                    _marketCache.TryGetValue(date, out var market);

                    if (market?.SkipAlpha == true) continue;

                    Parallel.ForEach(contexts, ctx =>
                    {
                        ctx.Market = market;
                        ExecuteTradeLogic(ctx, dayIndex, strategy, completedTrades);
                    });
                }
                // 最后收割所有未平仓
                foreach (var ctx in contexts)
                {
                    if (ctx.CurrentTrade != null) completedTrades.Add(ctx.CurrentTrade);
                }
            }

            result.Trades.AddRange(completedTrades.OrderBy(t => t.EntryDate).ThenBy(t => t.StockCode));
            stopwatch.Stop();
            Console.WriteLine($"交易模拟结束，共产生 {result.Trades.Count} 笔交易，耗时: {stopwatch.Elapsed.TotalSeconds:F3} 秒。");
            return result;
        }

        private void ExecuteTradeLogic(SignalStrategyContext ctx, int i, ITradeStrategy strategy, ConcurrentBag<Trade> completedTrades)
        {
            ctx.CurrentIndex = i;
            if (ctx.GetFrame(0)?.Status == TradeStatus.Halted) return;

            double currentPrice = ctx.GetClose(0) ?? 0;
            if (currentPrice <= 0) return;

            if (ctx.CurrentTrade == null)
            {
                // 【空仓状态】
                SignalResult? entryResult = strategy.CheckEntry(ctx);
                if (entryResult != null)
                {
                    // 开仓
                    ctx.CurrentTrade = new Trade(ctx.Series.StockCode, entryResult.Value.Group, entryResult.Value.Reason, ctx.CurrentDate, currentPrice);
                }
            }
            else
            {
                // 【持仓状态】
                // 1. 更新持仓状态
                ctx.CurrentTrade.UpdateOnNewBar(ctx);
                // 2. 检查平仓信号
                SignalResult? exitResult = strategy.CheckExit(ctx, ctx.CurrentTrade);
                if (exitResult != null)
                {
                    ctx.CurrentTrade.Close(exitResult.Value.Group, exitResult.Value.Reason, ctx.CurrentDate, currentPrice);
                    completedTrades.Add(ctx.CurrentTrade);
                    ctx.CurrentTrade = null; // 恢复到空仓状态
                }
            }
        }
    }
}
