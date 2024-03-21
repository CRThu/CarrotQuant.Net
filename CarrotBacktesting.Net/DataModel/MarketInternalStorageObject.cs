using MemoryPack;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataModel
{
    public readonly struct TickInfoStorage
    : IEquatable<TickInfoStorage>
    {
        public DateTime Tick { get; init; }
        public uint SymbolsMapOffset { get; init; }
        public uint TickNumbersOffset { get; init; }
        public uint TickStringsOffset { get; init; }

        public bool Equals(TickInfoStorage other) => Tick == other.Tick;

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
        private Dictionary<string, ushort> ColumnsMap { get; set; }

        /// <summary>
        /// 股票代码和offset位置映射字典
        /// </summary>
        private List<Dictionary<string, uint>> SymbolsMaps { get; set; }

        /// <summary>
        /// Ticks 列表, 存储每Tick 时间, Tick offset, Symbol offset
        /// </summary>
        private List<TickInfoStorage> TicksInfos { get; set; }

        /// <summary>
        /// 用于存储数字元素
        /// </summary>
        private List<double> Numbers { get; set; }

        /// <summary>
        /// 用于存储字符串元素
        /// </summary>
        private List<string> Strings { get; set; }


        /// <summary>
        /// 构造函数
        /// </summary>
        public MarketInternalStorageObject()
        {
            SymbolsMaps = [];
            TicksStorage = new();
            NumbersStorage = new();
            StringsStorage = new();
        }

        /// <summary>
        /// 编译本类
        /// </summary>
        public static MarketInternalStorageFrozenObject Compile(MarketInternalStorageObject obj)
        {
            // Compile
            MarketInternalStorageFrozenObject fo = new() {
                StocksSymbolIndex = obj.StocksSymbolIndex.ToFrozenDictionary(),
                TicksTime = obj.TicksStorage.ToFrozenSet()
            };

            return fo;
        }
    }
}
