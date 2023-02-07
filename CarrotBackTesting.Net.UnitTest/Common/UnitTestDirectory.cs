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
        public static string ProjectDirectory => @$"{Environment.CurrentDirectory}\..\..\..";
        public static string TestDataDirectory => @$"{ProjectDirectory}\TestData";
        public static string CsvDataDirectory => @$"{ProjectDirectory}\TestData\Csv";
        public static string JsonDirectory => @$"{ProjectDirectory}\TestData\Json";

        public static string InfoPath => @$"{ProjectDirectory}\TestData\Json\info.json";
        //https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-dom-utf8jsonreader-utf8jsonwriter?pivots=dotnet-7-0
        public static JsonNode Info = JsonNode.Parse(File.ReadAllText(InfoPath))!;
    }
}
