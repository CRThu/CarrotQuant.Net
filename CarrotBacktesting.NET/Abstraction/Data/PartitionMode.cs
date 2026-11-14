namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 数据表的物理分区模式。
    /// </summary>
    public enum PartitionMode
    {
        /// <summary>
        /// 无代码分区（单文件全市场多标的，或按 data 文件存储）。
        /// </summary>
        None,

        /// <summary>
        /// 按标的代码（Symbol）独立分区单文件存储。
        /// </summary>
        Symbol
    }
}
