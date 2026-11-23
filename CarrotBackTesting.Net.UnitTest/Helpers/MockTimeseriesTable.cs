using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data;

namespace CarrotBackTesting.Net.UnitTest
{
    public class MockTimeseriesTable : ITimeseriesTable
    {
        private readonly Func<string, object?>? _matrixProvider;
        private readonly Func<string, bool>? _hasFieldProvider;
        private readonly Dictionary<string, object> _matrices = new(StringComparer.OrdinalIgnoreCase);
        private readonly int[]? _subRowMap;

        public string Name { get; set; } = "mock_primary";
        public bool IsPrimary { get; set; } = true;
        public IMarketMetadata Metadata { get; set; }

        public MockTimeseriesTable(
            IMarketMetadata metadata,
            string name = "mock_primary",
            bool isPrimary = true,
            int[]? subRowMap = null)
        {
            Metadata = metadata;
            Name = name;
            IsPrimary = isPrimary;
            _subRowMap = subRowMap;
        }

        public MockTimeseriesTable(
            IMarketMetadata metadata,
            Func<string, object?> matrixProvider,
            Func<string, bool> hasFieldProvider,
            string name = "mock_primary",
            bool isPrimary = true,
            int[]? subRowMap = null)
        {
            Metadata = metadata;
            _matrixProvider = matrixProvider;
            _hasFieldProvider = hasFieldProvider;
            Name = name;
            IsPrimary = isPrimary;
            _subRowMap = subRowMap;
        }

        public void SetMatrix<T>(string fieldName, IReadOnlyMatrix2D<T> matrix) where T : unmanaged
        {
            _matrices[fieldName] = matrix;
        }

        public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged
        {
            if (_matrixProvider != null)
            {
                var val = _matrixProvider(fieldName);
                if (val is IReadOnlyMatrix2D<T> typed) return typed;
                return (IReadOnlyMatrix2D<T>)val!;
            }

            if (_matrices.TryGetValue(fieldName, out var m))
            {
                return (IReadOnlyMatrix2D<T>)m;
            }
            throw new KeyNotFoundException($"字段 '{fieldName}' 不存在。");
        }

        public bool HasField(string fieldName)
        {
            if (_hasFieldProvider != null) return _hasFieldProvider(fieldName);
            return _matrices.ContainsKey(fieldName);
        }

        public int GetCurrentRowIndex(int primaryStepIndex)
        {
            if (IsPrimary) return primaryStepIndex;
            if (_subRowMap == null || (uint)primaryStepIndex >= (uint)_subRowMap.Length) return -1;
            return _subRowMap[primaryStepIndex];
        }

        public ReadOnlySpan<T> GetCurrentRow<T>(string fieldName, int primaryStepIndex) where T : unmanaged
        {
            int row = GetCurrentRowIndex(primaryStepIndex);
            if (row < 0) return ReadOnlySpan<T>.Empty;
            var m = GetMatrix<T>(fieldName);
            if (m == null || (uint)row >= (uint)m.RowCount) return ReadOnlySpan<T>.Empty;
            return m.GetRowView(row, 0, m.Width);
        }

        public T GetCurrentValue<T>(string fieldName, int symbolIndex, int primaryStepIndex) where T : unmanaged
        {
            int row = GetCurrentRowIndex(primaryStepIndex);
            if (row < 0 || symbolIndex < 0) return MarketDataAligner.GetNaNValue<T>();
            var m = GetMatrix<T>(fieldName);
            if (m == null || (uint)row >= (uint)m.RowCount || (uint)symbolIndex >= (uint)m.Width) return MarketDataAligner.GetNaNValue<T>();
            return m[row, symbolIndex];
        }
    }
}
