using CarrotBacktesting.Net.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 回测系统模拟, 用于生成市场时间帧
    /// </summary>
    public class BackTestingSimulation
    {
        /// <summary>
        /// 从数据库提供数据
        /// </summary>
        public DataFeed DataFeed { get; set; }
        /// <summary>
        /// 回测模拟设置
        /// </summary>
        public BackTestingSimulationOptions Options { get; set; }

        /// <summary>
        /// 模拟时间
        /// </summary>
        public DateTime SimulationTime { get; set; }
        /// <summary>
        /// 模拟市场帧
        /// </summary>
        public OldMarketFrame SimulationMarketFrame { get; set; }
        /// <summary>
        /// 是否模拟结束Flag
        /// </summary>
        public bool IsSimulationEnd { get; set; }

        /// <summary>
        /// 计算模拟时长
        /// </summary>
        public TimeSpan SimulationDuration
        {
            get
            {
                return Options.SimulationEndDateTime - Options.SimulationStartDateTime;
            }
        }

        public BackTestingSimulation() : this(new BackTestingSimulationOptions())
        {
        }

        public BackTestingSimulation(BackTestingSimulationOptions options)
        {
            // 配置加载
            Options = options;

            // 数据库加载
            DataFeed = new DataFeed(options);

            // 数据源时间范围计算
            if (Options.SimulationStartDateTime == DateTime.MinValue)
                Options.SimulationStartDateTime = DataFeed.StartTime;
            if (Options.SimulationEndDateTime == DateTime.MaxValue)
                Options.SimulationEndDateTime = DataFeed.EndTime;

            // 初始化模拟属性
            SimulationTime = Options.SimulationStartDateTime;
            SimulationMarketFrame = new(Options.ShareNames);

        }

        /// <summary>
        /// 市场帧更新
        /// </summary>
        public void UpdateFrame()
        {
            // TODO
            bool isExist = DataFeed.GetMarketData(SimulationTime, out DataModel.MarketFrame frame);
            SimulationMarketFrame = new(new string[] { });
            throw new NotImplementedException();

            // 下一次更新时间并检测模拟是否结束
            SimulationTime += Options.SimulationDuration;
            if (SimulationTime >= Options.SimulationEndDateTime)
                IsSimulationEnd = true;
        }
    }
}
