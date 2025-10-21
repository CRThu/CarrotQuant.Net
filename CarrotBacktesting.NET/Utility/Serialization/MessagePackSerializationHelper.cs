using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Utility.Serialization
{
    /// <summary>
    /// 提供基于 MessagePack 的高性能二进制序列化和反序列化帮助方法。
    /// </summary>
    public class MessagePackSerializationHelper
    {
        /// <summary>
        /// 全局 MessagePack 序列化器选项。
        /// LZ4 是一个速度极快的压缩算法，可以进一步减小缓存文件体积。
        /// </summary>
        private static readonly MessagePackSerializerOptions _options =
            MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

        /// <summary>
        /// 将对象序列化为 MessagePack 二进制字节数组。
        /// </summary>
        public static byte[] SerializeToBytes<T>(T obj)
        {
            return MessagePackSerializer.Serialize(obj, _options);
        }

        /// <summary>
        /// 从 MessagePack 二进制字节数组反序列化为对象。
        /// </summary>
        public static T? DeserializeFromBytes<T>(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return default;

            return MessagePackSerializer.Deserialize<T>(bytes, _options);
        }

        /// <summary>
        /// 将对象持久化到二进制文件中。
        /// </summary>
        public static void SerializeToFile<T>(T obj, string filePath)
        {
            var bytes = SerializeToBytes(obj);
            File.WriteAllBytes(filePath, bytes);
        }

        /// <summary>
        /// 从二进制文件中加载对象。
        /// </summary>
        public static T? DeserializeFromFile<T>(string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            return DeserializeFromBytes<T>(bytes);
        }
    }
}