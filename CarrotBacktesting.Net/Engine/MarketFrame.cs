using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 市场信息帧, 包含时间片内的价格信息
    /// 目前只能存放一个时间帧内的单个股票信息
    /// TODO待重构
    /// </summary>
    public class MarketFrame
    {
        /// <summary>
        /// 当前日期
        /// </summary>
        public DateTime NowTime { get; set; }
        /// <summary>
        /// 当前价格
        /// </summary>
        public double NowPrice { get; set; }
        /// <summary>
        /// 是否开盘
        /// TODO 停牌
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 市场帧字符串数据
        /// </summary>
        public Dictionary<string, string> StringData { get; set; } = new();

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="stringKeysName"></param>
        public MarketFrame(string[] stringKeysName = null)
        {
            if (stringKeysName != null)
            {
                foreach (var key in stringKeysName)
                    StringData.Add(key, string.Empty);
            }
        }

        /// <summary>
        /// 更新帧
        /// </summary>
        /// <param name="nowTime"></param>
        /// <param name="nowPrice"></param>
        public void UpdateFrame(DateTime nowTime, double nowPrice, bool isActive = true)
        {
            NowTime = nowTime;
            NowPrice = nowPrice;
            IsActive = isActive;
        }

        public void UpdateStringData(string key, string value)
        {
            StringData[key] = value;
        }
    }
}
