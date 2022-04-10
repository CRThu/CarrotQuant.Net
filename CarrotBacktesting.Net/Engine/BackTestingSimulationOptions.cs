using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 回测系统模拟设置
    /// </summary>
    public class BackTestingSimulationOptions
    {
        /// <summary>
        /// 是否为Sqlite数据
        /// </summary>
        public bool IsSqliteDataFeed { get; set; } = true;
        /// <summary>
        /// 数据库中表的交易时间列名
        /// </summary>
        public string DateTimeColumnName { get; set; } = "交易日期";
        /// <summary>
        /// 数据库中表的开盘价列名
        /// </summary>
        public string OpenColumnName { get; set; } = "开盘价";
        /// <summary>
        /// 数据库中表的最高价列名
        /// </summary>
        public string HighColumnName { get; set; } = "最高价";
        /// <summary>
        /// 数据库中表的最低价列名
        /// </summary>
        public string LowColumnName { get; set; } = "最低价";
        /// <summary>
        /// 数据库中表的收盘价列名
        /// </summary>
        public string CloseColumnName { get; set; } = "收盘价";
        /// <summary>
        /// 数据库中表的OHLC各对应的列名数组
        /// </summary>
        public string[] OHLCColumnName
        {
            get
            {
                return new[] { OpenColumnName, HighColumnName, LowColumnName, CloseColumnName };
            }
            set
            {
                OpenColumnName = value[0];
                HighColumnName = value[1];
                LowColumnName = value[2];
                CloseColumnName = value[3];
            }
        }

        /// <summary>
        /// 模拟开始日期
        /// </summary>
        public DateTime SimulationStartDateTime { get; set; } = DateTime.MinValue;
        /// <summary>
        /// 模拟结束日期
        /// </summary>
        public DateTime SimulationEndDateTime { get; set; } = DateTime.MaxValue;
        /// <summary>
        /// 模拟日期间隔
        /// </summary>
        public TimeSpan SimulationDuration { get; set; } = new TimeSpan(1, 0, 0, 0);
        /// <summary>
        /// 模拟股票名称
        /// TODO
        /// </summary>
        public string ShareName { get; set; } = "sz.000422";
    }
}
