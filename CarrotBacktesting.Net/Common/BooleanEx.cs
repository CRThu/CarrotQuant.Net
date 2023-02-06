using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Common
{
    /// <summary>
    /// BooleanEx
    /// </summary>
    public static class BooleanEx
    {
        /// <summary>
        /// Boolean类型True值字符串原始转换字段
        /// </summary>
        private static readonly HashSet<string> RawTrueStrings = new() { "true", "yes", "1", "是" };

        /// <summary>
        /// Boolean类型False值字符串附原始转换字段
        /// </summary>
        private static readonly HashSet<string> RawFalseStrings = new() { "false", "no", "0", "否" };

        /// <summary>
        /// Boolean类型True值字符串附加转换字段
        /// </summary>
        public static HashSet<string> TrueStrings { get; private set; } = new(RawTrueStrings);

        /// <summary>
        /// Boolean类型False值字符串附加转换字段
        /// </summary>
        public static HashSet<string> FalseStrings { get; private set; } = new(RawFalseStrings);

        /// <summary>
        /// Boolean类型添加True值字符串方法
        /// </summary>
        /// <param name="trueStrings">字符串集合</param>
        public static void AddBooleanExTrueStrings(params string[] trueStrings)
        {
            for (int i = 0; i < trueStrings.Length; i++)
            {
                TrueStrings.Add(trueStrings[i]);
            }
        }

        /// <summary>
        /// Boolean类型添加False值字符串方法
        /// </summary>
        /// <param name="falseStrings">字符串集合</param>
        public static void AddBooleanExFalseStrings(params string[] falseStrings)
        {
            for (int i = 0; i < falseStrings.Length; i++)
            {
                FalseStrings.Add(falseStrings[i]);
            }
        }

        /// <summary>
        /// Boolean类型重置True值字符串方法
        /// </summary>
        public static void ResetBooleanExTrueStrings()
        {
            TrueStrings = new(RawTrueStrings);
        }

        /// <summary>
        /// Boolean类型重置False值字符串方法
        /// </summary>
        public static void ResetBooleanExFalseStrings()
        {
            FalseStrings = new(RawFalseStrings);
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
            if (TrueStrings.Contains(v)) return true;
            if (FalseStrings.Contains(v)) return false;
            throw new ArgumentException("Unexpected BooleanEx Format");
        }
    }
}
