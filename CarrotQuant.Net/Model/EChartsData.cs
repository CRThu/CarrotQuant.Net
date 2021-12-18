using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        // 第几张图显示
        public List<int> LegendDataOnGridIndex { get; set; } = new();
        public List<string> LegendDataDisplayType { get; set; } = new();

        // Data映射数据维度列
        public List<int> LegendDataDimensionIndex { get; set; } = new();

        // Echarts数据源
        // 图表名称：股价,[技术指标名1],[技术指标名2],...
        public List<string> LegendData { get; set; } = new();
        // 数据维度：datetime,open,high,low,close,volume,[技术指标名1],[技术指标名2],...
        public List<string> Dimension { get; set; } = new();
        public List<dynamic[]> Data { get; set; } = new();

        // 技术指标dataset
        public EChartsData()
        {
            GridsCount = 1;
            LegendData = new() { "股价", "成交量" };
            LegendDataDimensionIndex = new() { 0, 5 };
            LegendDataOnGridIndex = new() { 0, 1 };
            LegendDataDisplayType = new() { "candlestick", "bar" };
            Dimension = new() { "datetime", "open", "high", "low", "close", "volume" };
        }
        public EChartsData(int[] MAx, int[] MAxOnGridIndex, int gridCount = 1) : this()
        {
            GridsCount = gridCount;
            LegendData.AddRange(MAx.Select(x => $"MA{x}"));
            Dimension.AddRange(MAx.Select(x => $"MA{x}"));
            LegendDataDimensionIndex.AddRange(Enumerable.Range(6, MAx.Length));
            LegendDataOnGridIndex.AddRange(MAxOnGridIndex);
            LegendDataDisplayType.AddRange(MAx.Select(x => "line"));
        }

        public void AddTick(string dateTime, double open, double high, double low, double close, double volume = 0)
        {
            dynamic[] tickData = new dynamic[Dimension.Count];
            tickData[0] = dateTime;
            tickData[1] = open;
            tickData[2] = high;
            tickData[3] = low;
            tickData[4] = close;
            tickData[5] = volume;

            // Add Indicators
            Data.Add(tickData);
        }

        public void calcTA()
        {
            int start;
            int count;
            double[] real = new double[Data.Count];
            double[] real2 = new double[Data.Count];
            TicTacTec.TA.Library.Core.MovingAverage(0, Data.Count - 1, Data.Select(d => (double)d[4]).ToArray(), 5, TicTacTec.TA.Library.Core.MAType.Sma, out start, out count, real);
            TicTacTec.TA.Library.Core.MovingAverage(0, Data.Count - 1, Data.Select(d => (double)d[4]).ToArray(), 20, TicTacTec.TA.Library.Core.MAType.Sma, out start, out count, real2);
            Debug.WriteLine("Data:");
            Debug.WriteLine(string.Join("\n", Data.Select(d => (double)d[4])));
            Debug.WriteLine("MA5:");
            Debug.WriteLine(string.Join("\n", real));
            Debug.WriteLine("MA20:");
            Debug.WriteLine(string.Join("\n", real2));
        }

        public string ToJson()
        {
            var jsons = JsonSerializer.Serialize(this);
            return jsons;
        }
    }
}
