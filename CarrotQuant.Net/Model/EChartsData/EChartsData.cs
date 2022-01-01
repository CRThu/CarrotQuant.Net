using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CarrotQuant.Net.Model.EChartsData
{
    public class EChartsData
    {
        // 图表标题(股票名称+股票代码)
        public string StockName { get; set; } = "";
        public string StockCode { get; set; } = "";

        // 主副图数量:主图,[+副图1,][+副图2]
        public int GridsCount { get; set; } = 1;

        // 图表集合
        public List<EChartsSeries> Series { get; set; } = new();

        // 数据源集合
        public List<string> Dimension { get; set; } = new();
        public List<dynamic[]> Data { get; set; } = new();

        public string ToJson()
        {
            var jsons = JsonSerializer.Serialize(this);
            return jsons;
        }
    }
}
