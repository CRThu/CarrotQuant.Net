using CarrotBacktesting.Net.Common;

namespace CarrotBacktesting.Net.DataModel
{
    /// <summary>
    /// 市场信息帧集合类
    /// </summary>
    public class MarketData
    {

        /// <summary>
        /// 包含多个时间与多支股票信息的市场帧集合
        /// key: 时间
        /// value: 市场帧
        /// </summary>
        private Dictionary<DateTime, MarketFrame> MarketFrames { get; set; }

        /// <summary>
        /// 当<see cref="MarketFrames"/>新增新数据未更新<see cref="DateTimesCache"/>时则置true
        /// </summary>
        private bool IsCacheDirty { get; set; }

        /// <summary>
        /// MarketFrames Keys时间集合缓存
        /// </summary>
        private DateTime[] dateTimesCache = Array.Empty<DateTime>();
        /// <summary>
        /// MarketFrames Keys时间集合缓存访问器
        /// </summary>
        private DateTime[] DateTimesCache
        {
            get
            {
                // 若时间缓存不是最新的则刷新
                RefrehCache();
                return dateTimesCache;
            }
            set
            {
                dateTimesCache = value;
            }
        }

        /// <summary>
        /// 读取时间的市场信息, 若时间不存在则返回向前搜索最近的时间帧, 若向前搜索不存在帧则返回最早时间
        /// </summary>
        /// <param name="dateTime">时间</param>
        /// <returns>时间对应或相近的市场信息</returns>
        public MarketFrame this[DateTime dateTime]
        {
            get
            {
                return Get(dateTime);
            }
        }

        /// <summary>
        /// 获取时间索引对应的MarketFrame, 顺序为时间升序
        /// </summary>
        /// <param name="index">索引号</param>
        /// <returns>返回时间索引对应的MarketFrame</returns>
        public MarketFrame this[int index]
        {
            get
            {
                return Get(index);
            }
        }

        /// <summary>
        /// 时间帧数量
        /// </summary>
        public int Count => MarketFrames.Count;

        /// <summary>
        /// 时间帧列表
        /// </summary>
        public DateTime[] Times => DateTimesCache;

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime => Times.First();

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime => Times.Last();

        /// <summary>
        /// 获取市场帧
        /// </summary>
        public IEnumerable<MarketFrame> Frames => MarketFrames.Values;

        /// <summary>
        /// 构造函数
        /// </summary>
        public MarketData()
        {
            MarketFrames = new();
        }

        /// <summary>
        /// 更新日期缓存
        /// </summary>
        public void RefrehCache()
        {
            if (IsCacheDirty)
            {
                var dts = MarketFrames.Keys.ToList();
                dts.Sort();
                DateTimesCache = dts.ToArray();
                IsCacheDirty = false;
            }
        }

        /// <summary>
        /// 添加帧
        /// </summary>
        /// <param name="frame">市场信息帧, 若已添加相同时间的帧则覆盖</param>
        public void Add(MarketFrame frame)
        {
            IsCacheDirty = true;

            // 添加数据
            MarketFrames.Add(frame.DateTime, frame);
        }

        /// <summary>
        /// 是否存在此时间的市场信息帧
        /// </summary>
        /// <param name="dateTime">时间</param>
        /// <returns>存在则返回true, 否则返回false</returns>
        public bool Contains(DateTime dateTime)
        {
            return MarketFrames.ContainsKey(dateTime);
        }

        /// <summary>
        /// 获取此时间对应的市场信息帧
        /// </summary>
        /// <param name="dateTime">时间</param>
        /// <returns>此时间对应的市场信息帧</returns>
        public MarketFrame Get(DateTime dateTime)
        {
            // 精确时间查找
            return MarketFrames[dateTime];
        }

        /// <summary>
        /// 获取时间索引对应的MarketFrame, 顺序为时间升序
        /// </summary>
        /// <param name="index">索引号</param>
        /// <returns>返回时间索引对应的MarketFrame</returns>
        public MarketFrame Get(int index)
        {
            return MarketFrames[DateTimesCache[index]];
        }

        /// <summary>
        /// 获取此时间对应的市场信息帧
        /// </summary>
        /// <param name="dateTime">时间</param>
        /// <param name="marketFrame">此时间对应的市场信息帧</param>
        /// <returns>若存在则返回true, 否则返回false</returns>
        public bool TryGet(DateTime dateTime, out MarketFrame? marketFrame)
        {
            return MarketFrames.TryGetValue(dateTime, out marketFrame);
        }

        /// <summary>
        /// 获取此时间对应或相近的市场信息帧
        /// </summary>
        /// <param name="dateTime">时间</param>
        /// <param name="frame">返回帧, 若此时间帧不存在则返回向前搜索最近的时间帧, 若向前搜索不存在帧则返回最早时间</param>
        /// <returns>是否为此时间对应市场信息帧</returns>
        public bool GetNearby(DateTime dateTime, out MarketFrame frame)
        {
            // 若存在则直接返回
            if (TryGet(dateTime, out MarketFrame? marketFrame))
            {
                frame = marketFrame!;
                return true;
            }

            // 二分法查找最新时间, 若不存在则向前模糊时间查找
            var dtnb = DateTimesCache.GetTimeNearby(dateTime);
            frame = MarketFrames[dtnb];
            return false;
        }
    }
}
