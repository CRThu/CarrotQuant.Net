using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Result;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Strategy
{
    /// <summary>
    /// 演示策略：当日成交量 > 前5日平均成交量的2.5倍
    /// </summary>
    public class VolumeSignalStrategy : ITradeStrategy
    {
        public string Name => nameof(VolumeSignalStrategy);

        public string? CheckEntry(SignalStrategyContext context)
        {
            // 获取当日成交量
            double? currentVolume = context.GetVolume(0);

            // 如果当天没有成交量数据，则不触发
            if (!currentVolume.HasValue || currentVolume.Value == 0)
            {
                return null;
            }

            // 获取过去5天的成交量数据
            var pastVolumes = new List<double>();
            for (int i = 1; i <= 5; i++)
            {
                double? pastVol = context.GetVolume(-i);
                if (pastVol.HasValue)
                {
                    pastVolumes.Add(pastVol.Value);
                }
                else
                {
                    // 如果没有足够的历史数据，则不触发
                    return null;
                }
            }

            // 计算平均值
            double averageVolume = pastVolumes.Average();

            // 判断是否满足条件
            if (currentVolume.Value > pastVolumes.Average() * 2.5)
            {
                return "Volume";
            }
            return null;
        }

        public string? CheckExit(SignalStrategyContext context, Trade trade)
        {
            return null;
        }
    }
}
