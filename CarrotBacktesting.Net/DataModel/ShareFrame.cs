using CarrotBacktesting.Net.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


// TODO
// Reference:
// https://docs.microsoft.com/zh-cn/dotnet/csharp/write-safe-efficient-code#the-out-ref-and-in-keywords

namespace CarrotBacktesting.Net.DataModel
{
    /// <summary>
    /// 股票信息帧类
    /// TODO 改为struct优化性能
    /// </summary>
    public class ShareFrame
    {
        /// <summary>
        /// 股票代码
        /// </summary>
        public string StockCode { get; set; }

        /// <summary>
        /// 日期/时间
        /// </summary>
        public DateTime DateTime { get; set; }

        /// <summary>
        /// 开盘价
        /// </summary>
        public double OpenPrice { get; set; }

        /// <summary>
        /// 最高价
        /// </summary>
        public double HighPrice { get; set; }

        /// <summary>
        /// 最低价
        /// </summary>
        public double LowPrice { get; set; }

        /// <summary>
        /// 收盘价
        /// </summary>
        public double ClosePrice { get; set; }

        /// <summary>
        /// 成交量
        /// </summary>
        public double Volume { get; set; }

        /// <summary>
        /// 是否正常交易
        /// </summary>
        public bool IsTrading { get; set; }

        /// <summary>
        /// 其他数据键值对
        /// </summary>
        private Dictionary<string, dynamic> Data { get; set; }

        /// <summary>
        /// 写入或读取<see cref="ShareFrame"/>中的元素
        /// </summary>
        /// <param name="key">key, 若不存在则创建, 若存在则覆盖</param>
        /// <returns>value, 若key不存在则返回null</returns>
        public dynamic? this[string key]
        {
            get
            {
                return Get(key);
            }
            set
            {
                Set(key, value);
            }
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <param name="dateTime">日期/时间</param>
        /// <param name="openPrice">开盘价</param>
        /// <param name="highPrice">最高价</param>
        /// <param name="lowPrice">最低价</param>
        /// <param name="closePrice">收盘价</param>
        /// <param name="volume">成交量</param>
        /// <param name="isTrading">是否正常交易, 默认为true</param>
        public ShareFrame(string stockCode, DateTime dateTime, double openPrice, double highPrice, double lowPrice, double closePrice, double volume, bool isTrading = true)
        {
            StockCode = stockCode;
            DateTime = dateTime;
            OpenPrice = openPrice;
            HighPrice = highPrice;
            LowPrice = lowPrice;
            ClosePrice = closePrice;
            Volume = volume;
            IsTrading = isTrading;
            Data = new();
        }

        /// <summary>
        /// 构造函数
        /// </summary>        
        /// <param name="stockCode">股票代码</param>
        /// <param name="dateTime">日期/时间</param>
        /// <param name="openPrice">开盘价</param>
        /// <param name="highPrice">最高价</param>
        /// <param name="lowPrice">最低价</param>
        /// <param name="closePrice">收盘价</param>
        /// <param name="volume">成交量</param>
        /// <param name="isTrading">是否正常交易</param>
        /// <param name="kv">其他数据键值对</param>
        public ShareFrame(string stockCode, DateTime dateTime, double openPrice, double highPrice, double lowPrice, double closePrice, double volume, bool isTrading, Dictionary<string, dynamic> kv)
        {
            StockCode = stockCode;
            DateTime = dateTime;
            OpenPrice = openPrice;
            HighPrice = highPrice;
            LowPrice = lowPrice;
            ClosePrice = closePrice;
            Volume = volume;
            IsTrading = isTrading;
            Data = kv;
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="frameDictionary">字典接口导入数据</param>
        /// <param name="stockCode">股票代码</param>
        public ShareFrame(IDictionary<string, object> frameDictionary, string? stockCode = null)
        {
            StockCode = "";
            Data = new();

            if (stockCode is not null)
                StockCode = stockCode;

            foreach (var kv in frameDictionary)
            {
                Set(kv.Key, kv.Value);
            }
        }

        /// <summary>
        /// 新增数据键值对
        /// </summary>
        /// <param name="key">key, 若key存在则覆盖</param>
        /// <param name="val">value</param>
        public void SetKv(string key, dynamic val)
        {
            Data[key] = val;
        }

        /// <summary>
        /// 获取数据键值对
        /// </summary>
        /// <param name="key">key</param>
        /// <returns>key对应的value, 若key不存在则返回null</returns>
        public dynamic? GetKv(string key)
        {
            if (Data.TryGetValue(key, out dynamic? v))
                return v;
            else
                return null;
        }

        /// <summary>
        /// 写入<see cref="ShareFrame"/>中的元素
        /// </summary>
        /// <param name="key">key, 若key存在则覆盖</param>
        /// <param name="val">value</param>
        public void Set(string key, dynamic val)
        {
            switch (key)
            {
                case "StockCode": StockCode = DynamicConverter.GetValue<string>(val); break;
                case "DateTime": DateTime = DynamicConverter.GetValue<DateTime>(val); break;
                case "Open": OpenPrice = DynamicConverter.GetValue<double>(val); break;
                case "High": HighPrice = DynamicConverter.GetValue<double>(val); break;
                case "Low": LowPrice = DynamicConverter.GetValue<double>(val); break;
                case "Close": ClosePrice = DynamicConverter.GetValue<double>(val); break;
                case "Volume": Volume = DynamicConverter.GetValue<double>(val); break;
                case "IsTrading": IsTrading = DynamicConverter.GetValue<bool>(val); break;
                default: SetKv(key, val); break;
            };
        }

        /// <summary>
        /// 获取<see cref="ShareFrame"/>中的元素
        /// </summary>
        /// <param name="key">key</param>
        /// <returns>key对应的value, 若key不存在则返回null</returns>
        public dynamic? Get(string key)
        {
            return key switch
            {
                "StockCode" => StockCode,
                "DateTime" => DateTime,
                "Open" => OpenPrice,
                "High" => HighPrice,
                "Low" => LowPrice,
                "Close" => ClosePrice,
                "Volume" => Volume,
                "IsTrading" => IsTrading,
                _ => GetKv(key),
            };
        }
    }
}
