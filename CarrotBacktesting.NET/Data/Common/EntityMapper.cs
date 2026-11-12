using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 通用高性能实体反射与表达式树预编译构造器（跨 Events 与 Fields 共享）。
    /// 将列名到实体构造函数参数的映射一次性预编译为原生委托，彻底消灭逐行反射与装箱开销。
    /// 遵循强类型边界红线：合法空值（空串/空白符）安全转换为 null 或类型缺省，
    /// 非法格式脏数据（如文本填入浮点列）坚决杜绝静默假默认值，即时抛出携带上下文的清晰 InvalidDataException。
    /// </summary>
    public static class EntityMapper
    {
        public static readonly HashSet<string> ReservedEventKeys = DefaultDataColumns.ReservedEventKeys;

        private static readonly ConcurrentDictionary<string, Delegate> DelegateCache = new();

        /// <summary>
        /// 查找匹配目标值列集合的构造函数。
        /// 优先选择所有参数均能在值列中匹配且参数最多的构造函数（支持子集列投影）。
        /// </summary>
        public static ConstructorInfo ResolveConstructor<T>(IReadOnlyList<string> valueColumns) where T : class
        {
            var type = typeof(T);
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (ctors.Length == 0)
                throw new InvalidOperationException($"Type '{type.Name}' has no public constructor.");

            ConstructorInfo? bestCtor = null;
            int maxMatchedParams = -1;

            foreach (var ctor in ctors)
            {
                var parameters = ctor.GetParameters();
                if (parameters.Length == 0) continue;

                bool allParamsFound = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    bool found = false;
                    for (int j = 0; j < valueColumns.Count; j++)
                    {
                        if (NormalizedEquals(parameters[i].Name, valueColumns[j]))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        allParamsFound = false;
                        break;
                    }
                }

                if (allParamsFound)
                {
                    if (parameters.Length == valueColumns.Count)
                    {
                        return ctor;
                    }

                    if (parameters.Length > maxMatchedParams)
                    {
                        maxMatchedParams = parameters.Length;
                        bestCtor = ctor;
                    }
                }
            }

            if (bestCtor != null)
            {
                return bestCtor;
            }

            throw new InvalidOperationException(
                $"No constructor of '{type.Name}' matches value columns [{string.Join(", ", valueColumns)}]. " +
                $"Available constructors: {string.Join("; ", ctors.Select(c => $"({string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})"))}");
        }

        /// <summary>
        /// 获取或编译高性能实体构造委托。
        /// </summary>
        public static Func<string[], T> GetOrCreateCompiledFactory<T>(ConstructorInfo ctor, IReadOnlyList<string> valueColumns) where T : class
        {
            string cacheKey = $"{typeof(T).FullName}::{string.Join(",", valueColumns)}";
            return (Func<string[], T>)DelegateCache.GetOrAdd(cacheKey, _ => CompileFactory<T>(ctor, valueColumns));
        }

        private static Func<string[], T> CompileFactory<T>(ConstructorInfo ctor, IReadOnlyList<string> valueColumns) where T : class
        {
            var valuesParam = Expression.Parameter(typeof(string[]), "values");
            var parameters = ctor.GetParameters();
            var argExpressions = new Expression[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var targetType = parameters[i].ParameterType;
                var paramName = parameters[i].Name ?? $"p{i}";
                int colIndex = -1;
                for (int j = 0; j < valueColumns.Count; j++)
                {
                    if (NormalizedEquals(paramName, valueColumns[j]))
                    {
                        colIndex = j;
                        break;
                    }
                }

                if (colIndex == -1)
                {
                    throw new InvalidOperationException($"Parameter '{paramName}' not found in value columns [{string.Join(", ", valueColumns)}].");
                }

                var indexExpr = Expression.Constant(colIndex);
                var arrayAccess = Expression.ArrayIndex(valuesParam, indexExpr);
                var nameExpr = Expression.Constant(paramName);

                // 根据参数类型调用强类型转换静态方法（传入参数名提供上下文）
                MethodInfo parseMethod = GetParserMethod(targetType);
                argExpressions[i] = Expression.Call(parseMethod, arrayAccess, nameExpr);
            }

            var newExpr = Expression.New(ctor, argExpressions);
            var lambda = Expression.Lambda<Func<string[], T>>(newExpr, valuesParam);
            return lambda.Compile();
        }

        private static MethodInfo GetParserMethod(Type targetType)
        {
            // 1. 基础值类型与引用类型
            if (targetType == typeof(string)) return typeof(EntityMapper).GetMethod(nameof(ParseString), BindingFlags.Public | BindingFlags.Static)!;
            if (targetType == typeof(double)) return typeof(EntityMapper).GetMethod(nameof(ParseDouble), BindingFlags.Public | BindingFlags.Static)!;
            if (targetType == typeof(float)) return typeof(EntityMapper).GetMethod(nameof(ParseFloat), BindingFlags.Public | BindingFlags.Static)!;
            if (targetType == typeof(decimal)) return typeof(EntityMapper).GetMethod(nameof(ParseDecimal), BindingFlags.Public | BindingFlags.Static)!;
            if (targetType == typeof(long)) return typeof(EntityMapper).GetMethod(nameof(ParseLong), BindingFlags.Public | BindingFlags.Static)!;
            if (targetType == typeof(int)) return typeof(EntityMapper).GetMethod(nameof(ParseInt), BindingFlags.Public | BindingFlags.Static)!;
            if (targetType == typeof(bool)) return typeof(EntityMapper).GetMethod(nameof(ParseBool), BindingFlags.Public | BindingFlags.Static)!;
            if (targetType == typeof(DateTime)) return typeof(EntityMapper).GetMethod(nameof(ParseDateTime), BindingFlags.Public | BindingFlags.Static)!;

            // 2. 可空值类型 (Nullable<T>) 原生支持，避免 Convert.ChangeType 抛出 InvalidCastException
            if (targetType == typeof(double?)) return typeof(EntityMapper).GetMethod(nameof(ParseNullableDouble), BindingFlags.Public | BindingFlags.Static)!;
            if (targetType == typeof(float?)) return typeof(EntityMapper).GetMethod(nameof(ParseNullableFloat), BindingFlags.Public | BindingFlags.Static)!;
            if (targetType == typeof(decimal?)) return typeof(EntityMapper).GetMethod(nameof(ParseNullableDecimal), BindingFlags.Public | BindingFlags.Static)!;
            if (targetType == typeof(long?)) return typeof(EntityMapper).GetMethod(nameof(ParseNullableLong), BindingFlags.Public | BindingFlags.Static)!;
            if (targetType == typeof(int?)) return typeof(EntityMapper).GetMethod(nameof(ParseNullableInt), BindingFlags.Public | BindingFlags.Static)!;
            if (targetType == typeof(bool?)) return typeof(EntityMapper).GetMethod(nameof(ParseNullableBool), BindingFlags.Public | BindingFlags.Static)!;
            if (targetType == typeof(DateTime?)) return typeof(EntityMapper).GetMethod(nameof(ParseNullableDateTime), BindingFlags.Public | BindingFlags.Static)!;

            return typeof(EntityMapper).GetMethod(nameof(ParseFallback), BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(targetType);
        }

        public static string ParseString(string s, string paramName) => s ?? string.Empty;

        public static double ParseDouble(string s, string paramName)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0.0;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) return d;
            throw new InvalidDataException($"Cannot parse value '{s}' to double for parameter '{paramName}'.");
        }

        public static float ParseFloat(string s, string paramName)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0.0f;
            if (float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out float f)) return f;
            throw new InvalidDataException($"Cannot parse value '{s}' to float for parameter '{paramName}'.");
        }

        public static decimal ParseDecimal(string s, string paramName)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal m)) return m;
            throw new InvalidDataException($"Cannot parse value '{s}' to decimal for parameter '{paramName}'.");
        }

        public static long ParseLong(string s, string paramName)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0L;
            if (long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out long l)) return l;
            throw new InvalidDataException($"Cannot parse value '{s}' to long for parameter '{paramName}'.");
        }

        public static int ParseInt(string s, string paramName)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out int i)) return i;
            throw new InvalidDataException($"Cannot parse value '{s}' to int for parameter '{paramName}'.");
        }

        public static bool ParseBool(string s, string paramName)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (bool.TryParse(s, out bool b)) return b;
            if (s == "1") return true;
            if (s == "0") return false;
            throw new InvalidDataException($"Cannot parse value '{s}' to bool for parameter '{paramName}'.");
        }

        public static DateTime ParseDateTime(string s, string paramName)
        {
            if (string.IsNullOrWhiteSpace(s)) return DateTime.MinValue;
            if (long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out long ts))
                return DateTimeOffset.FromUnixTimeMilliseconds(ts).DateTime;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                return dt;
            if (DateTime.TryParse(s, out DateTime dtLocal))
                return dtLocal;
            throw new InvalidDataException($"Cannot parse value '{s}' to DateTime for parameter '{paramName}'.");
        }

        public static double? ParseNullableDouble(string s, string paramName)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) return d;
            throw new InvalidDataException($"Cannot parse value '{s}' to double? for parameter '{paramName}'.");
        }

        public static float? ParseNullableFloat(string s, string paramName)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out float f)) return f;
            throw new InvalidDataException($"Cannot parse value '{s}' to float? for parameter '{paramName}'.");
        }

        public static decimal? ParseNullableDecimal(string s, string paramName)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal m)) return m;
            throw new InvalidDataException($"Cannot parse value '{s}' to decimal? for parameter '{paramName}'.");
        }

        public static long? ParseNullableLong(string s, string paramName)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out long l)) return l;
            throw new InvalidDataException($"Cannot parse value '{s}' to long? for parameter '{paramName}'.");
        }

        public static int? ParseNullableInt(string s, string paramName)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out int i)) return i;
            throw new InvalidDataException($"Cannot parse value '{s}' to int? for parameter '{paramName}'.");
        }

        public static bool? ParseNullableBool(string s, string paramName)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (bool.TryParse(s, out bool b)) return b;
            if (s == "1") return true;
            if (s == "0") return false;
            throw new InvalidDataException($"Cannot parse value '{s}' to bool? for parameter '{paramName}'.");
        }

        public static DateTime? ParseNullableDateTime(string s, string paramName)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out long ts))
                return DateTimeOffset.FromUnixTimeMilliseconds(ts).DateTime;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                return dt;
            if (DateTime.TryParse(s, out DateTime dtLocal))
                return dtLocal;
            throw new InvalidDataException($"Cannot parse value '{s}' to DateTime? for parameter '{paramName}'.");
        }

        public static T? ParseFallback<T>(string s, string paramName)
        {
            if (string.IsNullOrWhiteSpace(s)) return default;
            var targetType = typeof(T);
            var underlying = Nullable.GetUnderlyingType(targetType);
            var effectiveType = underlying ?? targetType;

            if (effectiveType.IsEnum)
            {
                if (Enum.TryParse(effectiveType, s, ignoreCase: true, out object? enumVal))
                {
                    return (T?)enumVal;
                }
                throw new InvalidDataException($"Cannot parse value '{s}' to enum '{effectiveType.Name}' for parameter '{paramName}'.");
            }

            try
            {
                if (underlying != null)
                {
                    return (T)Convert.ChangeType(s, underlying, CultureInfo.InvariantCulture);
                }
                return (T)Convert.ChangeType(s, targetType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Cannot parse value '{s}' to type '{targetType.Name}' for parameter '{paramName}'.", ex);
            }
        }

        public static bool NormalizedEquals(string? a, string? b)
        {
            if (a == null || b == null) return false;
            return string.Equals(
                a.Replace("_", "").ToLowerInvariant(),
                b.Replace("_", "").ToLowerInvariant(),
                StringComparison.Ordinal);
        }
    }
}
