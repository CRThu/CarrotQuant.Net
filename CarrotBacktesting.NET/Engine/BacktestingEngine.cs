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
            if (strategy is ITradeStrategy tradeStrategy)
            {
                return RunTrade(tradeStrategy);
            }
            else if (strategy is ISignalStrategy signalStrategy)
            {
                return RunSignal(signalStrategy);
            }
            else
            {
                throw new ArgumentException("未知的策略接口类型。", nameof(strategy));
            }
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
                    Parallel.ForEach(contexts, ctx =>
                    {
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
                    Parallel.ForEach(contexts, ctx =>
                    {
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
