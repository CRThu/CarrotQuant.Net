using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotQuant.Net.Model
{
    public class CandleData
    {
        public List<string> DateTime { get; set; } = new();
        public List<double[]> OHLC { get; set; } = new();

        public void Add(string dateTime,double open,double high,double low,double close)
        {
            DateTime.Add(dateTime);
            OHLC.Add(new[] { open,close , low, high });
        }
    }
}
