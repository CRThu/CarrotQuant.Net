using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Engine;
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
    /// 分析器管理器
    /// </summary>
    public class AnalyzerManager
    {
        /// <summary>
        /// 交易记录器
        /// </summary>
        public TransactionLogger TransactionLogger { get; set; }

        /// <summary>
        /// 损益记录器
        /// </summary>
        public PnlLogger PnlLogger { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public AnalyzerManager()
        {
            TransactionLogger = new TransactionLogger();
            PnlLogger = new PnlLogger();
        }

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
                tickReturn[0] = new(pnlLogger.Logs[0].Time, pnlLogger.Logs[0].TotalPnl);
                for (int i = 1; i < pnlLogger.Logs.Count; i++)
                    tickReturn[i] = new(pnlLogger.Logs[i].Time,
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
                DateTime minDateTime = pnlLogger.Logs.Min(l => l.Time);
                DateTime maxDateTime = pnlLogger.Logs.Max(l => l.Time);

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
        /// 获取每Tick/日/月/年回报率数据
        /// </summary>
        /// <param name="pnlLogger"></param>
        /// <param name="dateSpan"></param>
        /// <returns></returns>
        public static DateRangeData<double>[] GetRateOfReturn(PnlLogger pnlLogger, DateSpan dateSpan)
        {
            DateRangeData<double>[] retValues = GetReturn(pnlLogger, dateSpan);
            if (pnlLogger.Logs.Count > 0)
            {
                PnlTickLog firstLog = pnlLogger.Logs[0];
                double cost = firstLog.TotalValue - firstLog.TotalPnl;
                for (int i = 0; i < retValues.Length; i++)
                    retValues[i].Value /= cost;
            }
            return retValues;
        }

        /// <summary>
        /// 获取Tick/日/月/年夏普比率
        /// </summary>
        /// <param name="pnlLogger"></param>
        /// <param name="dateSpan"></param>
        /// <returns></returns>
        public static double GetSharpeRatio(PnlLogger pnlLogger, DateSpan dateSpan)
        {
            DateRangeData<double>[] retValues = GetReturn(pnlLogger, dateSpan);

            // https://numerics.mathdotnet.com/DescriptiveStatistics.html
            double[] vals = retValues.Select(val => val.Value).ToArray();
            double mean = vals.Mean();
            double stddev = vals.PopulationStandardDeviation();
            return mean / stddev;
        }

        /// <summary>
        /// 获取每Tick回撤率
        /// </summary>
        /// <param name="pnlLogger"></param>
        /// <returns></returns>
        public static DateRangeData<double>[] GetDrawdown(PnlLogger pnlLogger)
        {
            DateRangeData<double>[] tickDrawdown = new DateRangeData<double>[pnlLogger.Logs.Count];

            // 存储历史最高总资产用于计算回撤
            double peakDrawdown = 0;

            for (int i = 0; i < pnlLogger.Logs.Count; i++)
            {
                // 回撤计算(恒定本金法)
                if (pnlLogger.Logs[i].TotalValue > peakDrawdown)
                    peakDrawdown = pnlLogger.Logs[i].TotalValue;
                tickDrawdown[i] = new(pnlLogger.Logs[i].Time, 1 - pnlLogger.Logs[i].TotalValue / peakDrawdown);
            }
            return tickDrawdown;
        }

        /// <summary>
        /// 获取最大回撤率
        /// </summary>
        /// <param name="pnlLogger"></param>
        /// <returns></returns>
        public static double GetMaxDrawdown(PnlLogger pnlLogger)
        {
            return GetDrawdown(pnlLogger).Max(data => data.Value);
        }

        /// <summary>
        /// 获取日/月/年化收益率
        /// </summary>
        /// <param name="pnlLogger"></param>
        /// <param name="dateSpan"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static double GetNormalizedRateOfReturn(PnlLogger pnlLogger, DateSpan dateSpan = DateSpan.Year)
        {
            double pnl = pnlLogger.Logs[^1].TotalPnl - pnlLogger.Logs[0].TotalPnl;
            double cost = pnlLogger.Logs[0].TotalValue - pnlLogger.Logs[0].TotalPnl;
            TimeSpan timeSpan = pnlLogger.Logs[^1].Time - pnlLogger.Logs[0].Time;

            return dateSpan switch {
                DateSpan.Day => (pnl / cost) / (timeSpan / TimeSpan.FromDays(1)),
                DateSpan.Month => (pnl / cost) / (timeSpan / TimeSpan.FromDays(30)),
                DateSpan.Year => (pnl / cost) / (timeSpan / TimeSpan.FromDays(365)),
                _ => throw new NotImplementedException(),
            };
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
            analyzerResult.Add("年化收益率", GetNormalizedRateOfReturn(pnlLogger));
            analyzerResult.Add("最大回撤率", GetMaxDrawdown(pnlLogger));
            analyzerResult.Add("日夏普比率", GetSharpeRatio(pnlLogger, DateSpan.Day));
            analyzerResult.Add("月夏普比率", GetSharpeRatio(pnlLogger, DateSpan.Month));
            return analyzerResult;
        }

        /// <summary>
        /// 获取每Tick/日/月/年回报数据
        /// </summary>
        /// <param name="dateSpan"></param>
        /// <returns></returns>
        public DateRangeData<double>[] GetReturn(DateSpan dateSpan) => GetReturn(PnlLogger, dateSpan);
        /// <summary>
        /// 获取每Tick/日/月/年回报率数据
        /// </summary>
        /// <param name="dateSpan"></param>
        /// <returns></returns>
        public DateRangeData<double>[] GetRateOfReturn(DateSpan dateSpan) => GetRateOfReturn(PnlLogger, dateSpan);
        /// <summary>
        /// 获取Tick/日/月/年夏普比率
        /// </summary>
        /// <param name="dateSpan"></param>
        /// <returns></returns>
        public double GetSharpeRatio(DateSpan dateSpan) => GetSharpeRatio(PnlLogger, dateSpan);
        /// <summary>
        /// 获取每Tick回撤率
        /// </summary>
        /// <returns></returns>
        public DateRangeData<double>[] GetDrawdown() => GetDrawdown(PnlLogger);
        /// <summary>
        /// 获取最大回撤率
        /// </summary>
        /// <returns></returns>
        public double GetMaxDrawdown() => GetMaxDrawdown(PnlLogger);
        /// <summary>
        /// 获取日/月/年化收益率
        /// </summary>
        /// <param name="dateSpan"></param>
        /// <returns></returns>
        public double GetNormalizedRateOfReturn(DateSpan dateSpan = DateSpan.Year) => GetNormalizedRateOfReturn(dateSpan);
        /// <summary>
        /// 分析方法, 返回结果字典
        /// 包含: 最大回撤率, 夏普比率
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, double> Analyze() => Analyze(PnlLogger);


        /// <summary>
        /// 市场更新事件回调
        /// </summary>
        /// <param name="_">市场数据更新</param>
        /// <param name="marketEventArgs">市场更新事件参数</param>
        public void OnMarketUpdate(MarketFrame data, MarketEventArgs marketEventArgs)
        {
            // TODO
            //PnlLogger.AddPnlSnapshot(data, PositionManager);
        }
    }
}
