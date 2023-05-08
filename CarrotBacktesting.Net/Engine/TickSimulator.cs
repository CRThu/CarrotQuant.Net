using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Portfolio.Order;
using CarrotBacktesting.Net.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 回测时间片模拟器
    /// </summary>
    public class TickSimulator : ITickDataManager
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
        public int ElapsedTickCount => CurrentTickIndex;


        /// <summary>
        /// 总k线数据条目数量
        /// </summary>
        public int KLineCount { get; init; }

        /// <summary>
        /// 当前市场数据
        /// </summary>
        public MarketFrame CurrentMarket => DataFeed.MarketData[CurrentTickIndex];

        /// <summary>
        /// 当前与历史时间市场数据
        /// </summary>
        /// <param name="tickOffset">偏移量,为0或负数<br/>
        /// 0为当前市场数据, -1为上一个时间数据<br/>
        /// (若 <see cref="SimulationOptions.SimulatorUseFutureData"/> 为True则可以通过正数获取未来市场数据, 否则输出null)<br/>
        /// </param>
        /// <returns>存在则返回, 不存在则返回null</returns>
        public MarketFrame? this[int tickOffset]
        {
            get
            {
                if ((CurrentTickIndex + tickOffset < 0)
                    && !(tickOffset <= 0 || Options.SimulatorUseFutureData))
                    return null;
                else
                    return DataFeed.MarketData[CurrentTickIndex + tickOffset];
            }
        }

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
        /// 市场信息更新委托
        /// </summary>
        /// <param name="marketFrame">市场信息更新帧数据</param>
        /// <param name="marketEventArgs">市场更新事件参数</param>
        public delegate void MarketUpdateHandler(MarketFrame marketFrame, MarketEventArgs marketEventArgs);

        /// <summary>
        /// 市场信息更新事件
        /// </summary>
        public event MarketUpdateHandler? OnMarketUpdate;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dataFeed"></param>
        /// <param name="options"></param>
        public TickSimulator(DataFeed dataFeed, SimulationOptions options)
        {
            DataFeed = dataFeed;
            Options = options;

            // Update DataFeedInfo
            DateTime optionStartTime = Options.SimulationStartTime ?? DataFeed.StartTime;
            DateTime optionEndTime = Options.SimulationEndTime ?? DataFeed.EndTime;
            DataFeed.GetMarketData(optionStartTime, out MarketFrame startFrame);
            DataFeed.GetMarketData(optionEndTime, out MarketFrame endFrame);
            StartTime = startFrame.Time;
            EndTime = endFrame.Time;
            TickCount = dataFeed.MarketData.Times.Count(t => t >= StartTime && t <= EndTime);
            KLineCount = dataFeed.MarketData.Frames.Where(t => t.Time >= StartTime && t.Time <= EndTime).Sum(t => t.Count);
            CurrentTickIndex = 0;
            IsRunning = false;
        }

        /// <summary>
        /// 时间片更新
        /// </summary>
        /// <returns>返回是否模拟器运行结束, 若结束返回false</returns>
        public bool UpdateTick()
        {
            OnRaiseMarketUpdateEvent();

            CurrentTickIndex++;
            IsRunning = CurrentTickIndex < TickCount;
            return IsRunning;
        }

        public void OnRaiseMarketUpdateEvent()
        {
            // Console.WriteLine($"TickSimulator: Tick: {CurrentTime} ({ElapsedTickCount + 1}/{TickCount})");
            OnMarketUpdate?.Invoke(CurrentMarket!, new MarketEventArgs(CurrentTime, true));
        }
    }
}
