using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Common
{
    public static class ArrayMisc
    {
        /// <summary>
        /// 多个数组拼接
        /// </summary>
        /// <typeparam name="T">数组类型</typeparam>
        /// <param name="value">多个数组, 可为null或Empty</param>
        /// <returns></returns>
        public static T[] ArrayCombine<T>(params T[]?[] value)
        {
            int len = value.Sum(x => x.Length);
            T[] newArray = new T[len];

            int cur = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != null)
                {
                    Array.Copy(value[i]!, 0, newArray, cur, value[i]!.Length);
                    cur += value[i]!.Length;
                }
            }

            return newArray.ToArray();
        }
    }
}
