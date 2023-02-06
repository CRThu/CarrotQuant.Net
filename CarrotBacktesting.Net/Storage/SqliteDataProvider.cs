using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.DataModel;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Storage
{
    /// <summary>
    /// Sqlite数据库历史数据获取接口<br/>
    /// 要求数据结构:<br/>
    /// 表名为股票代码<br/>
    /// 字段类型全部为TEXT<br/>
    /// 日期字段格式为 2021-01-01<br/>
    /// 时间字段格式为 2021-01-01 01:02:03.456<br/>
    /// 表每条数据按时间正序排列
    /// </summary>
    public class SqliteDataProvider : IDataProvider
    {
        /// <summary>
        /// Sqlite操作类实例
        /// </summary>
        private SqliteHelper SqliteHelper { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dbPath">数据库文件路径</param>
        /// <param name="mapper">字段映射信息存储类</param>
        public SqliteDataProvider(string dbPath, ShareFrameMapper? mapper = null)
        {
            SqliteHelper = new();
            SqliteHelper.Open(dbPath, mapper);
        }

        /// <summary>
        /// 获取某支股票数据
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <param name="fields">字段集合</param>
        /// <param name="startTime">开始时间</param>
        /// <param name="endTime">结束时间</param>
        /// <returns>股票数据帧集合</returns>
        public IEnumerable<ShareFrame> GetShareData(string stockCode, string[] fields, DateTime? startTime = null, DateTime? endTime = null)
        {
            IEnumerable<IDictionary<string, object>> table;
            // TODO startTime or endTime其中一个为null
            if (startTime is null || endTime is null)
            {
                table = SqliteHelper.GetTable(stockCode, fields);
            }
            else
            {
                table = SqliteHelper.GetTable(stockCode, fields, ("Time", ((DateTime)startTime).FormatDateTime(), ((DateTime)endTime).FormatDateTime(), FilterCondition.BigEqualAndSmallEqual)!);
            }

            List<ShareFrame> frames = new();
            foreach (var frameInfo in table)
            {
                frames.Add(new(frameInfo, stockCode));
            }
            return frames;
        }


        /// <summary>
        /// 获取多支股票数据
        /// </summary>
        /// <param name="stockCode">股票代码集合</param>
        /// <param name="fields">字段集合</param>
        /// <param name="startTime">开始时间</param>
        /// <param name="endTime">结束时间</param>
        /// <returns>股票数据帧集合</returns>
        public IEnumerable<ShareFrame> GetShareData(string[] stockCode, string[] fields, DateTime? startTime = null, DateTime? endTime = null)
        {
            List<ShareFrame> frames = new();
            for (int i = 0; i < stockCode.Length; i++)
            {
                var shareframes = GetShareData(stockCode[i], fields, startTime, endTime);
                frames.AddRange(shareframes);
            }
            return frames;
        }

        /// <summary>
        /// 获取目录下所有股票名称
        /// </summary>
        /// <returns>返回所有股票代码</returns>
        public string[] GetAllStockCode()
        {
            return SqliteHelper.GetTableNames().ToArray();
        }
    }
}
