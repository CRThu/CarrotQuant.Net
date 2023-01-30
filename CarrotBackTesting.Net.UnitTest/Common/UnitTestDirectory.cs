using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBackTesting.Net.UnitTest.Common
{
    public static class UnitTestDirectory
    {
        public static string ProjectDirectory = @$"{Environment.CurrentDirectory}\..\..\..";
        public static string TestDataDirectory = @$"{ProjectDirectory}\TestData";
        public static string CsvDataDirectory = @$"{ProjectDirectory}\TestData\Csv";

        public static string InfoPath = @$"{ProjectDirectory}\TestData\info.json";
    }
}
