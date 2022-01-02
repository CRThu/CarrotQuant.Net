using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CarrotQuant.Net.Model.EChartsData
{
    public class EChartsSeries
    {
        // 图表名称
        public string Name { get; set; }

        // 图表类型
        private EChartSeriesType type;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public EChartSeriesType Type
        {
            get
            {
                return type;
            }
            set
            {
                type = value;
                Options = GenerateDefaultOptions(type);
            }
        }

        // 图表显示在主图/副图
        public int GridIndex { get; set; }

        // 其他配置(修改Type时Setter自动更新默认配置)
        public Dictionary<string, dynamic> Options { get; set; }

        // 坐标数据对应数据源维度
        public List<string> DataXColumnsName { get; set; }
        public List<string> DataYColumnsName { get; set; }

        // 构造函数
        public EChartsSeries()
        {
            Name = string.Empty;
            Type = EChartSeriesType.line;
            GridIndex = 0;
            // Options = new();
            DataXColumnsName = new();
            DataYColumnsName = new();
        }

        public EChartsSeries(string name, EChartSeriesType type, int gridIndex, string dataXColumnName, string dataYColumnName) : this()
        {
            Name = name;
            Type = type;
            GridIndex = gridIndex;
            DataXColumnsName = new() { dataXColumnName };
            DataYColumnsName = new() { dataYColumnName };
        }

        public EChartsSeries(string name, EChartSeriesType type, int gridIndex, string dataXColumnName, string[] dataYColumnsName) : this()
        {
            Name = name;
            Type = type;
            GridIndex = gridIndex;
            DataXColumnsName = new() { dataXColumnName };
            DataYColumnsName = new(dataYColumnsName);
        }

        // 生成每种图表默认配置
        public static Dictionary<string, dynamic> GenerateDefaultOptions(EChartSeriesType type)
        {
            Dictionary<string, dynamic> options = new();
            switch (type)
            {
                case EChartSeriesType.candlestick:
                    options.Add("large", true);
                    break;
                case EChartSeriesType.line:
                    options.Add("large", true);
                    options.Add("linestyle", new Dictionary<string, dynamic> { { "width", 1 } });
                    options.Add("symbol", "none");
                    options.Add("sampling", "lttb");
                    break;
                case EChartSeriesType.bar:
                    options.Add("large", true);
                    options.Add("sampling", "lttb");
                    break;
                default:
                    break;
            }
            return options;
        }
    }
}
