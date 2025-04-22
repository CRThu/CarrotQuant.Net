using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 回测数据管理类 <br/>
    /// 存储文件结构如下 <br/>
    /// - BaseDirectory <br/>
    ///     |- csv <br/>
    ///     |   |- DataSet1 <br/>
    ///     |       |- DataSet1Child <br/>
    ///     |           |- 001.csv <br/>
    ///     |           |- 002.csv <br/>
    ///     |           |- ... <br/>
    ///     |- json <br/>
    ///     |   |- DataSet1 <br/>
    ///     |       |- DataSet1Child <br/>
    ///     |           |- options.json <br/>
    ///     |           |- stockcodes.json <br/>
    ///     |           |- fields.json <br/>
    ///     |           |- mapper.json <br/>
    ///     |           |- ... <br/>
    ///     |- cache <br/>       
    ///     |   |- status.json <br/>
    ///     |   |- raw <br/>
    ///     |       |- DataSet1.DataSet1Child.cache <br/>
    ///           
    /// </summary>
    public class BackTestingDataManager
    {
        /// <summary>
        /// 回测数据根目录
        /// </summary>
        public string BaseDirectory { get; set; }

        /// <summary>
        /// 回测数据集
        /// </summary>
        public string DataSet { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="baseDir">数据根目录</param>
        /// <param name="dataSet">回测数据集</param>
        private BackTestingDataManager(string baseDir, string dataSet)
        {
            BaseDirectory = baseDir;
            DataSet = dataSet;
        }

        /// <summary>
        /// 生成回测数据管理器
        /// </summary>
        /// <param name="baseDir">数据根目录</param>
        /// <param name="dataSet">回测数据集</param>
        /// <returns>返回实例</returns>
        public static BackTestingDataManager Create(string baseDir, string dataSet)
        {
            return new BackTestingDataManager(baseDir, dataSet);
        }

        public string[] ListFiles(string ext, bool withoutExt = true)
        {
            string findDir = Path.Combine(BaseDirectory, ext, DataSet);
            DirectoryInfo folder = new(findDir);
            if (withoutExt)
                return folder.GetFiles().Select(v => Path.GetFileNameWithoutExtension(v.Name)).ToArray();
            else
                return folder.GetFiles().Select(v => Path.GetFileName(v.Name)).ToArray();

        }

        public string GetFilePath(string fileName, string ext)
        {
            if (!fileName.EndsWith($".{ext}"))
                fileName += $".{ext}";
            return Path.Combine(BaseDirectory, ext, DataSet, fileName);
        }

        public string GetCsvFilePath(string csvName)
        {
            if (!csvName.EndsWith(".csv"))
                csvName += ".csv";
            return Path.Combine(BaseDirectory, "csv", DataSet, csvName);
        }

        public string GetJsonFilePath(string jsonName)
        {
            if (!jsonName.EndsWith(".json"))
                jsonName += ".json";
            return Path.Combine(BaseDirectory, "json", DataSet, jsonName);
        }

        public string GetCacheFilePath()
        {
            string cacheName = DataSet.Replace("/", ".").Replace("\\", ".") + ".cache";
            return Path.Combine(BaseDirectory, "cache", "raw", cacheName);
        }

        public string GetCacheStatusPath()
        {
            return Path.Combine(BaseDirectory, "cache", "status.json");
        }

        public bool IsCacheExist()
        {
            return Path.Exists(GetCacheFilePath());
        }
    }
}
