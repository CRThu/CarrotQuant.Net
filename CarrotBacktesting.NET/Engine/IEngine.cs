using CarrotBacktesting.NET.Strategy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Engine
{
    public interface IEngine
    {
        public void SetStrategy(IStrategy strategy);
        public void Run();
    }
}
