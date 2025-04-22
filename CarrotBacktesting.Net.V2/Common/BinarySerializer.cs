using CarrotBacktesting.Net.DataModel;
using MemoryPack;
using MemoryPack.Compression;
using Newtonsoft.Json;
using SQLitePCL;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Common
{
    /// <summary>
    /// 二进制序列化与反序列化通用配置静态类
    /// </summary>
    public static class BinarySerializer
    {
        /// <summary>
        /// 二进制序列化压缩性能配置项
        /// </summary>
        public static readonly BrotliCompressor Compressor = new BrotliCompressor(CompressionLevel.Optimal);
        /// <summary>
        /// 二进制序列化解压配置项
        /// </summary>
        public static readonly BrotliDecompressor Decompressor = new BrotliDecompressor();

        /// <summary>
        /// 二进制序列化方法
        /// </summary>
        /// <typeparam name="T">类型</typeparam>
        /// <param name="value">Object</param>
        /// <returns>返回序列化byte字节数组</returns>
        public static byte[] Serialize<T>(T value)
        {
            // MemoryPack
            //var bin = MemoryPackSerializer.Serialize(raw);

            // MemoryPack+Compression
            // Compression(require using)
            //using var compressor = new BrotliCompressor(CompressionLevel.Optimal);
            MemoryPackSerializer.Serialize(Compressor, value);
            // Get compressed byte[]
            return Compressor.ToArray();
        }

        /// <summary>
        /// 二进制序列化到文件方法
        /// </summary>
        /// <typeparam name="T">类型</typeparam>
        /// <param name="value">Object</param>
        /// <param name="filepath">序列化文件路径</param>
        public static void SerializeToFile<T>(T value, string filepath)
        {
            File.WriteAllBytes(filepath, Serialize(value));
        }

        /// <summary>
        /// 二进制反序列化方法
        /// </summary>
        /// <typeparam name="T">类型</typeparam>
        /// <param name="bin">序列化字符串</param>
        /// <returns>Object</returns>
        public static T? DeSerialize<T>(byte[] bin)
        {
            // MemoryPack
            //var val = MemoryPackSerializer.Deserialize<Dictionary<DateTime, MarketFrame>>(bin);

            // MemoryPack+Compression
            // Decompression(require using)
            //using var decompressor = new BrotliDecompressor();

            // Get decompressed ReadOnlySequence<byte> from ReadOnlySpan<byte> or ReadOnlySequence<byte>
            var decompressedBuffer = Decompressor.Decompress(bin);

            var val = MemoryPackSerializer.Deserialize<T>(decompressedBuffer);
            return val;
        }

        /// <summary>
        /// 二进制从文件反序列化方法
        /// </summary>
        /// <typeparam name="T">类型</typeparam>
        /// <param name="filepath">二进制文件路径</param>
        /// <returns>Object</returns>
        public static T? DeSerializeFromFile<T>(string filepath)
        {
            byte[] bin = File.ReadAllBytes(filepath);
            return DeSerialize<T>(bin);
        }
    }
}
