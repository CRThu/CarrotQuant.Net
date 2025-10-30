using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Utility;
using CarrotBacktesting.NET.Utility.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.DataFeed
{
    public class DataLoader
    {
        public IDataStorage? LoadData(EnvConfig config)
        {
            IDataStorage? ds;
            if (!config.Cache.Enabled)
            {
                return LoadFromSource(config);
            }
            else
            {
                if (!config.Cache.ForceRefresh)
                {
                    ds = LoadFromCache(config);
                    if (ds != null)
                    {
                        return ds;
                    }
                }
                ds = LoadFromSource(config);
                UpdateCache(config, ds);
                return ds;
            }
        }

        private string GetCacheFileName(StorageMode mode)
        {
            return mode == StorageMode.TimeSeries
                ? "history.cache"
                : "market.cache";
        }

        private void UpdateCache(EnvConfig config, IDataStorage data)
        {
            string fileName = Path.Combine(config.Runtime.ProjectDir, GetCacheFileName(config.Data.Mode));

            // 根据实际类型调用序列化
            if (data is HistoryStorage hs)
            {
                MessagePackSerializationHelper.SerializeToFile(hs, fileName);
            }
            else if (data is MarketStorage ms)
            {
                MessagePackSerializationHelper.SerializeToFile(ms, fileName);
            }
            Console.WriteLine($"[Cache] Data saved to cache file: {fileName}");
        }

        private IDataStorage? LoadFromCache(EnvConfig config)
        {
            string fileName = Path.Combine(config.Runtime.ProjectDir, GetCacheFileName(config.Data.Mode));
            if (!File.Exists(fileName)) return null;

            Console.WriteLine($"[Cache] Loading from cache file: {fileName}");
            if (config.Data.Mode == StorageMode.TimeSeries)
            {
                return MessagePackSerializationHelper.DeserializeFromFile<HistoryStorage>(fileName);
            }
            else
            {
                return MessagePackSerializationHelper.DeserializeFromFile<MarketStorage>(fileName);
            }
        }

        private IDataStorage? LoadFromSource(EnvConfig config)
        {
            string dir = config.Data.RawPath;
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException($"数据源目录不存在:{dir}");

            var info = FileScanner.GetFiles(dir);
            Console.WriteLine($"Found {info.Count} files.");
            //foreach (var kvp in info)
            //{
            //    Console.WriteLine($"Key: {kvp.Key}, Path: {kvp.Value}");
            //}

            FieldsMapper fieldsMapper = new FieldsMapper(config);
            Console.WriteLine($"BasicFieldNameMap: {fieldsMapper.BasicFieldNameMap.ToDebugString()}");
            Console.WriteLine($"ExtendedFieldNameMap: {fieldsMapper.ExtendedFieldNameMap.ToDebugString()}");
            
            IDataStorageBuilder builder = config.Data.Mode switch
            {
                StorageMode.TimeSeries => new HistoryStorageBuilder(info.Keys),
                StorageMode.MarketSnapshot => new MarketStorageBuilder(info.Keys),
                _ => throw new ArgumentOutOfRangeException(nameof(config.Data.Mode), "Unsupported storage mode.")
            };
            foreach (var kvp in info)
            {
                CsvMarketParser.Parse(builder, kvp.Key, kvp.Value, fieldsMapper);
            }

            return builder.Build();
        }
    }
}
