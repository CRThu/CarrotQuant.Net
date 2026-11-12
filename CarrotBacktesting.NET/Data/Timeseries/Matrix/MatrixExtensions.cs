using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序二维矩阵（<see cref="IReadOnlyMatrix2D{T}"/>）与市场数据集（<see cref="ITimeseriesRegistry"/>）的实用扩展方法。
    /// 为单标的回测（CTA、期货、加密货币、单股策略）及标的时序分析提供轻量、零拷贝的一维时序序列语义。
    /// </summary>
    public static class MatrixExtensions
    {
        /// <summary>
        /// 判定指定单元格 (row, col) 的数值是否为 NaN 或缺失哨兵值。
        /// 无论底层是浮点类型（double.NaN / float.NaN）还是整型哨兵（MinValue），统一一键判定。
        /// </summary>
        /// <typeparam name="T">非托管数值类型</typeparam>
        /// <param name="matrix">只读二维时序矩阵</param>
        /// <param name="row">交易日行索引</param>
        /// <param name="col">标的代码列索引</param>
        /// <returns>若该单元格为 NaN 或缺失哨兵值则返回 true，否则返回 false</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNaN<T>(this IReadOnlyMatrix2D<T> matrix, int row, int col) where T : unmanaged
        {
            return MarketDataAligner.IsValueNaN(matrix[row, col]);
        }

        /// <summary>
        /// 获取指定标的列（默认为第 0 列标的）的全生命周期只读一维时序序列视图。
        /// 针对单标的回测（CTA、期货、加密货币、单股策略），消除二维矩阵认知负担与切片计算摩擦。
        /// </summary>
        /// <typeparam name="T">非托管数值类型</typeparam>
        /// <param name="matrix">只读二维时序矩阵</param>
        /// <param name="symbolIndex">标的列索引，默认为 0（单标的回测场景）</param>
        /// <returns>标的的全周期连续只读一维列视图</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlyColumnView<T> GetSeries<T>(this IReadOnlyMatrix2D<T> matrix, int symbolIndex = 0) where T : unmanaged
        {
            return matrix.GetColumnView(0, symbolIndex, matrix.RowCount);
        }

        /// <summary>
        /// 根据标的代码名称获取其全生命周期只读一维时序序列视图。
        /// </summary>
        /// <typeparam name="T">非托管数值类型</typeparam>
        /// <param name="matrix">只读二维时序矩阵</param>
        /// <param name="symbol">标的代码（如 "sh.600000"）</param>
        /// <param name="metadata">市场元数据</param>
        /// <returns>标的的全周期连续只读一维列视图</returns>
        public static ReadOnlyColumnView<T> GetSeries<T>(this IReadOnlyMatrix2D<T> matrix, string symbol, IMarketMetadata metadata) where T : unmanaged
        {
            ArgumentNullException.ThrowIfNull(metadata);
            int symbolIndex = metadata.GetSymbolIndex(symbol);
            if (symbolIndex < 0)
            {
                throw new KeyNotFoundException($"Symbol '{symbol}' not found in metadata.");
            }
            return matrix.GetColumnView(0, symbolIndex, matrix.RowCount);
        }

        /// <summary>
        /// 获取指定字段在单标的场景（第 0 列标的）下的全生命周期一维时序序列视图。
        /// </summary>
        /// <typeparam name="T">非托管数值类型</typeparam>
        /// <param name="dataset">市场数据集实例</param>
        /// <param name="fieldName">字段名称（如 "close", "volume"）</param>
        /// <param name="symbolIndex">标的列索引，默认为 0</param>
        /// <returns>标的的全周期连续只读一维列视图</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlyColumnView<T> GetSeries<T>(this ITimeseriesRegistry registry, string fieldName, int symbolIndex = 0) where T : unmanaged
        {
            return registry.GetMatrix<T>(fieldName).GetSeries(symbolIndex);
        }

        /// <summary>
        /// 根据标的代码获取指定字段的全生命周期一维时序序列视图。
        /// </summary>
        /// <typeparam name="T">非托管数值类型</typeparam>
        /// <param name="dataset">市场数据集实例</param>
        /// <param name="fieldName">字段名称（如 "close", "volume"）</param>
        /// <param name="symbol">标的代码</param>
        /// <returns>标的的全周期连续只读一维列视图</returns>
        public static ReadOnlyColumnView<T> GetSeries<T>(this ITimeseriesRegistry registry, string fieldName, string symbol) where T : unmanaged
        {
            return registry.GetMatrix<T>(fieldName).GetSeries(symbol, registry.Metadata);
        }
    }
}
