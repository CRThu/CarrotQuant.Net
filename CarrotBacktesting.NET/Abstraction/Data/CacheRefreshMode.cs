namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 二进制对齐缓存刷新策略。
    /// </summary>
    public enum CacheRefreshMode
    {
        /// <summary>
        /// 首次缺失时构建一次，后续只要缓存存在则直接加载且不检查原始数据（默认）。
        /// </summary>
        Lazy = 0,

        /// <summary>
        /// 自动比对原始数据与缓存的头尾时间戳，存在新数据时增量补充。
        /// </summary>
        Auto = 1,

        /// <summary>
        /// 强制清空现有缓存并从原始数据全量重新构建。
        /// </summary>
        Force = 2
    }
}
