using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Common
{
    /// <summary>
    /// 自增Id生成器
    /// </summary>
    public class IncrementIdGenerator
    {
        /// <summary>
        /// Id属性
        /// </summary>
        public ulong Id { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="startId">初始Id</param>
        public IncrementIdGenerator(ulong startId = 0)
        {
            Id = startId;
        }

        /// <summary>
        /// 获取Id方法
        /// </summary>
        /// <returns>返回Id号</returns>
        public ulong GetId()
        {
            return Id++;
        }
    }
}
