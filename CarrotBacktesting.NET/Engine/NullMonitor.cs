using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Engine
{
    /// <summary>
    /// 空监控实现（Null Object Pattern），在无监控或静默运行时提供零开销的默认行为。
    /// </summary>
    public sealed class NullMonitor : IMonitor
    {
        /// <summary>
        /// 全局唯一共享单例实例。
        /// </summary>
        public static readonly NullMonitor Instance = new();

        private NullMonitor() { }

        /// <inheritdoc />
        public void Log(string message, LogLevel level) { }

        /// <inheritdoc />
        public void ReportMetric(string metricName, double value) { }

        /// <inheritdoc />
        public void Alert(string message) { }
    }
}
