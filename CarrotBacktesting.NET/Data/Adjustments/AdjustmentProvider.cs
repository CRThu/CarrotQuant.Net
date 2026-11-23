using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 除权因子专职解析与生命周期供给服务。
    /// 负责全量除权事件的加载、多周期时间步坐标映射、周末/非交易日除权事件推进合并、
    /// 本地 LZ4 二进制缓存（.cache/{table}/{field}/）的无损读取与持久化回写。
    /// </summary>
    public static class AdjustmentProvider
    {
        /// <summary>
        /// 解析并构建全市场复权因子矩阵与时步坐标映射。
        /// 遵循快速路径（LZ4 缓存直接解压，零原始文件 I/O）与冷路径（全量历史滚存并持久化至缓存）。
        /// </summary>
        /// <param name="adjustConfig">复权配置。</param>
        /// <param name="metadata">时序行情元数据（标的代码集与交易日历网格）。</param>
        /// <param name="resolver">物理存储解析器（可选，用于冷路径时按需读取原始除权表）。</param>
        /// <param name="events">事件注册中心（可选，优先复用已存在的内存除权表）。</param>
        /// <param name="timeseries">时序注册中心（可选，用于冷路径时执行行情与除权时序前置校验）。</param>
        /// <param name="monitor">监控与日志记录器（可选）。</param>
        /// <returns>已就绪的非托管因子矩阵与时间步映射数组；若未启用则返回 (null, null)。</returns>
        public static (ChunkMatrix2D<double>? FactorMatrix, int[]? StepToDateIndex) Resolve(
            AdjustmentConfig adjustConfig,
            IMarketMetadata metadata,
            IStorageResolver? resolver = null,
            IEventRegistry? events = null,
            ITimeseriesRegistry? timeseries = null,
            IMonitor? monitor = null)
        {
            ArgumentNullException.ThrowIfNull(adjustConfig);
            ArgumentNullException.ThrowIfNull(metadata);

            if (adjustConfig.Mode != AdjustmentMode.Post)
            {
                return (null, null);
            }

            var symbols = metadata.Symbols;
            int symbolCount = symbols.Count;
            var tradeDates = metadata.TradeDates;
            if (tradeDates.Count == 0 || symbolCount == 0)
            {
                return (null, null);
            }

            string tableName = adjustConfig.TableId;
            string factorField = adjustConfig.FactorField;

            // 1. 提取所有交易日并建立时间步至交易日映射 (压缩多周期行情为统一每日因子行)
            var dailyDates = new List<MarketTime>();
            var stepToDateIndex = new int[tradeDates.Count];
            MarketTime lastDate = MarketTime.MinValue;
            int currentDateIndex = -1;

            for (int i = 0; i < tradeDates.Count; i++)
            {
                var d = tradeDates[i].Date;
                if (d != lastDate)
                {
                    dailyDates.Add(d);
                    lastDate = d;
                    currentDateIndex++;
                }
                stepToDateIndex[i] = currentDateIndex;
            }
            int dailyCount = dailyDates.Count;

            ChunkMatrix2D<double>? factorMatrix = null;
            bool cacheLoaded = false;
            TimeseriesCache? cache = null;

            try
            {
                // 2. 若开启 Cache 且指定了有效存储根目录，优先尝试从 LZ4 二进制缓存直接加载（极速冷启动路径，0 原始文件 I/O）
                string? baseRoot = !string.IsNullOrWhiteSpace(adjustConfig.StorageRoot)
                    ? adjustConfig.StorageRoot
                    : resolver?.StorageRoot;

                if (adjustConfig.Cache && !string.IsNullOrWhiteSpace(baseRoot))
                {
                    string cacheRoot = Path.Combine(baseRoot, ".cache");
                    cache = new TimeseriesCache(cacheRoot, tableName, pageSize: 4096, compression: CacheCompression.Lz4);

                    if (cache.IsCompatible(factorField, symbols, typeof(double), out var meta) && meta != null)
                    {
                        // 严格双向维度匹配：行数与列宽必须完全一致，严禁窄缓存越界
                        if (meta.RowCount == dailyCount && meta.Width == symbolCount)
                        {
                            var cachedDates = cache.ReadTradeDates();
                            bool datesMatch = cachedDates.Count == dailyCount;
                            if (datesMatch)
                            {
                                for (int i = 0; i < dailyCount; i++)
                                {
                                    if (cachedDates[i] != dailyDates[i])
                                    {
                                        datesMatch = false;
                                        break;
                                    }
                                }
                            }

                            if (datesMatch)
                            {
                                try
                                {
                                    factorMatrix = cache.LoadMatrix<double>(factorField);
                                    cacheLoaded = true;
                                    monitor?.Log($"[Adjustment] 成功从二进制缓存加载除权因子矩阵 ({meta.RowCount} x {meta.Width})。", LogLevel.Info);
                                }
                                catch (Exception ex)
                                {
                                    factorMatrix?.Dispose();
                                    factorMatrix = null;
                                    cacheLoaded = false;
                                    monitor?.Log($"[Adjustment] 二进制缓存反序列化异常，将重新构建: {ex.Message}", LogLevel.Warning);
                                }
                            }
                        }
                    }
                }

                // 3. 若未命中缓存，从除权数据源完整推导构建因子矩阵
                if (!cacheLoaded)
                {
                    IEventTable? adjTable = null;
                    if (events != null && events.HasTable(tableName))
                    {
                        adjTable = events[tableName];
                    }
                    else if (resolver != null)
                    {
                        // 专职通道加载：除权数据必须加载完整历史，绝对不受回测起测日剪枝影响
                        adjTable = EventSourceFactory.Create(resolver, tableName, startDate: null, endDate: null);
                    }

                    if (adjTable == null)
                    {
                        throw new InvalidOperationException($"启用了后复权模式 (mode: post)，但在事件注册表和存储目录中均未找到指定的除权表 '{tableName}'。");
                    }

                    if (adjTable.Count == 0)
                    {
                        throw new InvalidOperationException($"启用了后复权模式 (mode: post)，但除权表 '{tableName}' 中的记录总数为 0。");
                    }

                    factorMatrix = new ChunkMatrix2D<double>(width: symbolCount, pageSize: 4096, initialRowCount: dailyCount);
                    var currentFactors = new double[symbolCount];
                    Array.Fill(currentFactors, 1.0);

                    void ApplyDayEvents(MarketTime eventDate)
                    {
                        if (adjTable.TryGetDay(eventDate, out var dayEvents) && dayEvents != null)
                        {
                            foreach (var (symbol, rows) in dayEvents)
                            {
                                if (rows.Count > 0)
                                {
                                    var row = rows[^1];
                                    double factor = row.GetDouble(factorField, 0.0);
                                    if (factor > 0.0)
                                    {
                                        int sIdx = metadata.GetSymbolIndex(symbol);
                                        if (sIdx >= 0)
                                        {
                                            currentFactors[sIdx] = factor;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    using var dateEnumerator = adjTable.Dates.GetEnumerator();
                    bool hasEvent = dateEnumerator.MoveNext();

                    // 3.1 起测首日之前的所有历史事件快进滚存
                    MarketTime firstDate = dailyDates[0];
                    while (hasEvent && dateEnumerator.Current < firstDate)
                    {
                        ApplyDayEvents(dateEnumerator.Current);
                        hasEvent = dateEnumerator.MoveNext();
                    }

                    // 3.2 逐交易日滚动推进：吸收所有 <= 当日交易日（包含周末与节假日）的未消费事件，防止周末除权事件丢失
                    for (int d = 0; d < dailyCount; d++)
                    {
                        MarketTime curDate = dailyDates[d];
                        while (hasEvent && dateEnumerator.Current <= curDate)
                        {
                            ApplyDayEvents(dateEnumerator.Current);
                            hasEvent = dateEnumerator.MoveNext();
                        }

                        for (int s = 0; s < symbolCount; s++)
                        {
                            factorMatrix[d, s] = currentFactors[s];
                        }
                    }

                    // 3.3 若开启 Cache 则持久化至磁盘
                    if (adjustConfig.Cache && cache != null)
                    {
                        try
                        {
                            cache.WriteMatrix(factorField, factorMatrix, symbols, dailyDates);
                            monitor?.Log($"[Adjustment] 已成功生成并持久化除权因子矩阵二进制缓存至: {cache.GetTableDir()}", LogLevel.Info);
                        }
                        catch (Exception ex)
                        {
                            monitor?.Log($"[Adjustment] 持久化除权因子缓存失败: {ex.Message}", LogLevel.Warning);
                        }
                    }

                    // 4. 冷路径执行：行情与因子的跨模态覆盖与时序前置严格校验
                    if (timeseries != null)
                    {
                        ValidateCrossModalAlignment(adjTable, timeseries, metadata, dailyDates, tableName, monitor);
                    }
                }

                var resultMatrix = factorMatrix;
                factorMatrix = null; // 成功转移所有权
                return (resultMatrix, stepToDateIndex);
            }
            finally
            {
                // 异常安全保护：若在所有权转移前发生异常，及时释放非托管矩阵
                factorMatrix?.Dispose();
            }
        }

        private static void ValidateCrossModalAlignment(
            IEventTable adjTable,
            ITimeseriesRegistry timeseries,
            IMarketMetadata metadata,
            List<MarketTime> dailyDates,
            string tableName,
            IMonitor? monitor)
        {
            var firstAdjDates = new Dictionary<string, MarketTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var date in adjTable.Dates)
            {
                if (adjTable.TryGetDay(date, out var dayEvents) && dayEvents != null)
                {
                    foreach (var (sym, _) in dayEvents)
                    {
                        if (!firstAdjDates.ContainsKey(sym))
                        {
                            firstAdjDates[sym] = date;
                        }
                    }
                }
            }

            IReadOnlyMatrix2D<double>? closeMatrix = null;
            if (timeseries.Primary.HasField("close"))
            {
                closeMatrix = timeseries.Primary.GetMatrix<double>("close");
            }

            var unassignedSymbols = new List<string>();
            var symbols = metadata.Symbols;
            var tradeDates = metadata.TradeDates;
            int symbolCount = symbols.Count;

            for (int s = 0; s < symbolCount; s++)
            {
                string symbol = symbols[s];
                MarketTime? firstTradeDate = null;
                if (closeMatrix != null && s < closeMatrix.Width)
                {
                    int rowCount = closeMatrix.RowCount;
                    for (int r = 0; r < rowCount; r++)
                    {
                        double p = closeMatrix[r, s];
                        if (!double.IsNaN(p) && p > 0.0)
                        {
                            firstTradeDate = r < tradeDates.Count ? tradeDates[r] : dailyDates[0];
                            break;
                        }
                    }
                }

                if (firstTradeDate.HasValue)
                {
                    if (!firstAdjDates.TryGetValue(symbol, out var firstAdjDate))
                    {
                        unassignedSymbols.Add($"{symbol}(首日:{firstTradeDate.Value:yyyy-MM-dd},除权表无记录)");
                    }
                    else if (firstAdjDate > firstTradeDate.Value.Date)
                    {
                        unassignedSymbols.Add($"{symbol}(首日:{firstTradeDate.Value:yyyy-MM-dd},首条除权:{firstAdjDate:yyyy-MM-dd})");
                    }
                }
            }

            if (unassignedSymbols.Count > 0 && monitor != null)
            {
                string samples = string.Join(", ", unassignedSymbols.Take(5));
                monitor.Log(
                    $"[Warning] 发现 {unassignedSymbols.Count} 只标的在产生有效行情前未在除权表 '{tableName}' 中提供过复权因子（未严格一一对应，已按 1.0 补齐，存在严重漏配风险！）。样本: [{samples}{(unassignedSymbols.Count > 5 ? ", ..." : "")}]",
                    LogLevel.Warning);
            }
        }
    }
}
