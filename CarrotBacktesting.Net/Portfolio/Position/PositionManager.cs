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
        public const string RESERVED_SYMBOLS = "|";
        public const string DEFAULT_CASH_NAME = "$CASH$";
        public const string DEFAULT_CASH_KEY = $"{DEFAULT_CASH_NAME}{RESERVED_SYMBOLS}{DEFAULT_CASH_NAME}";

        /// <summary>
        /// 头寸存储字典
        /// </summary>
        public Dictionary<string, GeneralPosition> Positions { get; set; } = new();

        /// <summary>
        /// 剩余现金
        /// </summary>
        public double Cash
        {
            get
            {
                return Positions[DEFAULT_CASH_KEY].Size;
            }
            set
            {
                SetCash(value);
            }
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        public PositionManager()
        {
            SetCash(0);
        }

        /// <summary>
        /// 设置现金数量(使用特殊键存放与Position字典, Key:"$CASH$|$CASH$", Value.Size=1000)
        /// </summary>
        /// <param name="cash"></param>
        /// <param name="exchangeName"></param>
        public void SetCash(double cash)
        {
            if (!Positions.ContainsKey(DEFAULT_CASH_KEY))
                Positions.Add(DEFAULT_CASH_KEY, GenerateCashPosition(cash));
            else
                Positions[DEFAULT_CASH_KEY].Size = cash;
        }

        /// <summary>
        /// 添加头寸, 若存在则累加, 若不存在则创建
        /// </summary>
        /// <param name="value"></param>
        /// <exception cref="Exception"></exception>
        public void SetPosition(GeneralPosition value)
        {
            string key = GeneratePositionKey(value);
            if (!Positions.ContainsKey(key))
                Positions.Add(key, value);
            else if (Positions[key].IsSameShare(value))
            {
                Positions[key].Cost = (Positions[key].Cost * Positions[key].Size + value.Cost * value.Size) / (Positions[key].Size + value.Size);
                Positions[key].Size += value.Size;
            }
            else
                throw new Exception($"PositionManager程序键缓存错误,Key={key},Value={value}.");

            // 若头寸为0,则移除头寸类
            if (Positions[key].Size == 0)
                Positions.Remove(key);
        }

        /// <summary>
        /// 交易方法
        /// </summary>
        /// <param name="exchangeName"></param>
        /// <param name="shareName"></param>
        /// <param name="price"></param>
        /// <param name="size"></param>
        /// <param name="direction"></param>
        /// <returns>返回本次交易股权头寸类</returns>
        public GeneralPosition Trade(string exchangeName, string shareName, double price, double size, OrderDirection direction)
        {
            // 设置股权头寸
            var tradePosition = new GeneralPosition(exchangeName, shareName, size, price, direction);
            SetPosition(tradePosition);
            // 计算现金剩余(Short股权时货币方向为Long)
            Positions[DEFAULT_CASH_KEY].Size += direction == OrderDirection.Short ? price * size : -price * size;

            return tradePosition;
        }

        /// <summary>
        /// 生成Position字典头寸存储键
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public static string GeneratePositionKey(GeneralPosition position)
        {
            return $"{position.ExchangeName}|{position.ShareName}";
        }

        /// <summary>
        /// 生成Position字典现金存储值, 返回现金的头寸类
        /// </summary>
        /// <param name="cash"></param>
        /// <returns></returns>
        public static GeneralPosition GenerateCashPosition(double cash)
        {
            return new(DEFAULT_CASH_NAME, DEFAULT_CASH_NAME, cash, 0, OrderDirection.Long);
        }

        public override string ToString()
        {
            return Serializer.Serialize(this);
        }
    }
}
