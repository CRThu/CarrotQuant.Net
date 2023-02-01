using CarrotBacktesting.Net.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CarrotBacktesting.Net.Storage
{
    /// <summary>
    /// Csv操作类
    /// </summary>
    public class CsvHelper
    {
        /// <summary>
        /// 字段映射表
        /// </summary>
        private ShareFrameMapper? Mapper { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="mapper"></param>
        public CsvHelper(ShareFrameMapper? mapper = null)
        {
            Mapper = mapper;
        }

        /// <summary>
        /// 读取历史数据
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="stockCode">股票代码</param>
        /// <param name="fields">字段集合</param>
        /// <returns>ShareFrame[]</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public ShareFrame[] Read(string fileName, string stockCode, string[] fields)
        {

            string[] content = File.ReadAllLines(fileName);
            if (content.Length == 0)
            {
                throw new InvalidOperationException($"文件:{fileName}为空");
            }

            // 筛选需要的列
            string[] cols = content[0].Split(',');
            bool[] mask = cols.Select(c => fields.Contains(c)).ToArray();

            // 映射
            if (Mapper != null)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    cols[i] = Mapper[cols[i]];
                }
            }



            ShareFrame[] elements = content.Skip(1)
                                           .Where(v => v.Contains(','))
                                           .Select(v => new ShareFrame(cols, v.Split(','), mask, stockCode))
                                           .ToArray();

            return elements;
        }
    }
}
