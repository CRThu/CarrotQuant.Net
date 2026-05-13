using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Data
{
    public class MarketStorageBuilder : IDataStorageBuilder
    {
        public Dictionary<string, int> SymbolsMap { get; set; } = new Dictionary<string, int>();

        public Dictionary<string, MarketFrameBuilder> Market { get; set; } = new Dictionary<string, MarketFrameBuilder>();

        public MarketStorageBuilder(IEnumerable<string> symbols)
        {
            foreach (string symbol in symbols)
            {
                SymbolsMap[symbol] = SymbolsMap.Count;
            }
        }

        public void AddFrame(string symbol, string time, StockFrame frame)
        {
            if (!Market.TryGetValue(time, out MarketFrameBuilder? marketFrame))
            {
                marketFrame = new MarketFrameBuilder(time, SymbolsMap.Count);
                Market[time] = marketFrame;
            }

            marketFrame.Add(SymbolsMap[symbol], frame);
        }

        public IDataStorage Build()
        {
            Dictionary<DateTime, MarketFrame> marketFrames = new Dictionary<DateTime, MarketFrame>();
            foreach (var kvp in Market)
            {
                marketFrames[kvp.Value.Time] = kvp.Value.Build();
            }
            return new MarketStorage(marketFrames, SymbolsMap);
        }
    }
}
