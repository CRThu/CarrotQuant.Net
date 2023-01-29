using CarrotBacktesting.Net.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CarrotBacktesting.Net.Storage
{
    public class CsvHelper
    {
        public string FileName { get; set; }

        /// <summary>
        /// 字段映射表
        /// </summary>
        private ShareFrameMapper? Mapper { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="mapper"></param>
        public CsvHelper(string fileName, ShareFrameMapper? mapper = null)
        {
            FileName = fileName;
            Mapper = mapper;
        }

        /// <summary>
        /// 读取历史数据
        /// </summary>
        /// <returns>ShareFrame[]</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public ShareFrame[] Read()
        {

            string[] content = File.ReadAllLines(FileName);
            if (content.Length == 0)
            {
                throw new InvalidOperationException($"文件:{FileName}为空");
            }

            // 映射
            string[] cols = content[0].Split(',');
            if (Mapper != null)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    cols[i] = Mapper[cols[i]];
                }
            }

            ShareFrame[] elements = content.Skip(1)
                                           .Where(v => v.Contains(','))
                                           .Select(v => new ShareFrame(cols, v.Split(',')))
                                           .ToArray();

            return elements;
        }
    }
}
