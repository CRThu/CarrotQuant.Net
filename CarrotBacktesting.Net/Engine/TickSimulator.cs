using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 回测时间片模拟器
    /// </summary>
    public class TickSimulator : ITickSimulator
    {
        /// <summary>
        /// 开始时间(第一个存在的时间片)
        /// </summary>
        public DateTime StartTime { get; init; }

        /// <summary>
        /// 结束时间(最后一个存在的时间片)
        /// </summary>
        public DateTime EndTime { get; init; }

        /// <summary>
        /// 模拟时长
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                return EndTime - StartTime;
            }
        }

        /// <summary>
        /// 总时间片数量
        /// </summary>
        public int TickCount { get; init; }

        /// <summary>
        /// 已完成时间片数量
        /// </summary>
        public int ElapsedTickCount => CurrentTickIndex + 1;

        /// <summary>
        /// 当前市场数据
        /// </summary>
        public MarketFrame CurrentMarket => DataFeed.MarketData[CurrentTickIndex];

        /// <summary>
        /// 当前时间
        /// </summary>
        public DateTime CurrentTime => CurrentMarket.Time;

        /// <summary>
        /// 当前时间片索引
        /// </summary>
        public int CurrentTickIndex { get; set; }

        /// <summary>
        /// 模拟器是否正在运行
        /// </summary>
        public bool IsRunning { get; set; }

        /// <summary>
        /// 数据源实例
        /// </summary>
        public DataFeed DataFeed { get; init; }

        /// <summary>
        /// 设置配置实例
        /// </summary>
        public SimulationOptions Options { get; init; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dataFeed"></param>
        /// <param name="options"></param>
        public TickSimulator(DataFeed dataFeed, SimulationOptions options)
        {
            DataFeed = dataFeed;
            Options = options;

            // TODO UpdateDataFeedInfo

        }

        /// <summary>
        /// 时间片更新
        /// </summary>
        public void UpdateTick()
        {
            // TODO
        }
    }
}
