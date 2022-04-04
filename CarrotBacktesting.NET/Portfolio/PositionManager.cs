using CarrotBacktesting.NET.Portfolio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio
{
    public class PositionManager
    {
        public const string RESERVED_SYMBOLS = "$|";
        public const string DEFAULT_CASH_NAME = "$CASH$";
        public Dictionary<string, GeneralPosition> Positions { get; set; } = new();

        /// <summary>
        /// 构造函数, 默认初始化为100000现金
        /// </summary>
        public PositionManager()
        {
            SetCash(100000);
        }

        /// <summary>
        /// 设置现金数量(存放在Position字典, Key:"$CASH$|$CASH$", Value.Size=1000)
        /// </summary>
        /// <param name="cash"></param>
        /// <param name="exchangeName"></param>
        public void SetCash(double cash, string exchangeName = DEFAULT_CASH_NAME)
        {
            string key = GenerateCashKey(exchangeName);
            var position = GenerateCashPosition(cash, exchangeName);
            if (!Positions.ContainsKey(key))
                Positions.Add(key, position);
            else
                Positions[key] = position;
        }

        /// <summary>
        /// 添加头寸, 若存在则累加, 若不存在则创建
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void AddPosition(GeneralPosition value)
        {
            string key = GeneratePositionKey(value);
            if (!Positions.ContainsKey(key))
                Positions.Add(key, value);
            else if (Positions[key].IsSameShare(value))
                Positions[key].Size += value.Size;
            else
                throw new Exception($"PositionManager程序键缓存错误,Key={key},Value={value}.");

            // 若头寸为0,则移除头寸类
            if (Positions[key].Size == 0)
                Positions.Remove(key);
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
        /// 生成Position字典现金存储键
        /// </summary>
        /// <param name="exchangeName"></param>
        /// <returns></returns>
        public static string GenerateCashKey(string exchangeName = DEFAULT_CASH_NAME)
        {
            return $"{exchangeName}|{DEFAULT_CASH_NAME}";
        }

        /// <summary>
        /// 生成Position字典现金存储值, 返回现金的头寸类
        /// </summary>
        /// <param name="cash"></param>
        /// <param name="exchangeName"></param>
        /// <returns></returns>
        public static GeneralPosition GenerateCashPosition(double cash, string exchangeName = DEFAULT_CASH_NAME)
        {
            return new(exchangeName, DEFAULT_CASH_NAME, cash, PositionDirectionEnum.Long);
        }
    }
}
