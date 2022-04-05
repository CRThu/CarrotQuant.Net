using CarrotBacktesting.Net.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio
{
    /// <summary>
    /// 常规头寸存储类
    /// </summary>
    public class GeneralPosition
    {
        /// <summary>
        /// 交易所名称
        /// </summary>
        public string ExchangeName { get; set; }
        /// <summary>
        /// 股票名称
        /// </summary>
        public string ShareName { get; set; }
        /// <summary>
        /// 头寸大小
        /// </summary>
        public double Size { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="exchangeName"></param>
        /// <param name="shareName"></param>
        /// <param name="size"></param>
        /// <param name="direction"></param>
        public GeneralPosition(string exchangeName, string shareName, double size, TradeDirection direction = TradeDirection.Long)
        {
            ExchangeName = exchangeName;
            ShareName = shareName;
            Size = direction == TradeDirection.Long ? size : -size;
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="generalOrder"></param>
        public GeneralPosition(GeneralOrder generalOrder)
        {
            ExchangeName = generalOrder.ExchangeName;
            ShareName = generalOrder.ShareName;
            Size = generalOrder.Direction == TradeDirection.Long ? generalOrder.Size : -generalOrder.Size;
        }

        /// <summary>
        /// 判断两个头寸类是否为同一标的
        /// </summary>
        /// <param name="positionA"></param>
        /// <param name="positionB"></param>
        /// <returns></returns>
        public static bool IsSameShare(GeneralPosition positionA, GeneralPosition positionB)
        {
            return positionA.ExchangeName == positionB.ExchangeName
            && positionA.ShareName == positionB.ShareName;
        }

        /// <summary>
        /// 判断两个头寸类是否为同一标的
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public bool IsSameShare(GeneralPosition position)
        {
            return IsSameShare(this, position);
        }

        public override string ToString()
        {
            return Serializer.Serialize(this);
        }
    }
}
