using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    public class OldShareFrame
    {
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
        /// 当前价格(收盘价格)
        /// </summary>
        public double NowPrice => ClosePrice;

        /// <summary>
        /// 是否开盘
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 股票帧额外添加浮点数据
        /// </summary>
        public Dictionary<string, double> FloatData { get; set; } = new();

        /// <summary>
        /// 股票帧额外添加字符串数据
        /// </summary>
        public Dictionary<string, string> StringData { get; set; } = new();

        /// <summary>
        /// 更新价格
        /// </summary>
        /// <param name="ohlc"></param>
        /// <param name="isActive"></param>
        public void UpdatePrice(double[] ohlc, bool isActive = true)
        {
            OpenPrice = ohlc[0];
            HighPrice = ohlc[1];
            LowPrice = ohlc[2];
            ClosePrice = ohlc[3];
            IsActive = isActive;
        }

        /// <summary>
        /// 更新额外加载浮点数据
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void UpdateAdditionalData(string key, double value)
        {
            FloatData[key] = value;
        }

        /// <summary>
        /// 更新额外加载字符串数据
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void UpdateAdditionalData(string key, string value)
        {
            StringData[key] = value;
        }
    }
}
