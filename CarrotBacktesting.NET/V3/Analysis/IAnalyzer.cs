using CarrotBacktesting.NET.Config.Model;
using System;

namespace CarrotBacktesting.NET.Analysis
{
    /// <summary>
    /// 分析器通用接口
    /// </summary>
    public interface IAnalyzer
    {
        string Name { get; }

        /// <summary>
        /// 初始化分析器，接受配置参数
        /// </summary>
        void Init(AnalyzerConfig config);

        /// <summary>
        /// 执行分析
        /// </summary>
        /// <param name="context">提供输入数据，并用于存储分析产出物</param>
        void Analyze(AnalysisContext context);
    }
}
