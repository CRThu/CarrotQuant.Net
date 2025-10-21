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
        public MarketStorage? LoadData(EnvConfig config)
        {
            MarketStorage? ms;
            if (!config.Cache.Enabled)
            {
                return LoadFromSource(config);
            }
            else
            {
                if (!config.Cache.ForceRefresh)
                {
                    ms = LoadFromCache(config);
                    if (ms != null)
                    {
                        return ms;
                    }
                }
                ms = LoadFromSource(config);
                UpdateCache(config, ms);
                return ms;
            }
        }

        private void UpdateCache(EnvConfig config, MarketStorage data)
        {
            string fileName = Path.Combine(config.Runtime.ProjectDir, "market.cache");
            MessagePackSerializationHelper.SerializeToFile(data, fileName);
        }

        private MarketStorage? LoadFromCache(EnvConfig config)
        {
            string fileName = Path.Combine(config.Runtime.ProjectDir, "market.cache");
            if (!File.Exists(fileName))
                return null;
            return MessagePackSerializationHelper.DeserializeFromFile<MarketStorage>(fileName);
        }

        private MarketStorage? LoadFromSource(EnvConfig config)
        {
            //string dir = Path.Combine(config.Runtime.ProjectDir, config.Data.RawPath);
            string dir = config.Data.RawPath;
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException($"数据源目录不存在:{dir}");

            var info = FileScanner.GetFiles(config.Data.FullPath);
            Console.WriteLine($"Found {info.Count} files.");
            //foreach (var kvp in info)
            //{
            //    Console.WriteLine($"Key: {kvp.Key}, Path: {kvp.Value}");
            //}

            FieldsMapper fieldsMapper = new FieldsMapper(config);
            Console.WriteLine($"BasicFieldNameMap: {fieldsMapper.BasicFieldNameMap.ToDebugString()}");
            Console.WriteLine($"ExtendedFieldNameMap: {fieldsMapper.ExtendedFieldNameMap.ToDebugString()}");

            MarketStorageBuilder msb = new MarketStorageBuilder(info.Keys);
            foreach (var kvp in info)
            {
                string symbol = kvp.Key;
                string path = kvp.Value;
                CsvMarketParser.Parse(msb, symbol, path, fieldsMapper);
            }

            return msb.Build();
        }
    }
}
