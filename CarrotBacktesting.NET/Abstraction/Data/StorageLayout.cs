namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 数据表的物理存储布局模式。
    /// </summary>
    public enum StorageLayout
    {
        /// <summary>
        /// Hive 分区模式 (含有 year=YYYY/ 子目录)。
        /// </summary>
        Hive,

        /// <summary>
        /// Flat 平铺模式 (所有数据文件直接平铺在表目录下)。
        /// </summary>
        Flat
    }
}
