using System;
using System.Runtime.CompilerServices;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序二维缓冲区（IReadOnlyBuffer2D）的实用扩展方法。
    /// </summary>
    public static class BufferExtensions
    {
        /// <summary>
        /// 判定指定单元格 (row, col) 的数值是否为 NaN 或缺失哨兵值。
        /// 无论底层是浮点类型（double.NaN / float.NaN）还是整型哨兵（MinValue），统一一键判定。
        /// </summary>
        /// <typeparam name="T">非托管数值类型</typeparam>
        /// <param name="buffer">只读二维缓冲区</param>
        /// <param name="row">交易日行索引</param>
        /// <param name="col">标的代码列索引</param>
        /// <returns>若该单元格为 NaN 或缺失哨兵值则返回 true，否则返回 false</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNaN<T>(this IReadOnlyBuffer2D<T> buffer, int row, int col) where T : unmanaged
        {
            return MarketDataAligner.IsValueNaN(buffer[row, col]);
        }
    }
}
