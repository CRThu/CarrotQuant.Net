using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Utility
{
    public static class DictionaryDebugHelper
    {
        public static string ToDebugString<TKey, TValue>(this IDictionary<TKey, TValue> dictionary)
        {
            if (dictionary == null) return "Dictionary is null";

            var sb = new StringBuilder();
            sb.AppendLine($"Dictionary ({dictionary.Count} items)");

            foreach (var kvp in dictionary)
            {
                string key = kvp.Key?.ToString() ?? "[null]";
                string value = kvp.Value?.ToString() ?? "null";

                sb.AppendLine($"|- [{key}] = {value}");
            }

            if (dictionary.Count > 0)
                sb.Append("-- (End of dictionary)");
            else
                sb.Append("-- (Empty dictionary)");

            return sb.ToString();
        }

        private const int DefaultMaxItems = 30;

        public static string ToDebugString<T>(this IEnumerable<T> source, int maxItems = DefaultMaxItems)
        {
            if (source == null) return "List is null";

            var items = source as T[] ?? source.ToArray();
            if (!items.Any()) return "[Empty List]";

            var sb = new StringBuilder()
                .AppendLine($"List ({items.Length} items)")
                .AppendLine("|--------------------");

            foreach (var (item, index) in items.Select((x, i) => (x, i)))
            {
                if (index >= maxItems)
                {
                    sb.AppendLine($"|- ... ({items.Length - maxItems} more items)");
                    break;
                }

                var value = item switch {
                    null => "null",
                    string s => $"\"{s}\"", // 字符串特殊处理
                    _ => item.ToString()
                };

                sb.AppendLine($"| [{index}] = {value}");
            }

            return sb.Append("--------------------").ToString();
        }
    }
}
