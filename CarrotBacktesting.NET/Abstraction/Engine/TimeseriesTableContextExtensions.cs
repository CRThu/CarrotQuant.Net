using System;
using System.Runtime.CompilerServices;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data;

namespace CarrotBacktesting.NET.Abstraction.Engine
{
    /// <summary>
    /// 提供基于 <see cref="IEngineContext"/> 当前时间步访问 <see cref="ITimeseriesTable"/> 数据的扩展方法。
    /// </summary>
    public static class TimeseriesTableContextExtensions
    {
        /// <summary>
        /// 获取子表在当前上下文时间步对应的数据行索引。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetCurrentRowIndex(this ITimeseriesTable table, IEngineContext context)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(context);
            return table.GetCurrentRowIndex(context.CurrentIndex);
        }

        /// <summary>
        /// 获取子表在当前上下文时间步对应行的标的数据切片。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<T> GetCurrentRow<T>(this ITimeseriesTable table, string fieldName, IEngineContext context) where T : unmanaged
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(context);
            return table.GetCurrentRow<T>(fieldName, context.CurrentIndex);
        }

        /// <summary>
        /// 根据标的列索引获取子表在当前上下文时间步对应的字段值。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T GetCurrentValue<T>(this ITimeseriesTable table, string fieldName, int symbolIndex, IEngineContext context) where T : unmanaged
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(context);
            return table.GetCurrentValue<T>(fieldName, symbolIndex, context.CurrentIndex);
        }

        /// <summary>
        /// 根据标的代码获取子表在当前上下文时间步对应的字段值。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T GetCurrentValue<T>(this ITimeseriesTable table, string fieldName, string symbol, IEngineContext context) where T : unmanaged
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(context);
            int col = table.Metadata.GetSymbolIndex(symbol);
            return col >= 0 ? table.GetCurrentValue<T>(fieldName, col, context.CurrentIndex) : MarketDataAligner.GetNaNValue<T>();
        }
    }
}
