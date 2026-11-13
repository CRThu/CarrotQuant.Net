using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CarrotBackTesting.Net.UnitTest.Helper;

/// <summary>
/// 测试专用的独立 Data YAML 文本构建器。
/// 专用于流式生成被引用的共享 data.yaml 物理文件（支持平铺结构与 data: 包装结构），
/// 彻底消除数据层配置测试中手写 raw string 的脆弱反模式。
/// </summary>
public class TestDataConfigBuilder
{
    private string? _root;
    private bool _wrappedInDataNode = false;
    private readonly List<TimeseriesItem> _timeseries = [];
    private readonly List<EventItem> _events = [];
    private readonly List<FieldItem> _fields = [];

    private record TimeseriesItem(
        string TableId,
        bool Cache,
        int PageSize,
        string Refresh,
        string Compression,
        int CommitBatchSize,
        int MaxPages,
        bool Prefetch,
        List<string>? Fields);

    private record EventItem(string Name, string TableId, List<string>? Fields);
    private record FieldItem(string Name, string TableId, string? KeyColumn, List<string>? ValueColumns);

    public TestDataConfigBuilder WithRoot(string root)
    {
        _root = root;
        return this;
    }

    public TestDataConfigBuilder WrappedInDataNode(bool wrapped = true)
    {
        _wrappedInDataNode = wrapped;
        return this;
    }

    public TestDataConfigBuilder AddTimeseries(
        string tableId,
        bool cache = false,
        int pageSize = 4096,
        string refresh = "lazy",
        string compression = "lz4",
        int commitBatchSize = 64,
        int maxPages = 4,
        bool prefetch = false,
        List<string>? fields = null)
    {
        _timeseries.Add(new TimeseriesItem(tableId, cache, pageSize, refresh, compression, commitBatchSize, maxPages, prefetch, fields));
        return this;
    }

    public TestDataConfigBuilder AddEvent(string name, string tableId, List<string>? fields = null)
    {
        _events.Add(new EventItem(name, tableId, fields));
        return this;
    }

    public TestDataConfigBuilder AddField(string name, string tableId, string? keyColumn = null, List<string>? valueColumns = null)
    {
        _fields.Add(new FieldItem(name, tableId, keyColumn, valueColumns));
        return this;
    }

    public string BuildYaml()
    {
        var sb = new StringBuilder();
        int baseIndent = _wrappedInDataNode ? 2 : 0;
        string pad = new(' ', baseIndent);

        if (_wrappedInDataNode)
        {
            sb.AppendLine("data:");
        }

        if (_root != null)
        {
            sb.AppendLine($"{pad}root: \"{_root.Replace("\\", "/")}\"");
        }

        if (_timeseries.Count > 0)
        {
            sb.AppendLine($"{pad}timeseries:");
            foreach (var ts in _timeseries)
            {
                sb.AppendLine($"{pad}  - table_id: {ts.TableId}");
                if (ts.Fields != null && ts.Fields.Count > 0)
                {
                    sb.AppendLine($"{pad}    fields: [{string.Join(", ", ts.Fields.ConvertAll(f => $"\"{f}\""))}]");
                }
            }
        }

        if (_events.Count > 0)
        {
            sb.AppendLine($"{pad}events:");
            foreach (var ev in _events)
            {
                sb.AppendLine($"{pad}  - name: {ev.Name}");
                sb.AppendLine($"{pad}    table_id: {ev.TableId}");
            }
        }

        if (_fields.Count > 0)
        {
            sb.AppendLine($"{pad}fields:");
            foreach (var fi in _fields)
            {
                sb.AppendLine($"{pad}  - name: {fi.Name}");
                sb.AppendLine($"{pad}    table_id: {fi.TableId}");
                if (fi.KeyColumn != null)
                {
                    sb.AppendLine($"{pad}    key_column: {fi.KeyColumn}");
                }
            }
        }

        return sb.ToString();
    }

    public string Save(string directory, string fileName = "data.yaml")
    {
        Directory.CreateDirectory(directory);
        string fullPath = Path.Combine(directory, fileName);
        File.WriteAllText(fullPath, BuildYaml());
        return fullPath;
    }
}
