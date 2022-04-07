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
        public SqliteDataFeed DataFeed { get; set; }

        public BackTestingSimulation(string dbPath, string shareCode, BackTestingSimulationOptions options)
        {
            DataFeed = new(dbPath);
            DataFeed.SetShareData(shareCode, "交易日期", new string[] { "开盘价", "最高价", "最低价", "收盘价" });
        }
    }
}
