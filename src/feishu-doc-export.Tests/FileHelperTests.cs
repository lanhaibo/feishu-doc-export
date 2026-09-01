using feishu_doc_export.Helper;
using System;
using System.IO;
using Xunit;

namespace feishu_doc_export.Tests
{
    /// <summary>
    /// FileHelper 文件名安全化 + 去重工具方法单测
    /// </summary>
    public class FileHelperTests : IDisposable
    {
        private readonly string _tempDir;

        public FileHelperTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "feishu-doc-export-filehelper-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        /* ============================================================
         * SafeName
         * ============================================================ */

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void SafeName_NullOrBlank_ReturnsEmpty(string input)
        {
            Assert.Equal(string.Empty, FileHelper.SafeName(input));
        }

        [Fact]
        public void SafeName_NormalShortName_ReturnsOriginal()
        {
            Assert.Equal("Codex专栏", FileHelper.SafeName("Codex专栏"));
        }

        [Fact]
        public void SafeName_InvalidChars_ReplacedWithHyphen()
        {
            // 所有 Windows / *nix 非法字符
            Assert.Equal("a-b-c-d-e-f-g-h-i-", FileHelper.SafeName(@"a/b:c*d?e""f<g>h|i\"));
        }

        [Fact]
        public void SafeName_Exactly60Chars_NoEllipsis()
        {
            string name = new string('x', FileHelper.SafeNameMaxLength); // 60
            string result = FileHelper.SafeName(name);

            Assert.Equal(FileHelper.SafeNameMaxLength, result.Length);
            Assert.Equal(name, result);
            Assert.DoesNotContain("...", result);
        }

        [Fact]
        public void SafeName_61Chars_AddsEllipsisTotal63()
        {
            string name = new string('x', 61);
            string result = FileHelper.SafeName(name);

            // 60 字符 + 3 省略号 = 63
            Assert.Equal(63, result.Length);
            Assert.EndsWith("...", result);
            Assert.Equal(new string('x', 60), result.Substring(0, 60));
        }

        [Fact]
        public void SafeName_VeryLongTitle_TruncatesBeforeEllipsis()
        {
            // 对应真实失败案例：Codex cli启动报错：Error loading config.toml: invalid transport in 'mcp_servers.node_repl' ...
            string longTitle = "Codex cli启动报错：Error loading config.toml: invalid transport in 'mcp_servers.node_repl' " +
                               "extra stuff to exceed limit by a lot extra stuff extra stuff";

            string result = FileHelper.SafeName(longTitle);

            Assert.Equal(63, result.Length);
            Assert.EndsWith("...", result);
            // 非法字符检查：单引号不在替换列表（不是 Windows 非法字符），冒号是 Windows 非法字符应被替换为 -
            Assert.DoesNotContain(":", result);
            Assert.Contains("-", result); // 冒号替换产物
        }

        [Fact]
        public void SafeName_LongPlusInvalid_InvalidReplacedBeforeTruncate()
        {
            // 非法字符位于 60 边界之后，仍会先替换再截断
            string name = new string('a', 58) + @":"; // 59 字符（含非法冒号） + 再多一个让长度过线
            name += "extraextra";

            string result = FileHelper.SafeName(name);

            // 冒号一定被替换为 -，不应出现在结果里
            Assert.DoesNotContain(":", result);
            Assert.True(result.Length > 58);
        }

        /* ============================================================
         * DeduplicateOnExist
         * ============================================================ */

        [Fact]
        public void DeduplicateOnExist_DoesNotExist_ReturnsOriginal()
        {
            string path = Path.Combine(_tempDir, "不存在的.md");
            Assert.Equal(path, FileHelper.DeduplicateOnExist(path));
        }

        [Fact]
        public void DeduplicateOnExist_ExistsOnce_AddsSuffix1()
        {
            string path = Path.Combine(_tempDir, "dup.md");
            File.WriteAllText(path, "v1");

            string result = FileHelper.DeduplicateOnExist(path);

            Assert.Equal(Path.Combine(_tempDir, "dup_1.md"), result);
        }

        [Fact]
        public void DeduplicateOnExist_MultipleExist_AddsNextSuffix()
        {
            string path = Path.Combine(_tempDir, "dup.txt");
            File.WriteAllText(path, "v1");
            File.WriteAllText(Path.Combine(_tempDir, "dup_1.txt"), "v2");
            File.WriteAllText(Path.Combine(_tempDir, "dup_2.txt"), "v3");

            string result = FileHelper.DeduplicateOnExist(path);

            Assert.Equal(Path.Combine(_tempDir, "dup_3.txt"), result);
        }

        [Fact]
        public async Task Save_Bytes_Suffix1IfDuplicate()
        {
            string path = Path.Combine(_tempDir, "s.bin");
            await path.Save(new byte[] { 0xAA, 0x01 });
            Assert.True(File.Exists(path));

            // Save 扩展方法返回 Task，无法直接拿到写入最终路径；
            // 通过"写 v1 → 再写 v2 → 找 *_1.bin 并校验内容"验证去重逻辑生效
            await path.Save(new byte[] { 0xBB, 0x02 });

            string p1 = Path.Combine(_tempDir, "s_1.bin");
            Assert.True(File.Exists(p1), "第二次 Save 应生成 s_1.bin");
            Assert.Equal(new byte[] { 0xAA, 0x01 }, File.ReadAllBytes(path));
            Assert.Equal(new byte[] { 0xBB, 0x02 }, File.ReadAllBytes(p1));
        }

        [Fact]
        public async Task Save_String_Suffix1IfDuplicate()
        {
            string path = Path.Combine(_tempDir, "s.md");
            await path.Save("alpha");
            await path.Save("beta");

            string p1 = Path.Combine(_tempDir, "s_1.md");
            Assert.True(File.Exists(p1), "第二次 Save 应生成 s_1.md");
            Assert.Equal("alpha", File.ReadAllText(path));
            Assert.Equal("beta", File.ReadAllText(p1));
        }
    }
}
