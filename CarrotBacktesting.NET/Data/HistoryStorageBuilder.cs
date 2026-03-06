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
                StockFrame? lastValidFrame = null;
                // 双指针对齐算法 (v4.8 Forward Fill 优化)
                for (int i = 0; i < globalTradeDates.Count; i++)
                {
                    DateTime targetDate = globalTradeDates[i];
                    alignedDates.Add(targetDate);

                    if (sourceIdx < sortedData.Count && sortedData[sourceIdx].Item1 == targetDate)
                    {
                        // 匹配日期，更新最近有效帧
                        var frame = sortedData[sourceIdx].Item2;
                        alignedFrames.Add(frame);
                        lastValidFrame = frame;
                        sourceIdx++;
                    }
                    else
                    {
                        // 缺失日期/停牌，依据 Forward Fill 策略补全
                        if (lastValidFrame.HasValue)
                        {
                            double lastClose = lastValidFrame.Value.Close;
                            // 价格向前填充，成交量为 0，状态为 Halted
                            alignedFrames.Add(new StockFrame(lastClose, lastClose, lastClose, lastClose, 0, TradeStatus.Halted));
                        }
                        else
                        {
                            // 上市前无有效价格，填充零值
                            alignedFrames.Add(new StockFrame(0, 0, 0, 0, 0, TradeStatus.Halted));
                        }
                    }
                }

                stockHistories[stockCode] = new StockHistory(stockCode, alignedDates, alignedFrames);
            }

            return new HistoryStorage(stockHistories, globalTradeDates);
        }
    }
}
