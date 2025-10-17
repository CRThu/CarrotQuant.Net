// DataFeed/CsvMarketParser.cs

using CarrotBacktesting.NET.Data;
using Sylvan.Data.Csv; // 引入Sylvan库
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CarrotBacktesting.NET.DataFeed
{
    /// <summary>
    /// 使用 Sylvan.Data.Csv 库高性能地解析CSV市场数据
    /// </summary>
    public static class CsvMarketParser
    {
        /// <summary>
        /// 解析单个CSV文件并将其数据添加到MarketStorageBuilder中
        /// </summary>
        /// <param name="msb">市场数据构建器</param>
        /// <param name="symbol">股票代码</param>
        /// <param name="path">CSV文件路径</param>
        /// <param name="fieldsMapper">字段映射器</param>
        public static void Parse(MarketStorageBuilder msb, string symbol, string path, FieldsMapper fieldsMapper)
        {
            // Sylvan CsvDataReader 的配置选项，指定包含标题行
            var options = new CsvDataReaderOptions { HasHeaders = true };

            try
            {
                // 使用 using 语句确保 CsvDataReader 被正确释放
                using (var reader = CsvDataReader.Create(path, options))
                {
                    // 1. 如果CSV文件为空或只有标题行，则直接返回
                    if (!reader.HasRows)
                    {
                        return;
                    }

                    // 2. 解析CSV标题行，构建从内部字段名到列索引的映射
                    //    这个步骤和之前的版本逻辑完全一样，是解析前的准备工作
                    var header = new string[reader.FieldCount];
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        header[i] = reader.GetName(i);
                    }
                    var internalNameToIndexMap = new Dictionary<string, int>();

                    // 创建一个从别名(CSV列名)到内部字段名的反向映射
                    var aliasToInternalNameMap = fieldsMapper.BasicFieldNameMap
                        .ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

                    for (int i = 0; i < header.Length; i++)
                    {
                        if (aliasToInternalNameMap.TryGetValue(header[i], out var internalName))
                        {
                            internalNameToIndexMap[internalName] = i;
                        }
                    }

                    // 3. 检查StockFrame所需的基本字段是否都已在CSV中找到
                    var requiredFields = new[] { "time", "open", "high", "low", "close", "volume", "status" };
                    foreach (var field in requiredFields)
                    {
                        if (!internalNameToIndexMap.ContainsKey(field))
                        {
                            Console.WriteLine($"[Error] Skipped file {path}. Required column for '{field}' (alias: {fieldsMapper.BasicFieldNameMap[field]}) not found.");
                            return; // 跳过整个文件
                        }
                    }

                    // 预先获取各字段的列索引，避免在循环内重复查找字典，进一步提升性能
                    int timeIdx = internalNameToIndexMap["time"];
                    int openIdx = internalNameToIndexMap["open"];
                    int highIdx = internalNameToIndexMap["high"];
                    int lowIdx = internalNameToIndexMap["low"];
                    int closeIdx = internalNameToIndexMap["close"];
                    int volumeIdx = internalNameToIndexMap["volume"];
                    int statusIdx = internalNameToIndexMap["status"];
                    var statusFieldDef = fieldsMapper.FieldDefinitions["status"];

                    // 4. 使用 Sylvan 高效地逐行读取数据
                    while (reader.Read())
                    {
                        try
                        {
                            // 5. 根据索引直接获取强类型数据，性能远高于 string.Split 和 double.Parse
                            string time = reader.GetString(timeIdx);
                            double open = reader.GetDouble(openIdx);
                            double high = reader.GetDouble(highIdx);
                            double low = reader.GetDouble(lowIdx);
                            double close = reader.GetDouble(closeIdx);
                            double volume = reader.GetDouble(volumeIdx);

                            // 解析交易状态 (逻辑与之前版本相同)
                            string statusCsvValue = reader.GetString(statusIdx);
                            TradeStatus tradeStatus = TradeStatus.Unknown;
                            if (statusFieldDef.ValueMap.TryGetValue(statusCsvValue, out var internalStatusString))
                            {
                                Enum.TryParse<TradeStatus>(internalStatusString, true, out tradeStatus);
                            }

                            // 6. 创建StockFrame实例
                            var frame = new StockFrame(open, high, low, close, volume, tradeStatus);

                            // 7. 将创建的Frame添加到构建器中
                            msb.AddFrame(symbol, time, frame);
                        }
                        catch (Exception ex)
                        {
                            // 如果单行解析失败，打印错误并继续处理下一行
                            // reader.RowNumber 可以获取当前出错的行号，便于调试
                            Console.WriteLine($"[Warning] Error parsing line {reader.RowNumber} in file {path}: {ex.Message}");
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 处理文件读取等IO异常
                Console.WriteLine($"[Error] Failed to read or process file {path}: {ex.Message}");
            }
        }
    }
}