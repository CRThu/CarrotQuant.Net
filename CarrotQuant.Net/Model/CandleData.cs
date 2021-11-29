using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotQuant.Net.Model
{
    public class CandleData
    {
        public List<string> datetime { get; set; } = new();
        public List<double> open { get; set; } = new();
        public List<double> high { get; set; } = new();
        public List<double> low { get; set; } = new();
        public List<double> close { get; set; } = new();
    }
}
