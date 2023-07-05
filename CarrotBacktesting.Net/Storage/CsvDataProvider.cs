using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Engine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Storage
{
    /// <summary>
    /// Csv历史数据获取接口<br/>
    /// 要求数据结构:<br/>
    /// 文件名为股票代码<br/>
    /// 字段类型全部为TEXT<br/>
    /// 日期字段格式为 2021-01-01<br/>
    /// 时间字段格式为 2021-01-01 01:02:03.456<br/>
    /// 表每条数据按时间正序排列
    /// </summary>
    public class CsvDataProvider : IDataProvider
    {
        /// <summary>
        /// 数据管理器
        /// </summary>
        public BackTestingDataManager DataManager { get; set; }

        /// <summary>
        /// Csv操作类实例
        /// </summary>
        private CsvHelper CsvHelper { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dataManager">数据管理器</param>
        /// <param name="mapper">字段映射信息</param>
        /// <exception cref="DirectoryNotFoundException"></exception>
        public CsvDataProvider(BackTestingDataManager dataManager, ShareFrameMapper? mapper = null)
        {
            DataManager = dataManager;
            CsvHelper = new(mapper);
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
            // string filename = Path.Combine(DirectoryPath, stockCode + ".csv");
            string filename = DataManager.GetCsvFilePath(stockCode);

            ShareFrame[] data = CsvHelper.Read(filename, stockCode, fields, startTime, endTime);
            return data.Where(v => (startTime == null || v.Time >= startTime)
                                && (endTime == null || v.Time <= endTime));
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
            return DataManager.ListFiles("csv");
        }
    }
}
