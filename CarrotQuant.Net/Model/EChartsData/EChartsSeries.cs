using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotQuant.Net.Model.EChartsData
{
    public class EChartsSeries
    {
        // 图表名称
        public string Name { get; set; } = "";

        // 图表类型
        private string type = "";
        public string Type
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

        // 其他配置
        public Dictionary<string, dynamic> Options { get; set; } = new();

        // 坐标数据对应数据源维度
        public List<string> DataXColumnsName { get; set; } = new();
        public List<string> DataYColumnsName { get; set; } = new();

        // 生成每种图表默认配置
        public static Dictionary<string, dynamic> GenerateDefaultOptions(string type)
        {
            Dictionary<string, dynamic> options = new();
            switch (type)
            {
                case "candlestick":
                    options.Add("large", true);
                    break;
                case "line":
                    options.Add("large", true);
                    options.Add("linestyle", new Dictionary<string, dynamic> { { "width", 1 } });
                    options.Add("symbol", "none");
                    options.Add("sampling", "lttb");
                    break;
                case "bar":
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
