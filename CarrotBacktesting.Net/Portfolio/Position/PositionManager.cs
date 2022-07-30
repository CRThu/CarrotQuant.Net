using CarrotBacktesting.Net.Shared;
using CarrotBacktesting.Net.Portfolio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBacktesting.Net.Portfolio.Order;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.DataModel;

namespace CarrotBacktesting.Net.Portfolio.Position
{
    /// <summary>
    /// 头寸管理器
    /// </summary>
    public class PositionManager
    {
        /// <summary>
        /// 日期(临时)
        /// </summary>
        //public DateTime NowTime { get; set; }

        /// <summary>
        /// 头寸存储字典
        /// </summary>
        public Dictionary<string, GeneralPosition> PositionsStorage { get; set; } = new();

        /// <summary>
        /// 现金
        /// </summary>
        public double Cash { get; set; }

        /// <summary>
        /// 未平仓头寸数组
        /// </summary>
        //public GeneralPosition[] OpenedPositions => Positions.Values.Where(p => p.Size != 0).ToArray();
        /// <summary>
        /// 已平仓头寸数组
        /// </summary>
        //public GeneralPosition[] ClosedPositions => Positions.Values.Where(p => p.Size == 0).ToArray();


        public delegate void CashUpdateDelegate(DateTime time, double cash);
        public event CashUpdateDelegate? CashUpdateEvent;

        public delegate void PositionUpdateDelegate(DateTime time, string shareName, double cost, double size, OrderDirection direction);
        public event PositionUpdateDelegate? PositionUpdateEvent;

        /// <summary>
        /// 构造函数
        /// </summary>
        public PositionManager()
        {
        }

        /// <summary>
        /// 获取头寸信息
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <returns>头寸信息类</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public GeneralPosition GetPosition(string stockCode)
        {
            if (PositionsStorage.ContainsKey(stockCode))
            {
                return PositionsStorage[stockCode];
            }
            else
            {
                throw new InvalidOperationException($"不存在此头寸, StockCode:{stockCode}.");
            }
        }

        /// <summary>
        /// 获取头寸信息
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <param name="position">头寸信息类, 不存在则返回null</param>
        /// <returns>是否存在</returns>
        public bool TryGetPosition(string stockCode, out GeneralPosition? position)
        {
            return PositionsStorage.TryGetValue(stockCode, out position);
        }

        /// <summary>
        /// 获取头寸持仓成本
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <returns>头寸持仓成本</returns>
        public double GetPositionCost(string stockCode)
        {
            bool hasPosition = TryGetPosition(stockCode, out GeneralPosition? position);
            if (hasPosition)
            {
                return position!.Cost;
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// 获取头寸持仓量
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <returns>头寸持仓量</returns>
        public double GetPositionSize(string stockCode)
        {
            bool hasPosition = TryGetPosition(stockCode, out GeneralPosition? position);
            if (hasPosition)
            {
                return position!.Size;
            }
            else
            {
                return 0;
            }
        }


        public void UpdatePosition(string stockCode, double price, double size)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 市场价格更新
        /// </summary>
        /// <param name="marketFrame"></param>
        public void OnPriceUpdate(MarketFrame marketFrame)
        {
            // 更新日期
            //NowTime = marketFrame.DateTime;
            // 更新未实现收益
            foreach (var position in PositionsStorage.Values)
            {
                // TODO
                throw new NotImplementedException();
                //position.CurrentPrice = marketFrame[position.StockName].ClosePrice;
            }
        }

        /// <summary>
        /// 头寸更新
        /// </summary>
        /// <param name="orderId"></param>
        /// <param name="order"></param>
        /// <param name="tradeInfo"></param>
        /// <param name="operation"></param>
        internal void OnTradeUpdate(int orderId, GeneralOrder order, (DateTime time, double tradePrice, double tradeVolume) tradeInfo, OrderUpdatedEventOperation operation)
        {
            Console.WriteLine($"头寸管理器:头寸已更新.\t股票名称:{order.StockCode}, 价格:{tradeInfo.tradePrice}, 数量:{tradeInfo.tradeVolume}, 方向:{order.Direction}.");

            // 设置股权头寸
            SetPosition(order.StockCode, order.Direction == OrderDirection.Buy ? tradeInfo.tradeVolume : -tradeInfo.tradeVolume, tradeInfo.tradePrice);
            // 计算现金剩余(Short股权时货币方向为Long)
            Cash += order.Direction == OrderDirection.Sell ? tradeInfo.tradePrice * tradeInfo.tradeVolume : -tradeInfo.tradePrice * tradeInfo.tradeVolume;

            PositionUpdateEvent?.Invoke(tradeInfo.time, order.StockCode, tradeInfo.tradePrice, tradeInfo.tradeVolume, order.Direction);
        }

        /// <summary>
        /// 设置资金
        /// </summary>
        /// <param name="cash"></param>
        public void SetCash(double cash)
        {
            Cash += cash;
            //CashUpdateEvent?.Invoke(NowTime, cash);
        }

        /// <summary>
        /// 添加头寸, 若存在则累加, 若不存在则创建
        /// </summary>
        /// <param name="shareName"></param>
        /// <param name="size"></param>
        /// <param name="cost"></param>
        public void SetPosition(string shareName, double size, double cost)
        {
            if (!PositionsStorage.ContainsKey(shareName))
                PositionsStorage.Add(shareName, new GeneralPosition(shareName, size, cost));
            else
            {
                var currentPosition = PositionsStorage[shareName];
                var size_out = currentPosition.Size + size;
                var cost_out = size_out != 0 ? (currentPosition.CostValue + cost * size) / (currentPosition.Size + size) : 0;
                double realizedPnl;
                if (size_out == 0)
                {
                    // 平仓时PNL未实现损益移至已实现损益并清零未实现损益
                    throw new NotImplementedException();
                    //realizedPnl = currentPosition.UnRealizedPnl + currentPosition.RealizedPnl;
                }
                else
                {
                    throw new NotImplementedException();
                    //realizedPnl = currentPosition.RealizedPnl;
                }
                //currentPosition.Size = size_out;
                //currentPosition.Cost = cost_out;
                throw new NotImplementedException();
                //currentPosition.RealizedPnl = realizedPnl;
            }

        }

        public override string ToString()
        {
            return ClassFormatter.Formatter(PositionsStorage.Values);
        }
    }
}
