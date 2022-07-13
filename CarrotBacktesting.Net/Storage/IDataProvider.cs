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
        /// 获取某支股票数据
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <param name="fields">字段集合</param>
        /// <param name="startTime">开始时间</param>
        /// <param name="endTime">结束时间</param>
        /// <returns>股票数据帧集合</returns>
        public IEnumerable<ShareFrame> GetShareData(string stockCode, string[] fields,
            DateTime? startTime = null, DateTime? endTime = null);

        /// <summary>
        /// 获取多支股票数据
        /// </summary>
        /// <param name="stockCode">股票代码集合</param>
        /// <param name="fields">字段集合</param>
        /// <param name="startTime">开始时间</param>
        /// <param name="endTime">结束时间</param>
        /// <returns>股票数据帧集合</returns>
        public IEnumerable<ShareFrame> GetShareData(string[] stockCode, string[] fields,
            DateTime? startTime = null, DateTime? endTime = null);
    }
}
