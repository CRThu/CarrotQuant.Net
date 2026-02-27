using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Utility.Serialization;
using System;
using System.IO;

namespace CarrotBacktesting.NET.Analysis.Exporters
{
    public class SignalExporter : IExporter
    {
        public string Name => nameof(SignalExporter);
        private string _fileName = "signals.json";

        public void Init(ExporterConfig config)
        {
            _fileName = config.File;
        }

        public void Export(AnalysisContext context)
        {
            var trades = context.BacktestResult.Trades;
            if (trades == null || trades.Count == 0)
            {
                Console.WriteLine("[SignalExporter] 没有交易数据可导出。");
                return;
            }

            string outputDir = context.Config.ResolvePath(context.Config.Out.Dir);
            string filePath = Path.Combine(outputDir, _fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            JsonSerializationHelper.SerializeToFile(trades, filePath);

            Console.WriteLine($"[SignalExporter] 原始信号已保存到: {Path.GetFullPath(filePath)}");
        }
    }
}
