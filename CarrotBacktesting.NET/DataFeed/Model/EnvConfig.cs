using CarrotBacktesting.NET.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace CarrotBacktesting.NET.DataFeed.Model
{
    [XmlRoot("env")]
    public class EnvConfig
    {
        [XmlElement("data")]
        public EnvDataConfig Data { get; set; } = new();


        [XmlElement("runtime")]
        public EnvRuntimeConfig Runtime { get; set; } = new();
    }

    public class EnvDataConfig
    {
        [XmlElement("path")]
        public string RawPath { get; set; } = "";

        public string FullPath => Path.Combine(PathHelper.RuntimeRoot, RawPath);
    }

    public class EnvRuntimeConfig
    {
        [XmlElement("jobs")]
        public int ThreadCount { get; set; } = 4;
    }
}