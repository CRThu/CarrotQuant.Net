using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CarrotQuant.Net.Model
{
    public class EChartsData
    {

        // 股票名称
        public string StockName { get; set; }
        public string StockCode { get; set; }

        // Echarts数据源
        public List<string> Dimension { get; set; } = new();
        public List<dynamic[]> Data { get; set; } = new();

        // 技术指标dataset
        public EChartsData()
        {
            Dimension = new() { "datetime", "open", "high", "low", "close" };
        }
        public EChartsData(int[] MAx) : this()
        {
            Dimension.AddRange(MAx.Select(x => $"MA{x}"));
        }

        public void Add(string dateTime, double open, double high, double low, double close)
        {
            dynamic[] tickData = new dynamic[Dimension.Count];
            tickData[0] = dateTime;
            tickData[1] = open;
            tickData[2] = high;
            tickData[3] = low;
            tickData[4] = close;

            // Add Indicators
            int index;
            if ((index = Dimension.IndexOf("MA5")) != -1)
            {
                double? MA5;
                if (Data.Count < 5)
                    MA5 = null;
                else
                    MA5 = Data.ToArray()[^5..].Select(k => (double)k[4]).Sum() / 5;
                tickData[index] = MA5;
            }
            if ((index = Dimension.IndexOf("MA10")) != -1)
            {
                double? MA10;
                if (Data.Count < 10)
                    MA10 = null;
                else
                    MA10 = Data.ToArray()[^10..].Select(k => (double)k[4]).Sum() / 10;
                tickData[index] = MA10;
            }
            Data.Add(tickData);
        }

        public string ToJson()
        {
            var jsons = JsonSerializer.Serialize(this);
            return jsons;
        }
    }
}
