using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        private List<DateTime> DateTimesCache { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public MarketData()
        {
            MarketFrames = new();
            DateTimesCache = new();
        }

        /// <summary>
        /// 更新日期缓存
        /// </summary>
        public void UpdateCache()
        {
            if (IsCacheDirty)
            {
                DateTimesCache = MarketFrames.Keys.ToList();
                DateTimesCache.Sort();
                IsCacheDirty = false;
            }
        }

        /// <summary>
        /// 添加帧
        /// </summary>
        /// <param name="frame">市场信息帧, 若已添加相同时间的帧则覆盖</param>
        public void Set(MarketFrame frame)
        {
            IsCacheDirty = true;

            throw new NotImplementedException();
        }

        /// <summary>
        /// 获取帧
        /// </summary>
        /// <param name="dateTime">帧时间</param>
        /// <returns>返回帧, 若此时间帧不存在则返回向前搜索最近的时间帧, 若向前搜索不存在帧则返回null</returns>
        public MarketFrame Get(DateTime dateTime)
        {
            if (IsCacheDirty)
                UpdateCache();

            throw new NotImplementedException();
        }
    }
}
