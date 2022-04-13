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
        public BackTestingSimulationOptions Options { get; set; }

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
                return Options.SimulationEndDateTime - Options.SimulationStartDateTime;
            }
        }

        public BackTestingSimulation(string dbPath) : this(dbPath, new BackTestingSimulationOptions())
        {
        }

        public BackTestingSimulation(string dbPath, BackTestingSimulationOptions options)
        {
            // 配置加载
            Options = options;

            // 数据库加载
            DataFeed = new(dbPath);
            DataFeed.SetShareData(options.ShareName, Options.DateTimeColumnName, Options.OHLCColumnName, Options.StringDataColumnNames);

            // 数据源时间范围计算
            (DateTime minStart, DateTime maxEnd) = DataFeed.GetDateTimeRange();
            if (Options.SimulationStartDateTime == DateTime.MinValue)
                Options.SimulationStartDateTime = minStart;
            if (Options.SimulationEndDateTime == DateTime.MaxValue)
                Options.SimulationEndDateTime = maxEnd;

            // 初始化模拟属性
            SimulationTime = Options.SimulationStartDateTime;
            SimulationMarketFrame = new();

        }

        /// <summary>
        /// 市场帧更新
        /// </summary>
        public void UpdateFrame()
        {
            // 更新市场帧
            (int index, bool isPrecise) = DataFeed.GetTimeIndex(Options.ShareName, SimulationTime);
            (double price, bool isActive) = GetPrice(index, isPrecise, Options.ShareName, Options.CloseColumnName);
            SimulationMarketFrame.UpdateFrame(SimulationTime, price, isActive);
            foreach (var additionalStringColumnName in Options.AdditionalStringColumnNames)
            {
                string val = DataFeed.GetStringData(Options.ShareName, index, additionalStringColumnName);
                SimulationMarketFrame.UpdateStringData(additionalStringColumnName, val);
            }

            // 下一次更新时间并检测模拟是否结束
            SimulationTime += Options.SimulationDuration;
            if (SimulationTime >= Options.SimulationEndDateTime)
                IsSimulationEnd = true;
        }

        /// <summary>
        /// 获取价格, 若使能停牌标志则判断是否停牌
        /// </summary>
        /// <param name="dateTime"></param>
        /// <param name="shareName"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public (double price, bool isActive) GetPrice(int index, bool isPrecise, string shareName, string key)
        {
            double price = DataFeed.GetPrice(shareName, index, key);

            // 若精确查找有具体日期, 则寻找是否存在停牌标志
            if (Options.IsEnableShareStatusFlag && isPrecise)
            {
                string shareStatus = DataFeed.GetStringData(shareName, index, Options.ShareStatusColumnName);
                if (shareStatus != Options.ShareStatusCanTradeName)
                {
                    isPrecise = false;
                }
            }
            return (price, isPrecise);
        }
    }
}
