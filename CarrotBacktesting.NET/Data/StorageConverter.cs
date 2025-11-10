using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 提供数据存储格式之间转换的静态辅助方法。
    /// </summary>
    public static class StorageConverter
    {
        /// <summary>
        /// 将任意 IDataStorage 实现转换为引擎所需的纵向数据格式 (List<StockHistory>)。
        /// </summary>
        /// <param name="data">输入的数据存储对象。</param>
        /// <returns>一个包含所有股票时间序列的列表。</returns>
        public static List<StockHistory> ToStockHistories(IDataStorage data)
        {
            // 模式1: 如果数据已经是纵向的 (HistoryStorage)，直接返回，零开销。
            if (data is HistoryStorage hs)
            {
                Console.WriteLine("Data is already in TimeSeries mode. Conversion skipped.");
                return hs.StockHistories.Values.ToList();
            }

            // 模式2: 如果数据是横向的 (MarketStorage)，执行一次性转换。
            if (data is MarketStorage ms)
            {
                Console.WriteLine("Data is in MarketSnapshot mode. Converting to TimeSeries...");
                var stockHistories = new List<StockHistory>(ms.Symbols.Count);
                var globalDates = ms.TradeDates;

                // 使用并行转换以提高效率
                Parallel.ForEach(ms.Symbols, symbol =>
                {
                    int stockIndex = ms.SymbolsMap[symbol];
                    var dates = new List<DateTime>();
                    var frames = new List<StockFrame>();

                    for (int i = 0; i < globalDates.Count; i++)
                    {
                        if (ms.TryGetFrame(globalDates[i], out var frame) &&
                            stockIndex < frame.PrimaryData.Length &&
                            frame.PrimaryData[stockIndex].HasValue)
                        {
                            dates.Add(globalDates[i]);
                            frames.Add(frame.PrimaryData[stockIndex].Value);
                        }
                    }

                    // 使用 lock 确保线程安全地添加到共享列表中
                    lock (stockHistories)
                    {
                        stockHistories.Add(new StockHistory(symbol, dates, frames));
                    }
                });
                Console.WriteLine("Conversion completed.");
                return stockHistories;
            }

            // 如果是未知的 IDataStorage 实现，抛出异常
            throw new ArgumentException("Unsupported IDataStorage implementation for conversion.", nameof(data));
        }
    }
}
