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
        /// <returns>股票数据帧集合</returns>
        public IEnumerable<ShareFrame> GetShareData(string stockCode);

        /// <summary>
        /// 获取多支股票数据
        /// </summary>
        /// <param name="stockCode">股票代码集合</param>
        /// <returns>股票数据帧集合</returns>
        public IEnumerable<ShareFrame> GetShareData(string[] stockCode);

        /// <summary>
        /// 获取目录下所有股票名称
        /// </summary>
        /// <returns>返回所有股票代码</returns>
        public string[] GetAllStockCode();
    }
}
