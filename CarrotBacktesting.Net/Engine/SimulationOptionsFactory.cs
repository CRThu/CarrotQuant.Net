using CarrotBacktesting.Net.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 回测配置工厂类
    /// </summary>
    public static class SimulationOptionsFactory
    {
        /// <summary>
        /// 通过Json载入SimulationObject配置
        /// </summary>
        /// <param name="jsonpath">Json配置文件</param>
        /// <returns>返回创建的配置类</returns>
        public static SimulationOptions CreateFromJson(string jsonpath)
        {
            string jsonString = File.ReadAllText(jsonpath);
            SimulationOptions options = Json.DeSerialize<SimulationOptions>(jsonString)!;
            options.Load();
            return options;
        }
    }
}
