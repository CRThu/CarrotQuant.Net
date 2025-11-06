using ScottPlot;
using ScottPlot.Colormaps;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Utility.ScottPlot
{
    /// <summary>
    /// https://www.kennethmoreland.com/color-maps/
    /// </summary>
    public class Coolwarm : IColormap
    {
        public string Name => "Coolwarm";
        private readonly CustomInterpolated Colormap;
        public Color GetColor(double position) => Colormap.GetColor(position);

        public Coolwarm()
        {
            Color[] colors = new Color[]
            {
                new(59, 76, 192), new(68, 90, 204), new(77, 104, 215), new(87, 117, 225),
                new(98, 130, 234), new(108, 142, 241), new(119, 154, 247), new(130, 165, 251),
                new(141, 176, 254), new(152, 185, 255), new(163, 194, 255), new(174, 201, 253),
                new(184, 208, 249), new(194, 213, 244), new(204, 217, 238), new(213, 219, 230),
                new(221, 221, 221), new(229, 216, 209), new(236, 211, 197), new(241, 204, 185),
                new(245, 196, 173), new(247, 187, 160), new(247, 177, 148), new(247, 166, 135),
                new(244, 154, 123), new(241, 141, 111), new(236, 127, 99), new(229, 112, 88),
                new(222, 96, 77), new(213, 80, 66), new(203, 62, 56), new(192, 40, 47),
                new(180, 4, 38)
            };
            Colormap = new CustomInterpolated(colors);
        }
    }
}
