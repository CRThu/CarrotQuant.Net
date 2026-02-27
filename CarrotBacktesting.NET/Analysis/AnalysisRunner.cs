using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Result;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CarrotBacktesting.NET.Analysis
{
    public class AnalysisRunner
    {
        private readonly AnalysisContext _context;
        private readonly List<IAnalyzer> _analyzers = new();
        private readonly List<IExporter> _exporters = new();

        public AnalysisRunner(EnvConfig config, BacktestingResult result, IDataStorage data)
        {
            _context = new AnalysisContext(config, result, data);

            // 遍历配置中的分析器列表，动态实例化并初始化
            foreach (var analyzerConfig in config.Analysis.Analyzers)
            {
                var analyzer = CreateInstance<IAnalyzer>(analyzerConfig.Type);
                analyzer.Init(analyzerConfig);
                _analyzers.Add(analyzer);
            }

            // 遍历配置中的导出器列表，动态实例化并初始化
            foreach (var exporterConfig in config.Analysis.Exporters)
            {
                var exporter = CreateInstance<IExporter>(exporterConfig.Type);
                exporter.Init(exporterConfig);
                _exporters.Add(exporter);
            }
        }

        /// <summary>
        /// 动态创建实例的工厂方法
        /// </summary>
        private T CreateInstance<T>(string type) where T : class
        {
            var typeLower = type.ToLowerInvariant();
            var fullName = typeof(T).FullName;

            // 根据类型名称创建对应的实例
            object? instance = typeLower switch
            {
                "signalanalyzer" => fullName == typeof(IAnalyzer).FullName ? new Analyzers.SignalAnalyzer() : null,
                "tradeanalyzer" => fullName == typeof(IAnalyzer).FullName ? new Analyzers.TradeAnalyzer() : null,
                "consoleexporter" => fullName == typeof(IExporter).FullName ? new Exporters.ConsoleExporter() : null,
                "plotexporter" => fullName == typeof(IExporter).FullName ? new Exporters.PlotExporter() : null,
                "signalexporter" => fullName == typeof(IExporter).FullName ? new Exporters.SignalExporter() : null,
                "excelexporter" => fullName == typeof(IExporter).FullName ? new Exporters.ExcelExporter() : null,
                _ => null
            };

            if (instance is not T result)
            {
                throw new InvalidOperationException($"无法创建类型 '{type}' 的实例: 未知的{typeof(T).Name}类型");
            }

            return result;
        }

        public void Run()
        {
            Console.WriteLine("\n--- 开始回测分析 ---");

            // 依次执行所有分析器
            foreach (var analyzer in _analyzers)
            {
                Console.WriteLine($"[AnalysisRunner] 执行分析器: {analyzer.Name}");
                analyzer.Analyze(_context);
            }

            // 依次执行所有导出器
            foreach (var exporter in _exporters)
            {
                Console.WriteLine($"[AnalysisRunner] 执行导出器: {exporter.Name}");
                exporter.Export(_context);
            }
        }
    }
}
