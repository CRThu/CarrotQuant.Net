﻿using CarrotBacktesting.Net.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataModel
{
    /// <summary>
    /// 市场数据构造器
    /// </summary>
    public class MarketDataBuilder
    {
        /// <summary>
        /// 市场数据实例
        /// </summary>
        private MarketData MarketData { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public MarketDataBuilder()
        {
            MarketData = new();
        }

        /// <summary>
        /// 添加一帧股票数据
        /// </summary>
        /// <param name="shareFrame">股票帧</param>
        public void Add(ShareFrame shareFrame)
        {
            // 若Data不包含此日期的市场信息帧则新建一帧
            MarketData.Add(shareFrame);
        }

        /// <summary>
        /// 添加股票帧集合
        /// </summary>
        /// <param name="shareFrames">帧集合</param>
        public void AddRange(IEnumerable<ShareFrame> shareFrames)
        {
            foreach (var sf in shareFrames)
                Add(sf);
        }

        /// <summary>
        /// 返回市场数据实例
        /// </summary>
        /// <returns>市场数据实例</returns>
        public MarketData ToMarketData()
        {
            return MarketData;
        }

    }
}
