using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Common
{
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
            if (dyn is string timeString && default(T) is DateTime)
                return (T)(object)timeString.ParseDateTime();
            else if (dyn is DateTime time && default(T) is string)
                return (T)(object)time.FormatDateTime();
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
                else
                    return Convert.ChangeType(dyn, type);
            }
        }
    }
}
