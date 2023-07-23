using CarrotBacktesting.Net.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
        public string Code { get; set; }

        /// <summary>
        /// 时间
        /// </summary>
        public DateTime Time { get; set; }

        /// <summary>
        /// 开盘价
        /// </summary>
        public double Open { get; set; }

        /// <summary>
        /// 最高价
        /// </summary>
        public double High { get; set; }

        /// <summary>
        /// 最低价
        /// </summary>
        public double Low { get; set; }

        /// <summary>
        /// 收盘价
        /// </summary>
        public double Close { get; set; }

        /// <summary>
        /// 成交量
        /// </summary>
        public double Volume { get; set; }

        /// <summary>
        /// 是否正常交易
        /// </summary>
        public bool Status { get; set; }

        /// <summary>
        /// 其他数据键值对(必须为值类型)
        /// </summary>
        public Dictionary<string, dynamic>? Params { get; set; }

        /// <summary>
        /// 写入或读取<see cref="ShareFrame"/>中的元素
        /// </summary>
        /// <param name="key">key, 若不存在则创建, 若存在则覆盖</param>
        /// <returns>value, 若key不存在则返回null</returns>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public dynamic? this[string key]
        {
            get
            {
                return Get(key);
            }
        }

        public ShareFrame()
        {

        }

        /// <summary>
        /// 构造函数
        /// </summary>        
        /// <param name="code">股票代码</param>
        /// <param name="time">时间</param>
        /// <param name="open">开盘价</param>
        /// <param name="high">最高价</param>
        /// <param name="low">最低价</param>
        /// <param name="close">收盘价</param>
        /// <param name="volume">成交量</param>
        /// <param name="status">是否正常交易</param>
        /// <param name="kv">其他数据键值对</param>
        public ShareFrame(string code, DateTime time, double open, double high, double low, double close, double volume, bool status, IDictionary<string, dynamic>? kv = null)
        {
            Code = code;
            Time = time;
            Open = open;
            High = high;
            Low = low;
            Close = close;
            Volume = volume;
            Status = status;
            Params = kv == null ? null : new(kv);
        }

        /// <summary>
        /// 信息帧生成新实例,数据为传入实例的副本
        /// </summary>
        /// <param name="frame">信息帧</param>
        public ShareFrame(ShareFrame frame) : this(
            code: frame.Code,
            time: frame.Time,
            open: frame.Open,
            high: frame.High,
            low: frame.Low,
            close: frame.Close,
            volume: frame.Volume,
            status: frame.Status,
            kv: frame.Params)
        {
        }


        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="keys"></param>
        /// <param name="vals"></param>
        /// <param name="stockCode"></param>
        /// <exception cref="NotImplementedException"></exception>
        public ShareFrame(string[] keys, string[] vals, string? stockCode = null)
        {
            if (keys.Length != vals.Length)
            {
                throw new ArgumentException("ShareFrame keys与vals参数长度不匹配");
            }

            for (int i = 0; i < keys.Length; i++)
            {
                switch (keys[i])
                {
                    case "Code": Code = DynamicConverter.GetValue<string>(vals[i]); break;
                    case "Time": Time = DynamicConverter.GetValue<DateTime>(vals[i]); break;
                    case "Open": Open = DynamicConverter.GetValue<double>(vals[i]); break;
                    case "High": High = DynamicConverter.GetValue<double>(vals[i]); break;
                    case "Low": Low = DynamicConverter.GetValue<double>(vals[i]); break;
                    case "Close": Close = DynamicConverter.GetValue<double>(vals[i]); break;
                    case "Volume": Volume = DynamicConverter.GetValue<double>(vals[i]); break;
                    case "Status": Status = DynamicConverter.GetValue<bool>(vals[i]); break;
                    default:
                        Params ??= new();
                        Params[keys[i]] = vals[i];
                        break;
                };
            }

            if (stockCode is not null)
                Code = stockCode;

            if (Code == null)
                throw new NotImplementedException("Code == null");
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
                    case "Code": Code = DynamicConverter.GetValue<string>(kv.Value); break;
                    case "Time": Time = DynamicConverter.GetValue<DateTime>(kv.Value); break;
                    case "Open": Open = DynamicConverter.GetValue<double>(kv.Value); break;
                    case "High": High = DynamicConverter.GetValue<double>(kv.Value); break;
                    case "Low": Low = DynamicConverter.GetValue<double>(kv.Value); break;
                    case "Close": Close = DynamicConverter.GetValue<double>(kv.Value); break;
                    case "Volume": Volume = DynamicConverter.GetValue<double>(kv.Value); break;
                    case "Status": Status = DynamicConverter.GetValue<bool>(kv.Value); break;
                    default:
                        Params ??= new();
                        Params[kv.Key] = kv.Value;
                        break;
                };
            }

            if (stockCode is not null)
                Code = stockCode;

            if (Code == null)
                throw new NotImplementedException("Code == null");
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="col">列名数组</param>
        /// <param name="data">数据行数组</param>
        /// <param name="mask">数组元素是否使用, 若未使用则该元素位置传入false</param>
        /// <param name="types">
        /// 数组元素是否需要转换<br/>
        /// 若为null则为默认System.String<br/>
        /// 转换类型<br/>
        /// 例如: System.Double, System.String, System.Boolean
        /// </param>
        /// <param name="stockCode">股票代码,若列中未包含股票代码则使用该参数作为股票代码</param>
        /// <exception cref="ArgumentException"></exception>
        public ShareFrame(string[] col, string[] data, bool[]? mask, string?[]? types = null, string? stockCode = null)
        {
            if (col.Length != data.Length)
            {
                throw new ArgumentException("ShareFrame col与data参数长度不匹配");
            }

            if (mask != null && col.Length != mask.Length)
            {
                throw new ArgumentException("ShareFrame col与mask参数长度不匹配");
            }

            if (types != null && col.Length != types.Length)
            {
                throw new ArgumentException("ShareFrame col与types参数长度不匹配");
            }

            for (int i = 0; i < col.Length; i++)
            {
                if (mask == null || (mask != null && mask[i]))
                {
                    switch (col[i])
                    {

                        case "Code": Code = data[i]; break;
                        case "Time": Time = DateTimeMisc.ParseDateTime(data[i]); break;
                        case "Open": Open = Convert.ToDouble(data[i]); break;
                        case "High": High = Convert.ToDouble(data[i]); break;
                        case "Low": Low = Convert.ToDouble(data[i]); break;
                        case "Close": Close = Convert.ToDouble(data[i]); break;
                        case "Volume": Volume = Convert.ToDouble(data[i]); break;
                        case "Status": Status = BooleanEx.ToBooleanEx(data[i]); break;

                        default:
                            Params ??= new();
                            if (types == null || (types != null && types[i] == null))
                            {
                                Params[col[i]] = data[i];
                            }
                            else
                            {
                                Params[col[i]] = DynamicConverter.GetValue(data[i], types![i]!);
                            }
                            break;
                    };
                }
            }

            if (Code == null)
            {
                if (stockCode != null)
                {
                    Code = stockCode;
                }
                else
                {
                    throw new NotImplementedException("Code == null");
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
            return key switch {
                "Code" => Code,
                "Time" => Time,
                "Open" => Open,
                "High" => High,
                "Low" => Low,
                "Close" => Close,
                "Volume" => Volume,
                "Status" => Status,
                _ => GetKv(key),
            };
        }
    }
}
