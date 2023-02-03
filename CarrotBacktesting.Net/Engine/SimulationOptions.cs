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
        /// 数据源
        /// </summary>
        public DataFeedSource DataFeedSource { get; set; }

        /// <summary>
        /// 数据源路径
        /// </summary>
        public string DataFeedPath { get; set; }

        /// <summary>
        /// 映射器json文件路径, 若为映射器程序定义则为null
        /// </summary>
        public string MapperJsonFilePath { get; set; }

        /// <summary>
        /// 字段映射信息存储类, MapperJsonFilePath需为null
        /// </summary>
        public ShareFrameMapper Mapper { get; set; }

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

        /// <summary>
        /// 回测交易流动性估计(实际成交量与Tick总成交量比值)
        /// </summary>
        public double ExchangeEstimateLiquidityRatio { get; set; } = 0.2;

        /// <summary>
        /// 回测交易滑点
        /// </summary>
        public double ExchangePriceSlippage { get; set; } = 0.02;

        /// <summary>
        /// 初始资金
        /// </summary>
        public double InitialCash { get; set; } = 100000;
    }
}
