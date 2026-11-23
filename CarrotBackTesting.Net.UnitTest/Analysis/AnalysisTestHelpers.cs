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

    public ReadOnlyRowView<double> GetRowView(int row, int col, int len)
    {
        return new ReadOnlyRowView<double>(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref _data[row, col], len));
    }
    public ReadOnlyColumnView<double> GetColumnView(int row, int col, int len) => throw new NotImplementedException();
}

public sealed class MockMetadata : IMarketMetadata
{
    public IReadOnlyList<string> Symbols { get; }
    public IReadOnlyList<string> FieldNames { get; }
    public IReadOnlyList<MarketTime> TradeDates { get; }

    private readonly Dictionary<string, int> _symbolMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<MarketTime, int> _dateMap = new();

    public MockMetadata(IReadOnlyList<string> symbols, IReadOnlyList<MarketTime> tradeDates, IReadOnlyList<string>? fieldNames = null)
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

    public MockMetadata(IReadOnlyList<string> symbols, IReadOnlyList<DateTime> tradeDates, IReadOnlyList<string>? fieldNames = null)
        : this(symbols, System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(tradeDates, d => (MarketTime)d)), fieldNames)
    {
    }

    public Type GetFieldType(string fieldName) => typeof(double);
    public int GetSymbolIndex(string symbol) => _symbolMap.TryGetValue(symbol, out int idx) ? idx : -1;
    public int GetDateIndex(MarketTime date)
    {
        if (_dateMap.TryGetValue(date, out int idx)) return idx;
        return _dateMap.TryGetValue(date.Date, out idx) ? idx : -1;
    }
}

public sealed class MockTimeseriesRegistry : ITimeseriesRegistry
{
    private readonly MockTimeseriesTable _primaryTable;
    public ITimeseriesTable Primary => _primaryTable;
    public IMarketMetadata Metadata => _primaryTable.Metadata;

    public ITimeseriesTable this[string tableName] => GetTable(tableName);
    public ITimeseriesTable GetTable(string tableName) => tableName == _primaryTable.Name ? _primaryTable : throw new KeyNotFoundException($"时序表 '{tableName}' 不存在。");
    public bool HasTable(string tableName) => tableName == _primaryTable.Name;
    public IReadOnlyList<string> GetTableNames() => new[] { _primaryTable.Name };

    public MockTimeseriesRegistry(IMarketMetadata metadata)
    {
        _primaryTable = new MockTimeseriesTable(metadata);
    }

    public void SetMatrix<T>(string fieldName, IReadOnlyMatrix2D<T> matrix) where T : unmanaged
    {
        _primaryTable.SetMatrix(fieldName, matrix);
    }

    public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged
    {
        return _primaryTable.GetMatrix<T>(fieldName);
    }

    public bool HasField(string fieldName) => _primaryTable.HasField(fieldName);
    public void StepTo(int currentLogicalRow) { }
    public bool IsUpdated(int tableIndex) => true;
    public bool IsUpdated(string tableName) => true;
    public int GetTableIndex(string tableName) => 0;
}
