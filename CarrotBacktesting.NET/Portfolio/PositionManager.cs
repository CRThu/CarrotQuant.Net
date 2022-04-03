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
        public const string DEFAULT_CASH_KEY = "$CASH$";
        public Dictionary<string, GeneralPosition> Positions { get; set; } = new();

        /// <summary>
        /// 构造函数
        /// </summary>
        public PositionManager()
        {
            Positions.Add(DEFAULT_CASH_KEY, DEFAULT_CASH_VALUE);
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
        }

        public static string GeneratePositionKey(GeneralPosition position)
        {
            return $"{position.ExchangeName}|{position.ShareName}";
        }

        public static GeneralPosition GenerateCashPosition(double cash)
        {
            return new(DEFAULT_CASH_KEY, DEFAULT_CASH_KEY, cash, PositionDirectionEnum.Long);
        }
    }
}
