using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataModel
{
    /// <summary>
    /// 字段映射信息存储类
    /// </summary>
    public class ShareFrameMapper
    {
        /// <summary>
        /// key:字段名(对应DataProvider数据源字段)<br/>
        /// value:映射名(对应ShareFrame输入属性)<br/>
        /// </summary>
        public Dictionary<string, string> MapDict { get; set; }

        /// <summary>
        /// TODO
        /// key:字段名(对应DataProvider数据源字段)<br/>
        /// value:映射类型(仅对<see cref="ShareFrame.Params"/>有效)<br/>
        /// </summary>
        public Dictionary<string, string> TypeDict { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public ShareFrameMapper()
        {
            MapDict = new();
            TypeDict = new();
        }

        /// <summary>
        /// 读取或写入映射键值对
        /// </summary>
        /// <param name="key">字段名</param>
        /// <returns>映射名, 若不存在字段映射则返回字段名</returns>
        public string this[string key]
        {
            get => Get(key);
            set => MapDict[key] = value;
        }

        /// <summary>
        /// 读取映射值
        /// </summary>
        /// <param name="key">字段名</param>
        /// <returns>映射名, 若不存在字段映射则返回字段名</returns>
        public string Get(string key)
        {
            if (MapDict.TryGetValue(key, out var value))
                return value;
            else
                return key;
        }

        /// <summary>
        /// 通过json文件构造映射器
        /// </summary>
        /// <param name="jsonpath">json文件路径</param>
        /// <returns>映射器实例</returns>
        public static ShareFrameMapper? Deserialize(string jsonpath)
        {
            string jsonString = File.ReadAllText(jsonpath);
            ShareFrameMapper? mapper = JsonSerializer.Deserialize<ShareFrameMapper>(jsonString);
            return mapper;
        }

        /// <summary>
        /// 映射器存储为json文件
        /// </summary>
        /// <param name="jsonpath">json文件路径</param>
        /// <returns>映射器实例</returns>
        public void Serialize(string jsonpath)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonString = JsonSerializer.Serialize(this, options);
            File.WriteAllText(jsonpath, jsonString);
        }
    }
}
