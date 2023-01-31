using CarrotBacktesting.Net.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataModel
{
    /// <summary>
    /// 股票信息帧类
    /// </summary>
    public class ShareFrame
    {
        /// <summary>
        /// 股票代码
        /// </summary>
        public string StockCode { get; init; }

        /// <summary>
        /// 时间
        /// </summary>
        public DateTime DateTime { get; init; }

        /// <summary>
        /// 开盘价
        /// </summary>
        public double OpenPrice { get; init; }

        /// <summary>
        /// 最高价
        /// </summary>
        public double HighPrice { get; init; }

        /// <summary>
        /// 最低价
        /// </summary>
        public double LowPrice { get; init; }

        /// <summary>
        /// 收盘价
        /// </summary>
        public double ClosePrice { get; init; }

        /// <summary>
        /// 成交量
        /// </summary>
        public double Volume { get; init; }

        /// <summary>
        /// 是否正常交易
        /// </summary>
        public bool IsTrading { get; init; }

        /// <summary>
        /// 其他数据键值对(必须为值类型)
        /// </summary>
        public Dictionary<string, dynamic>? Params { get; init; }

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
        }

        /// <summary>
        /// 构造函数
        /// </summary>        
        /// <param name="stockCode">股票代码</param>
        /// <param name="dateTime">时间</param>
        /// <param name="openPrice">开盘价</param>
        /// <param name="highPrice">最高价</param>
        /// <param name="lowPrice">最低价</param>
        /// <param name="closePrice">收盘价</param>
        /// <param name="volume">成交量</param>
        /// <param name="isTrading">是否正常交易</param>
        /// <param name="kv">其他数据键值对</param>
        public ShareFrame(string stockCode, DateTime dateTime, double openPrice, double highPrice, double lowPrice, double closePrice, double volume, bool isTrading, IDictionary<string, dynamic>? kv = null)
        {
            StockCode = stockCode;
            DateTime = dateTime;
            OpenPrice = openPrice;
            HighPrice = highPrice;
            LowPrice = lowPrice;
            ClosePrice = closePrice;
            Volume = volume;
            IsTrading = isTrading;
            Params = kv == null ? null : new(kv);
        }

        /// <summary>
        /// 信息帧生成新实例,数据为传入实例的副本
        /// </summary>
        /// <param name="frame">信息帧</param>
        public ShareFrame(ShareFrame frame) : this(
            stockCode: frame.StockCode,
            dateTime: frame.DateTime,
            openPrice: frame.OpenPrice,
            highPrice: frame.HighPrice,
            lowPrice: frame.LowPrice,
            closePrice: frame.ClosePrice,
            volume: frame.Volume,
            isTrading: frame.IsTrading,
            kv: frame.Params)
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="frameDictionary">字典接口导入数据</param>
        /// <param name="stockCode">股票代码</param>
        public ShareFrame(IDictionary<string, object> frameDictionary, string? stockCode = null)
        {
            foreach (var kv in frameDictionary)
            {
                switch (kv.Key)
                {
                    case "StockCode": StockCode = DynamicConverter.GetValue<string>(kv.Value); break;
                    case "DateTime": DateTime = DynamicConverter.GetValue<DateTime>(kv.Value); break;
                    case "Open": OpenPrice = DynamicConverter.GetValue<double>(kv.Value); break;
                    case "High": HighPrice = DynamicConverter.GetValue<double>(kv.Value); break;
                    case "Low": LowPrice = DynamicConverter.GetValue<double>(kv.Value); break;
                    case "Close": ClosePrice = DynamicConverter.GetValue<double>(kv.Value); break;
                    case "Volume": Volume = DynamicConverter.GetValue<double>(kv.Value); break;
                    case "IsTrading": IsTrading = DynamicConverter.GetValue<bool>(kv.Value); break;
                    default:
                        Params ??= new();
                        Params[kv.Key] = kv.Value;
                        break;
                };
            }

            if (stockCode is not null)
                StockCode = stockCode;

            if (StockCode == null)
                throw new NotImplementedException("StockCode == null");
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="col">列名</param>
        /// <param name="data">数据行(字符串)</param>
        /// <param name="stockCode">股票代码,若列中未包含股票代码则使用该参数作为股票代码</param>
        /// <exception cref="ArgumentException"></exception>
        public ShareFrame(string[] col, string[] data, string? stockCode = null)
        {
            if (col.Length != data.Length)
            {
                throw new ArgumentException("ShareFrame参数长度不匹配");
            }

            for (int i = 0; i < col.Length; i++)
            {
                switch (col[i])
                {
                    case "StockCode": StockCode = DynamicConverter.GetValue<string>(data[i]); break;
                    case "DateTime": DateTime = DynamicConverter.GetValue<DateTime>(data[i]); break;
                    case "Open": OpenPrice = DynamicConverter.GetValue<double>(data[i]); break;
                    case "High": HighPrice = DynamicConverter.GetValue<double>(data[i]); break;
                    case "Low": LowPrice = DynamicConverter.GetValue<double>(data[i]); break;
                    case "Close": ClosePrice = DynamicConverter.GetValue<double>(data[i]); break;
                    case "Volume": Volume = DynamicConverter.GetValue<double>(data[i]); break;
                    case "IsTrading": IsTrading = DynamicConverter.GetValue<bool>(data[i]); break;
                    default:
                        Params ??= new();
                        Params[col[i]] = data[i];
                        break;
                };
            }

            if (StockCode == null)
            {
                if(stockCode != null)
                {
                    StockCode = stockCode;
                }
                else
                {
                    throw new NotImplementedException("StockCode == null");
                }
            }
        }

        /// <summary>
        /// 获取数据键值对
        /// </summary>
        /// <param name="key">key</param>
        /// <returns>key对应的value, 若key不存在则返回null</returns>
        private dynamic? GetKv(string key)
        {
            if (Params != null && Params.TryGetValue(key, out dynamic? v))
                return v;
            else
                return null;
        }

        /// <summary>
        /// 获取<see cref="ShareFrame"/>中的元素
        /// </summary>
        /// <param name="key">key</param>
        /// <returns>key对应的value, 若key不存在则返回null</returns>
        private dynamic? Get(string key)
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
