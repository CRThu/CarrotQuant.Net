using MessagePack;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Result
{
    [Obsolete]
    [MessagePackObject]
    public class SignalSet
    {
        /// <summary>
        /// 内部使用线程安全的 ConcurrentBag 来收集信号。
        /// </summary>
        private ConcurrentBag<SignalInfo> _signalsBag = new();

        /// <summary>
        /// 获取信号总数。
        /// </summary>
        [JsonIgnore]
        [IgnoreMember]
        public int Count => _signalsBag.Count;

        /// <summary>
        /// 缓存排序后的列表，避免每次访问都重新排序。
        /// </summary>
        private IEnumerable<SignalInfo>? _sortedSignalsCache = null;

        [Key(0)]
        [JsonInclude]
        public IEnumerable<SignalInfo> Signals
        {
            get => GetSignals();
            set => _signalsBag = new ConcurrentBag<SignalInfo>(value);
        }

        /// <summary>
        /// 获取所有信号的只读列表，并确保它们是按日期和代码排序的。
        /// 第一次访问时会进行排序和缓存。
        /// </summary>
        public IEnumerable<SignalInfo> GetSignals()
        {
            if (_sortedSignalsCache == null)
            {
                _sortedSignalsCache = _signalsBag.OrderBy(s => s.Date).ThenBy(s => s.StockCode);
            }
            return _sortedSignalsCache;
        }

        /// <summary>
        /// 向集合中添加一个新信号。
        /// </summary>
        /// <param name="stockCode"></param>
        /// <param name="date"></param>
        public void Store(string stockCode, DateTime date)
        {
            _signalsBag.Add(new SignalInfo(stockCode, date));
            // 每当有新信号加入时，就使缓存失效。
            _sortedSignalsCache = null;
        }
    }
}
