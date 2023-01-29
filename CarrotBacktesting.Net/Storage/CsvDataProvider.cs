using CarrotBacktesting.Net.DataModel;
using System;
using System.Collections.Generic;
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
        public string DirectoryPath { get; set; }

        private CsvHelper CsvHelper { get; set; }

        public CsvDataProvider(string directoryPath, ShareFrameMapper? mapper = null)
        {
            DirectoryPath = directoryPath;
            CsvHelper = new(mapper);
        }

        public IEnumerable<ShareFrame> GetShareData(string stockCode, string[] fields, DateTime? startTime = null, DateTime? endTime = null)
        {
            string filename = Path.Combine(DirectoryPath, stockCode + ".csv");

            ShareFrame[] data = CsvHelper.Read(filename);
            if (startTime == null)
                startTime = data[0].DateTime;
            if (endTime == null)
                endTime = data[^1].DateTime;
            return data.Where(d => d.DateTime >= startTime && d.DateTime <= endTime);
        }

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
    }
}
