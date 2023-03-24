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
        /// 头寸存储字典
        /// </summary>
        private Dictionary<string, GeneralPosition> PositionsStorage { get; set; }

        /// <summary>
        /// 现金
        /// </summary>
        public double Cash { get; set; }

        /// <summary>
        /// 全部持仓头寸集合
        /// </summary>
        public IEnumerable<GeneralPosition> Positions
        {
            get
            {
                return PositionsStorage.Values;
            }
        }

        /// <summary>
        /// 持仓头寸数量
        /// </summary>
        public int Count => PositionsStorage.Count;

        /// <summary>
        /// 获取头寸
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <returns>头寸, 若不存在返回null</returns>
        public GeneralPosition? this[string stockCode]
        {
            get
            {
                TryGetPosition(stockCode, out GeneralPosition? val);
                return val;
            }
        }

        /// <summary>
        /// 现金更新委托
        /// </summary>
        /// <param name="cash">现金数量</param>
        public delegate void CashUpdateDelegate(double cash);
        /// <summary>
        /// 现金更新事件
        /// </summary>
        public event CashUpdateDelegate? CashUpdateEvent;

        /// <summary>
        /// 头寸更新委托
        /// </summary>
        /// <param name="positionEventArgs"></param>
        public delegate void PositionUpdateDelegate(PositionEventArgs positionEventArgs);
        /// <summary>
        /// 头寸更新事件
        /// </summary>
        public event PositionUpdateDelegate? PositionUpdateEvent;

        /// <summary>
        /// 构造函数
        /// </summary>
        public PositionManager(SimulationOptions options)
        {
            PositionsStorage = new();
            UpdateCash(options.InitialCash);
        }

        /// <summary>
        /// 获取头寸信息
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <param name="position">头寸信息类, 不存在则返回null</param>
        /// <returns>是否存在该头寸</returns>
        public bool TryGetPosition(string stockCode, out GeneralPosition? position)
        {
            return PositionsStorage.TryGetValue(stockCode, out position);
        }

        ///// <summary>
        ///// 获取头寸持仓成本
        ///// </summary>
        ///// <param name="stockCode">股票代码</param>
        ///// <returns>头寸持仓成本, 若不存在则返回null</returns>
        //public double? GetPositionCost(string stockCode)
        //{
        //    bool hasPosition = TryGetPosition(stockCode, out GeneralPosition? position);
        //    if (hasPosition)
        //    {
        //        return position!.Cost;
        //    }
        //    else
        //    {
        //        return null;
        //    }
        //}

        ///// <summary>
        ///// 获取头寸持仓量
        ///// </summary>
        ///// <param name="stockCode">股票代码</param>
        ///// <returns>头寸持仓量, 若不存在则返回null</returns>
        //public double? GetPositionSize(string stockCode)
        //{
        //    bool hasPosition = TryGetPosition(stockCode, out GeneralPosition? position);
        //    if (hasPosition)
        //    {
        //        return position!.Size;
        //    }
        //    else
        //    {
        //        return null;
        //    }
        //}

        /// <summary>
        /// 新建头寸
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <param name="price">成交价</param>
        /// <param name="size">成交数量</param>
        public void CreatePosition(string stockCode, double price, double size)
        {
            var position = new GeneralPosition(stockCode, price, size);
            PositionsStorage.Add(stockCode, position);
        }

        /// <summary>
        /// 更新头寸(若不存在则创建, 若清仓则删除)
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <param name="price">成交价</param>
        /// <param name="size">成交数量</param>
        public void UpdatePosition(string stockCode, double price, double size)
        {
            if (TryGetPosition(stockCode, out GeneralPosition? position))
            {
                position!.Trade(size, price);
            }
            else
            {
                CreatePosition(stockCode, price, size);
            }

            PositionEventArgs positionEventArgs = new(stockCode, price, size);
            PositionUpdateEvent?.Invoke(positionEventArgs);

            // 若清仓则删除持仓头寸数据
            if (position!.Size == 0)
                PositionsStorage.Remove(stockCode);

            // 计算现金剩余
            UpdateCash(Cash - price * size);
        }

        /// <summary>
        /// 更新Cash
        /// </summary>
        /// <param name="cash"></param>
        public void UpdateCash(double cash)
        {
            Cash = cash;
            CashUpdateEvent?.Invoke(cash);
        }

        /// <summary>
        /// 交易所成交更新事件回调
        /// </summary>
        /// <param name="_">交易所实例</param>
        /// <param name="tradeEventArgs">成交事件参数</param>
        public void OnTradeUpdate(BackTestingExchange _, TradeEventArgs tradeEventArgs)
        {
            UpdatePosition(tradeEventArgs.StockCode, tradeEventArgs.Price, tradeEventArgs.Volume);
        }
    }
}
