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
    /// 可高效序列化的数据存储类
    /// </summary>
    [MemoryPackable]
    public partial class MarketInternalStorageObject
    {
        /// <summary>
        /// StocksSymbol : Index 的键值对映射存储字典
        /// </summary>
        private Dictionary<string, uint> StocksSymbolIndexDict { get; set; }

        /// <summary>
        /// 对读优化的 StocksSymbol : Index 的键值对映射存储字典
        /// </summary>
        public FrozenDictionary<string, uint> StocksSymbolIndex { get; set; }


        /// <summary>
        /// 构造函数
        /// </summary>
        public MarketInternalStorageObject()
        {
            StocksSymbolIndexDict = [];
            StocksSymbolIndex = null;
        }

        /// <summary>
        /// 编译本类
        /// </summary>
        public void Compile()
        {
            // Compile
            StocksSymbolIndex = StocksSymbolIndexDict.ToFrozenDictionary();

            // GC Collect
            StocksSymbolIndexDict.Clear();
            GC.Collect();
        }

        [MemoryPackOnSerializing]
        void OnSerializing()
        {
            Compile();
        }
    }
}
