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
    /// 可高效序列化/反序列化的市场数据存储类
    /// </summary>
    [MemoryPackable]
    public partial class MarketInternalStorageObject
    {
        /// <summary>
        /// StocksSymbol : Index 的键值对映射存储字典
        /// </summary>
        private Dictionary<string, uint> StocksSymbolIndex { get; set; }

        /// <summary>
        /// TickTime 列表, 存储每Tick具体时间
        /// </summary>
        private List<DateTime> TicksTime { get; set; }

        /// <summary>
        /// 
        /// </summary>
        private List<double> DoubleElements { get; set; }

        private List<string> StringElements { get; set; }


        /// <summary>
        /// 构造函数
        /// </summary>
        public MarketInternalStorageObject()
        {
            StocksSymbolIndex = [];
            TicksTime = new();
            DoubleElements = new();
            StringElements = new();
        }

        /// <summary>
        /// 编译本类
        /// </summary>
        public static MarketInternalStorageFrozenObject Compile(MarketInternalStorageObject obj)
        {
            // Compile
            MarketInternalStorageFrozenObject fo = new() {
                StocksSymbolIndex = obj.StocksSymbolIndex.ToFrozenDictionary(),
                TicksTime = obj.TicksTime.ToFrozenSet()
            };

            return fo;
        }
    }
}
