using CarrotBacktesting.NET.Strategy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Engine
{
    /// <summary>
    /// 回测/交易引擎接口
    /// </summary>
    public interface IEngine
    {
        public void Run();
    }
}
