using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis
{
    /// <summary>
    /// 分析器通用接口
    /// </summary>
    public interface IAnalyzer
    {
        string Name { get; }

        /// <summary>
        /// 执行分析
        /// </summary>
        /// <param name="context">提供输入数据，并用于存储分析产出物</param>
        void Analyze(AnalysisContext context);
    }
}
