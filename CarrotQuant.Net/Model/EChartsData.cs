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

        // ECharts副图数量：1:仅主图,2:主图+副图,3:主图+2副图
        public int GridsCount { get; set; } = 1;

        // Echarts数据源
        // 图表名称：股价,[技术指标名1],[技术指标名2],...
        public List<string> LegendData { get; set; } = new();
        // 数据维度：datetime,open,high,low,close,[技术指标名1],[技术指标名2],...
        public List<string> Dimension { get; set; } = new();
        public List<dynamic[]> Data { get; set; } = new();

        // 技术指标dataset
        public EChartsData()
        {
            GridsCount = 1;
            LegendData = new() { "股价" };
            Dimension = new() { "datetime", "open", "high", "low", "close" };
        }
        public EChartsData(int[] MAx, int gridCount = 1) : this()
        {
            GridsCount = gridCount;
            LegendData.AddRange(MAx.Select(x => $"MA{x}"));
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
