using CarrotBacktesting.NET.Analysis.Analyzers;
using CarrotBacktesting.NET.Analysis.Presenters;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Result;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

            // 根据配置动态构建分析器和表现器列表
            if (config.Analysis.ForwardReturns.Enabled)
            {
                _analyzers.Add(new ForwardReturnsAnalyzer(config.Analysis.ForwardReturns));
                _analyzers.Add(new SummaryAnalyzer());
            }

            foreach (var exporterName in config.Analysis.Exporters)
            {
                switch (exporterName)
                {
                    case "console":
                        _exporters.Add(new ConsoleExporter());
                        break;
                    case "plot":
                        _exporters.Add(new PlotExporter());
                        break;
                }
            }
        }

        public void Run()
        {
            Console.WriteLine("\n--- 5. 开始回测分析 ---");
            // 依次执行所有分析器
            foreach (var analyzer in _analyzers)
            {
                analyzer.Analyze(_context);
            }

            // 依次执行所有表现器
            foreach (var exporter in _exporters)
            {
                exporter.Export(_context);
            }
        }
    }
}
