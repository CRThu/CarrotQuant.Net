using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        public string? DataFeedPath { get; set; } = null;

        /// <summary>
        /// 映射器json文件路径字段
        /// </summary>
        private string? mapperJsonFilePath = null;

        /// <summary>
        /// 映射器json文件路径
        /// </summary>
        public string? MapperJsonFilePath
        {
            get => mapperJsonFilePath;
            set
            {
                mapperJsonFilePath = value;
                DeserializeMapper();
            }
        }

        /// <summary>
        /// 字段映射信息存储类
        /// </summary>
        public ShareFrameMapper? Mapper { get; set; } = null;

        /// <summary>
        /// 字段json文件路径字段
        /// </summary>
        private string? fieldsJsonFilePath = null;

        /// <summary>
        /// 字段json文件路径
        /// </summary>
        public string? FieldsJsonFilePath
        {
            get => fieldsJsonFilePath;
            set
            {
                fieldsJsonFilePath = value;
                DeserializeFields();
            }
        }

        /// <summary>
        /// 字段集合
        /// </summary>
        public string[]? Fields { get; set; } = null;

        /// <summary>
        /// 是否导入股票交易状态, 例如停牌/休市
        /// </summary>
        public bool IsEnableShareStatusFlag { get; set; } = false;

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

        /// <summary>
        /// 构造函数
        /// </summary>
        public SimulationOptions()
        {

        }

        private void DeserializeMapper()
        {
            if (MapperJsonFilePath != null)
            {
                Mapper = ShareFrameMapper.Deserialize(MapperJsonFilePath)!;
            }
        }

        private void DeserializeFields()
        {
            if (FieldsJsonFilePath != null)
            {
                string jsonString = File.ReadAllText(FieldsJsonFilePath);
                FieldsJsonObject? fieldsJsonObject = JsonSerializer.Deserialize<FieldsJsonObject>(jsonString);
                Fields = fieldsJsonObject!.Fields;
            }
        }
    }
}
