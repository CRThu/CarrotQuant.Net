using ScottPlot;
using ScottPlot.Colormaps;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Utility.ScottPlot
{
    public class CenteredColormap(IColormap colormap, double centerValue, double minValue, double maxValue) : IColormap
    {
        public string Name => $"Centered {colormap.Name}";
        private readonly double _centerFraction = maxValue == minValue ? 0.5 : (centerValue - minValue) / (maxValue - minValue);

        public Color GetColor(double position)
        {
            if (position < _centerFraction)
            {
                // 将范围 [0, _centerFraction] 重新映射到 [0, 0.5]
                // Remap the range [0, _centerFraction] to [0, 0.5]
                double remapped = _centerFraction == 0 ? 0.5 : position / _centerFraction * 0.5;
                return colormap.GetColor(remapped);
            }
            else
            {
                // 将范围 [_centerFraction, 1] 重新映射到 [0.5, 1]
                // Remap the range [_centerFraction, 1] to [0.5, 1]
                double remapped = _centerFraction == 1 ? 0.5 : 0.5 + (position - _centerFraction) / (1 - _centerFraction) * 0.5;
                return colormap.GetColor(remapped);
            }
        }
    }
}
