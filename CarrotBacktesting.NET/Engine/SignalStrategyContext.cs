using CarrotBacktesting.NET.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Engine
{
    /// <summary>
    /// 为策略提供数据访问的上下文
    /// </summary>
    public class SignalStrategyContext
    {
        /// <summary>
        /// 当前股票的时间序列数据
        /// </summary>
        public StockHistory Series { get; }

        /// <summary>
        /// 当前计算点在时间序列中的索引
        /// </summary>
        public int CurrentIndex { get; set; }

        /// <summary>
        /// 当前计算点的时间
        /// </summary>
        public DateTime CurrentDate => Series.Dates[CurrentIndex];

        public SignalStrategyContext(StockHistory series)
        {
            Series = series;
        }

        /// <summary>
        /// 获取相对于当前点的StockFrame
        /// </summary>
        /// <param name="offset">偏移量 (0 for current, -1 for previous, etc.)</param>
        /// <returns>StockFrame, 如果越界或无数据则返回null</returns>
        public StockFrame? GetFrame(int offset)
        {
            int index = CurrentIndex + offset;
            if (index >= 0 && index < Series.Data.Count)
            {
                return Series.Data[index];
            }
            return null;
        }

        public double? GetOpen(int offset) => GetFrame(offset)?.Open;
        public double? GetHigh(int offset) => GetFrame(offset)?.High;
        public double? GetLow(int offset) => GetFrame(offset)?.Low;
        public double? GetClose(int offset) => GetFrame(offset)?.Close;
        public double? GetVolume(int offset) => GetFrame(offset)?.Volume;

        public double? GetValue(string column, int offset)
        {
            return column.ToLower() switch
            {
                "open" => GetOpen(offset),
                "high" => GetHigh(offset),
                "low" => GetLow(offset),
                "close" => GetClose(offset),
                "volume" => GetVolume(offset),
                _ => null,
            };
        }
    }
}