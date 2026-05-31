using System;
using System.Collections.Generic;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    public class SimpleFieldRegistry : IFieldRegistry
    {
        private readonly Dictionary<string, FieldInfo> _fields = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterField(string name, Type dataType, bool isCustom = false)
        {
            _fields[name] = new FieldInfo(name, dataType, isCustom);
        }

        public IEnumerable<string> GetAvailableFields() => _fields.Keys;

        public FieldInfo GetFieldInfo(string fieldName)
        {
            if (_fields.TryGetValue(fieldName, out var info)) return info;
            throw new KeyNotFoundException($"Field {fieldName} not found.");
        }

        public bool FieldExists(string fieldName) => _fields.ContainsKey(fieldName);

        public Type GetFieldType(string fieldName) => GetFieldInfo(fieldName).DataType;
    }
}
