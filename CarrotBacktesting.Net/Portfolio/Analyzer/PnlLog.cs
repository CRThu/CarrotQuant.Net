using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Portfolio.Position;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Analyzer
{
    /// <summary>
    /// 损益记录器
    /// </summary>
    public class PnlLog
    {
        /// <summary>
        /// 时间片
        /// </summary>
        public DateTime DateTime { get; set; }

        /// <summary>
        /// 权益价值
        /// </summary>
        public double ShareValue { get; set; }

        /// <summary>
        /// 现金价值
        /// </summary>
        public double CashValue { get; set; }

        /// <summary>
        /// 已实现损益
        /// </summary>
        public double RealizedPnl { get; set; }

        /// <summary>
        /// 未实现损益
        /// </summary>
        public double UnRealizedPnl { get; set; }

        /// <summary>
        /// 总价值
        /// </summary>
        public double TotalValue => ShareValue + CashValue;

        /// <summary>
        /// 总损益
        /// </summary>
        public double TotalPnl => RealizedPnl + UnRealizedPnl;

        public PnlLog(PositionManager positionManager, MarketFrame marketFrame)
        {
            DateTime = marketFrame.NowTime;

            double shareValue = 0;
            foreach (var position in positionManager.Positions)
            {
                shareValue += marketFrame[position.ShareName].NowPrice * position.Size;
            }
            ShareValue = shareValue;

            CashValue = positionManager.Cash;

            // TODO PNL Log不应在此直接计算盈亏情况而应用实时价格驱动PositionManager并计算
            // PNL Log 类应仅用于存放PNL信息与持仓变化快照
        }
    }
}
