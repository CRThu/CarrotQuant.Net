using CarrotBacktesting.Net.DataModel;
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

        public string SqliteDatabasePath { get; set; } = string.Empty;

        /// <summary>
        /// 字段映射信息存储类
        /// TODO NEW
        /// </summary>
        public ShareFrameMapper FieldsMapper { get; set; } =
            new ShareFrameMapper()
            {
                ["[index]"] = "[index]",
                ["交易日期"] = "DateTime",
                ["收盘价"] = "close"
            };

        /// <summary>
        /// 字段信息集合
        /// TODO NEW
        /// </summary>
        public string[] Fields
        {
            get
            {
                List<string> fieldList = new();
                fieldList.Add(DateTimeColumnName);
                fieldList.AddRange(DataColumnNames);
                fieldList.AddRange(StringDataColumnNames);
                return fieldList.ToArray();
            }
        }

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
        /// 附加数据库读取的数据列名数组
        /// </summary>
        public string[] AdditionalDataColumnNames { get; set; } = Array.Empty<string>();


        /// <summary>
        /// 是否导入股票交易状态, 例如停牌/休市
        /// </summary>
        public bool IsEnableShareStatusFlag { get; set; } = false;
        /// <summary>
        /// 当使能IsShareStatusImport时, 用于数据库中股票的交易状态列名
        /// </summary>
        public string ShareStatusColumnName { get; set; } = "交易状态";
        /// <summary>
        /// 当使能IsShareStatusImport时, 用于数据库中股票的交易状态列的可交易状态判断字符串
        /// </summary>
        public string ShareStatusCanTradeName { get; set; } = "正常交易";

        /// <summary>
        /// 附加数据库读取的字符串数据列名数组
        /// </summary>
        public string[] AdditionalStringColumnNames { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 从数据库读取的浮点数据名
        /// </summary>
        public string[] DataColumnNames
        {
            get
            {
                List<string> cols = new();
                cols.AddRange(OHLCColumnName);
                cols.AddRange(AdditionalDataColumnNames);
                // 去重后输出数组
                return cols.Distinct().ToArray();
            }
        }

        /// <summary>
        /// 从数据库读取的字符串数据名
        /// </summary>
        public string[] StringDataColumnNames
        {
            get
            {
                List<string> cols = new();
                if (IsEnableShareStatusFlag)
                    cols.Add(ShareStatusColumnName);
                cols.AddRange(AdditionalStringColumnNames);
                // 去重后输出数组
                return cols.Distinct().ToArray();
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
        /// </summary>
        public string[] ShareNames { get; set; } = Array.Empty<string>();
    }
}
