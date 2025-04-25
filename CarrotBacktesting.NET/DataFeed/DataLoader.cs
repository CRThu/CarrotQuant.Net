using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.DataFeed
{
    public class DataLoader
    {
        public MarketStorage LoadData(EnvConfig config)
        {
            if (config.Cache.Enabled)
            {
                if (config.Cache.ForceRefresh || !CacheExists())
                {
                    var newData = LoadFromSource();
                    UpdateCache(newData);
                    return newData;
                }
                return LoadFromCache();
            }
            return LoadFromSource();
        }

        private void UpdateCache(MarketStorage newData)
        {
            throw new NotImplementedException();
        }

        private MarketStorage LoadFromCache()
        {
            throw new NotImplementedException();
        }

        private MarketStorage LoadFromSource()
        {
            throw new NotImplementedException();
        }

        private bool CacheExists()
        {
            throw new NotImplementedException();
        }
    }
}
