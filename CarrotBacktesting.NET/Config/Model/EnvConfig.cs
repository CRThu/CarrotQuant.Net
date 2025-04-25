using CarrotBacktesting.NET.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Config.Model
{
    public class EnvConfig
    {
        public EnvDataConfig Data { get; set; } = new();

        public EnvRuntimeConfig Runtime { get; set; } = new();
    }

    public class EnvDataConfig
    {
        public string RawPath { get; set; } = "";

        public string FullPath => Path.Combine(PathHelper.RuntimeRoot, RawPath);
    }

    public class EnvRuntimeConfig
    {
        public int ThreadCount { get; set; } = 4;
    }
}