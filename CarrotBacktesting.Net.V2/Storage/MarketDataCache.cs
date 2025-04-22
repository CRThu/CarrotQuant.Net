using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Storage
{
    public class MarketDataCache : MarketData
    {
        public SimulationOptions Options { get; set; }

        public MarketDataCache(SimulationOptions options)
        {
            Options = options;
        }

        public MarketDataCache CreateCache()
        {
            return null;
        }

        public void LoadCache(MarketDataCache cache)
        {

        }
    }
}
