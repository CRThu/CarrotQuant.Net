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
        /// 对读优化的 StocksSymbol : Index 的键值对映射存储字典
        /// </summary>
        public FrozenDictionary<string, uint>? StocksSymbolIndex { get; set; }

        /// <summary>
        /// 对读优化的 TickTime 列表, 存储每Tick具体时间
        /// </summary>
        public FrozenSet<DateTime>? TicksTime { get; set; }

        public FrozenSet<bool>? StringElements { get; set; }
         
        public FrozenSet<double>? DoubleElements { get; set; }

         
        /// <summary>
        /// 构造函数
        /// </summary>
        public MarketInternalStorageFrozenObject()
        {
            StocksSymbolIndex = null;
            TicksTime = null;
            DoubleElements = null;
            StringElements = null;
        }

        [MemoryPackOnSerializing]
        void OnSerializing()
        {
        }
    }
}
