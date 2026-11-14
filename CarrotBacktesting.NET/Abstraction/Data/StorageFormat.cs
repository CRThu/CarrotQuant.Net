namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 数据表的物理存储格式。
    /// </summary>
    public enum StorageFormat
    {
        /// <summary>
        /// Apache Parquet 列式存储格式。
        /// </summary>
        Parquet,

        /// <summary>
        /// CSV 纯文本定界符格式。
        /// </summary>
        Csv
    }
}
