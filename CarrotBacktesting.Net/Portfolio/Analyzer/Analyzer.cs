using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.Shared;
using MathNet.Numerics.Statistics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Analyzer
{
    /// <summary>
    /// 投资组合分析器
    /// </summary>
    public class Analyzer
    {
        public TransactionLogger TransactionLogger { get; set; }
        public PnlLogger PnlLogger { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public Analyzer(TransactionLogger transactionLogger, PnlLogger pnlLogger)
        {
            TransactionLogger = transactionLogger;
            PnlLogger = pnlLogger;
        }

        /// <summary>
        /// 获取回撤曲线
        /// </summary>
        /// <param name="value">总资产</param>
        /// <returns></returns>
        public static double[] GetDrawdown(double[] value)
        {
            double[] drawdown = new double[value.Length];

            // 存储历史最高总资产用于计算回撤
            double peakDrawdown = 0;

            for (int i = 0; i < value.Length; i++)
            {
                // 回撤计算(恒定本金法)
                if (value[i] > peakDrawdown)
                    peakDrawdown = value[i];
                drawdown[i] = 1 - value[i] / peakDrawdown;
            }
            return drawdown;
        }

        /// <summary>
        /// 获取最大回撤率
        /// </summary>
        /// <param name="value">总资产</param>
        /// <returns></returns>
        public static double GetMaxDrawdown(double[] value)
        {
            return GetDrawdown(value).Max();
        }

        /// <summary>
        /// 计算每Tick收益率
        /// </summary>
        /// <param name="pnl">每Tick总损益</param>
        /// <param name="cost">成本</param>
        /// <param name="normalize">归一化至单位收益</param>
        /// <returns></returns>
        //public static double[] GetRateOfReturn(double[] pnl, double cost, double normalize = 1)
        //{
        //    return GetTickReturn(pnl).Select(r => r / cost * normalize).ToArray();
        //}

        /// <summary>
        /// 获取夏普比率(Tick单位)
        /// </summary>
        /// <param name="pnl">每Tick总损益</param>
        /// <returns></returns>
        //public static double GetSharpeRatio(double[] pnl)
        //{
        //    double[] tickReturn = GetTickReturn(pnl);

        //    // https://numerics.mathdotnet.com/DescriptiveStatistics.html
        //    double mean = tickReturn.Mean();
        //    double stddev = tickReturn.PopulationStandardDeviation();
        //    return mean / stddev;
        //}

        /// <summary>
        /// 获取回撤曲线
        /// </summary>
        /// <param name="pnlLogger"></param>
        /// <returns></returns>
        public static double[] GetDrawdown(PnlLogger pnlLogger)
        {
            return GetDrawdown(pnlLogger.Logs.Select(l => l.TotalValue).ToArray());
        }

        /// <summary>
        /// 获取最大回撤率
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static double GetMaxDrawdown(PnlLogger pnlLogger)
        {
            return GetDrawdown(pnlLogger).Max();
        }

        /// <summary>
        /// 获取夏普比率
        /// </summary>
        /// <param name="pnlLogger"></param>
        /// <returns></returns>
        //public static double GetSharpeRatio(PnlLogger pnlLogger)
        //{
        //    return GetSharpeRatio(pnlLogger.Logs.Select(l => l.TotalPnl).ToArray());
        //}

        // TODO 重构以上代码

        /// <summary>
        /// 获取每Tick/日/月/年回报数据
        /// </summary>
        /// <param name="pnlLogger"></param>
        /// <param name="dateSpan"></param>
        /// <returns></returns>
        public static DateRangeData<double>[] GetReturn(PnlLogger pnlLogger, DateSpan dateSpan)
        {
            // 每Tick回报生成
            DateRangeData<double>[] tickReturn = new DateRangeData<double>[pnlLogger.Logs.Count];
            if (pnlLogger.Logs.Count > 0)
            {
                tickReturn[0] = new(pnlLogger.Logs[0].DateTime, pnlLogger.Logs[0].TotalPnl);
                for (int i = 1; i < pnlLogger.Logs.Count; i++)
                    tickReturn[i] = new(pnlLogger.Logs[i].DateTime,
                        pnlLogger.Logs[i].TotalPnl - pnlLogger.Logs[i - 1].TotalPnl);
            }

            // 时间跨度生成
            DateRangeData<double>[] data;
            if (dateSpan == DateSpan.Tick)
            {
                // Tick数据则直接返回
                return tickReturn;
                //data = pnlLogger.Logs.Select(log => new DateRangeData<double>(log.DateTime, dateSpan, 0)).ToArray();
            }
            else
            {
                DateTime minDateTime = pnlLogger.Logs.Min(l => l.DateTime);
                DateTime maxDateTime = pnlLogger.Logs.Max(l => l.DateTime);

                DateTime[] dts = DateTimeMisc.GetDateTimeSpan(minDateTime, maxDateTime, dateSpan);
                data = dts.Select(dt => new DateRangeData<double>(dt, dateSpan, 0)).ToArray();
            }

            // 时间跨度合成总回报
            for (int i = 0; i < tickReturn.Length; i++)
            {
                for (int j = 0; j < data.Length; j++)
                {
                    if (data[j].IsInRange(tickReturn[i]))
                    {
                        data[j].Value += tickReturn[i].Value;
                        break;
                    }
                }
            }

            return data;
        }

        /// <summary>
        /// 分析方法, 返回结果字典
        /// 包含: 最大回撤率, 夏普比率
        /// </summary>
        /// <param name="pnlLogger"></param>
        /// <returns></returns>
        public static Dictionary<string, double> Analyze(PnlLogger pnlLogger)
        {
            Dictionary<string, double> analyzerResult = new();
            analyzerResult.Add("MaxDrawdown", GetMaxDrawdown(pnlLogger));
            //analyzerResult.Add("SharpeRatio", GetSharpeRatio(pnlLogger));
            return analyzerResult;
        }


        /// <summary>
        /// 获取每Tick/日/月/年回报数据
        /// </summary>
        /// <param name="dateSpan"></param>
        /// <returns></returns>
        public DateRangeData<double>[] GetReturn(DateSpan dateSpan) => GetReturn(PnlLogger, dateSpan);
        /// <summary>
        /// 分析方法, 返回结果字典
        /// 包含: 最大回撤率, 夏普比率
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, double> Analyze() => Analyze(PnlLogger);
    }
}
