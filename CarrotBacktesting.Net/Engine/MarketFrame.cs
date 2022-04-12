using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 市场信息帧, 包含时间片内的价格信息
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
        /// 构造函数
        /// </summary>
        public MarketFrame()
        {

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

    }
}
