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
    /// 回测系统模拟设置
    /// </summary>
    public class SimulationOptions
    {
        /// <summary>
        /// 是数据源
        /// </summary>
        public DataFeedSource DataFeedSource { get; set; } = DataFeedSource.Sqlite;

        /// <summary>
        /// 数据源路径
        /// </summary>
        public string SqliteDatabasePath { get; set; } = string.Empty;

        /// <summary>
        /// 字段映射信息存储类
        /// </summary>
        public ShareFrameMapper FieldsMapper { get; set; } =
            new ShareFrameMapper()
            {
                ["交易日期"] = "DateTime",
                ["开盘价"] = "Open",
                ["最高价"] = "High",
                ["最低价"] = "Low",
                ["收盘价"] = "Close",

                ["滚动市盈率"] = "PE",
            };

        /// <summary>
        /// 字段集合
        /// </summary>
        public string[] Fields
        {
            get
            {
                List<string> fieldList = new();
                fieldList.AddRange(BasicFields.ToArray());
                fieldList.AddRange(AdditionalFields);
                // 去重后输出数组
                return fieldList.Distinct().ToArray();
            }
        }

        /// <summary>
        /// 基础字段数组(交易日期, 开盘价, 收盘价, 最高价, 最低价, 成交量)
        /// </summary>
        public BasicFields BasicFields { get; set; } = new BasicFields()
        {
            Time = "交易日期",
            Open = "开盘价",
            High = "最高价",
            Low = "最低价",
            Close = "收盘价",
            Volume = "成交量"
        };

        /// <summary>
        /// 额外字段数组
        /// </summary>
        public string[] AdditionalFields { get; set; } = Array.Empty<string>();

        // TODO 
        ///// <summary>
        ///// 是否导入股票交易状态, 例如停牌/休市
        ///// </summary>
        //public bool IsEnableShareStatusFlag { get; set; } = false;
        ///// <summary>
        ///// 当使能IsShareStatusImport时, 用于数据库中股票的交易状态列名
        ///// </summary>
        //public string ShareStatusColumnName { get; set; } = "交易状态";
        ///// <summary>
        ///// 当使能IsShareStatusImport时, 用于数据库中股票的交易状态列的可交易状态判断字符串
        ///// </summary>
        //public string ShareStatusCanTradeName { get; set; } = "正常交易";

        /// <summary>
        /// 模拟开始日期
        /// </summary>
        public DateTime? SimulationStartTime { get; set; }

        /// <summary>
        /// 模拟结束日期
        /// </summary>
        public DateTime? SimulationEndTime { get; set; }

        /// <summary>
        /// 模拟股票名称数组
        /// </summary>
        public string[] ShareNames { get; set; } = Array.Empty<string>();
    }

    public struct BasicFields
    {
        /// <summary>
        /// 时间
        /// </summary>
        public string? Time { get; set; }

        /// <summary>
        /// 开盘价
        /// </summary>
        public string? Open { get; set; }

        /// <summary>
        /// 最高价
        /// </summary>
        public string? High { get; set; }

        /// <summary>
        /// 最低价
        /// </summary>
        public string? Low { get; set; }

        /// <summary>
        /// 收盘价
        /// </summary>
        public string? Close { get; set; }

        /// <summary>
        /// 成交量
        /// </summary>
        public string? Volume { get; set; }

        public string[] ToArray()
        {
            List<string> l = new();
            if (Time is not null)
                l.Add(Time);
            if (Open is not null)
                l.Add(Open);
            if (High is not null)
                l.Add(High);
            if (Low is not null)
                l.Add(Low);
            if (Close is not null)
                l.Add(Close);
            if (Volume is not null)
                l.Add(Volume);
            return l.ToArray();
        }
    }
}
