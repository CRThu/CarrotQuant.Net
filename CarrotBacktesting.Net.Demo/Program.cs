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
        }
    }
}