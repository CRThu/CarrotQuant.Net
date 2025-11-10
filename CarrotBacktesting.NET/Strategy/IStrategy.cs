using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Strategy
{
    /// <summary>
    /// 所有策略的通用基接口
    /// </summary>
    public interface IStrategy
    {
        string Name { get; }
    }
}
