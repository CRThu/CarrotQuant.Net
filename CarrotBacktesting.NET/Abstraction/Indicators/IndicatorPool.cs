using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Abstraction.Indicators
{
    /// <summary>
    /// 全市场标的池指标内部基类。
    /// 统筹生命周期管理与引擎内部批量步进驱动，对策略隐藏内部驱动方法。
    /// </summary>
    public abstract class IndicatorPool
    {
        /// <summary>
        /// 指标池全局唯一注册标识名。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 指标池中管理的标的实例总数（等于全市场标的数）。
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// 绑定的价格复权模式（Raw 为物理原价，Post 为后复权价）。
        /// </summary>
        public PriceMode Mode { get; }

        /// <summary>
        /// 绑定的单字段名（如 "close", "volume", "turnover", "pe"；若为多字段模式则为 null）。
        /// </summary>
        public virtual string? Field { get; protected set; }

        /// <summary>
        /// 绑定的多字段名列表（若为单字段模式则为 null）。
        /// </summary>
        public virtual IReadOnlyList<string>? Fields { get; protected set; }

        protected IndicatorPool(
            string name,
            int count,
            PriceMode mode,
            string? field = null,
            IReadOnlyList<string>? fields = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            Name = name;
            Count = count;
            Mode = mode;
            Field = field;
            Fields = fields;
        }

        /// <summary>
        /// 推进指标池完成当期步进计算（内部驱动，对外部策略不可见）。
        /// </summary>
        internal abstract void Step(IEngineContext context);

        /// <summary>
        /// 重置指标池内所有标的实例的状态（内部驱动，对外部策略不可见）。
        /// </summary>
        internal abstract void Reset();
    }

    /// <summary>
    /// 全市场标的池指标高效连续集合器（IndicatorPool）。
    /// 内部维护全市场连续扁平数组（T[] _instances），按 symbolIndex 寻址，
    /// 实现纳秒级 O(1) 访问、常驻 CPU L2 缓存、以及引擎单步统一批量泵送（Zero GC）。
    /// 策略可直接持有此具象类实例，享受激进内联与零虚方法调用开销。
    /// </summary>
    /// <typeparam name="T">流式指标类型约束。</typeparam>
    public sealed class IndicatorPool<T> : IndicatorPool where T : class, IIndicator
    {
        private readonly IMarketMetadata _metadata;
        private readonly T[] _instances;
        private readonly string[] _fields;

        /// <inheritdoc />
        public override string? Field => _fields.Length == 1 ? _fields[0] : null;

        /// <inheritdoc />
        public override IReadOnlyList<string>? Fields => _fields;

        /// <summary>
        /// 构建单字段绑定的标的池实例。
        /// </summary>
        /// <param name="name">指标池标识名。</param>
        /// <param name="factory">单标的指标工厂委托。</param>
        /// <param name="metadata">市场元数据（提供代码与全局列索引映射）。</param>
        /// <param name="field">绑定的时序字段名（如 "close", "volume", "turnover"；缺省默认为 "close"）。</param>
        /// <param name="mode">价格复权模式（默认为 PriceMode.Post）。</param>
        public IndicatorPool(
            string name,
            Func<T> factory,
            IMarketMetadata metadata,
            string field = "close",
            PriceMode mode = PriceMode.Post)
            : base(name, metadata?.Symbols.Count ?? 0, mode, field, null)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentException.ThrowIfNullOrWhiteSpace(field);

            _metadata = metadata;
            _fields = [field];
            int symbolCount = metadata.Symbols.Count;
            _instances = new T[symbolCount];
            for (int i = 0; i < symbolCount; i++)
            {
                _instances[i] = factory();
            }
        }

        /// <summary>
        /// 构建多字段绑定的标的池实例。
        /// </summary>
        /// <param name="name">指标池标识名。</param>
        /// <param name="factory">单标的指标工厂委托。</param>
        /// <param name="metadata">市场元数据。</param>
        /// <param name="fields">绑定的时序字段名列表（如 ["high", "low", "close"]）。</param>
        /// <param name="mode">价格复权模式（默认为 PriceMode.Post）。</param>
        public IndicatorPool(
            string name,
            Func<T> factory,
            IMarketMetadata metadata,
            IReadOnlyList<string> fields,
            PriceMode mode = PriceMode.Post)
            : base(name, metadata?.Symbols.Count ?? 0, mode, null, fields)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(fields);
            if (fields.Count == 0) throw new ArgumentException("字段列表不能为空。", nameof(fields));

            _metadata = metadata;
            _fields = fields.ToArray();
            int symbolCount = metadata.Symbols.Count;
            _instances = new T[symbolCount];
            for (int i = 0; i < symbolCount; i++)
            {
                _instances[i] = factory();
            }
        }

        /// <summary>
        /// 通过标的全局列索引获取对应的流式指标实例（热路径零哈希开销）。
        /// </summary>
        /// <param name="symbolIndex">标的全局列索引（0 &lt;= symbolIndex &lt; SymbolCount）。</param>
        public T this[int symbolIndex]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)symbolIndex >= (uint)_instances.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(symbolIndex), $"标的索引越界: {symbolIndex}，总标的数: {_instances.Length}。");
                }
                return _instances[symbolIndex];
            }
        }

        /// <summary>
        /// 通过标的代码获取对应的流式指标实例。
        /// </summary>
        /// <param name="symbol">标的代码（如 "sh.600000"）。</param>
        public T this[string symbol]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                int index = _metadata.GetSymbolIndex(symbol);
                if (index < 0)
                {
                    throw new ArgumentException($"未在市场元数据中找到标的代码: '{symbol}'。", nameof(symbol));
                }
                return _instances[index];
            }
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override void Step(IEngineContext context)
        {
            int count = _instances.Length;
            if (count == 0 || context.CurrentIndex < 0) return;

            int fCount = _fields.Length;
            if (fCount == 0) return;

            unsafe
            {
                double** ptrs = stackalloc double*[fCount];
                int safeCount = count;

                for (int k = 0; k < fCount; k++)
                {
                    ReadOnlySpan<double> row = context.GetFieldRow(_fields[k], 0, Mode);
                    if (row.IsEmpty) return;
                    if (row.Length < safeCount) safeCount = row.Length;
                    ptrs[k] = (double*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(row));
                }

                Span<double> vals = stackalloc double[fCount];
                for (int i = 0; i < safeCount; i++)
                {
                    for (int k = 0; k < fCount; k++)
                    {
                        vals[k] = ptrs[k][i];
                    }
                    _instances[i].Update(vals);
                }
            }
        }

        /// <inheritdoc />
        internal override void Reset()
        {
            for (int i = 0; i < _instances.Length; i++)
            {
                _instances[i].Reset();
            }
        }
    }
}
