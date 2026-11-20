namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 二进制对齐缓存压缩格式。
    /// </summary>
    public enum CacheCompression
    {
        /// <summary>
        /// LZ4 原生块压缩（默认）。
        /// </summary>
        Lz4 = 0,

        /// <summary>
        /// 无压缩二进制块。
        /// </summary>
        None = 1
    }
}
