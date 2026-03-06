using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Data
{
    public class HistoryStorageBuilder : IDataStorageBuilder
    {
        // 临时存储，Key: 股票代码, Value: (日期, Frame) 列表
        private readonly Dictionary<string, List<(DateTime, StockFrame)>> _tempData;
        private readonly HashSet<DateTime> _globalDates;

        public HistoryStorageBuilder(IEnumerable<string> symbols)
        {
            _tempData = new Dictionary<string, List<(DateTime, StockFrame)>>();
            _globalDates = new HashSet<DateTime>();

            foreach (string symbol in symbols)
            {
                _tempData[symbol] = new List<(DateTime, StockFrame)>();
            }
        }

        public void AddFrame(string symbol, string time, StockFrame frame)
        {
            var date = DateTime.Parse(time);
            _tempData[symbol].Add((date, frame));
            _globalDates.Add(date);
        }

        public IDataStorage Build()
        {
            var stockHistories = new Dictionary<string, StockHistory>();
            var globalTradeDates = _globalDates.OrderBy(d => d).ToList();

            foreach (var kvp in _tempData)
            {
                var stockCode = kvp.Key;
                // 按日期对每支股票的数据进行排序
                var sortedData = kvp.Value.OrderBy(d => d.Item1).ToList();

                var alignedDates = new List<DateTime>(globalTradeDates.Count);
                var alignedFrames = new List<StockFrame>(globalTradeDates.Count);

                int sourceIdx = 0;
                // 双指针对齐算法
                for (int i = 0; i < globalTradeDates.Count; i++)
                {
                    DateTime targetDate = globalTradeDates[i];
                    alignedDates.Add(targetDate);

                    if (sourceIdx < sortedData.Count && sortedData[sourceIdx].Item1 == targetDate)
                    {
                        // 匹配日期
                        alignedFrames.Add(sortedData[sourceIdx].Item2);
                        sourceIdx++;
                    }
                    else
                    {
                        // 缺失日期，填充停牌帧
                        alignedFrames.Add(new StockFrame(0, 0, 0, 0, 0, TradeStatus.Halted));
                    }
                }

                stockHistories[stockCode] = new StockHistory(stockCode, alignedDates, alignedFrames);
            }

            return new HistoryStorage(stockHistories, globalTradeDates);
        }
    }
}
