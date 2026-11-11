using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 基于内存字典索引的异构事件表数据容器实现。
    /// 支持 O(1) 的 (date, symbol) 点查和全市场日快照。
    /// </summary>
    public class EventTable<T> : IEventTable<T> where T : class
    {
        private readonly Dictionary<DateTime, Dictionary<string, T>> _data;

        public EventTable(Dictionary<DateTime, Dictionary<string, T>> data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public bool TryGet(DateTime date, string symbol, out T? value)
        {
            if (_data.TryGetValue(date.Date, out var dayDict))
            {
                return dayDict.TryGetValue(symbol, out value);
            }
            value = null;
            return false;
        }

        public bool TryGetDaily(DateTime date, out IReadOnlyDictionary<string, T> values)
        {
            if (_data.TryGetValue(date.Date, out var dayDict))
            {
                values = dayDict;
                return true;
            }
            values = null!;
            return false;
        }
    }
}
