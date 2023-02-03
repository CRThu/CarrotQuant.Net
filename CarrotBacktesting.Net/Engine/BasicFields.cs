namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 基础字段
    /// </summary>
    public struct BasicFields
    {
        /// <summary>
        /// 时间
        /// </summary>
        public string? Time { get; set; }

        /// <summary>
        /// 开盘价
        /// </summary>
        public string? Open { get; set; }

        /// <summary>
        /// 最高价
        /// </summary>
        public string? High { get; set; }

        /// <summary>
        /// 最低价
        /// </summary>
        public string? Low { get; set; }

        /// <summary>
        /// 收盘价
        /// </summary>
        public string? Close { get; set; }

        /// <summary>
        /// 成交量
        /// </summary>
        public string? Volume { get; set; }

        public string[] ToArray()
        {
            List<string> l = new();
            if (Time is not null)
                l.Add(Time);
            if (Open is not null)
                l.Add(Open);
            if (High is not null)
                l.Add(High);
            if (Low is not null)
                l.Add(Low);
            if (Close is not null)
                l.Add(Close);
            if (Volume is not null)
                l.Add(Volume);
            return l.ToArray();
        }
    }
}
