using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Common
{
    /// <summary>
    /// 通用类型转换器
    /// </summary>
    public static class DynamicConverter
    {
        /// <summary>
        /// Boolean类型True值字符串附加转换字段
        /// </summary>
        private static readonly HashSet<string> BooleanExTrueStrings = new() { "yes", "1", "是" };
        /// <summary>
        /// Boolean类型False值字符串附加转换字段
        /// </summary>
        private static readonly HashSet<string> BooleanExFalseStrings = new() { "no", "0", "否" };

        /// <summary>
        /// Boolean类型添加True值字符串方法
        /// </summary>
        /// <param name="trueStrings">字符串集合</param>
        public static void SetBooleanExTrueStrings(IEnumerable<string> trueStrings)
        {
            foreach (string trueString in trueStrings)
            {
                BooleanExTrueStrings.Add(trueString);
            }
        }

        /// <summary>
        /// Boolean类型添加False值字符串方法
        /// </summary>
        /// <param name="falseStrings">字符串集合</param>
        public static void SetBooleanExFalseStrings(IEnumerable<string> falseStrings)
        {
            foreach (string falseString in falseStrings)
            {
                BooleanExFalseStrings.Add(falseString);
            }
        }

        /// <summary>
        /// BooleanEx类型转换方法
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static bool ToBooleanEx(string value)
        {
            var v = value?.ToLower()?.Trim() ?? "";
            if (BooleanExTrueStrings.Contains(v)) return true;
            if (BooleanExFalseStrings.Contains(v)) return false;
            throw new ArgumentException("Unexpected Boolean Format");
        }

        /// <summary>
        /// 通用转换方法
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="dyn">转换变量</param>
        /// <returns>转换后的变量, 若转换不成功则抛出异常</returns>
        public static T GetValue<T>(dynamic dyn)
        {
            if (dyn is string timeString && default(T) is DateTime)
                return (T)(object)timeString.ParseDateTime();
            else if (dyn is DateTime time && default(T) is string)
                return (T)(object)time.FormatDateTime();
            else if (dyn is string boolString && default(T) is bool)
                return (T)(object)ToBooleanEx(boolString);
            else
                return Convert.ChangeType(dyn, typeof(T));
        }

        /// <summary>
        /// 通用转换方法
        /// </summary>
        /// <param name="dyn">转换变量</param>
        /// <param name="typeName">
        /// 转换类型<br/>
        /// 例如: System.Double, System.String, System.Boolean
        /// </param>
        /// <returns></returns>
        public static dynamic ConvertValue(dynamic dyn, string typeName)
        {
            Type? type = Type.GetType(typeName);
            if (type == null)
                return dyn;
            else
            {
                if (dyn is string timeString && type == typeof(DateTime))
                    return (DateTime)(object)timeString.ParseDateTime();
                else if (dyn is DateTime time && type == typeof(string))
                    return (string)(object)time.FormatDateTime();
                else if (dyn is string boolString && type == typeof(bool))
                    return (bool)(object)ToBooleanEx(boolString);
                else
                    return Convert.ChangeType(dyn, type);
            }
        }
    }
}
