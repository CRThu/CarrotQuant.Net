using System;

namespace CarrotBacktesting.NET.Abstraction.Engine
{
    /// <summary>
    /// 定义组件支持检查点保存与断点复原的正交能力契约。
    /// 遵循接口隔离原则（ISP），使状态持久化能力与引擎核心调度生命周期完全解耦。
    /// </summary>
    public interface ICheckpointable
    {
        /// <summary>
        /// 将当前运行状态序列化为检查点字节数组。
        /// </summary>
        /// <returns>检查点二进制数据。</returns>
        byte[] SaveCheckpoint();

        /// <summary>
        /// 从指定的检查点字节数据恢复运行状态。
        /// 仅允许在空闲（Idle）状态下调用。
        /// </summary>
        /// <param name="checkpoint">之前通过 SaveCheckpoint 获取的字节数组。</param>
        void RestoreCheckpoint(byte[] checkpoint);
    }
}
