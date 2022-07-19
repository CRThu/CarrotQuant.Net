using CarrotBacktesting.Net.Common;
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
        public SimulationOptions Options { get; set; }

        /// <summary>
        /// 模拟开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 模拟结束时间
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 计算模拟时长
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                return EndTime - StartTime;
            }
        }

        /// <summary>
        /// 当前时间索引
        /// </summary>
        private int NextTimeIndex { get; set; }

        /// <summary>
        /// 模拟时间列表
        /// </summary>
        public DateTime[] SimulateTimes { get; set; }

        /// <summary>
        /// 是否正在模拟
        /// </summary>
        public bool IsSimulating { get; set; }

        /// <summary>
        /// 当前时间
        /// </summary>
        public DateTime CurrentTime { get; set; }

        /// <summary>
        /// 当前市场帧
        /// </summary>
        private MarketFrame currentMarket;

        /// <summary>
        /// 当前市场帧
        /// </summary>
        public MarketFrame CurrentMarket => currentMarket;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="options">配置</param>
        /// <exception cref="InvalidOperationException">MarketData日期序列异常</exception>
        public BackTestingSimulation(SimulationOptions options)
        {
            // 配置加载
            Options = options;

            // 数据库加载
            DataFeed = new DataFeed(options);
            SimulateTimes = DataFeed.MarketData.Times;

            // 数据源时间范围计算
            if (Options.SimulationStartTime is not null)
            {
                StartTime = (DateTime)Options.SimulationStartTime;
            }
            else
            {
                StartTime = DataFeed.StartTime;
            }

            if (Options.SimulationEndTime is not null)
            {
                EndTime = (DateTime)Options.SimulationEndTime;
            }
            else
            {
                EndTime = DataFeed.EndTime;
            }

            // 模拟器初始化
            NextTimeIndex = 0;
            CurrentTime = SimulateTimes[NextTimeIndex];
            bool isExist = DataFeed.GetMarketData(CurrentTime, out currentMarket);
            if (!isExist)
            {
                throw new InvalidOperationException($"MarketFrame at {CurrentTime} is not Exist, may be error?");
            }

            NextTimeIndex++;
            IsSimulating = true;
        }

        /// <summary>
        /// 市场帧更新
        /// </summary>
        /// <exception cref="InvalidOperationException">MarketData日期序列异常</exception>
        public void UpdateFrame()
        {
            CurrentTime = SimulateTimes[NextTimeIndex];

            // 市场数据更新
            bool isExist = DataFeed.GetMarketData(CurrentTime, out currentMarket);
            if (!isExist)
            {
                throw new InvalidOperationException($"MarketFrame at {CurrentTime} is not Exist, may be error?");
            }

            // 下一次更新时间并检测模拟是否结束
            NextTimeIndex++;

            if (NextTimeIndex >= SimulateTimes.Length)
                IsSimulating = false;
        }
    }
}
