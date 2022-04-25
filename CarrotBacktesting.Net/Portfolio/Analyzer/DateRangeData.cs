using CarrotBacktesting.Net.Common;
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
        public DateTime End
        {
            get
            {
                switch (Span)
                {
                    case DateSpan.Day:
                        return Start.AddDays(1).AddMilliseconds(-1);
                    case DateSpan.Month:
                        return Start.AddMonths(1).AddMilliseconds(-1);
                    case DateSpan.Year:
                        return Start.AddYears(1).AddMilliseconds(-1);
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        /// <summary>
        /// 范围跨度
        /// </summary>
        public DateSpan Span { get; set; }
        /// <summary>
        /// 存储数据
        /// </summary>
        public T Value { get; set; }

        /// <summary>
        /// 范围格式化
        /// </summary>
        public string RangeFormat
        {
            get
            {
                return $"{Start} - {End}";
            }
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="start"></param>
        /// <param name="span"></param>
        /// <param name="value"></param>
        public DateRangeData(DateTime start, DateSpan span, T value)
        {
            Start = start;
            Span = span;
            Value = value;
        }

        public bool IsInRange(DateTime dateTime)
        {
            return dateTime >= Start && dateTime <= End;
        }
    }
}
