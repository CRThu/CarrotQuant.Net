using CarrotBacktesting.NET.Engine.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Engine
{
    public interface ISignalStrategy
    {
        /// <summary>
        /// 策略名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 策略的核心逻辑，判断在当前时间点是否应该发出信号
        /// </summary>
        /// <param name="context">提供数据访问的上下文</param>
        /// <returns>如果触发信号则为true，否则为false</returns>
        bool CheckSignal(SignalStrategyContext context);
    }
}
