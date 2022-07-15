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

        public BackTestingSimulation() : this(new BackTestingSimulationOptions())
        {
        }

        public BackTestingSimulation(BackTestingSimulationOptions options)
        {
            // 配置加载
            Options = options;

            // 数据库加载
            if (options.IsSqliteDataFeed)
                DataFeed = new DataFeed(options.SqliteDatabasePath);
            else
                throw new NotImplementedException("未实现非Sqlite数据库数据载入接口");

            foreach (var shareName in options.ShareNames)
                DataFeed.AddShareData(shareName, Options.DateTimeColumnName, Options.DataColumnNames, Options.StringDataColumnNames);

            // 数据源时间范围计算
            (DateTime minStart, DateTime maxEnd) = DataFeed.GetDateTimeRange();
            if (Options.SimulationStartDateTime == DateTime.MinValue)
                Options.SimulationStartDateTime = minStart;
            if (Options.SimulationEndDateTime == DateTime.MaxValue)
                Options.SimulationEndDateTime = maxEnd;

            // 初始化模拟属性
            SimulationTime = Options.SimulationStartDateTime;
            SimulationMarketFrame = new(Options.ShareNames);

        }

        /// <summary>
        /// 市场帧更新
        /// </summary>
        public void UpdateFrame()
        {
            // 更新市场帧
            foreach (var shareName in Options.ShareNames)
            {
                (int index, bool isPrecise) = DataFeed.GetTimeIndex(shareName, SimulationTime);
                (double[] ohlc, bool isActive) = GetPrice(index, isPrecise, shareName);
                SimulationMarketFrame.UpdateTime(SimulationTime);
                SimulationMarketFrame.MarketFrameCache[shareName].UpdatePrice(ohlc, isActive);

                foreach (var additionalStringColumnName in Options.AdditionalStringColumnNames)
                {
                    var val = DataFeed.GetStringData(shareName, index, additionalStringColumnName);
                    SimulationMarketFrame.MarketFrameCache[shareName].UpdateAdditionalData(additionalStringColumnName, val);
                }
                foreach (var additionalDataColumnName in Options.AdditionalDataColumnNames)
                {
                    var val = DataFeed.GetData(shareName, index, additionalDataColumnName);
                    SimulationMarketFrame.MarketFrameCache[shareName].UpdateAdditionalData(additionalDataColumnName, val);
                }
            }

            // 下一次更新时间并检测模拟是否结束
            SimulationTime += Options.SimulationDuration;
            if (SimulationTime >= Options.SimulationEndDateTime)
                IsSimulationEnd = true;
        }

        /// <summary>
        /// 获取OHLC价格, 若使能停牌标志则判断是否停牌
        /// </summary>
        /// <param name="index"></param>
        /// <param name="isPrecise"></param>
        /// <param name="shareName"></param>
        /// <returns></returns>
        public (double[] ohlc, bool isActive) GetPrice(int index, bool isPrecise, string shareName)
        {
            double[] ohlc = new[] {
                DataFeed.GetPrice(shareName, index, Options.OpenColumnName),
                DataFeed.GetPrice(shareName, index, Options.HighColumnName),
                DataFeed.GetPrice(shareName, index, Options.LowColumnName),
                DataFeed.GetPrice(shareName, index, Options.CloseColumnName),
                };

            // 若精确查找有具体日期, 则寻找是否存在停牌标志
            if (Options.IsEnableShareStatusFlag && isPrecise)
            {
                string shareStatus = DataFeed.GetStringData(shareName, index, Options.ShareStatusColumnName);
                if (shareStatus != Options.ShareStatusCanTradeName)
                {
                    isPrecise = false;
                }
            }
            return (ohlc, isPrecise);
        }
    }
}
