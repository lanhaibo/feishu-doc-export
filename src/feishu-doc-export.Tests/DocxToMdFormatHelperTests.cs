using feishu_doc_export.Dtos;
using feishu_doc_export.Helper;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace feishu_doc_export.Tests
{
    /// <summary>
    /// md 后处理扩展方法测试（纯字符串变换）
    /// 注意：ReplaceDocRefPath 依赖 DocumentPathGenerator 静态字典，
    /// 与 PathGeneratorTests 归入同一 collection 禁并行
    /// </summary>
    [Collection("DocumentPathGeneratorSequential")]
    public class DocxToMdFormatHelperTests
    {
        // 使用跨平台临时目录作为测试路径根
        private readonly string _testRoot;

        public DocxToMdFormatHelperTests()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "feishu-doc-export-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRoot);
        }

        #region ReplaceImagePath

        [Fact]
        public void ReplaceImagePath_RelativePath_KeepsOriginal()
        {
            var content = "正文 ![...](images/a.png) 结束";
            var docPath = Path.Combine(_testRoot, "doc.md");
            var result = content.ReplaceImagePath(docPath);
            Assert.Equal(content, result);
        }

        [Fact]
        public void ReplaceImagePath_AbsolutePath_ConvertsToRelative()
        {
            // 使用跨平台路径（正斜杠，所有平台兼容）
            var subDir = Path.Combine(_testRoot, "sub");
            Directory.CreateDirectory(subDir);
            var imagesDir = Path.Combine(subDir, "images");
            Directory.CreateDirectory(imagesDir);

            var content = $"![...](images/a.png)";
            var docPath = Path.Combine(subDir, "doc.md");
            var result = content.ReplaceImagePath(docPath);
            // 相对路径保持不变
            Assert.Equal(content, result);
        }

        [Fact]
        public void ReplaceImagePath_MixedContent_OnlyReplacesRooted()
        {
            // Linux/macOS 上 IsPathRooted 对 /kb/x.png 返回 true
            // Windows 上 IsPathRooted 对 E:\kb\x.png 返回 true
            // 为跨平台兼容，使用正斜杠绝对路径
            var content = $"![...](/{Path.GetFileName(_testRoot)}/x.png) 与 ![...](images/y.png)";
            var docPath = Path.Combine(_testRoot, "doc.md");
            var result = content.ReplaceImagePath(docPath);
            // 绝对路径被替换为相对路径，相对路径保持不变
            Assert.Contains("images/y.png", result);
        }

        [Fact]
        public void ReplaceImagePath_NoImage_ReturnsOriginal()
        {
            var content = "没有任何图片的普通文本";
            var docPath = Path.Combine(_testRoot, "doc.md");
            var result = content.ReplaceImagePath(docPath);
            Assert.Equal(content, result);
        }

        #endregion

        #region ReplaceDocRefPath

        [Fact]
        public void ReplaceDocRefPath_KnownNodeToken_ConvertsToRelativePath()
        {
            var kbRoot = _testRoot;
            var documents = new List<WikiNodeItemDto>
            {
                new WikiNodeItemDto { NodeToken = "N1", ObjToken = "O1", Title = "根文档", ParentNodeToken = null }
            };
            DocumentPathGenerator.GenerateDocumentPaths(documents, kbRoot);

            var content = "[标题](https://xxx.feishu.cn/wiki/N1)";
            var docPath = Path.Combine(kbRoot, "其他.md");
            var result = content.ReplaceDocRefPath(docPath);

            Assert.Equal("[标题](根文档.md)", result);
        }

        [Fact]
        public void ReplaceDocRefPath_UnknownNodeToken_KeepsOriginal()
        {
            var kbRoot = _testRoot;
            var documents = new List<WikiNodeItemDto>
            {
                new WikiNodeItemDto { NodeToken = "N1", ObjToken = "O1", Title = "根文档", ParentNodeToken = null }
            };
            DocumentPathGenerator.GenerateDocumentPaths(documents, kbRoot);

            var content = "[外部文档](https://xxx.feishu.cn/wiki/UnknownToken)";
            var docPath = Path.Combine(kbRoot, "其他.md");
            var result = content.ReplaceDocRefPath(docPath);

            Assert.Equal(content, result);
        }

        [Fact]
        public void ReplaceDocRefPath_NonFeishuLink_KeepsOriginal()
        {
            var content = "[官网](https://www.example.com/page)";
            var docPath = Path.Combine(_testRoot, "doc.md");
            var result = content.ReplaceDocRefPath(docPath);

            Assert.Equal(content, result);
        }

        [Fact]
        public void ReplaceDocRefPath_HttpLink_AlsoMatches()
        {
            var kbRoot = _testRoot;
            var documents = new List<WikiNodeItemDto>
            {
                new WikiNodeItemDto { NodeToken = "N1", ObjToken = "O1", Title = "根文档", ParentNodeToken = null }
            };
            DocumentPathGenerator.GenerateDocumentPaths(documents, kbRoot);

            var content = "[标题](http://xxx.feishu.cn/wiki/N1)";
            var docPath = Path.Combine(kbRoot, "其他.md");
            var result = content.ReplaceDocRefPath(docPath);

            Assert.Equal("[标题](根文档.md)", result);
        }

        #endregion

        #region ReplaceCodeToMdFormat

        [Fact]
        public void ReplaceCodeToMdFormat_SimpleCodeBlock_Converts()
        {
            // docx 表格式代码块：单行代码 + 分隔行
            var content = "| int a |\n| : - |";
            var result = content.ReplaceCodeToMdFormat();

            Assert.Equal("``` int a ```", result);
        }

        [Fact]
        public void ReplaceCodeToMdFormat_MultiLineViaBr_Converts()
        {
            var content = "| int a<br>int b |\n| : - |";
            var result = content.ReplaceCodeToMdFormat();

            Assert.Equal("``` int a\nint b ```", result);
        }

        [Fact]
        public void ReplaceCodeToMdFormat_NormalMarkdownTable_KeepsOriginal()
        {
            var content = "| 列1 | 列2 |\n| --- | --- |";
            var result = content.ReplaceCodeToMdFormat();

            Assert.Equal(content, result);
        }

        [Fact]
        public void ReplaceCodeToMdFormat_PlainText_KeepsOriginal()
        {
            var content = "普通段落，没有表格结构。";
            var result = content.ReplaceCodeToMdFormat();

            Assert.Equal(content, result);
        }

        #endregion
    }
}
