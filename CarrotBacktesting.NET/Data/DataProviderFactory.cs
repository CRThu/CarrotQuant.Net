using System.IO;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;
using Carrot.Memory.Providers;

namespace CarrotBacktesting.NET.Data
{
    public enum StorageMode { Heap, Mmf }

    public static class DataProviderFactory
    {
        public static IDataProvider Create(
            string storageRoot,
            IFieldRegistry registry, 
            IMarketSeriesSource source,
            int pageSize = 4096,
            bool enableCache = false,
            string refreshMode = "lazy",
            string tableId = "default",
            string compression = "lz4")
        {
            return new BufferedDataProvider(source, registry, source, storageRoot, pageSize, enableCache, refreshMode, tableId, compression);
        }
    }
}
