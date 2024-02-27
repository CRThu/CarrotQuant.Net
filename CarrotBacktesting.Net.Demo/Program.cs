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
            //CsvReaderTest.Run();
            EngineTest.Run();

            //Dictionary<(DateTime t, string c), int> kvp = new() {
            //    { (DateTime.Parse("2023-11-29 23:41:00"), "0"),0 },
            //    { (DateTime.Parse("2023-11-29 23:41:00"), "1"),1 },
            //    { (DateTime.Parse("2023-11-29 23:42:00"), "0"),2 },
            //    { (DateTime.Parse("2023-11-29 23:42:00"), "1"),3 }
            //};
            //string c = Console.ReadLine();
            //kvp.Add((DateTime.Parse("2023-11-29 23:42:00"), c), 0);
        }
    }
}