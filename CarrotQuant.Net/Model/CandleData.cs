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

        // 技术指标dataset
        public string[] IndicatorsName { get; set; } = Array.Empty<string>();
        public List<dynamic[]> IndicatorsData { get; set; } = new();

        public CandleData()
        {
            IndicatorsName = new[] { "DateTime", "MA5", "MA20" };
        }

        public void Add(string dateTime, double open, double high, double low, double close)
        {
            DateTime.Add(dateTime);
            OHLC.Add(new[] { open, close, low, high });

            // Add Indicators
            double? MA5;
            double? MA20;
            if (OHLC.Count < 5)
                MA5 = null;
            else
                MA5 = OHLC.ToArray()[^5..].Select(k => k[1]).Sum() / 5;
            if (OHLC.Count < 20)
                MA20 = null;
            else
                MA20 = OHLC.ToArray()[^20..].Select(k => k[1]).Sum() / 20;
            IndicatorsData.Add(new dynamic[] { dateTime, MA5, MA20 });
        }
    }
}
