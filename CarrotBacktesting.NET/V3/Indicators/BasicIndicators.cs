using CarrotBacktesting.NET.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Indicators
{
    public static class BasicIndicators
    {
        public static double? SMA(HistoricalContext context, string column, int period)
        {
            var values = new List<double>();
            for (int i = 0; i < period; i++)
            {
                double? value = context.GetValue(column, -i);
                if (!value.HasValue) return null;
                values.Add(value.Value);
            }
            return values.Average();
        }

        public static double? Min(HistoricalContext context, string column, int period, int endOffset = 0)
        {
            var values = new List<double>();
            for (int i = 0; i < period; i++)
            {
                double? value = context.GetValue(column, endOffset - i);
                if (!value.HasValue) return null;
                values.Add(value.Value);
            }
            return values.Min();
        }

        public static double? Max(HistoricalContext context, string column, int period, int endOffset = 0)
        {
            var values = new List<double>();
            for (int i = 0; i < period; i++)
            {
                double? value = context.GetValue(column, endOffset - i);
                if (!value.HasValue) return null;
                values.Add(value.Value);
            }
            return values.Max();
        }
    }
}
