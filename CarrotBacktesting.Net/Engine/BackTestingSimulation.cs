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

        public BackTestingSimulation(string dbPath, string shareCode) : this(dbPath, shareCode, new BackTestingSimulationOptions())
        {
        }

        public BackTestingSimulation(string dbPath, string shareCode, BackTestingSimulationOptions options)
        {
            // 配置加载
            SimulationOptions = options;

            // 数据库加载
            DataFeed = new(dbPath);
            DataFeed.SetShareData(shareCode, SimulationOptions.DateTimeColumnName, SimulationOptions.OHLCColumnName);

            // 数据源时间范围计算
            (DateTime minStart, DateTime maxEnd) = DataFeed.GetDateTimeRange();
            if (options.SimulationStartDateTime == DateTime.MinValue)
                options.SimulationStartDateTime = minStart;
            if (options.SimulationEndDateTime == DateTime.MaxValue)
                options.SimulationEndDateTime = maxEnd;
        }
    }
}
