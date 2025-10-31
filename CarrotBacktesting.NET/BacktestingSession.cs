using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.DataFeed;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Model;
using CarrotBacktesting.NET.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET
{
    /// <summary>
    /// 管理一次完整的的回测会话，封装了从配置加载到结果产出的整个流程。
    /// </summary>
    public class BacktestingSession
    {
        /// <summary>
        /// 会话使用的环境配置
        /// </summary>
        public EnvConfig Config { get; private set; }

        /// <summary>
        /// 加载的市场数据
        /// </summary>
        public IDataStorage? Data { get; private set; }

        /// <summary>
        /// 最新一次策略运行产生的信号
        /// </summary>
        public List<SignalInfo>? Signals { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public BacktestingEngine? Engine { get; private set; }

        /// <summary>
        /// 构造一个新的回测会话
        /// </summary>
        /// <param name="configPath">env.yaml 配置文件路径</param>
        public BacktestingSession(string configPath)
        {
            Config = EnvConfigLoader.Load(configPath);

            if (Config == null)
                throw new InvalidOperationException("Config is null");
        }

        /// <summary>
        /// 加载数据。数据加载完成后会存储在 Data 属性中。
        /// </summary>
        public void Load()
        {
            var dataLoader = new DataLoader();
            Data = dataLoader.LoadData(Config);

            if (Data == null)
                throw new InvalidOperationException("Data is null");
        }

        /// <summary>
        /// 使用指定的策略运行回测引擎
        /// </summary>
        /// <param name="strategy">要运行的信号策略</param>
        public void RunSignal(ISignalStrategy strategy)
        {
            if (Data == null)
                throw new InvalidOperationException("Data is null");

            Engine = new BacktestingEngine(Data, strategy, Config);
            Signals = Engine.Run();
        }
    }
}
