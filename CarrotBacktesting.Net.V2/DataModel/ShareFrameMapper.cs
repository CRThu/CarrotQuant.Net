using CarrotBacktesting.Net.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataModel
{
    /// <summary>
    /// 字段映射信息存储类
    /// </summary>
    public class ShareFrameMapper
    {
        /// <summary>
        /// 字段名映射字典
        /// key:字段名(对应DataProvider数据源字段)<br/>
        /// value:映射名(对应ShareFrame输入属性)<br/>
        /// </summary>
        public Dictionary<string, string> MapDict { get; set; }

        /// <summary>
        /// 字段类型映射字典
        /// key:字段名(对应DataProvider数据源字段)<br/>
        /// value:映射类型(仅对<see cref="ShareFrame.Params"/>有效)<br/>
        /// </summary>
        public Dictionary<string, string> TypeDict { get; set; }

        /// <summary>
        /// 回测数据中数据类型为bool并转换为System.Boolean的True结果的字符串, 不区分大小写
        /// </summary>
        public HashSet<string> BooleanTrueString { get; set; }

        /// <summary>
        /// 回测数据中数据类型为bool并转换为System.Boolean的False结果的字符串, 不区分大小写
        /// </summary>
        public HashSet<string> BooleanFalseString { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public ShareFrameMapper()
        {
            MapDict = new();
            TypeDict = new();
            BooleanTrueString = new();
            BooleanFalseString = new();
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="mapDict">字段名映射字典</param>
        /// <param name="typeDict">字段类型映射字典</param>
        /// <param name="booleanTrueString">Boolean真值映射集合</param>
        /// <param name="booleanFalseString">Boolean假值映射集合</param>
        public ShareFrameMapper(Dictionary<string, string>? mapDict = null, Dictionary<string, string>? typeDict = null, IEnumerable<string>? booleanTrueString = null, IEnumerable<string>? booleanFalseString = null)
        {
            MapDict = mapDict ?? new();
            TypeDict = typeDict ?? new();
            BooleanTrueString = new();
            BooleanFalseString = new();
            if (booleanTrueString != null)
                BooleanTrueString = new HashSet<string>(booleanTrueString);
            if (booleanFalseString != null)
                BooleanFalseString = new HashSet<string>(booleanFalseString);
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
        /// 添加Boolean字符串对
        /// </summary>
        /// <param name="trueString">真字符串</param>
        /// <param name="falseString">假字符串</param>
        public void AddBooleanStringPair(string? trueString = null, string? falseString = null)
        {
            if (trueString != null)
            {
                BooleanTrueString.Add(trueString);
                BooleanEx.AddBooleanExTrueStrings(trueString);
            }
            if (falseString != null)
            {
                BooleanFalseString.Add(falseString);
                BooleanEx.AddBooleanExFalseStrings(falseString);
            }
        }

        /// <summary>
        /// 更新全局自定义字符串
        /// </summary>
        public void UpdateGlobalBoolString()
        {
            // 在BooleanEx新增自定义字符串
            BooleanEx.ResetBooleanExTrueStrings();
            BooleanEx.AddBooleanExTrueStrings(BooleanTrueString.ToArray());
            // 在BooleanEx新增自定义字符串
            BooleanEx.ResetBooleanExFalseStrings();
            BooleanEx.AddBooleanExFalseStrings(BooleanFalseString.ToArray());
        }
    }
}
