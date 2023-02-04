using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Portfolio;
using CarrotBacktesting.Net.Portfolio.Order;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 回测交易所
    /// </summary>
    public class BackTestingExchange
    {
        /// <summary>
        /// 委托单存储字典
        /// </summary>
        public Dictionary<int, GeneralOrder> Orders { get; set; } = new();

        ///// <summary>
        ///// 当前交易信息
        ///// TODO
        ///// </summary>
        //public PortfolioManager Portfolio { get; set; }

        /// <summary>
        /// 当前余额
        /// </summary>
        public double Cash { get; set; }

        /// <summary>
        /// 回测设置
        /// </summary>
        private SimulationOptions Options { get; set; }

        /// <summary>
        /// 交易所成交更新委托
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="tradeEventArgs"></param>
        public delegate void TradeUpdateHandler(BackTestingExchange sender, TradeEventArgs tradeEventArgs);

        /// <summary>
        /// 交易所成交更新事件
        /// </summary>
        public event TradeUpdateHandler? OnTradeUpdate;

        /// <summary>
        /// 构造函数
        /// </summary>
        public BackTestingExchange(SimulationOptions options)
        {
            Options = options;
            //EventRegister();
        }

        ///// <summary>
        ///// 事件监听
        ///// </summary>
        //public void EventRegister()
        //{
        //    // 委托单更新事件监听
        //    //Portfolio.OrderManager.OnOrderUpdated += OnOrderUpdate;
        //    throw new NotImplementedException();
        //    // 交易所成交更新事件订阅
        //    //OnTradeUpdated += Portfolio.OrderManager.OnTradeUpdate;
        //    // 头寸管理器更新
        //    //OnTradeUpdated += Portfolio.PositionManager.OnTradeUpdate;
        //    throw new NotImplementedException();
        //}

        /// <summary>
        /// 委托单更新事件订阅方法
        /// </summary>
        /// <param name="sender">委托单管理器</param>
        /// <param name="operation">委托单事件参数</param>
        public void OnOrderUpdate(OrderManager sender, OrderEventArgs operation)
        {
            switch (operation.Operation)
            {
                case OrderUpdatedEventOperation.CreateOrder:
                    Orders.Add(operation.OrderId, sender[operation.OrderId]);
                    break;
                case OrderUpdatedEventOperation.CancelOrder:
                    Orders.Remove(operation.OrderId);
                    break;
                case OrderUpdatedEventOperation.UpdateOrder:
                    if (Orders.ContainsKey(operation.OrderId))
                    {
                        Orders[operation.OrderId] = sender[operation.OrderId];
                        // 若更新的委托单不为待成交则删除
                        if (Orders[operation.OrderId].Status != GeneralOrderStatus.Pending)
                            Orders.Remove(operation.OrderId);
                    }
                    else
                    {
                        throw new InvalidOperationException($"交易所委托单中不存在此委托, Id:{operation.OrderId}.");
                    }
                    break;
            }
        }

        /// <summary>
        /// 市场信息更新事件订阅方法
        /// </summary>
        /// <param name="marketFrame">市场信息</param>
        /// <param name="marketEventArgs">市场事件参数</param>
        public void OnMarketUpdate(MarketFrame marketFrame, MarketEventArgs marketEventArgs)
        {
            CheckOrders(marketFrame, marketEventArgs.IsMarketOpen);
        }

        /// <summary>
        /// 余额更新事件订阅方法
        /// </summary>
        /// <param name="cash"></param>
        public void OnCashUpdate(double cash)
        {
            Cash = cash;
        }

        /// <summary>
        /// 交易所检查委托单成交
        /// </summary>
        /// <param name="marketFrame">市场信息</param>
        /// <param name="isMarketOpen">市场是否可交易</param>
        public void CheckOrders(MarketFrame marketFrame, bool isMarketOpen)
        {
            if (isMarketOpen)
            {
                // 按股票代码分组遍历
                foreach (var orderGroup in Orders.Values.GroupBy(o => o.StockCode))
                {
                    var shareInfo = marketFrame[orderGroup.First().StockCode];
                    // 总流动性
                    double estimateLiquidity = shareInfo.Volume * Options.ExchangeEstimateLiquidityRatio;
                    // 遍历所有待成交的委托单
                    foreach (var order in orderGroup.Where(o => o.Status == GeneralOrderStatus.Pending))
                    {
                        // TODO IF SUPPORT ISTRADING
                        if (shareInfo.Status)
                        {
                            double tradeVolume;
                            double tradePrice;
                            TradeEventArgs tradeEventArgs;
                            switch (order.Type)
                            {
                                case OrderType.LimitOrder:
                                    if ((order.Direction == OrderDirection.Buy && order.Price >= shareInfo.Close)
                                        || (order.Direction == OrderDirection.Sell && order.Price <= shareInfo.Close))
                                    {
                                        tradePrice = shareInfo.Close;
                                        // 成交量限制计算
                                        tradeVolume = Math.Min(estimateLiquidity, order.PendingSize);
                                        tradeVolume = Math.Min(Cash / tradePrice, tradeVolume);
                                        if (tradeVolume > 0)
                                        {
                                            tradeEventArgs = new(order.OrderId, shareInfo.Time, shareInfo.Code, order.Direction, tradePrice, tradeVolume);
                                            OnTradeUpdate?.Invoke(this, tradeEventArgs);
                                            estimateLiquidity -= tradeVolume;
                                        }
                                    }
                                    break;
                                case OrderType.MarketOrder:
                                    tradePrice = 1 + (order.Direction == OrderDirection.Buy ? 1 : -1) * shareInfo.Close * Options.ExchangePriceSlippage;
                                    // 成交量限制计算
                                    tradeVolume = Math.Min(estimateLiquidity, order.PendingSize);
                                    tradeVolume = Math.Min(Cash / tradePrice, tradeVolume);
                                    if (tradeVolume > 0)
                                    {
                                        tradeEventArgs = new(order.OrderId, shareInfo.Time, shareInfo.Code, order.Direction, tradePrice, tradeVolume);
                                        OnTradeUpdate?.Invoke(this, tradeEventArgs);
                                        estimateLiquidity -= tradeVolume;
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
        }
    }
}
