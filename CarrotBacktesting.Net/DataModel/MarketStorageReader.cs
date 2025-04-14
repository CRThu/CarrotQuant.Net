using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataModel
{
    public class MarketStorageReader
    {
        public MarketInternalStorageFrozenObject MarketStorage { get; init; }

        public uint TickIndex { get; set; }


        public MarketStorageReader(MarketInternalStorageFrozenObject storage)
        {
            MarketStorage = storage;
        }

        public 
    }
}
