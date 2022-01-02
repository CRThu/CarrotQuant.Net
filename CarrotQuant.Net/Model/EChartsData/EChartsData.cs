using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CarrotQuant.Net.Model.EChartsData
{
    public class EChartsData
    {
        // 图表标题(股票名称+股票代码)
        public string StockName { get; set; }
        public string StockCode { get; set; }

        // 主副图数量:主图,[+副图1,][+副图2]
        public int GridsCount { get; set; }

        // 图表集合
        public List<EChartsSeries> Series { get; set; }

        // 数据源集合
        public List<string> Dimension { get; set; }
        public List<dynamic[]> Data { get; set; }

        // 构造函数
        public EChartsData()
        {
            StockName = string.Empty;
            StockCode = string.Empty;
            GridsCount = 1;
            Series = new();
            Dimension = new();
            Data = new();
        }

        public EChartsData(string stockName, string stockCode, int gridsCount = 1) : this()
        {
            StockName = stockName;
            StockCode = stockCode;
            GridsCount = gridsCount;
        }

        // 添加Series
        public void AddSeries(string name, EChartSeriesType type, int gridIndex, string dataXColumnName, string dataYColumnName)
        {
            Series.Add(new EChartsSeries(name, type, gridIndex, dataXColumnName, dataYColumnName));
        }

        public void AddSeries(string name, EChartSeriesType type, int gridIndex, string dataXColumnName, string[] dataYColumnsName)
        {
            Series.Add(new EChartsSeries(name, type, gridIndex, dataXColumnName, dataYColumnsName));
        }

        // 添加Data
        public void AddData(string dimension, dynamic[] data)
        {
            Dimension.Add(dimension);
            Data.Add(data);
        }

        // 序列化Json
        public string ToJson()
        {
            var jsons = JsonSerializer.Serialize(this);
            return jsons;
        }
    }
}
