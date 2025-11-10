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

        /// <summary>
        /// 构造回测引擎
        /// </summary>
        /// <param name="data">加载完毕的市场数据，可以是任意实现IDataStorage的类型</param>
        public BacktestingEngine(IDataStorage data, EnvConfig config)
        {
            _config = config;

            Console.WriteLine("Initializing backtesting engine...");

            // --- 适配器模式 ---
            // 无论传入的是哪种数据结构，引擎都将其适配为内部需要的List<StockHistory>
            if (data is HistoryStorage hs)
            {
                // 模式1: 数据已经是纵向的(TimeSeries)，直接使用，零开销。
                Console.WriteLine("Data is in TimeSeries mode. Engine ready.");
                _stockHistories = hs.StockHistories.Values.ToList();
            }
            else if (data is MarketStorage ms)
            {
                // 模式2: 数据是横向的(MarketSnapshot)，在引擎初始化时进行一次性转换。
                // 这保证了后续策略计算的高性能，同时兼容了横向数据结构。
                Console.WriteLine("Data is in MarketSnapshot mode. Converting to TimeSeries for strategy calculation...");
                _stockHistories = new List<StockHistory>(ms.Symbols.Count);
                var globalDates = ms.TradeDates;

                // 并行转换以提高效率
                Parallel.ForEach(ms.Symbols, symbol =>
                {
                    int stockIndex = ms.SymbolsMap[symbol];
                    var dates = new List<DateTime>();
                    var frames = new List<StockFrame>();

                    // 遍历所有交易日，为当前股票提取数据
                    for (int i = 0; i < globalDates.Count; i++)
                    {
                        if (ms.TryGetFrame(globalDates[i], out var frame) &&
                            stockIndex < frame.PrimaryData.Length && // 安全检查
                            frame.PrimaryData[stockIndex].HasValue)
                        {
                            dates.Add(globalDates[i]);
                            frames.Add(frame.PrimaryData[stockIndex].Value);
                        }
                    }

                    // 线程安全地添加到列表中
                    lock (_stockHistories)
                    {
                        _stockHistories.Add(new StockHistory(symbol, dates, frames));
                    }
                });
                Console.WriteLine("Conversion completed. Engine ready.");
            }
            else
            {
                throw new ArgumentException("Unsupported IDataStorage implementation.", nameof(data));
            }
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


            Parallel.ForEach(_stockHistories, history =>
            {
                if (history.Data.Count == 0) return;
                var context = new SignalStrategyContext(history);
                bool lastSignalState = false;

                for (int i = 0; i < history.Data.Count; i++)
                {
                    context.CurrentIndex = i;

                    // 调用策略判断当前是否触发
                    string? entryReason = strategy.CheckSignal(context);

                    bool currentSignalState = (entryReason != null);
                    if (currentSignalState && !lastSignalState)
                    {
                        double price = context.GetClose(0) ?? 0;
                        if (price > 0)
                        {
                            // 只有在脉冲点才记录信号
                            signals.Add(new Trade(history.StockCode, entryReason!, context.CurrentDate, price));
                        }
                    }

                    // 更新上次触发状态
                    lastSignalState = currentSignalState;
                }
            });

            result.Trades.AddRange(signals.OrderBy(t => t.EntryDate));
            stopwatch.Stop();
            Console.WriteLine($"信号生成结束，共产生 {result.Trades.Count} 个买入信号，耗时: {stopwatch.Elapsed.TotalSeconds:F3} 秒。");
            return result;
        }

        private BacktestingResult RunTrade(ITradeStrategy strategy)
        {
            Console.WriteLine($"交易回测开始，策略名: '{strategy.Name}'。");
            var stopwatch = Stopwatch.StartNew();
            var result = new BacktestingResult();
            var completedTrades = new ConcurrentBag<Trade>();
            var openTrades = new ConcurrentBag<Trade>();

            // 引擎的核心计算逻辑总是基于高效的纵向数据(_stockHistories)
            Parallel.ForEach(_stockHistories, history =>
            {
                if (history.Data.Count == 0) return;

                Trade? currentTrade = null;
                var context = new SignalStrategyContext(history);

                // 开始循环
                for (int i = 0; i < history.Data.Count; i++)
                {
                    context.CurrentIndex = i;
                    double currentPrice = context.GetClose(0) ?? 0;
                    if (currentPrice <= 0) continue; // 价格无效，跳过

                    if (currentTrade == null)
                    {
                        // 【空仓状态】
                        string? entryReason = strategy.CheckEntry(context);
                        if (entryReason != null)
                        {
                            // 开仓
                            currentTrade = new Trade(history.StockCode, entryReason, context.CurrentDate, currentPrice);
                        }
                    }
                    else
                    {
                        // 【持仓状态】
                        // 1. 更新持仓状态
                        currentTrade.UpdateOnNewBar(context);
                        // 2. 检查平仓信号
                        string? exitReason = strategy.CheckExit(context, currentTrade);
                        if (exitReason != null)
                        {
                            currentTrade.Close(exitReason, context.CurrentDate, currentPrice);
                            completedTrades.Add(currentTrade);
                            currentTrade = null;// 恢复到空仓状态
                        }

                    }
                }
                if (currentTrade != null)
                {
                    openTrades.Add(currentTrade);
                }
            });

            result.Trades.AddRange(completedTrades.OrderBy(t => t.EntryDate));
            result.Trades.AddRange(openTrades.OrderBy(t => t.EntryDate));
            stopwatch.Stop();
            Console.WriteLine($"交易模拟结束，共产生 {result.Trades.Count} 笔交易 " +
                $"({completedTrades.Count} 已平仓, {openTrades.Count} 未平仓)，" +
                $"耗时: {stopwatch.Elapsed.TotalSeconds:F3} 秒。");
            return result;
        }
    }
}
