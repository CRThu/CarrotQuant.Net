using CarrotBacktesting.NET.Config.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.DataFeed
{
    public class FieldsMapper
    {
        public Dictionary<string, string> BasicFieldNameMap { get; set; } = new()
        {
            { "time", "time" },
            { "open", "open" },
            { "high", "high" },
            { "low", "low" },
            { "close", "close" },
            { "volume", "volume" },
            { "amount", "amount" },
            { "status", "status" },
        };

        public Dictionary<string, string> ExtendedFieldNameMap { get; set; } = new();

        public FieldsMapper(EnvConfig config)
        {
            config.Fields.ToList().ForEach(x => {
                if (BasicFieldNameMap.ContainsKey(x.Key))
                {
                    // field is a basic field
                    if (x.Value.Format == FieldFormat.Null)
                    {
                        // basic field is not used
                        BasicFieldNameMap.Remove(x.Key);
                    }
                    else
                    {
                        // update name for basic field
                        BasicFieldNameMap[x.Key] = x.Value.Alias ?? x.Key;
                    }
                }
                else
                {
                    // field is a extended field
                    ExtendedFieldNameMap.TryAdd(x.Key,
                        (x.Value != null && x.Value.Alias != null) ? x.Value.Alias : x.Key);
                }
            });
        }
    }
}
