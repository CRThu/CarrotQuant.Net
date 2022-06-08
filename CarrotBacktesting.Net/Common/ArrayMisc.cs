using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Common
{
    public static class ArrayMisc
    {
        // 多个数组拼接
        public static T[] ArrayCombine<T>(params T[][] value)
        {
            int len = value.Sum(x => x.Length);
            T[] newArray = new T[len];

            int cur = 0;
            for (int i = 0; i < value.Length; i++)
            {
                Array.Copy(value[i], 0, newArray, cur, value[i].Length);
                cur += value[i].Length;
            }

            return newArray.ToArray();
        }
    }
}
