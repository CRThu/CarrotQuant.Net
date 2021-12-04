using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotQuant.Net.Model
{
    public class CandleData
    {
        public string StockName { get; set; }
        public string StockCode { get; set; }
        public List<string> DateTime { get; set; } = new();
        public List<double[]> OHLC { get; set; } = new();
        public List<double?> MA5 { get; set; } = new();
        public List<double?> MA20 { get; set; } = new();

        public void Add(string dateTime, double open, double high, double low, double close)
        {
            DateTime.Add(dateTime);
            OHLC.Add(new[] { open, close, low, high });
            if (OHLC.Count < 5)
                MA5.Add(null);
            else
                MA5.Add(OHLC.ToArray()[^5..].Select(k => k[1]).Sum() / 5);
            if (OHLC.Count < 20)
                MA20.Add(null);
            else
                MA20.Add(OHLC.ToArray()[^20..].Select(k => k[1]).Sum() / 20);
        }
    }
}
