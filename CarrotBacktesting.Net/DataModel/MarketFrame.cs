using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataModel
{
    /// <summary>
    /// 市场信息帧类
    /// </summary>
    public class MarketFrame
    {
        /// <summary>
        /// 帧日期
        /// </summary>
        public DateTime DateTime { get; set; }

        /// <summary>
        /// 存放一帧市场信息
        /// key: 股票代码
        /// value: 股票帧
        /// </summary>
        private Dictionary<string, ShareFrame> ShareFrames { get; set; }

        /// <summary>
        /// 读取股票在<see cref="DateTime"/>时间的信息帧
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <returns>返回stockCode对应帧, 若stockCode不存在则返回null</returns>
        public ShareFrame this[string stockCode]
        {
            get
            {
                return Get(stockCode);
            }
        }

        /// <summary>
        /// 获取帧存储的股票代码
        /// </summary>
        public string[] StockCodes => ShareFrames.Keys.ToArray();

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dateTime">市场帧日期/时间</param>
        public MarketFrame(DateTime dateTime)
        {
            DateTime = dateTime;
            ShareFrames = new();
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dateTime">市场帧日期/时间</param>
        /// <param name="frames">市场帧数据字典</param>
        public MarketFrame(DateTime dateTime, Dictionary<string, ShareFrame> frames)
        {
            DateTime = dateTime;
            ShareFrames = frames;
        }

        /// <summary>
        /// 是否存在此时间的股票信息帧
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <returns>存在则返回true, 否则返回false</returns>
        public bool Contains(string stockCode)
        {
            return ShareFrames.ContainsKey(stockCode);
        }

        /// <summary>
        /// 添加帧
        /// </summary>
        /// <param name="shareFrame">股票帧</param>
        /// <exception cref="InvalidOperationException">新增数据帧与现有数据帧时间不符时抛出异常</exception>
        public void Add(ShareFrame shareFrame)
        {
            // 新增数据与帧时间不符时抛出异常
            if (shareFrame.DateTime != DateTime)
                throw new InvalidOperationException();

            // 添加数据
            ShareFrames.Add(shareFrame.StockCode, shareFrame);
        }

        /// <summary>
        /// 获取帧
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <returns>返回stockCode对应帧, 若stockCode不存在则返回null</returns>
        public ShareFrame? Get(string stockCode)
        {
            if (ShareFrames.TryGetValue(stockCode, out ShareFrame? v))
                return v;
            else
                return null;
        }
    }
}
