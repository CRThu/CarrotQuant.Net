using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Analyzer
{
    /// <summary>
    /// 记录时间范围信息的数据类型
    /// </summary>
    public struct DateRangeData<T>
    {
        /// <summary>
        /// 范围开始时间
        /// </summary>
        public DateTime Start { get; set; }
        /// <summary>
        /// 范围结束时间
        /// </summary>
        public DateTime End { get; set; }
        /// <summary>
        /// 存储数据
        /// </summary>
        public T Value { get; set; }

        /// <summary>
        /// 范围格式化
        /// </summary>
        public string Range
        {
            get
            {
                return $"{Start} - {End}";
            }
        }
    }
}
