using CarrotBacktesting.Net.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Storage
{
    /// <summary>
    /// 历史数据获取接口
    /// </summary>
    internal interface IDataProvider
    {
        /// <summary>
        /// 获取历史股票数据
        /// </summary>
        /// <param name="shareCode">股票代码</param>
        /// <param name="fields">字段名</param>
        /// <param name="startTime">开始时间</param>
        /// <param name="endTime">结束时间</param>
        /// <returns>存放历史数据的<see cref="ShareData"/>类</returns>
        public MarketData GetShareHistoryData(string shareCode, string[] fields,
            DateTime? startTime = null, DateTime? endTime = null);
    }
}
