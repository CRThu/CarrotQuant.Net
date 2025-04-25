using CarrotBacktesting.NET.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Config.Model
{
    public class EnvConfig
    {
        public DataConfig Data { get; set; } = new();

        public CacheConfig Cache { get; set; } = new();

        public RuntimeConfig Runtime { get; set; } = new();

        public Dictionary<string, FieldDefinition> Fields { get; set; } = new();
    }

    public class DataConfig
    {
        public string RawPath { get; set; } = "";

        public string FullPath => Path.Combine(PathHelper.RuntimeRoot, RawPath);
    }

    public class CacheConfig
    {
        public bool Enabled { get; set; } = true;
        public bool ForceRefresh { get; set; } = false;
    }

    public class RuntimeConfig
    {
        public int ThreadCount { get; set; } = 4;
    }

    public class FieldDefinition
    {
        public FieldFormat Format { get; set; } = FieldFormat.Auto;
        public string? Alias { get; set; }
        public Dictionary<string, string> ValueMap { get; set; } = new();
    }

    public enum FieldFormat
    {
        Null,
        Auto,
        Float,
        String,
    }
}