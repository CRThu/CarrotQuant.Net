using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Utility
{
    public class FileScanner
    {
        /// <summary>
        /// 递归获取指定路径下所有文件，并生成文件名与子目录结构的键值对
        /// <br/>
        /// <code>
        /// "C:\data\file.txt"=> file
        /// "C:\data\a.b\sub\file.txt" => file.a+b-sub
        /// "C:\data\x-y\z\test.txt"=> test.x+y-z
        /// </code>
        /// </summary>
        /// <param name="path">要搜索的根目录路径</param>
        /// <param name="searchPattern">要匹配的文件名模式</param>
        /// <returns>
        /// 字典集合，其中：<br/>
        /// - Key: 格式为 [文件名].[目录结构]<br/>
        /// - Value: 文件的完整物理路径<br/>
        /// </returns>
        public static Dictionary<string, string> GetFiles(string path, string searchPattern = "*")
        {
            var fileDict = new Dictionary<string, string>();
            char[] separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

            foreach (var fullPath in Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories))
            {
                // 获取相对路径并分割
                string relativePath = Path.GetRelativePath(path, fullPath);
                string[] parts = relativePath
                    .Split(separators, StringSplitOptions.RemoveEmptyEntries)
                    .Where(p => p != ".")
                    .ToArray();

                // 提取文件名（不含扩展名）和目录层级
                string fileName = Path.GetFileNameWithoutExtension(parts.LastOrDefault() ?? "");
                string[] directories = parts
                    .Take(parts.Length - 1)
                    .Select(dir => dir.Replace(".", "+").Replace("-", "+")) // 将目录名中的 . - 替换为 +
                    .ToArray();

                // 构建唯一键名
                string key = directories.Length > 0
                    ? $"{fileName}.{string.Join("-", directories)}"
                    : fileName;

                fileDict[key] = fullPath;
            }

            return fileDict;
        }
    }
}
