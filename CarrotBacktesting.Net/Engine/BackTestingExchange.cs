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
    public class BackTestingExchange
    {
        /// <summary>
        /// 委托单存储字典
        /// </summary>
        public Dictionary<int, GeneralOrder> Orders { get; set; } = new();

        /// <summary>
        /// 当前时间市场帧
        /// </summary>
        public MarketFrame MarketFrame { get; set; }

        /// <summary>
        /// 当前交易信息
        /// TODO
        /// </summary>
        public PortfolioManager Portfolio { get; set; }

        /// <summary>
        /// 交易所成交更新委托
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="tradeEventArgs"></param>
        public delegate void TradeUpdatedHandler(BackTestingExchange sender, TradeEventArgs tradeEventArgs);

        /// <summary>
        /// 交易所成交更新事件
        /// </summary>
        public event TradeUpdatedHandler? OnTradeUpdated;

        /// <summary>
        /// 构造函数
        /// </summary>
        public BackTestingExchange()
        {
            EventRegister();
        }

        /// <summary>
        /// 事件监听
        /// </summary>
        public void EventRegister()
        {
            // 委托单更新事件监听
            //Portfolio.OrderManager.OnOrderUpdated += OnOrderUpdate;
            throw new NotImplementedException();
            // 交易所成交更新事件订阅
            OnTradeUpdated += Portfolio.OrderManager.OnTradeUpdate;
            // 头寸管理器更新
            //OnTradeUpdated += Portfolio.PositionManager.OnTradeUpdate;
            throw new NotImplementedException();
        }

        /// <summary>
        /// 交易信息同步
        /// </summary>
        /// <param name="portfolio"></param>
        public void SyncTradeInfo(PortfolioManager portfolio)
        {
            Portfolio = portfolio;
        }

        /// <summary>
        /// 市场信息同步
        /// </summary>
        /// <param name="marketFrame"></param>
        public void SyncMarketInfo(MarketFrame marketFrame)
        {
            MarketFrame = marketFrame;
        }

        /// <summary>
        /// 市场价格更新
        /// </summary>
        public void OnPriceUpdate()
        {
            CheckOrder();
        }

        /// <summary>
        /// 同步交易所的委托单
        /// </summary>
        /// <param name="orderId"></param>
        /// <param name="order"></param>
        /// <param name="operation"></param>
        /// <exception cref="Exception"></exception>
        public void OnOrderUpdate(int orderId, GeneralOrder order, OrderUpdatedEventOperation operation)
        {
            throw new NotImplementedException();
            switch (operation)
            {
                case OrderUpdatedEventOperation.CreateOrder:
                    Orders.Add(orderId, order);
                    break;
                //case OrderUpdatedEventOperation.RemoveOrder:
                //    Orders.Remove(orderId);
                //    break;
                default:
                    throw new Exception($"OnTradeUpdate(): operation={operation}, OrderId={orderId}.");
            }
        }

        /// <summary>
        /// 检查委托单是否成交
        /// </summary>
        public void CheckOrder()
        {
            foreach (var orderId in Orders.Keys)
            {
                var orderInfo = Orders[orderId];
                if (MarketFrame[orderInfo.StockCode].IsTrading)
                {
                    // TODO
                    if ((orderInfo.Price >= MarketFrame[orderInfo.StockCode].ClosePrice && orderInfo.Direction == OrderDirection.Buy)
                        || (orderInfo.Price <= MarketFrame[orderInfo.StockCode].ClosePrice && orderInfo.Direction == OrderDirection.Sell))
                    {
                        // 模拟委托单全部成交状态
                        // Console.WriteLine($"交易所({MarketFrame.DateTime:d}):委托单已被成交.\t股票名称:{orderInfo.ShareName}, 价格:{MarketFrame[orderInfo.ShareName].ClosePrice}, 数量:{orderInfo.Size}, 方向:{orderInfo.Direction}.");
                        double tradeSize = orderInfo.OrderSize;
                        orderInfo.OrderSize = 0;
                        throw new NotImplementedException();
                        //OrderDealEvent?.Invoke(orderId, orderInfo,
                        //    (MarketFrame.DateTime, MarketFrame[orderInfo.StockCode].ClosePrice, tradeSize),
                        //    OrderUpdatedEventOperation.RemoveOrder);
                        Orders.Remove(orderId);
                    }
                }
            }
        }
    }
}
