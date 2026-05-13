using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Model
{
    /// <summary>
    /// 信号分析的最终产出物容器。
    /// 封装了按组分类的所有信号报告。
    /// </summary>
    public class SignalAnalysisResult
    {
        /// <summary>
        /// 分组报告集合。
        /// Key: 分组名 (如 "Total", "MainBoard")
        /// Value: T+N 报告数组
        /// </summary>
        public Dictionary<string, SignalReport[]> Groups { get; } = new();

        /// <summary>
        /// 方便的索引器
        /// </summary>
        public SignalReport[] this[string groupName] => Groups[groupName];

        public void Add(string group, SignalReport[] reports)
        {
            Groups[group] = reports;
        }
    }
}
