using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine.Model;
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
    /// 回测引擎，负责执行策略并生成信号
    /// </summary>
    public class BacktestingEngine
    {
        private readonly ISignalStrategy _strategy;
        private readonly EnvConfig _config;

        /// <summary>
        /// 引擎内部统一使用纵向的StockHistory列表进行计算
        /// </summary>
        private readonly List<StockHistory> _stockHistories;

        /// <summary>
        /// 构造回测引擎
        /// </summary>
        /// <param name="data">加载完毕的市场数据，可以是任意实现IDataStorage的类型</param>
        /// <param name="strategy">要执行的信号策略</param>
        public BacktestingEngine(IDataStorage data, ISignalStrategy strategy, EnvConfig config)
        {
            _strategy = strategy;
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
        /// 运行回测
        /// </summary>
        /// <returns>所有触发的信号列表</returns>
        public List<SignalInfo> Run()
        {
            Console.WriteLine($"回测开始，策略名: '{_strategy.Name}'。");
            var stopwatch = Stopwatch.StartNew();

            // 使用线程安全的集合来存储来自不同线程的结果
            var signals = new ConcurrentBag<SignalInfo>();

            // 引擎的核心计算逻辑总是基于高效的纵向数据(_stockHistories)
            Parallel.ForEach(_stockHistories, history =>
            {
                var context = new SignalStrategyContext(history);
                bool lastSignalState = false; // 用于生成脉冲信号

                // 开始循环
                for (int i = 0; i < history.Data.Count; i++)
                {
                    context.CurrentIndex = i;

                    // 调用策略判断当前是否触发
                    bool currentSignalState = _strategy.CheckSignal(context);

                    // 脉冲逻辑：当本次触发，且上次未触发时，才记录信号
                    if (currentSignalState && !lastSignalState)
                    {
                        signals.Add(new SignalInfo(history.StockCode, history.Dates[i]));
                    }

                    // 更新上次触发状态
                    lastSignalState = currentSignalState;
                }
            });

            stopwatch.Stop();
            Console.WriteLine($"回测结束，耗时: {stopwatch.Elapsed.TotalSeconds:F2} 秒。");

            // 排序结果
            var sortedSignals = signals.OrderBy(s => s.Date).ThenBy(s => s.StockCode).ToList();

            // 保存文件
            if (!string.IsNullOrWhiteSpace(_config.Out.Signal))
            {
                try
                {
                    string fileName = Path.Combine(_config.Runtime.ProjectDir, _config.Out.Signal);

                    JsonSerializationHelper.SerializeToFile(sortedSignals, fileName);
                    Console.WriteLine($"信号已自动保存到: {fileName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] 引擎保存信号文件失败: {ex.Message}");
                }
            }

            return sortedSignals;
        }
    }
}
