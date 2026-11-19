using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Parquet.Serialization;

namespace CarrotBacktesting.NET.Benchmark;

/// <summary>
/// 真实物理 Parquet 测试数据夹具生成器。
/// 在系统隔离的临时目录（%TEMP%）中物理生成规范的 Hive 分区 Parquet 文件（Snappy 压缩），
/// 供官方 ParquetTimeseriesSource 进行真实的磁盘 I/O、解压缩和反序列化压测。
/// </summary>
public static class RealParquetFixture
{
    public sealed class MinuteKlineBar
    {
        public string Symbol { get; set; } = "";
        public string Datetime { get; set; } = "";
        public long Timestamp { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public double Volume { get; set; }
        public double Amount { get; set; }
    }

    /// <summary>
    /// 生成符合系统规范的真实物理 Parquet 数据表。
    /// </summary>
    /// <param name="targetRoot">存储根目录</param>
    /// <param name="tableId">表名</param>
    /// <param name="symbols">标的代码列表</param>
    /// <param name="timeSteps">时间步数（默认 240 分钟步）</param>
    public static async Task GenerateAsync(string targetRoot, string tableId, IReadOnlyList<string> symbols, int timeSteps = 240)
    {
        string tableDir = Path.Combine(targetRoot, tableId);
        string yearDir = Path.Combine(tableDir, "year=2025");
        Directory.CreateDirectory(yearDir);

        // 1. 写入 metadata.json 单一事实来源
        var metadata = new
        {
            table_id = tableId,
            category = "timeseries",
            format = "parquet",
            partition = "year",
            layout = "hive",
            schema = new Dictionary<string, string>
            {
                ["symbol"] = "String",
                ["datetime"] = "String",
                ["timestamp"] = "Int64",
                ["open"] = "Float64",
                ["high"] = "Float64",
                ["low"] = "Float64",
                ["close"] = "Float64",
                ["volume"] = "Float64",
                ["amount"] = "Float64"
            }
        };

        string metaJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(tableDir, "metadata.json"), metaJson);

        // 2. 生成行列表并序列化为真实磁盘物理 Parquet 文件
        var baseDate = new DateTime(2025, 1, 2, 9, 30, 0);
        int totalRows = symbols.Count * timeSteps;
        var records = new List<MinuteKlineBar>(totalRows);

        for (int s = 0; s < symbols.Count; s++)
        {
            string sym = symbols[s];
            double basePrice = 50.0 + (s % 100);
            double freq = 0.08 + (s % 10) * 0.005;

            for (int t = 0; t < timeSteps; t++)
            {
                var dt = baseDate.AddMinutes(t);
                double wave = Math.Sin((t + s * 3) * freq) * 5.0;
                double close = Math.Round(basePrice + wave, 2);
                double open = Math.Round(close - 0.2, 2);
                double high = Math.Round(close + 0.5, 2);
                double low = Math.Round(close - 0.5, 2);
                double vol = 1000.0 + (t % 50) * 10;
                double amt = close * vol;

                records.Add(new MinuteKlineBar
                {
                    Symbol = sym,
                    Datetime = dt.ToString("yyyy-MM-dd HH:mm:ss"),
                    Timestamp = new DateTimeOffset(dt).ToUnixTimeSeconds(),
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = vol,
                    Amount = amt
                });
            }
        }

        string parquetFile = Path.Combine(yearDir, "data.parquet");
        await using var fs = File.Create(parquetFile);
        await ParquetSerializer.SerializeAsync(records, fs);
    }
}
