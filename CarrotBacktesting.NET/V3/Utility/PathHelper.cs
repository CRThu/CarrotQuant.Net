using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Utility
{
    public static class PathHelper
    {
        private static string Root => AppDomain.CurrentDomain.BaseDirectory;

        public static bool IsDebugging => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VisualStudioVersion"));

        /// <summary>
        /// 从当前目录开始，逐级向上查找指定的文件或目录。
        /// </summary>
        /// <param name="relativePath">要查找的相对路径（例如 "env.yaml" 或 "Data/Market"）。</param>
        /// <param name="maxLevels">向上搜索的最大层数，防止无限循环。默认为10层。</param>
        /// <returns>如果找到，返回完整物理路径；如果未找到，则返回 null。</returns>
        public static string? FindPathUpwards(string relativePath, int maxLevels = 10)
        {
            // 从应用程序的基目录开始搜索
            var currentDir = new DirectoryInfo(Root);

            for (int i = 0; i < maxLevels; i++)
            {
                // 构造当前目录下的完整路径
                string fullPath = Path.Combine(currentDir.FullName, relativePath);

                // 检查文件或目录是否存在
                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                {
                    // 找到了，返回完整路径
                    return Path.GetFullPath(fullPath);
                }

                // 如果没找到，且当前目录不是根目录，则向上一级
                if (currentDir.Parent == null)
                {
                    // 已经到达根目录，停止搜索
                    break;
                }
                currentDir = currentDir.Parent;
            }

            // 在所有层级中都未找到
            return null;
        }
    }
}
