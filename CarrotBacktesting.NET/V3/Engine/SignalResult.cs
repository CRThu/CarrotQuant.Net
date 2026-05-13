using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Engine
{
    public readonly struct SignalResult
    {
        public string Reason { get; }
        public string Group { get; }

        public SignalResult(string reason = "default", string group = "default")
        {
            Reason = reason;
            Group = group;
        }

        // 为了兼容旧代码，可以保留隐式转换（可选）
        public static implicit operator SignalResult(string reason) => new SignalResult(reason);
    }
}
