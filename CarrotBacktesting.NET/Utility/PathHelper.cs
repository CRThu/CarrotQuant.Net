using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Utility
{
    public static class PathHelper
    {
        private static string Root => AppDomain.CurrentDomain.BaseDirectory;

        private static string DebugRoot => Path.GetFullPath(Path.Combine(Root, @"..\..\..\.."));

        public static bool IsDebugging => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VisualStudioVersion"));

        public static string RuntimeRoot => IsDebugging ? DebugRoot : Root;
    }
}
