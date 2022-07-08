using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private readonly Dictionary<string, string> MapDict;

        /// <summary>
        /// 构造函数
        /// </summary>
        public ShareFrameMapper()
        {
            MapDict = new();
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
    }
}
