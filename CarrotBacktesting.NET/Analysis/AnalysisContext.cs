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
    /// <summary>
    /// 在分析流程中传递上下文数据。
    /// 这是一个动态的容器，允许分析器将其产出物放入，供后续的分析器或表现器使用。
    /// </summary>
    public class AnalysisContext
    {
        public EnvConfig Config { get; }
        public BacktestingResult BacktestResult { get; }
        public IDataStorage Data { get; }

        /// <summary>
        /// 全局对齐的股票历史序列
        /// </summary>
        public IReadOnlyDictionary<string, StockHistory> StockHistories { get; }

        /// <summary>
        /// 存储各个分析器产出物的通用字典。
        /// Key: 产出物的类型名 (e.g., "ForwardReturnsResult")
        /// Value: 产出物实例
        /// </summary>
        private readonly Dictionary<string, object> _artifacts = new();

        /// <summary>
        /// 存储生成的物理文件路径，供后续 Exporter 消费
        /// </summary>
        private readonly Dictionary<string, string> _fileArtifacts = new();

        public AnalysisContext(EnvConfig config, BacktestingResult backtestResult, IDataStorage data, IReadOnlyDictionary<string, StockHistory> stockHistories)
        {
            Config = config;
            BacktestResult = backtestResult;
            Data = data;
            StockHistories = stockHistories;
        }

        /// <summary>
        /// 将一个分析产出物存入上下文。
        /// </summary>
        public void SetArtifact<T>(T artifact) where T : class
        {
            _artifacts[typeof(T).Name] = artifact;
        }

        /// <summary>
        /// 从上下文中获取一个指定类型的分析产出物。
        /// </summary>
        public T? GetArtifact<T>() where T : class
        {
            if (_artifacts.TryGetValue(typeof(T).Name, out var artifact))
            {
                return artifact as T;
            }
            return null;
        }

        /// <summary>
        /// 存储生成的物理文件路径
        /// </summary>
        public void SetFileArtifact(string key, string path)
        {
            _fileArtifacts[key] = path;
        }

        /// <summary>
        /// 获取已存储的文件路径
        /// </summary>
        public string? GetFileArtifact(string key)
        {
            return _fileArtifacts.TryGetValue(key, out var path) ? path : null;
        }
    }
}
