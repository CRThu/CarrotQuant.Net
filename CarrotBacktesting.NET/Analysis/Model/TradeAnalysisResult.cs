using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Model
{
    /// <summary>
    /// 交易分析的最终产出物容器。
    /// 封装了按组分类的所有交易报告。
    /// </summary>
    public class TradeAnalysisResult
    {
        /// <summary>
        /// 分组交易报告集合。
        /// Key: 分组名
        /// Value: 交易报告
        /// </summary>
        public Dictionary<string, TradeReport> Groups { get; } = new();

        public TradeReport this[string groupName] => Groups[groupName];

        public void Add(string group, TradeReport report)
        {
            Groups[group] = report;
        }
    }
}
