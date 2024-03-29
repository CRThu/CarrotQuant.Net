﻿using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.Tasks;
using static CarrotBacktesting.Net.Common.Enums;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 回测系统模拟设置
    /// </summary>
    public class SimulationOptions
    {
        /// <summary>
        /// 数据管理器
        /// </summary>
        public BackTestingDataManager? DataManager { get; set; } = null;

        /// <summary>
        /// 映射器Json文件路径, 若反序列化后不为null则反序列化该路径文件至<see cref="Mapper"/>
        /// </summary>
        public string? MapperJsonFilePath { get; set; } = null;

        /// <summary>
        /// 导入字段列表Json文件路径, 若反序列化后不为null则反序列化该路径文件至<see cref="Fields"/>
        /// </summary>
        public string? FieldsJsonFilePath { get; set; } = null;

        /// <summary>
        /// 股票列表json文件路径, 若反序列化后不为null则反序列化该路径文件至<see cref="StockCodes"/>
        /// </summary>
        public string? StockCodesJsonFilePath { get; set; } = null;

        /// <summary>
        /// 数据源
        /// </summary>
        public DataFeedSource DataFeedSource { get; set; } = DataFeedSource.Csv;

        /// <summary>
        /// 字段映射信息存储类
        /// </summary>
        public ShareFrameMapper? Mapper { get; set; } = null;

        /// <summary>
        /// 字段集合
        /// </summary>
        public string[]? Fields { get; set; } = null;

        /// <summary>
        /// 模拟开始日期
        /// </summary>
        public DateTime? SimulationStartTime { get; set; } = null;

        /// <summary>
        /// 模拟结束日期
        /// </summary>
        public DateTime? SimulationEndTime { get; set; } = null;

        /// <summary>
        /// 是否支持回测未来函数
        /// </summary>
        public bool SimulatorUseFutureData { get; set; } = false;

        /// <summary>
        /// 股票列表, 若为null则获取所有股票代码
        /// </summary>
        public string[]? StockCodes { get; set; } = null;

        /// <summary>
        /// 回测价格参考
        /// </summary>
        public ExchangePriceRef ExchangePriceRef { get; set; } = ExchangePriceRef.Close;

        /// <summary>
        /// 回测交易流动性估计(实际成交量与Tick总成交量比值)
        /// </summary>
        public double ExchangeEstimateLiquidityRatio { get; set; } = 0.1;

        /// <summary>
        /// 回测交易滑点
        /// </summary>
        public double ExchangePriceSlippage { get; set; } = 0.02;

        /// <summary>
        /// 回测交易手续费比例
        /// </summary>
        public double ExchangeTradeFeeRatio { get; set; } = 0.0003;

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


        /// <summary>
        /// 通过Json载入SimulationObject配置
        /// </summary>
        /// <param name="jsonpath">Json配置文件</param>
        /// <returns>返回创建的配置类</returns>
        public static SimulationOptions CreateFromJson(string baseDir, string dataSet, string optionsJsonName)
        {
            BackTestingDataManager btdm = BackTestingDataManager.Create(baseDir, dataSet);
            string optionsJsonPath = btdm.GetJsonFilePath(optionsJsonName);
            string jsonString = File.ReadAllText(optionsJsonPath);
            SimulationOptions options = Json.DeSerialize<SimulationOptions>(jsonString)!;

            options.DataManager = btdm;

            // 反序列化嵌套配置读取
            if (options.MapperJsonFilePath != null)
            {
                string mapperFilePath = options.DataManager!.GetJsonFilePath(options.MapperJsonFilePath);
                options.Mapper = Json.DeSerializeFromFile<ShareFrameMapper>(mapperFilePath);
            }

            if (options.FieldsJsonFilePath != null)
            {
                string fieldsFilePath = options.DataManager.GetJsonFilePath(options.FieldsJsonFilePath);
                options.Fields = Json.DeSerializeFromFile<string[]>(fieldsFilePath);
            }

            if (options.StockCodesJsonFilePath != null)
            {
                string stockcodesFilePath = options.DataManager!.GetJsonFilePath(options.StockCodesJsonFilePath);
                options.StockCodes = Json.DeSerializeFromFile<string[]>(stockcodesFilePath);
            }

            // 更新全局布尔值字符串
            options.Mapper!.UpdateGlobalBoolString();

            return options;
        }
    }
}
