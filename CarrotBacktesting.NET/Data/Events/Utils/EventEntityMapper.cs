using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 事件实体反射绑定与参数转换工具。
    /// 统一处理 CSV 与 Parquet 字段名到实体构造函数参数的无缝映射。
    /// </summary>
    internal static class EventEntityMapper
    {
        public static readonly HashSet<string> KeyColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            "symbol", "datetime", "timestamp"
        };

        public static ConstructorInfo ResolveConstructor<T>(List<string> valueColumns) where T : class
        {
            var type = typeof(T);
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (ctors.Length == 0)
                throw new InvalidOperationException($"Type {type.Name} has no public constructor.");

            foreach (var ctor in ctors)
            {
                var parameters = ctor.GetParameters();
                if (parameters.Length == valueColumns.Count)
                {
                    bool match = true;
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (!NormalizedEquals(parameters[i].Name!, valueColumns[i]))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return ctor;
                }
            }

            throw new InvalidOperationException(
                $"No constructor of {type.Name} matches value columns [{string.Join(", ", valueColumns)}]. " +
                $"Available constructors: {string.Join("; ", ctors.Select(c => $"({string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})"))}");
        }

        public static T InvokeConstructor<T>(ConstructorInfo ctor, List<string> valueColumns, string[] values) where T : class
        {
            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                string valStr = i < values.Length ? values[i] : "";
                args[i] = ConvertValue(valStr, parameters[i].ParameterType);
            }

            return (T)ctor.Invoke(args);
        }

        public static object? ConvertValue(string valStr, Type targetType)
        {
            if (string.IsNullOrWhiteSpace(valStr))
            {
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            }

            if (targetType == typeof(string)) return valStr;
            if (targetType == typeof(double)) return double.Parse(valStr, CultureInfo.InvariantCulture);
            if (targetType == typeof(float)) return float.Parse(valStr, CultureInfo.InvariantCulture);
            if (targetType == typeof(long)) return long.Parse(valStr, CultureInfo.InvariantCulture);
            if (targetType == typeof(int)) return int.Parse(valStr, CultureInfo.InvariantCulture);
            if (targetType == typeof(bool)) return bool.Parse(valStr);
            if (targetType == typeof(DateTime)) return DateTime.Parse(valStr, CultureInfo.InvariantCulture);

            return Convert.ChangeType(valStr, targetType, CultureInfo.InvariantCulture);
        }

        public static bool NormalizedEquals(string a, string b)
        {
            return string.Equals(
                a.Replace("_", "").ToLowerInvariant(),
                b.Replace("_", "").ToLowerInvariant(),
                StringComparison.Ordinal);
        }
    }
}
