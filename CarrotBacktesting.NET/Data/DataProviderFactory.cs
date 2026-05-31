using System.IO;
using CarrotBacktesting.NET.Abstraction.Data;
using Carrot.Memory.Providers;

namespace CarrotBacktesting.NET.Data
{
    public enum StorageMode { Heap, Mmf }

    public static class DataProviderFactory
    {
        public static IDataProvider Create(
            string storageRoot,
            IFieldRegistry registry, 
            IMarketSeriesSource source)
        {
            return new BufferedDataProvider(source, registry, source, storageRoot);
        }
    }
}
