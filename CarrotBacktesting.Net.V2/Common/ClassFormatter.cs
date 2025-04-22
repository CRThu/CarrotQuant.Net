using ConsoleTables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Common
{
    public static class ClassFormatter
    {
        public static string Formatter<T>(IEnumerable<T> obj)
        {
            return ConsoleTable
                .From(obj)
                .Configure(T => T.OutputTo.ToString())
                .ToMarkDownString();
        }
    }
}
