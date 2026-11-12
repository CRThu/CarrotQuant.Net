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
        /// 安全同步创建 ParquetReader。通过 Task.Run 强制在纯净线程池执行，
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
        /// 支持 ReadOnlyMemory&lt;char&gt;、数值型、布尔型及时间戳/日期类型，空值安全回退为空字符串。
        /// </summary>
        public static string[] ReadStringColumn(this ParquetRowGroupReader groupReader, DataField field, int rowCount)
        {
            var arr = new string[rowCount];
            var clrType = field.ClrType;

            if (clrType == typeof(ReadOnlyMemory<char>))
            {
                var memArr = new ReadOnlyMemory<char>[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, memArr, def);
                for (int i = 0; i < rowCount; i++)
                {
                    arr[i] = def[i] > 0 ? memArr[i].ToString() : "";
                }
                return arr;
            }

            if (clrType == typeof(double))
            {
                var numArr = new double[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, numArr, def);
                for (int i = 0; i < rowCount; i++)
                    arr[i] = def[i] > 0 ? numArr[i].ToString(CultureInfo.InvariantCulture) : "";
                return arr;
            }
            if (clrType == typeof(float))
            {
                var numArr = new float[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, numArr, def);
                for (int i = 0; i < rowCount; i++)
                    arr[i] = def[i] > 0 ? numArr[i].ToString(CultureInfo.InvariantCulture) : "";
                return arr;
            }
            if (clrType == typeof(long))
            {
                var numArr = new long[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, numArr, def);
                for (int i = 0; i < rowCount; i++)
                    arr[i] = def[i] > 0 ? numArr[i].ToString(CultureInfo.InvariantCulture) : "";
                return arr;
            }
            if (clrType == typeof(int))
            {
                var numArr = new int[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, numArr, def);
                for (int i = 0; i < rowCount; i++)
                    arr[i] = def[i] > 0 ? numArr[i].ToString(CultureInfo.InvariantCulture) : "";
                return arr;
            }
            if (clrType == typeof(bool))
            {
                var boolArr = new bool[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, boolArr, def);
                for (int i = 0; i < rowCount; i++)
                    arr[i] = def[i] > 0 ? (boolArr[i] ? "true" : "false") : "";
                return arr;
            }
            if (clrType == typeof(DateTime))
            {
                var dtArr = new DateTime[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, dtArr, def);
                for (int i = 0; i < rowCount; i++)
                    arr[i] = def[i] > 0 ? dtArr[i].ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "";
                return arr;
            }
            if (clrType == typeof(DateTimeOffset))
            {
                var dtoArr = new DateTimeOffset[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, dtoArr, def);
                for (int i = 0; i < rowCount; i++)
                    arr[i] = def[i] > 0 ? dtoArr[i].DateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "";
                return arr;
            }

            throw new NotSupportedException($"Parquet column type '{clrType}' is not supported by ReadStringColumn.");
        }

        /// <summary>
        /// 批量读取 RowGroup 中的日期列并转换为统一的 DateTime 数组（截断至 Date 精度）。
        /// 自动适配 DateTime、DateTimeOffset、long（毫秒时间戳）以及文本日期。
        /// </summary>
        public static DateTime[] ReadDateColumn(this ParquetRowGroupReader groupReader, DataField field, int rowCount)
        {
            var dates = new DateTime[rowCount];
            var clrType = field.ClrType;

            if (clrType == typeof(DateTime))
            {
                var dtArr = new DateTime[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, dtArr, def);
                for (int i = 0; i < rowCount; i++)
                    dates[i] = def[i] > 0 ? dtArr[i].Date : DateTime.MinValue;
                return dates;
            }

            if (clrType == typeof(DateTimeOffset))
            {
                var dtoArr = new DateTimeOffset[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, dtoArr, def);
                for (int i = 0; i < rowCount; i++)
                    dates[i] = def[i] > 0 ? dtoArr[i].DateTime.Date : DateTime.MinValue;
                return dates;
            }

            if (clrType == typeof(long))
            {
                var tsArr = new long[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawSync(field, tsArr, def);
                for (int i = 0; i < rowCount; i++)
                    dates[i] = def[i] > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(tsArr[i]).DateTime.Date : DateTime.MinValue;
                return dates;
            }

            if (clrType == typeof(ReadOnlyMemory<char>))
            {
                var strArr = groupReader.ReadStringColumn(field, rowCount);
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

            return dates;
        }
    }
}
