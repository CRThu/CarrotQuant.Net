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
        public string Type { get; set; } = "";
        // 坐标数据对应数据源维度
        public List<string> DataXColumnsName { get; set; } = new();
        public List<string> DataYColumnsName { get; set; } = new();
        // 图表显示在主图/副图
        public int GridIndex { get; set; }
    }
}
