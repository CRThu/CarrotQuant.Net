using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace CarrotBacktesting.NET.Config;

/// <summary>
/// 通用配置属性反射注入工具 (Configuration Property Injector)。
/// 负责将 YAML 解析出的松散参数字典（支持 snake_case、camelCase 或 PascalCase）
/// 严格类型化转换并安全注入到目标对象（策略、分析器、导出器、Hook 实例等）的公共属性中（SSOT）。
/// </summary>
public static class ConfigPropertyInjector
{
    /// <summary>
    /// 将键值参数字典安全注入到目标对象的公共可写属性中。
    /// </summary>
    /// <param name="target">待注入的目标对象实例。</param>
    /// <param name="parameters">YAML 解析生成的参数键值对。</param>
    /// <param name="targetDisplayName">用于异常提示的目标实体友好名称。</param>
    public static void Inject(object target, Dictionary<string, object> parameters, string? targetDisplayName = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (parameters == null || parameters.Count == 0) return;

        string displayName = targetDisplayName ?? target.GetType().Name;
        var properties = target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.SetMethod != null && p.SetMethod.IsPublic)
            .ToList();

        // 建立双重快速索引：原名与去下划线紧凑名（全大小写不敏感）
        var byExactName = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        var byNormalizedName = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in properties)
        {
            byExactName[prop.Name] = prop;
            string norm = prop.Name.Replace("_", "");
            byNormalizedName.TryAdd(norm, prop);
        }

        foreach (var (key, rawValue) in parameters)
        {
            if (!TryFindProperty(key, byExactName, byNormalizedName, out var prop))
            {
                var availableProps = properties.Select(p => p.Name).OrderBy(k => k);
                throw new ArgumentException(
                    $"目标 '{displayName}' (类型 {target.GetType().FullName}) 不存在可写入的公共属性 '{key}'。请检查 YAML 配置中的拼写。可用属性: [{string.Join(", ", availableProps)}]");
            }

            try
            {
                var convertedValue = ConvertValue(rawValue, prop.PropertyType);
                prop.SetValue(target, convertedValue);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"为目标 '{displayName}' 注入参数 '{key}' 失败 (目标类型 {prop.PropertyType.Name}, 原始值 '{rawValue}'): {ex.Message}", ex);
            }
        }
    }

    private static bool TryFindProperty(
        string key,
        Dictionary<string, PropertyInfo> byExactName,
        Dictionary<string, PropertyInfo> byNormalizedName,
        out PropertyInfo prop)
    {
        if (byExactName.TryGetValue(key, out prop!)) return true;

        string normKey = key.Replace("_", "");
        return byNormalizedName.TryGetValue(normKey, out prop!);
    }

    /// <summary>
    /// 将原始对象值安全转换为目标强类型。
    /// 支持基础数值、布尔、枚举、TimeSpan、数组及泛型 List 容器。
    /// </summary>
    public static object? ConvertValue(object? rawValue, Type targetType)
    {
        if (rawValue == null)
        {
            return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null
                ? Activator.CreateInstance(targetType)
                : null;
        }

        if (targetType.IsInstanceOfType(rawValue))
        {
            return rawValue;
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // 1. 枚举解析
        if (underlyingType.IsEnum)
        {
            return Enum.Parse(underlyingType, rawValue.ToString()!, ignoreCase: true);
        }

        // 2. TimeSpan 支持
        if (underlyingType == typeof(TimeSpan))
        {
            return rawValue switch
            {
                TimeSpan ts => ts,
                string s => TimeSpan.Parse(s, CultureInfo.InvariantCulture),
                _ => TimeSpan.FromSeconds(Convert.ToDouble(rawValue, CultureInfo.InvariantCulture))
            };
        }

        // 3. 数组转换 (例如 YAML 的 List<object> -> string[], double[], int[])
        if (underlyingType.IsArray)
        {
            var elementType = underlyingType.GetElementType()!;
            if (rawValue is IEnumerable enumerable && rawValue is not string)
            {
                var items = new List<object?>();
                foreach (var item in enumerable)
                {
                    items.Add(ConvertValue(item, elementType));
                }
                var array = Array.CreateInstance(elementType, items.Count);
                for (int i = 0; i < items.Count; i++)
                {
                    array.SetValue(items[i], i);
                }
                return array;
            }
        }

        // 4. 泛型集合容器支持 (List<T>, IList<T>, IReadOnlyList<T>, IReadOnlyCollection<T>, ICollection<T>, IEnumerable<T>)
        if (underlyingType.IsGenericType)
        {
            var genericDef = underlyingType.GetGenericTypeDefinition();
            if (genericDef == typeof(List<>)
             || genericDef == typeof(IList<>)
             || genericDef == typeof(IReadOnlyList<>)
             || genericDef == typeof(IReadOnlyCollection<>)
             || genericDef == typeof(ICollection<>)
             || genericDef == typeof(IEnumerable<>))
            {
                var elementType = underlyingType.GetGenericArguments()[0];
                if (rawValue is IEnumerable enumerable && rawValue is not string)
                {
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    var listInstance = (IList)Activator.CreateInstance(listType)!;
                    foreach (var item in enumerable)
                    {
                        listInstance.Add(ConvertValue(item, elementType));
                    }
                    return listInstance;
                }
            }
        }

        // 5. 基础基元类型与数值安全转换
        return Convert.ChangeType(rawValue, underlyingType, CultureInfo.InvariantCulture);
    }
}
