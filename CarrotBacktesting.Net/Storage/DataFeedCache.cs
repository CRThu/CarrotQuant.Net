using CarrotBacktesting.Net.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Storage
{
    public class DataFeedCache
    {
        public DataFeedCache(SimulationOptions options)
        {

        }

        public static bool HasCache()
        {
            return false;
        }
    }
}
