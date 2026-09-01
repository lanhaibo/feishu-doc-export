using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace feishu_doc_export.Helper
{
    public static class FileHelper
    {
        /// <summary>
        /// DeduplicateOnExist 将「原路径 → _N 新路径」时触发。
        /// <para>并发安全：内部以 <see cref="DedupGate"/> 锁保护回调的添加/移除与触发。
        /// 订阅方若对共享集合写，仍需自行加锁。</para>
        /// </summary>
        public static event Action<string /*original*/, string /*actual*/> Deduplicated
        {
            add    { lock (DedupGate) _deduplicated += value; }
            remove { lock (DedupGate) _deduplicated -= value; }
        }
        private static event Action<string, string> _deduplicated;
        private static readonly object DedupGate = new();

        private static void OnDeduplicated(string original, string actual)
        {
            Action<string, string> handler;
            lock (DedupGate) { handler = _deduplicated; }
            if (handler != null) handler(original, actual);
        }

        /// <summary>
        /// Windows / *nix 非法文件名字符，统一替换为连字符
        /// </summary>
        private static readonly Regex InvalidFileNameCharsRegex = new(@"[\\/:\*\?""<>\|]", RegexOptions.Compiled);

        /// <summary>
        /// 文件名（不含扩展名）/文件夹名的单组件长度上限。
        /// <para>保守取 60（留 4 字符给 <c>...</c> 省略号、额外空间给扩展名和 _1/_2 去重尾号）。
        /// Windows 单组件实际上限 255，但 NTFS 长路径 + 扩展名校验 + 去重尾号叠加下，
        /// 提前控制可避免下游 <see cref="PathTooLongException"/>。</para>
        /// </summary>
        public const int SafeNameMaxLength = 60;

        /// <summary>
        /// 文件名/文件夹名规范化：非法字符替换 + 超长截断为「前 60 字符 + ...」
        /// </summary>
        /// <param name="name">原始名称（不含扩展名 / 路径分隔符）</param>
        /// <returns>规范化后的名称；若 <paramref name="name"/> 为 null 或空白返回空串</returns>
        public static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            name = InvalidFileNameCharsRegex.Replace(name, "-");

            if (name.Length > SafeNameMaxLength)
            {
                name = name.Substring(0, SafeNameMaxLength) + "...";
            }

            return name;
        }

        /// <summary>
        /// 若目标文件已存在，追加 <c>_1</c> / <c>_2</c> … 直到不冲突为止。
        /// 用于防止 SafeName 截断后重名覆盖。
        /// </summary>
        /// <param name="filePath">原始完整路径（含扩展名）</param>
        /// <returns>可安全写入的新路径</returns>
        public static string DeduplicateOnExist(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            string dir   = Path.GetDirectoryName(filePath);
            string ext   = Path.GetExtension(filePath);
            string stem  = Path.GetFileNameWithoutExtension(filePath);

            int suffix = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"{stem}_{suffix}{ext}");
                suffix++;
            } while (File.Exists(candidate));

            // 走到这里 candidate != filePath（因为 filePath 已存在被外层 if 拦了），触发一次事件
            OnDeduplicated(filePath, candidate);
            return candidate;
        }

        /// <summary>
        /// 保存文件
        /// </summary>
        /// <param name="path">目标路径</param>
        /// <param name="content">二进制内容</param>
        /// <returns>实际写入的最终路径（若触发去重，会与 <paramref name="path"/> 不同）</returns>
        public static async Task<string> Save(this string path, byte[] content)
        {
            path = DeduplicateOnExist(path);

            var dir = Path.GetDirectoryName(path);
            dir.CreateIfNotExist();

            await File.WriteAllBytesAsync(path, content).ConfigureAwait(false);
            return path;
        }

        /// <summary>
        /// 保存文件
        /// </summary>
        /// <param name="path">目标路径</param>
        /// <param name="content">文本内容（UTF-8 without BOM，<see cref="File.WriteAllTextAsync(string,string?,CancellationToken)"/> 默认编码）</param>
        /// <returns>实际写入的最终路径（若触发去重，会与 <paramref name="path"/> 不同）</returns>
        public static async Task<string> Save(this string path, string content)
        {
            path = DeduplicateOnExist(path);

            var dir = Path.GetDirectoryName(path);
            dir.CreateIfNotExist();

            await File.WriteAllTextAsync(path, content).ConfigureAwait(false);
            return path;
        }

        /// <summary>
        /// 如果目录不存在，那么创建目录
        /// </summary>
        /// <param name="path"></param>
        public static void CreateIfNotExist(this string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}
