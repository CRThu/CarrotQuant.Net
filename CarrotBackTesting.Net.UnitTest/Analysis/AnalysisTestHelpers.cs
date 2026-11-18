using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBackTesting.Net.UnitTest.Analysis;

public sealed class MockMatrix : IReadOnlyMatrix2D<double>
{
    private readonly double[,] _data;
    public int RowCount => _data.GetLength(0);
    public int Width => _data.GetLength(1);

    public MockMatrix(double[,] data) => _data = data;

    public ref readonly double this[int r, int c] => ref _data[r, c];

    public ReadOnlyRowView<double> GetRowView(int row, int col, int len) => throw new NotImplementedException();
    public ReadOnlyColumnView<double> GetColumnView(int row, int col, int len) => throw new NotImplementedException();
}

public sealed class MockMetadata : IMarketMetadata
{
    public IReadOnlyList<string> Symbols { get; }
    public IReadOnlyList<string> FieldNames { get; }
    public IReadOnlyList<DateTime> TradeDates { get; }

    private readonly Dictionary<string, int> _symbolMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<DateTime, int> _dateMap = new();

    public MockMetadata(IReadOnlyList<string> symbols, IReadOnlyList<DateTime> tradeDates, IReadOnlyList<string>? fieldNames = null)
    {
        Symbols = symbols;
        TradeDates = tradeDates;
        FieldNames = fieldNames ?? new[] { "close", "open", "high", "low" };

        for (int i = 0; i < symbols.Count; i++) _symbolMap[symbols[i]] = i;
        for (int i = 0; i < tradeDates.Count; i++)
        {
            _dateMap[tradeDates[i]] = i;
            _dateMap.TryAdd(tradeDates[i].Date, i);
        }
    }

    public Type GetFieldType(string fieldName) => typeof(double);
    public int GetSymbolIndex(string symbol) => _symbolMap.TryGetValue(symbol, out int idx) ? idx : -1;
    public int GetDateIndex(DateTime date)
    {
        if (_dateMap.TryGetValue(date, out int idx)) return idx;
        return _dateMap.TryGetValue(date.Date, out idx) ? idx : -1;
    }
}

public sealed class MockTimeseriesRegistry : ITimeseriesRegistry
{
    public IMarketMetadata Metadata { get; }
    private readonly Dictionary<string, object> _matrices = new(StringComparer.OrdinalIgnoreCase);

    public MockTimeseriesRegistry(IMarketMetadata metadata)
    {
        Metadata = metadata;
    }

    public void SetMatrix<T>(string fieldName, IReadOnlyMatrix2D<T> matrix) where T : unmanaged
    {
        _matrices[fieldName] = matrix;
    }

    public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged
    {
        if (_matrices.TryGetValue(fieldName, out var m)) return (IReadOnlyMatrix2D<T>)m;
        throw new KeyNotFoundException($"字段 '{fieldName}' 不存在。");
    }

    public bool HasField(string fieldName) => _matrices.ContainsKey(fieldName);
    public void StepTo(int currentLogicalRow) { }
    public bool IsUpdated(int tableIndex) => true;
    public bool IsUpdated(string tableName) => true;
    public int GetTableIndex(string tableName) => 0;
}
