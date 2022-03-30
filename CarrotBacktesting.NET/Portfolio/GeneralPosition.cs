using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Portfolio
{
    /// <summary>
    /// 常规头寸存储类
    /// </summary>
    public class GeneralPosition
    {
        /// <summary>
        /// 头寸存储类名
        /// </summary>
        public string PositionType;
        /// <summary>
        /// 头寸存储字典
        /// </summary>
        public Dictionary<string, double> Data = new();

        public double this[string name]
        {
            get
            {
                return Data[name];
            }
            set
            {
                Data[name] = value;
            }
        }

        public GeneralPosition(string positionType)
        {
            this.PositionType = positionType;
        }
    }
}
