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
        private Dictionary<string, uint> StocksSymbolIndexDict { get; set; }

        /// <summary>
        /// 对读优化的 StocksSymbol : Index 的键值对映射存储字典
        /// </summary>
        public FrozenDictionary<string, uint> StocksSymbolIndex { get; set; }

        /// <summary>
        /// TickTime 列表, 存储每Tick具体时间
        /// </summary>
        private List<DateTime> TicksTimeList { get; set; }

        /// <summary>
        /// 对读优化的 TickTime 列表, 存储每Tick具体时间
        /// </summary>
        public FrozenSet<DateTime> TicksTime { get; set; }


        private List<double> DoubleElementsList { get; set; }

        public List<double> DoubleElements { get; set; }

        private List<string> StringElementsList { get; set; }

        public List<bool> StringElements { get; set; }



        /// <summary>
        /// 构造函数
        /// </summary>
        public MarketInternalStorageObject()
        {
            StocksSymbolIndexDict = [];
            StocksSymbolIndex = null;
            TicksTimeList = new();
            TicksTime = null;
        }

        /// <summary>
        /// 编译本类
        /// </summary>
        public void Compile()
        {
            // Compile
            StocksSymbolIndex = StocksSymbolIndexDict.ToFrozenDictionary();
            StocksSymbolIndexDict.Clear();
            TicksTime = TicksTimeList.ToFrozenSet();
            TicksTimeList.Clear();

            // GC Collect
            GC.Collect();
        }

        [MemoryPackOnSerializing]
        void OnSerializing()
        {
            Compile();
        }
    }
}
