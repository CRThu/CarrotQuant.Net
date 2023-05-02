using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        /// 通用转换方法
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="dyn">转换变量</param>
        /// <returns>转换后的变量, 若转换不成功则抛出异常</returns>
        public static T GetValue<T>(dynamic dyn)
        {
            // 日期与时间类型转换为限定类型
            if (dyn is string timeString && default(T) is DateTime)
                return (T)(object)timeString.ParseDateTime();
            else if (dyn is DateTime time && default(T) is string)
                return (T)(object)time.FormatDateTime();
            // Boolean类型转换为限定类型
            else if (dyn is string boolString && default(T) is bool)
                return (T)(object)BooleanEx.ToBooleanEx(boolString);
            // 输入为字符串且内容为null或empty,转换类型为数字类型则返回0
            else if (string.IsNullOrEmpty(dyn.ToString()) && IsNumeric(typeof(T)))
            {
                return (T)(object)default(T)!;
            }
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
                MethodInfo method = typeof(DynamicConverter).GetMethod("GetValue")!;
                MethodInfo generic = method.MakeGenericMethod(type);
                return generic.Invoke(null, new object[] { dyn })!;
            }
        }

        /// <summary>
        /// 是否为数字类型
        /// </summary>
        /// <param name="dataType"></param>
        /// <returns></returns>
        public static bool IsNumeric(Type dataType)
        {
            // The following data types are considered numeric
            return (dataType == typeof(System.Boolean)
                    || dataType == typeof(System.Byte)
                    || dataType == typeof(System.Char)
                    || dataType == typeof(System.Decimal)
                    || dataType == typeof(System.Double)
                    || dataType == typeof(System.Int16)
                    || dataType == typeof(System.Int32)
                    || dataType == typeof(System.Int64)
                    || dataType == typeof(System.SByte)
                    || dataType == typeof(System.Single)
                    || dataType == typeof(System.UInt16)
                    || dataType == typeof(System.UInt32)
                    || dataType == typeof(System.UInt64)
                   );
        }
    }
}
