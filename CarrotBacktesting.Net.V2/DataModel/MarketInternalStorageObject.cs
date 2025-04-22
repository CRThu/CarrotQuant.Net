using MemoryPack;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataModel
{
    /// <summary>
    /// 结构体记录Tick在存储结构中的地址偏移和时间信息
    /// </summary>
    public readonly struct TickInfoInternalStorage : IEquatable<TickInfoInternalStorage>
    {
        /// <summary>
        /// Tick时间
        /// </summary>
        public DateTime Tick { get; init; }
        /// <summary>
        /// 股票代码-offset映射字典列表索引
        /// </summary>
        public uint SymbolsMapIndex { get; init; }
        /// <summary>
        /// Numbers中本Tick存储开始地址
        /// </summary>
        public uint TickNumbersOffset { get; init; }
        /// <summary>
        /// Strings中本Tick存储开始地址
        /// </summary>
        public uint TickStringsOffset { get; init; }

        public bool Equals(TickInfoInternalStorage other) => Tick == other.Tick;

        public override int GetHashCode() => Tick.GetHashCode();
    }

    /// <summary>
    /// 可高效序列化/反序列化的市场数据存储类
    /// </summary>
    [MemoryPackable]
    public partial class MarketInternalStorageObject
    {
        /// <summary>
        /// tick内数据列名称和offset位置映射字典
        /// </summary>
        public Dictionary<string, ushort> ColumnsMap { get; set; }

        /// <summary>
        /// 股票代码和offset位置映射字典
        /// </summary>
        public List<Dictionary<string, uint>> SymbolsMaps { get; set; }

        /// <summary>
        /// Ticks 列表, 结构体记录Tick在存储结构中的地址偏移和时间信息
        /// </summary>
        public List<TickInfoInternalStorage> TicksInfos { get; set; }

        /// <summary>
        /// 用于存储数字元素
        /// </summary>
        public List<double> Numbers { get; set; }

        /// <summary>
        /// 用于存储字符串元素
        /// </summary>
        public List<string> Strings { get; set; }


        /// <summary>
        /// 构造函数
        /// </summary>
        public MarketInternalStorageObject()
        {
            ColumnsMap = [];
            SymbolsMaps = [];
            TicksInfos = [];
            Numbers = [];
            Strings = [];
        }

        /// <summary>
        /// 编译本类对访问优化
        /// </summary>
        public static MarketInternalStorageFrozenObject Compile(MarketInternalStorageObject obj)
        {
            // Compile
            MarketInternalStorageFrozenObject fo = new() {
                ColumnsMap = obj.ColumnsMap.ToFrozenDictionary(),
                SymbolsMaps = obj.SymbolsMaps.Select(m=>m.ToFrozenDictionary()).ToFrozenSet(),
                TicksInfos = obj.TicksInfos.ToFrozenSet(),
                Numbers = obj.Numbers.ToFrozenSet(),
                Strings = obj.Strings.ToFrozenSet()
            };

            return fo;
        }
    }
}
