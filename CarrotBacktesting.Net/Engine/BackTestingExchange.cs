using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Portfolio;
using CarrotBacktesting.Net.Portfolio.Order;
using CarrotBacktesting.Net.Portfolio.Position;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CarrotBacktesting.Net.Common.Enums;


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
        public Dictionary<string, Dictionary<int, GeneralOrder>> Orders { get; set; }

        /// <summary>
        /// 头寸存储字典
        /// </summary>
        //public Dictionary<string, GeneralPosition> Positions { get; set; }

        /// <summary>
        /// 当前余额
        /// </summary>
        //public double Cash { get; set; }

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
            Orders = new();
            //Positions = new();
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
        /// 新增或更新委托单(交易所内部)
        /// </summary>
        /// <param name="order">委托单实例</param>
        private void AddOrUpdateOrder(GeneralOrder order)
        {
            if (!Orders.ContainsKey(order.StockCode))
                Orders.Add(order.StockCode, new());
            if (!Orders[order.StockCode].ContainsKey(order.OrderId))
                Orders[order.StockCode].Add(order.OrderId, order.Clone());
            else
                Orders[order.StockCode][order.OrderId] = order.Clone();
        }

        /// <summary>
        /// 移除委托单(交易所内部)
        /// </summary>
        /// <param name="order"></param>
        private void RemoveOrder(GeneralOrder order)
        {
            Orders[order.StockCode].Remove(order.OrderId);
            if (Orders[order.StockCode].Count == 0)
                Orders.Remove(order.StockCode);
        }

        /// <summary>
        /// 同步订单簿
        /// </summary>
        /// <param name="sender">委托单管理器</param>
        /// <param name="operation">委托单事件参数</param>
        public void SyncOrderBook(OrderManager sender, OrderEventArgs operation)
        {
            GeneralOrder order = sender[operation.OrderId]!;
            switch (operation.Operation)
            {
                case OrderUpdatedEventOperation.CreateOrder:
                    AddOrUpdateOrder(order);
                    break;
                case OrderUpdatedEventOperation.CancelOrder:
                    RemoveOrder(order);
                    break;
                case OrderUpdatedEventOperation.UpdateOrder:
                    // 若更新的委托单不为待成交则删除
                    if (order.Status != GeneralOrderStatus.Pending)
                        RemoveOrder(order);
                    else
                        AddOrUpdateOrder(order);
                    break;
            }
        }

        /// <summary>
        /// 交易所检查订单簿成交
        /// </summary>
        /// <param name="marketFrame">市场信息</param>
        public void CheckOrders(MarketFrame marketFrame)
        {
            // 遍历股票代码委托单组
            foreach (var orderGroup in Orders)
            {
                // 股票信息
                ShareFrame shareInfo = marketFrame[orderGroup.Key]!;
                // 总流动性
                double estimateLiquidity = shareInfo.Volume * Options.ExchangeEstimateLiquidityRatio;
                // 成交价参考
                var exchangePrice = Options.ExchangePriceRef switch {
                    ExchangePriceRef.Open => shareInfo.Open,
                    ExchangePriceRef.Close => shareInfo.Close,
                    _ => throw new NotImplementedException("Not Implemented ExchangePriceRef: ExchangePriceRef.TradeAverage"),
                };

                // 若股票不能交易或无成交量则继续遍历下一支股票
                if ((!shareInfo.Status) || estimateLiquidity == 0)
                    continue;

                // 遍历此股票的委托单
                foreach (var order in orderGroup.Value.Values)
                {
                    if (((order.Type == OrderType.LimitOrder)
                        && ((order.Direction == OrderDirection.Buy && order.Price >= exchangePrice)
                        || (order.Direction == OrderDirection.Sell && order.Price <= exchangePrice))
                        )
                    || (order.Type == OrderType.MarketOrder))
                    {
                        // 成交量限制计算
                        double tradeVolume = Math.Min(estimateLiquidity, order.PendingSize);
                        double tradePrice;
                        if (order.Direction == OrderDirection.Buy)
                        {
                            // 滑点计算
                            tradePrice = (1 + Options.ExchangePriceSlippage) * exchangePrice;
                            // 手续费计算
                            tradePrice *= (1 + Options.ExchangeTradeFeeRatio);
                        }
                        else
                        {
                            // 滑点计算
                            tradePrice = (1 - Options.ExchangePriceSlippage) * exchangePrice;
                            // 手续费计算
                            tradePrice *= (1 - Options.ExchangeTradeFeeRatio);
                        }

                        // TODO 余额必须为正的规则
                        TradeEventArgs tradeEventArgs = new(order.OrderId, shareInfo.Time, shareInfo.Code, order.Direction, tradePrice, tradeVolume);
                        estimateLiquidity -= tradeVolume;
                        OnTradeUpdate?.Invoke(this, tradeEventArgs);
                    }
                }
            }
        }

        /// <summary>
        /// 委托单更新事件订阅方法
        /// </summary>
        /// <param name="sender">委托单管理器</param>
        /// <param name="operation">委托单事件参数</param>
        public void OnOrderUpdate(OrderManager sender, OrderEventArgs operation)
        {
            SyncOrderBook(sender, operation);
        }

        /// <summary>
        /// 市场信息更新事件订阅方法
        /// </summary>
        /// <param name="marketFrame">市场信息</param>
        /// <param name="marketEventArgs">市场事件参数</param>
        public void OnMarketUpdate(MarketFrame marketFrame, MarketEventArgs marketEventArgs)
        {
            if (marketEventArgs.IsMarketOpen)
                CheckOrders(marketFrame);
        }
    }
}
