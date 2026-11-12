using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 数据子系统默认标的与双时间轴基础列名定义（单一事实来源 SSOT）。
    /// 收敛时序、事件与维表等数据流的默认列命名约定与兜底候选别名。
    /// </summary>
    public static class DefaultDataColumns
    {
        public const string Symbol = "symbol";
        public const string DateTime = "datetime";
        public const string Timestamp = "timestamp";
        public const string Date = "date";

        /// <summary>
        /// 默认时间列候选名称（按探测优先级排序：优先 datetime，次选 timestamp，兼容 date）。
        /// </summary>
        public static readonly string[] TimeCandidates = { DateTime, Timestamp, Date };

        /// <summary>
        /// 默认标的列候选名称（按探测优先级排序：优先 symbol，兼容 code）。
        /// </summary>
        public static readonly string[] SymbolCandidates = { Symbol, "code" };

        /// <summary>
        /// 事件实体映射保留键集合（由时间轴与标的代码统领，严禁作为实体普通业务属性映射）。
        /// </summary>
        public static readonly HashSet<string> ReservedEventKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            Symbol, DateTime, Timestamp, Date, "code"
        };
    }
}
