using CarrotBacktesting.Net.DataFeed;
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
        public SqliteDataFeed DataFeed { get; set; }
        /// <summary>
        /// 回测模拟设置
        /// </summary>
        public BackTestingSimulationOptions SimulationOptions { get; set; }

        /// <summary>
        /// 模拟时间
        /// </summary>
        public DateTime SimulationTime { get; set; }
        /// <summary>
        /// 模拟市场帧
        /// </summary>
        public MarketFrame SimulationMarketFrame { get; set; }
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
                return SimulationOptions.SimulationEndDateTime - SimulationOptions.SimulationStartDateTime;
            }
        }

        public BackTestingSimulation(string dbPath) : this(dbPath, new BackTestingSimulationOptions())
        {
        }

        public BackTestingSimulation(string dbPath, BackTestingSimulationOptions options)
        {
            // 配置加载
            SimulationOptions = options;

            // 数据库加载
            DataFeed = new(dbPath);
            DataFeed.SetShareData(options.ShareName, SimulationOptions.DateTimeColumnName, SimulationOptions.OHLCColumnName);

            // 数据源时间范围计算
            (DateTime minStart, DateTime maxEnd) = DataFeed.GetDateTimeRange();
            if (SimulationOptions.SimulationStartDateTime == DateTime.MinValue)
                SimulationOptions.SimulationStartDateTime = minStart;
            if (SimulationOptions.SimulationEndDateTime == DateTime.MaxValue)
                SimulationOptions.SimulationEndDateTime = maxEnd;

            // 初始化模拟属性
            SimulationTime = SimulationOptions.SimulationStartDateTime;
            SimulationMarketFrame = new();

        }

        /// <summary>
        /// 市场帧更新
        /// </summary>
        public void UpdateFrame()
        {
            // 更新市场帧
            (double price,bool isActive) = DataFeed.GetPrice(SimulationTime, SimulationOptions.ShareName, SimulationOptions.CloseColumnName);
            SimulationMarketFrame.UpdateFrame(SimulationTime, price, isActive);

            // 下一次更新时间并检测模拟是否结束
            SimulationTime += SimulationOptions.SimulationDuration;
            if (SimulationTime >= SimulationOptions.SimulationEndDateTime)
                IsSimulationEnd = true;
        }
    }
}
