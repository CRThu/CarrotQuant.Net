using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Storage;
using CarrotBacktesting.Net.Strategy;
using CarrotBackTesting.Net.UnitTest.Common;
using MemoryPack;
using MemoryPack.Compression;
using Newtonsoft.Json.Linq;
using System;
using System.Data;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace CarrotBacktesting.Net.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
            Stopwatch loadStopwatch = new();
            Stopwatch runStopwatch = new();


            // string dataSet = "testdata/daily.no-adjust";
            // string dataSet = "testdata/daily.post-adjust";
            string dataSet = "testdata/5min.post-adjust";
            string optionFile = "options.json";
            string dataDir = UnitTestDirectory.DataRootDirectory;
            loadStopwatch.Start();
            BackTestingEngine engine = BackTestingEngine.Create(new DebugStrategy(), dataDir, dataSet, optionFile);
            loadStopwatch.Stop();
            runStopwatch.Start();
            engine.Run();
            runStopwatch.Stop();

            int klinesCount = engine.Simulator.KLineCount;
            int ticksCount = engine.Simulator.TickCount;
            double loadTicksSpeed = (double)ticksCount / loadStopwatch.ElapsedMilliseconds * 1000;
            double loadKlinesSpeed = (double)klinesCount / loadStopwatch.ElapsedMilliseconds * 1000;
            double runTicksSpeed = (double)ticksCount / runStopwatch.ElapsedMilliseconds * 1000;
            double runKlinesSpeed = (double)klinesCount / runStopwatch.ElapsedMilliseconds * 1000;

            Console.WriteLine($"回测已完成, 共测试 {ticksCount} Ticks, "
                + $"{klinesCount} KLines");
            Console.WriteLine($"加载耗时: {loadStopwatch.ElapsedMilliseconds / 1000.0} Sec, "
                + $"加载速度: {loadTicksSpeed:F3} Ticks/Sec, "
                + $"{loadKlinesSpeed:F3} KLines/Sec");
            Console.WriteLine($"回测耗时: {runStopwatch.ElapsedMilliseconds / 1000.0} Sec, "
                + $"回测速度: {runTicksSpeed:F3} Ticks/Sec, "
                + $"{runKlinesSpeed:F3} KLines/Sec");


            var raw = engine.DataFeed.MarketData.MarketFrames;

            Stopwatch serializeStopwatch = new();
            Stopwatch deserializeStopwatch = new();
            serializeStopwatch.Start();

            // json
            //Json.SerializeToFile(raw, "C:\\Users\\Carrot\\Desktop\\a.json");

            // MemoryPack
            //var bin = MemoryPackSerializer.Serialize(raw);

            // MemoryPack+Compression
            // Compression(require using)
            using var compressor = new BrotliCompressor(CompressionLevel.Optimal);
            MemoryPackSerializer.Serialize(compressor, raw);
            // Get compressed byte[]
            var bin = compressor.ToArray();

            serializeStopwatch.Stop();
            deserializeStopwatch.Start();

            // json
            //var val = Json.DeSerializeFromFile<Dictionary<DateTime, MarketFrame>>("C:\\Users\\Carrot\\Desktop\\a.json");

            // MemoryPack
            //var val = MemoryPackSerializer.Deserialize<Dictionary<DateTime, MarketFrame>>(bin);

            // MemoryPack+Compression
            // Decompression(require using)
            using var decompressor = new BrotliDecompressor();

            // Get decompressed ReadOnlySequence<byte> from ReadOnlySpan<byte> or ReadOnlySequence<byte>
            var decompressedBuffer = decompressor.Decompress(bin);

            var val = MemoryPackSerializer.Deserialize<Dictionary<DateTime, MarketFrame>>(decompressedBuffer);

            deserializeStopwatch.Stop();

            double serializeTicksSpeed = (double)ticksCount / loadStopwatch.ElapsedMilliseconds * 1000;
            double serializeKlinesSpeed = (double)klinesCount / loadStopwatch.ElapsedMilliseconds * 1000;
            double deserializeTicksSpeed = (double)ticksCount / runStopwatch.ElapsedMilliseconds * 1000;
            double deserializeKlinesSpeed = (double)klinesCount / runStopwatch.ElapsedMilliseconds * 1000;

            Console.WriteLine($"序列化耗时: {serializeStopwatch.ElapsedMilliseconds / 1000.0} Sec, "
                );
            //+ $"二进制大小: {bin.Length} Bytes");
            Console.WriteLine($"反序列化耗时: {deserializeStopwatch.ElapsedMilliseconds / 1000.0} Sec, "
                );
            //+ $"一致性: {bin.Length} Bytes");

            bool isCorrect = true;
            var rawFirstTime = raw.Keys.First();
            var rawFirstCode = raw[rawFirstTime].Codes.First();
            if (raw.Count != val!.Count)
                isCorrect = false;
            if (raw[rawFirstTime].Count != val![rawFirstTime].Count)
                isCorrect = false;
            if (raw[rawFirstTime][rawFirstCode]!.Close != val![rawFirstTime][rawFirstCode]!.Close)
                isCorrect = false;
            if (raw[rawFirstTime][rawFirstCode]!.Params!.Count != val![rawFirstTime][rawFirstCode]!.Params!.Count)
                isCorrect = false;

            Console.WriteLine($"二进制大小: {bin.Length} Bytes, "
                + $"一致性: {isCorrect}"
                );
        }
    }
}