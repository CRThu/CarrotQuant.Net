using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Engine
{
    /// <summary>
    /// 历史时间点上下文
    /// </summary>
    public readonly struct HistoricalContext
    {
        private readonly SignalStrategyContext _baseContext;
        private readonly int _offset;

        public HistoricalContext(SignalStrategyContext baseContext, int offset)
        {
            _baseContext = baseContext;
            _offset = offset;
        }

        public double? GetValue(string column, int relativeOffset)
        {
            return _baseContext.GetValue(column, _offset + relativeOffset);
        }

        public double? Close(int relativeOffset) => GetValue("close", relativeOffset);
        public double? High(int relativeOffset) => GetValue("high", relativeOffset);
        public double? Low(int relativeOffset) => GetValue("low", relativeOffset);
        public double? Volume(int relativeOffset) => GetValue("volume", relativeOffset);
    }
}
