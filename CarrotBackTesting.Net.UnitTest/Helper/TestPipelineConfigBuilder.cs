using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CarrotBacktesting.NET.Config;

namespace CarrotBackTesting.Net.UnitTest.Helper;

/// <summary>
/// 测试专用的策略管线 YAML 文本构建器。
/// 以流式 Fluent 方式直接生成规范的策略管线 YAML 文本或物理文件，避免在测试中硬编码多行字符串，
/// 同时完全独立于任何内部序列化器，确保对 PipelineConfigLoader 真实解析能力的有效验证。
/// </summary>
public class TestPipelineConfigBuilder
{
    private string? _name = "TestPipeline";
    private string _mode = "sequential";
    private bool _concurrent = false;
    private bool _emptyPipeline = false;
    private bool _omitPipeline = false;
    private readonly List<Action<StringBuilder, int>> _nodeWriters = [];

    public TestPipelineConfigBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public TestPipelineConfigBuilder WithEmptyPipeline()
    {
        _emptyPipeline = true;
        _omitPipeline = false;
        return this;
    }

    public TestPipelineConfigBuilder WithoutPipeline()
    {
        _omitPipeline = true;
        _emptyPipeline = false;
        return this;
    }

    public TestPipelineConfigBuilder ClearStrategies()
    {
        _nodeWriters.Clear();
        return this;
    }

    public TestPipelineConfigBuilder AddEmptyNode(string? name = null)
    {
        _nodeWriters.Add((sb, indent) =>
        {
            string pad = new(' ', indent);
            if (!string.IsNullOrEmpty(name))
            {
                sb.AppendLine($"{pad}- name: \"{name}\"");
            }
            else
            {
                sb.AppendLine($"{pad}- {{}}");
            }
        });
        return this;
    }

    public TestPipelineConfigBuilder AddInvalidNode(Dictionary<string, object> parameters)
    {
        _nodeWriters.Add((sb, indent) =>
        {
            string pad = new(' ', indent);
            sb.AppendLine($"{pad}- params:");
            foreach (var (k, v) in parameters)
            {
                WriteParam(sb, indent + 4, k, v);
            }
        });
        return this;
    }

    public TestPipelineConfigBuilder WithMode(string mode)
    {
        _mode = mode;
        return this;
    }

    public TestPipelineConfigBuilder WithMode(PipelineMode mode)
    {
        _mode = mode.ToString().ToLowerInvariant();
        return this;
    }

    public TestPipelineConfigBuilder WithConcurrent(bool concurrent)
    {
        _concurrent = concurrent;
        return this;
    }

    public TestPipelineConfigBuilder AddStrategy(Type type, Dictionary<string, object>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        return AddStrategy(type.AssemblyQualifiedName ?? type.FullName ?? type.Name, parameters);
    }

    public TestPipelineConfigBuilder AddStrategy(string typeName, Dictionary<string, object>? parameters = null)
    {
        _nodeWriters.Add((sb, indent) =>
        {
            string pad = new(' ', indent);
            sb.AppendLine($"{pad}- type: \"{typeName}\"");
            if (parameters != null && parameters.Count > 0)
            {
                sb.AppendLine($"{pad}  params:");
                foreach (var (k, v) in parameters)
                {
                    WriteParam(sb, indent + 4, k, v);
                }
            }
        });
        return this;
    }

    public TestPipelineConfigBuilder AddSourceStrategy(string sourcePath, Dictionary<string, object>? parameters = null)
    {
        _nodeWriters.Add((sb, indent) =>
        {
            string pad = new(' ', indent);
            sb.AppendLine($"{pad}- source: \"{sourcePath.Replace("\\", "/")}\"");
            if (parameters != null && parameters.Count > 0)
            {
                sb.AppendLine($"{pad}  params:");
                foreach (var (k, v) in parameters)
                {
                    WriteParam(sb, indent + 4, k, v);
                }
            }
        });
        return this;
    }

    public TestPipelineConfigBuilder AddSubPipeline(Action<TestPipelineConfigBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var subBuilder = new TestPipelineConfigBuilder();
        configure(subBuilder);

        _nodeWriters.Add((sb, indent) =>
        {
            string pad = new(' ', indent);
            if (!string.IsNullOrEmpty(subBuilder._name))
            {
                sb.AppendLine($"{pad}- name: \"{subBuilder._name}\"");
                sb.AppendLine($"{pad}  mode: \"{subBuilder._mode}\"");
            }
            else
            {
                sb.AppendLine($"{pad}- mode: \"{subBuilder._mode}\"");
            }

            if (subBuilder._concurrent)
            {
                sb.AppendLine($"{pad}  concurrent: true");
            }
            if (subBuilder._nodeWriters.Count > 0)
            {
                sb.AppendLine($"{pad}  pipeline:");
                foreach (var writer in subBuilder._nodeWriters)
                {
                    writer(sb, indent + 4);
                }
            }
        });
        return this;
    }

    public void WriteTo(StringBuilder sb, int indent)
    {
        string pad = new(' ', indent);
        if (!string.IsNullOrEmpty(_name))
        {
            sb.AppendLine($"{pad}name: \"{_name}\"");
        }
        sb.AppendLine($"{pad}mode: \"{_mode}\"");
        if (_concurrent)
        {
            sb.AppendLine($"{pad}concurrent: true");
        }
        if (_emptyPipeline)
        {
            sb.AppendLine($"{pad}pipeline: []");
        }
        else if (!_omitPipeline)
        {
            sb.AppendLine($"{pad}pipeline:");
            foreach (var writer in _nodeWriters)
            {
                writer(sb, indent + 2);
            }
        }
    }

    private static void WriteParam(StringBuilder sb, int indent, string key, object? value)
    {
        string pad = new(' ', indent);
        if (value is null)
        {
            sb.AppendLine($"{pad}{key}: null");
        }
        else if (value is bool b)
        {
            sb.AppendLine($"{pad}{key}: {b.ToString().ToLowerInvariant()}");
        }
        else if (value is int or long or double or float or decimal)
        {
            sb.AppendLine($"{pad}{key}: {Convert.ToString(value, CultureInfo.InvariantCulture)}");
        }
        else if (value is System.Collections.IEnumerable list && value is not string)
        {
            sb.AppendLine($"{pad}{key}:");
            foreach (var item in list)
            {
                if (item is null)
                {
                    sb.AppendLine($"{pad}  - null");
                }
                else if (item is int or long or double or float or decimal)
                {
                    sb.AppendLine($"{pad}  - {Convert.ToString(item, CultureInfo.InvariantCulture)}");
                }
                else
                {
                    sb.AppendLine($"{pad}  - \"{item}\"");
                }
            }
        }
        else
        {
            sb.AppendLine($"{pad}{key}: \"{value}\"");
        }
    }

    /// <summary>
    /// 生成格式化 YAML 文本内容。
    /// </summary>
    public string BuildYaml()
    {
        var sb = new StringBuilder();
        WriteTo(sb, 0);
        return sb.ToString();
    }

    /// <summary>
    /// 将构造出的 YAML 文本直接保存至指定物理文件，返回绝对路径。
    /// </summary>
    public string Save(string directory, string fileName = "pipeline.yaml")
    {
        Directory.CreateDirectory(directory);
        string fullPath = Path.Combine(directory, fileName);
        File.WriteAllText(fullPath, BuildYaml());
        return fullPath;
    }
}
