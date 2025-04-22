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
    /// 对读优化的可高效序列化/反序列化的市场数据存储类
    /// </summary>
    [MemoryPackable]
    public partial class MarketInternalStorageFrozenObject
    {
        /// <summary>
        /// tick内数据列名称和offset位置映射字典
        /// </summary>
        required public FrozenDictionary<string, ushort> ColumnsMap { get; init; }

        /// <summary>
        /// 股票代码和offset位置映射字典
        /// </summary>
        required public FrozenSet<FrozenDictionary<string, uint>> SymbolsMaps { get; init; }

        /// <summary>
        /// Ticks 列表, 结构体记录Tick在存储结构中的地址偏移和时间信息
        /// </summary>
        required public FrozenSet<TickInfoInternalStorage> TicksInfos { get; init; }

        /// <summary>
        /// 用于存储数字元素
        /// </summary>
        required public FrozenSet<double> Numbers { get; init; }

        /// <summary>
        /// 用于存储字符串元素
        /// </summary>
        required public FrozenSet<string> Strings { get; init; }


        [MemoryPackOnSerializing]
        void OnSerializing()
        {
        }
    }
}
