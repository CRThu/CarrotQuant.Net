using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// Parquet 数据读取与列转换扩展工具类（跨时序、事件与维表共享）。
    /// 提供 Schema 字段安全探测、全类型字符串列归一化、日期列高性能读取，
    /// 以及线程池脱离上下文的安全同步读取包装（彻底规避 SynchronizationContext 死锁）。
    /// </summary>
    public static class ParquetReaderExtensions
    {
        /// <summary>
        /// 安全同步创建 ParquetReader。通过 Task.Run 强制在独立线程池任务中执行，
        /// 彻底规避带有 SynchronizationContext（如 UI 调度器）时直接同步阻塞引发的死锁。
        /// </summary>
        public static ParquetReader OpenParquetReaderSync(Stream stream, ParquetOptions? options = null)
        {
            return Task.Run(async () => await ParquetReader.CreateAsync(stream, options).ConfigureAwait(false))
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 安全同步释放 ParquetReader。
        /// </summary>
        public static void DisposeSync(this ParquetReader reader)
        {
            Task.Run(async () => await reader.DisposeAsync().ConfigureAwait(false))
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 安全同步批量读取底层原始数据列。
        /// </summary>
        public static void ReadRawSync<T>(
            this ParquetRowGroupReader groupReader,
            DataField field,
            T[] buffer,
            int[]? defLevels = null) where T : struct
        {
            Memory<int>? defMem = defLevels != null ? (Memory<int>)defLevels : null;
            Task.Run(async () => await groupReader.ReadRawAsync<T>(field, buffer, defMem, null, default).ConfigureAwait(false))
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 忽略大小写在 Schema 中按候选名称顺序查找首个匹配的数据字段，未找到返回 null。
        /// 规避原生 ParquetSchema.FindDataField 大小写敏感且未命中抛 ArgumentException 的问题。
        /// </summary>
        public static DataField? FindDataFieldIgnoreCase(this ParquetSchema schema, params string[] candidateNames)
        {
            var fields = schema.GetDataFields();
            foreach (var name in candidateNames)
            {
                var match = fields.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return null;
        }

        /// <summary>
        /// 批量读取 RowGroup 中的指定列并归一化转换为字符串数组。
        /// <summary>
        /// 批量读取 RowGroup 中的指定列并转换为字符串数组。
        /// 支持 ReadOnlyMemory&lt;char&gt;、数值型、布尔型及时间戳/日期类型。
        /// 默认严禁 NULL 值；除非字段显式在 nullableFields 集合中声明，否则一旦检测到 NULL 立即抛出详细诊断异常。
        /// </summary>
        public static string[] ReadStringColumn(
            this ParquetRowGroupReader groupReader,
            DataField field,
            int rowCount,
            IReadOnlySet<string>? nullableFields = null)
        {
            var arr = new string[rowCount];
            var clrType = field.ClrType;
            bool isNullable = field.IsNullable;
            bool isAllowedNull = nullableFields?.Contains(field.Name) ?? false;

            int nullCount = 0;
            List<int>? badIndices = null;

            void CheckNull(int i)
            {
                nullCount++;
                if (badIndices == null) badIndices = new List<int>(5);
                if (badIndices.Count < 5) badIndices.Add(i);
            }

            void ThrowIfInvalid()
            {
                if (nullCount > 0 && !isAllowedNull)
                {
                    string sampleStr = string.Join(", ", badIndices!);
                    string moreSuffix = nullCount > badIndices!.Count ? " ..." : "";
                    throw new InvalidDataException(
                        $"Parquet 列 '{field.Name}' 检测到缺失值 (NULL)：共 {nullCount}/{rowCount} 行无效 (占比 {(double)nullCount / rowCount:P2})。损坏样本行号: [{sampleStr}{moreSuffix}]。该字段未显式配置允许空值，严禁缺失。");
                }
            }

            if (clrType == typeof(ReadOnlyMemory<char>))
            {
                var memArr = new ReadOnlyMemory<char>[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, memArr, def);
                int valIdx = 0;
                for (int i = 0; i < rowCount; i++)
                {
                    if (!isNullable || def[i] > 0)
                    {
                        arr[i] = memArr[valIdx++].ToString();
                    }
                    else
                    {
                        CheckNull(i);
                        arr[i] = "";
                    }
                }
                ThrowIfInvalid();
                return arr;
            }

            if (clrType == typeof(double))
            {
                var numArr = new double[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, numArr, def);
                int valIdx = 0;
                for (int i = 0; i < rowCount; i++)
                {
                    if (!isNullable || def[i] > 0)
                    {
                        arr[i] = numArr[valIdx++].ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        CheckNull(i);
                        arr[i] = "";
                    }
                }
                ThrowIfInvalid();
                return arr;
            }
            if (clrType == typeof(float))
            {
                var numArr = new float[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, numArr, def);
                int valIdx = 0;
                for (int i = 0; i < rowCount; i++)
                {
                    if (!isNullable || def[i] > 0)
                    {
                        arr[i] = numArr[valIdx++].ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        CheckNull(i);
                        arr[i] = "";
                    }
                }
                ThrowIfInvalid();
                return arr;
            }
            if (clrType == typeof(long))
            {
                var numArr = new long[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, numArr, def);
                int valIdx = 0;
                for (int i = 0; i < rowCount; i++)
                {
                    if (!isNullable || def[i] > 0)
                    {
                        arr[i] = numArr[valIdx++].ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        CheckNull(i);
                        arr[i] = "";
                    }
                }
                ThrowIfInvalid();
                return arr;
            }
            if (clrType == typeof(int))
            {
                var numArr = new int[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, numArr, def);
                int valIdx = 0;
                for (int i = 0; i < rowCount; i++)
                {
                    if (!isNullable || def[i] > 0)
                    {
                        arr[i] = numArr[valIdx++].ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        CheckNull(i);
                        arr[i] = "";
                    }
                }
                ThrowIfInvalid();
                return arr;
            }
            if (clrType == typeof(bool))
            {
                var boolArr = new bool[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, boolArr, def);
                int valIdx = 0;
                for (int i = 0; i < rowCount; i++)
                {
                    if (!isNullable || def[i] > 0)
                    {
                        arr[i] = (boolArr[valIdx++] ? "true" : "false");
                    }
                    else
                    {
                        CheckNull(i);
                        arr[i] = "";
                    }
                }
                ThrowIfInvalid();
                return arr;
            }
            if (clrType == typeof(DateTime))
            {
                var dtArr = new DateTime[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, dtArr, def);
                int valIdx = 0;
                for (int i = 0; i < rowCount; i++)
                {
                    if (!isNullable || def[i] > 0)
                    {
                        arr[i] = dtArr[valIdx++].ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        CheckNull(i);
                        arr[i] = "";
                    }
                }
                ThrowIfInvalid();
                return arr;
            }
            if (clrType == typeof(DateTimeOffset))
            {
                var dtoArr = new DateTimeOffset[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, dtoArr, def);
                int valIdx = 0;
                for (int i = 0; i < rowCount; i++)
                {
                    if (!isNullable || def[i] > 0)
                    {
                        arr[i] = dtoArr[valIdx++].DateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        CheckNull(i);
                        arr[i] = "";
                    }
                }
                ThrowIfInvalid();
                return arr;
            }

            throw new NotSupportedException($"Parquet column type '{clrType}' is not supported by ReadStringColumn.");
        }

        /// <summary>
        /// 批量读取 RowGroup 中的日期列并转换为统一的 DateTime 数组（截断至 Date 精度）。
        /// 自动适配 DateTime、DateTimeOffset、long（毫秒时间戳）以及文本日期。
        /// 默认严禁 NULL 值；除非字段显式在 nullableFields 集合中声明，否则一旦检测到 NULL 立即抛出详细诊断异常。
        /// </summary>
        public static DateTime[] ReadDateColumn(
            this ParquetRowGroupReader groupReader,
            DataField field,
            int rowCount,
            IReadOnlySet<string>? nullableFields = null)
        {
            var dates = new DateTime[rowCount];
            var clrType = field.ClrType;
            bool isNullable = field.IsNullable;
            bool isAllowedNull = nullableFields?.Contains(field.Name) ?? false;

            int nullCount = 0;
            List<int>? badIndices = null;

            void CheckNull(int i)
            {
                nullCount++;
                if (badIndices == null) badIndices = new List<int>(5);
                if (badIndices.Count < 5) badIndices.Add(i);
            }

            void ThrowIfInvalid()
            {
                if (nullCount > 0 && !isAllowedNull)
                {
                    string sampleStr = string.Join(", ", badIndices!);
                    string moreSuffix = nullCount > badIndices!.Count ? " ..." : "";
                    throw new InvalidDataException(
                        $"Parquet 列 '{field.Name}' 检测到缺失值 (NULL)：共 {nullCount}/{rowCount} 行无效 (占比 {(double)nullCount / rowCount:P2})。损坏样本行号: [{sampleStr}{moreSuffix}]。该字段未显式配置允许空值，严禁缺失。");
                }
            }

            if (clrType == typeof(DateTime))
            {
                var dtArr = new DateTime[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, dtArr, def);
                int valIdx = 0;
                for (int i = 0; i < rowCount; i++)
                {
                    if (!isNullable || def[i] > 0)
                    {
                        dates[i] = dtArr[valIdx++].Date;
                    }
                    else
                    {
                        CheckNull(i);
                        dates[i] = DateTime.MinValue;
                    }
                }
                ThrowIfInvalid();
                return dates;
            }

            if (clrType == typeof(DateTimeOffset))
            {
                var dtoArr = new DateTimeOffset[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, dtoArr, def);
                int valIdx = 0;
                for (int i = 0; i < rowCount; i++)
                {
                    if (!isNullable || def[i] > 0)
                    {
                        dates[i] = dtoArr[valIdx++].DateTime.Date;
                    }
                    else
                    {
                        CheckNull(i);
                        dates[i] = DateTime.MinValue;
                    }
                }
                ThrowIfInvalid();
                return dates;
            }

            if (clrType == typeof(long))
            {
                var tsArr = new long[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, tsArr, def);
                int valIdx = 0;
                for (int i = 0; i < rowCount; i++)
                {
                    if (!isNullable || def[i] > 0)
                    {
                        dates[i] = DateTimeOffset.FromUnixTimeMilliseconds(tsArr[valIdx++]).DateTime.Date;
                    }
                    else
                    {
                        CheckNull(i);
                        dates[i] = DateTime.MinValue;
                    }
                }
                ThrowIfInvalid();
                return dates;
            }

            if (clrType == typeof(ReadOnlyMemory<char>))
            {
                var strArr = groupReader.ReadStringColumn(field, rowCount, nullableFields);
                for (int i = 0; i < rowCount; i++)
                {
                    if (!string.IsNullOrEmpty(strArr[i]) && DateTime.TryParse(strArr[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    {
                        dates[i] = dt.Date;
                    }
                    else
                    {
                        dates[i] = DateTime.MinValue;
                    }
                }
                return dates;
            }

            throw new NotSupportedException($"Parquet column type '{clrType}' is not supported by ReadDateColumn.");
        }
    }
}
