using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static TicTacTec.TA.Library.Core;

namespace CarrotQuant.Net.Model
{
    public static class TechnicalAnalysis
    {
        public static double[] MovingAverage(double[] data, int MAx = 5)
        {
            double[] real = new double[data.Length];
            double[] real1 = new double[data.Length];
            RetCode code = TicTacTec.TA.Library.Core.MovingAverage(0, data.Length - 1, data, MAx, MAType.Sma, out int idx, out int cnt, real);
            if (code != RetCode.Success)
                throw new InvalidOperationException($"TA-Lib return: {code}.");
            Array.Copy(real, 0, real1, idx, cnt);
            return real1;
        }
    }
}
