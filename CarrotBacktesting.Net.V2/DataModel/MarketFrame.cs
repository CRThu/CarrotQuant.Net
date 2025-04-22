using MemoryPack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataModel
{
    /// <summary>
    /// 市场信息帧类
    /// </summary>
    [MemoryPackable]
    public partial class MarketFrame
    {
        /// <summary>
        /// 帧日期
        /// </summary>
        public DateTime Time { get; init; }

        /// <summary>
        /// 存放一帧市场所有股票信息
        /// key: 股票代码
        /// value: 股票帧
        /// </summary>
        public Dictionary<string, ShareFrame> ShareFrames { get; set; }

        /// <summary>
        /// 读取股票在<see cref="Time"/>时间的信息帧
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <returns>返回stockCode对应帧,若不存在则返回null</returns>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        [MemoryPackIgnore]
        public ShareFrame? this[string stockCode]
        {
            get
            {
                if (TryGet(stockCode, out ShareFrame? f))
                    return f;
                else
                    return null;
            }
        }

        /// <summary>
        /// 获取帧存储的股票代码
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        [MemoryPackIgnore]
        public IEnumerable<string> Codes => ShareFrames.Keys;

        /// <summary>
        /// 获取股票帧
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        [MemoryPackIgnore]
        public IEnumerable<ShareFrame> Frames => ShareFrames.Values;

        /// <summary>
        /// 获取当前市场股票数量
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        [MemoryPackIgnore]
        public int Count => ShareFrames.Count;

        [MemoryPackConstructor]
        public MarketFrame()
        {

        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dateTime">市场帧时间</param>
        public MarketFrame(DateTime dateTime)
        {
            Time = dateTime;
            ShareFrames = new();
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dateTime">市场帧日期/时间</param>
        /// <param name="frames">字典接口导入股票帧数据</param>
        public MarketFrame(DateTime dateTime, IDictionary<string, ShareFrame> frames)
        {
            Time = dateTime;
            ShareFrames = new(frames);
        }

        /// <summary>
        /// 添加数据帧
        /// </summary>
        /// <param name="shareFrame">股票数据帧</param>
        /// <exception cref="InvalidOperationException">新增数据帧与现有数据帧时间不符时抛出异常</exception>
        public void Add(ShareFrame shareFrame)
        {
            // 新增数据与帧时间不符时抛出异常
            if (shareFrame.Time != Time)
                throw new InvalidOperationException();

            // 添加数据
            ShareFrames.Add(shareFrame.Code, new(shareFrame));
        }

        /// <summary>
        /// 读取股票在<see cref="Time"/>时间的信息帧
        /// </summary>
        /// <param name="key">股票代码</param>
        /// <param name="value">stockCode对应帧,若不存在则返回null</param>
        /// <returns>存在返回true,若不存在则返回false</returns>
        public bool TryGet(string key, out ShareFrame? value)
        {
            return ShareFrames.TryGetValue(key, out value);
        }
    }
}
