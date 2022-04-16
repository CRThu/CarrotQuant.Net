using CarrotBacktesting.Net.Shared;
using CarrotBacktesting.Net.Portfolio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBacktesting.Net.Portfolio.Order;

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
        /// 现金
        /// </summary>
        public double Cash { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public PositionManager()
        {
        }

        /// <summary>
        /// 添加头寸, 若存在则累加, 若不存在则创建
        /// </summary>
        /// <param name="value"></param>
        /// <exception cref="Exception"></exception>
        public void SetPosition(GeneralPosition value)
        {
            if (!Positions.ContainsKey(value.ShareName))
                Positions.Add(value.ShareName, value);
            else if (Positions[value.ShareName].IsSameShare(value))
            {
                Positions[value.ShareName].Cost = (Positions[value.ShareName].Cost * Positions[value.ShareName].Size + value.Cost * value.Size) / (Positions[value.ShareName].Size + value.Size);
                Positions[value.ShareName].Size += value.Size;
            }
            else
                throw new Exception($"PositionManager程序键缓存错误, Key={value.ShareName}, Value={value}.");

            // 若头寸为0,则移除头寸类
            if (Positions[value.ShareName].Size == 0)
                Positions.Remove(value.ShareName);
        }

        /// <summary>
        /// 交易方法
        /// </summary>
        /// <param name="shareName"></param>
        /// <param name="price"></param>
        /// <param name="size"></param>
        /// <param name="direction"></param>
        /// <returns>返回本次交易股权头寸类</returns>
        public GeneralPosition Trade(string shareName, double price, double size, OrderDirection direction)
        {
            // 设置股权头寸
            var tradePosition = new GeneralPosition(shareName, size, price, direction);
            SetPosition(tradePosition);
            // 计算现金剩余(Short股权时货币方向为Long)
            Cash += direction == OrderDirection.Short ? price * size : -price * size;

            return tradePosition;
        }

        public override string ToString()
        {
            return Serializer.Serialize(this);
        }
    }
}
