using CarrotBacktesting.Net.Shared;
using CarrotBacktesting.Net.Portfolio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBacktesting.Net.Portfolio.Order;
using CarrotBacktesting.Net.Engine;

namespace CarrotBacktesting.Net.Portfolio.Position
{
    /// <summary>
    /// 头寸管理器
    /// </summary>
    public class PositionManager
    {
        /// <summary>
        /// 头寸存储字典
        /// </summary>
        public Dictionary<string, GeneralPosition> Positions { get; set; } = new();
        /// <summary>
        /// 未平仓头寸数组
        /// </summary>
        public GeneralPosition[] OpenedPositions => Positions.Values.Where(p => p.Size != 0).ToArray();
        /// <summary>
        /// 已平仓头寸数组
        /// </summary>
        public GeneralPosition[] ClosedPositions => Positions.Values.Where(p => p.Size == 0).ToArray();
        /// <summary>
        /// 现金
        /// </summary>
        public double Cash { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public PositionManager()
        {
        }

        public void OnPriceUpdate(MarketFrame marketFrame)
        {
            // 更新未实现收益
            foreach (var position in Positions.Values)
            {
                position.CurrentPrice = marketFrame[position.ShareName].NowPrice;
            }
        }

        internal void OnTradeUpdate(int orderId, GeneralOrder order, (DateTime time, double tradePrice, double tradeVolume) tradeInfo, OrderUpdatedEventOperation operation)
        {
            Console.WriteLine($"头寸管理器:头寸已更新.\t股票名称:{order.ShareName}, 价格:{tradeInfo.tradePrice}, 数量:{tradeInfo.tradeVolume}, 方向:{order.Direction}.");

            Trade(order.ShareName, tradeInfo.tradePrice, tradeInfo.tradeVolume, order.Direction);
        }

        /// <summary>
        /// 添加头寸, 若存在则累加, 若不存在则创建
        /// </summary>
        /// <param name="shareName"></param>
        /// <param name="size"></param>
        /// <param name="cost"></param>
        public void SetPosition(string shareName, double size, double cost)
        {
            if (!Positions.ContainsKey(shareName))
                Positions.Add(shareName, new GeneralPosition(shareName, size, cost));
            else
            {
                var currentPosition = Positions[shareName];
                var size_out = currentPosition.Size + size;
                var cost_out = size_out != 0 ? (currentPosition.CostValue + cost * size) / (currentPosition.Size + size) : 0;
                double realizedPnl;
                if (size_out == 0)
                {
                    // 平仓时PNL未实现损益移至已实现损益并清零未实现损益
                    realizedPnl = currentPosition.UnRealizedPnl + currentPosition.RealizedPnl;
                }
                else
                {
                    realizedPnl = currentPosition.RealizedPnl;
                }
                currentPosition.Size = size_out;
                currentPosition.Cost = cost_out;
                currentPosition.RealizedPnl = realizedPnl;
            }

        }

        /// <summary>
        /// 交易
        /// </summary>
        /// <param name="shareName"></param>
        /// <param name="price"></param>
        /// <param name="size"></param>
        /// <param name="direction"></param>
        public void Trade(string shareName, double price, double size, OrderDirection direction)
        {
            // 设置股权头寸
            SetPosition(shareName, direction == OrderDirection.Long ? size : -size, price);
            // 计算现金剩余(Short股权时货币方向为Long)
            Cash += direction == OrderDirection.Short ? price * size : -price * size;
        }

        public override string ToString()
        {
            return ClassFormatter.Formatter(Positions.Values);
        }
    }
}
