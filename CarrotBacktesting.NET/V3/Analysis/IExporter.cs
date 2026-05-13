using CarrotBacktesting.NET.Config.Model;
using System;

namespace CarrotBacktesting.NET.Analysis
{
    public interface IExporter
    {
        string Name { get; }

        /// <summary>
        /// 初始化导出器，接受配置参数
        /// </summary>
        void Init(ExporterConfig config);

        void Export(AnalysisContext context);
    }
}
