using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Shared
{
    public static class Serializer
    {
        public static string Serialize(object value, bool isIndented = true)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = isIndented
            };

            return JsonSerializer.Serialize(value, options);
        }
    }
}
