using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TicTacTec.TA.Library;

namespace CarrotBacktesting.NET.Indicator
{
    /// <summary>
    /// 技术指标类,使用TA-Lib计算,包含常用指标如下:
    /// MA EMA MACD KDJ RSI BOLL WR SAR CCI
    /// </summary>
    public static class TechnicalIndicator
    {
        /// <summary>
        /// 数组内移动 real[1,2,3,4,0,0,0] => [0,0,0,1,2,3,4] when idx=3,cnt=4
        /// </summary>
        /// <param name="real"></param>
        /// <param name="idx"></param>
        /// <param name="cnt"></param>
        public static void ArrayMoveBack(double[] real, int idx, int cnt)
        {
            Array.Copy(real, 0, real, idx, cnt);
            Array.Clear(real, 0, idx);
        }

        public static void CheckRetCode(Core.RetCode code)
        {
            if (code != Core.RetCode.Success)
                throw new InvalidOperationException($"TA-Lib return: {code}.");
        }

        /// <summary>
        /// 均线
        /// </summary>
        /// <param name="price"></param>
        /// <param name="period"></param>
        /// <param name="maType"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static double[] MA(double[] price, int period = 5, Core.MAType maType = Core.MAType.Sma)
        {
            double[] real = new double[price.Length];
            Core.RetCode code = Core.MovingAverage(0, price.Length - 1, price,
                period, maType,
                out int idx, out int cnt, real);
            CheckRetCode(code);
            ArrayMoveBack(real, idx, cnt);
            return real;
        }

        /// <summary>
        /// 指数加权均线
        /// </summary>
        /// <param name="price"></param>
        /// <param name="period"></param>
        /// <returns></returns>
        public static double[] EMA(double[] price, int period = 5)
        {
            return MA(price, period, Core.MAType.Ema);
        }

        /// <summary>
        /// MACD指标
        /// TALIB MACDHist计算为一倍, 同花顺为两倍, TradingView为一倍
        /// Reference: https://sourceforge.net/p/ta-lib/code/HEAD/tree/trunk/ta-lib/c/src/ta_func/ta_MACD.c#l495
        /// </summary>
        /// <param name="price"></param>
        /// <param name="fastPeriod"></param>
        /// <param name="slowPeriod"></param>
        /// <param name="signalPeriod"></param>
        /// <returns>(DIF,DEA,MACD)或(MACD,Signal,Hist)</returns>
        public static (double[] macd, double[] macdSignal, double[] macdHist) MACD(double[] price, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
        {
            double[] macd = new double[price.Length];
            double[] macdSignal = new double[price.Length];
            double[] macdHist = new double[price.Length];
            Core.RetCode code = Core.Macd(0, price.Length - 1, price,
                fastPeriod, slowPeriod, signalPeriod,
                out int idx, out int cnt,
                macd, macdSignal, macdHist);
            CheckRetCode(code);
            ArrayMoveBack(macd, idx, cnt);
            ArrayMoveBack(macdSignal, idx, cnt);
            ArrayMoveBack(macdHist, idx, cnt);
            return (macd, macdSignal, macdHist);
        }

        /// <summary>
        /// KDJ指标
        /// </summary>
        /// <param name="highPrices"></param>
        /// <param name="lowPrices"></param>
        /// <param name="closePrices"></param>
        /// <param name="fastkPeriod"></param>
        /// <param name="slowkPeriod"></param>
        /// <param name="slowdPeriod"></param>
        /// <returns>(K,D,J)</returns>
        public static (double[] k, double[] d, double[] j) KDJ(double[] highPrices, double[] lowPrices, double[] closePrices,
            int fastkPeriod = 9, int slowkPeriod = 3, int slowdPeriod = 3)
        {
            // period转换
            // https://xueqiu.com/1747761477/198676825
            slowkPeriod = slowkPeriod * 2 - 1;
            slowdPeriod = slowdPeriod * 2 - 1;

            double[] k = new double[highPrices.Length];
            double[] d = new double[highPrices.Length];
            double[] j = new double[highPrices.Length];
            Core.RetCode code = Core.Stoch(0, highPrices.Length - 1, highPrices, lowPrices, closePrices,
               fastkPeriod, slowkPeriod, Core.MAType.Ema, slowdPeriod, Core.MAType.Ema,
               out int idx, out int cnt, k, d);
            CheckRetCode(code);
            ArrayMoveBack(k, idx, cnt);
            ArrayMoveBack(d, idx, cnt);
            j = k.Zip(d, (k, d) => 3 * k - 2 * d).ToArray();
            return (k, d, j);
        }

        /// <summary>
        /// RSI指标
        /// </summary>
        /// <param name="price"></param>
        /// <param name="period"></param>
        /// <returns></returns>
        public static double[] RSI(double[] price, int period = 6)
        {
            double[] real = new double[price.Length];
            Core.RetCode code = Core.Rsi(0, price.Length - 1, price,
                period,
                out int idx, out int cnt, real);
            CheckRetCode(code);
            ArrayMoveBack(real, idx, cnt);
            return real;
        }

        /// <summary>
        /// BOLL指标
        /// </summary>
        /// <param name="price"></param>
        /// <param name="period"></param>
        /// <param name="nbDev"></param>
        /// <returns></returns>
        public static (double[] up, double[] mid, double[] low) BOLL(double[] price, int period = 20, int nbDev = 2)
        {
            double[] up = new double[price.Length];
            double[] mid = new double[price.Length];
            double[] low = new double[price.Length];
            Core.RetCode code = Core.Bbands(0, price.Length - 1, price,
                period, nbDev, nbDev, Core.MAType.Sma,
                out int idx, out int cnt, up, mid, low);
            CheckRetCode(code);
            ArrayMoveBack(up, idx, cnt);
            ArrayMoveBack(mid, idx, cnt);
            ArrayMoveBack(low, idx, cnt);
            return (up, mid, low);
        }

        /// <summary>
        /// WR指标
        /// </summary>
        /// <param name="highPrices"></param>
        /// <param name="lowPrices"></param>
        /// <param name="closePrices"></param>
        /// <param name="period"></param>
        /// <param name="invert">反相至0-100区间</param>
        /// <returns></returns>
        public static double[] WR(double[] highPrices, double[] lowPrices, double[] closePrices, int period = 14, bool invert = true)
        {
            double[] real = new double[highPrices.Length];
            Core.RetCode code = Core.WillR(0, highPrices.Length - 1, highPrices, lowPrices, closePrices,
                period,
                out int idx, out int cnt, real);
            if (invert)
                real = real.Select(x => -x).ToArray();
            CheckRetCode(code);
            ArrayMoveBack(real, idx, cnt);
            return real;
        }

        /// <summary>
        /// SAR指标
        /// 本实现数据与TradingView相比存在误差
        /// </summary>
        /// <param name="highPrices"></param>
        /// <param name="lowPrices"></param>
        /// <param name="acceleration"></param>
        /// <param name="maximum"></param>
        /// <returns></returns>
        public static double[] SAR(double[] highPrices, double[] lowPrices, double acceleration = 0.02, double maximum = 0.2)
        {
            double[] real = new double[highPrices.Length];
            Core.RetCode code = Core.Sar(0, highPrices.Length - 1, highPrices, lowPrices,
                acceleration, maximum,
                out int idx, out int cnt, real);
            CheckRetCode(code);
            ArrayMoveBack(real, idx, cnt);
            return real;
        }

        /// <summary>
        /// CCI指标
        /// </summary>
        /// <param name="highPrices"></param>
        /// <param name="lowPrices"></param>
        /// <param name="closePrices"></param>
        /// <param name="period"></param>
        /// <returns></returns>
        public static double[] CCI(double[] highPrices, double[] lowPrices, double[] closePrices, int period = 14)
        {
            double[] real = new double[highPrices.Length];
            Core.RetCode code = Core.Cci(0, highPrices.Length - 1, highPrices, lowPrices, closePrices,
                period,
                out int idx, out int cnt, real);
            CheckRetCode(code);
            ArrayMoveBack(real, idx, cnt);
            return real;
        }
    }
}
