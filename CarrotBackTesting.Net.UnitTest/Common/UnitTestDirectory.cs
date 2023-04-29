using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace CarrotBackTesting.Net.UnitTest.Common
{
    public static class UnitTestDirectory
    {
        public static string SolutionDirectory => @$"{Environment.CurrentDirectory}\..\..\..\..";
        public static string BenchmarkSolutionDirectory => @$"{Environment.CurrentDirectory}\..\..\..\..\..\..\..\..";
        public static string ProjectDirectory => @$"{SolutionDirectory}\CarrotBackTesting.Net.UnitTest";
        public static string BenchmarkProjectDirectory => @$"{BenchmarkSolutionDirectory}\CarrotBackTesting.Net.UnitTest";
        public static string TestDataDirectory => @$"{ProjectDirectory}\TestData";
        public static string CsvDataDirectory => @$"{ProjectDirectory}\TestData\csv";
        public static string SqliteDataDirectory => @$"{ProjectDirectory}\TestData\sqlite";
        public static string JsonDirectory => @$"{ProjectDirectory}\TestData\json";
        public static string BenchmarkJsonDirectory => @$"{BenchmarkProjectDirectory}\TestData\json";

        /// <summary>
        /// 历史数据测试数据路径
        /// </summary>
        public static string AsBsCsvTestDataDir => @$"{SolutionDirectory}\CarrotQuant.Data\ashare\baostock\csv\testdata";

        /// <summary>
        /// 历史数据测试数据配置文件路径
        /// </summary>
        public static string AsBsJsonTestDataDir => @$"{SolutionDirectory}\CarrotQuant.Data\ashare\baostock\json\testdata";

        public static string InfoPath => @$"{ProjectDirectory}\TestData\json\info.json";
        //https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-dom-utf8jsonreader-utf8jsonwriter?pivots=dotnet-7-0
        public static JsonNode Info = JsonNode.Parse(File.ReadAllText(InfoPath))!;
    }
}
