using feishu_doc_export.Dtos;
using feishu_doc_export.Helper;
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
        #region ReplaceImagePath

        [Fact]
        public void ReplaceImagePath_RelativePath_KeepsOriginal()
        {
            var content = "正文 ![...](images/a.png) 结束";
            var result = content.ReplaceImagePath(@"E:\kb\doc.md");
            Assert.Equal(content, result);
        }

        [Fact]
        public void ReplaceImagePath_AbsolutePath_ConvertsToRelative()
        {
            var content = @"![...](E:\kb\sub\images\a.png)";
            var result = content.ReplaceImagePath(@"E:\kb\sub\doc.md");
            Assert.Equal(@"![...](images\a.png)", result);
        }

        [Fact]
        public void ReplaceImagePath_MixedContent_OnlyReplacesRooted()
        {
            var content = "![...](E:\\kb\\x.png) 与 ![...](images/y.png)";
            var result = content.ReplaceImagePath(@"E:\kb\doc.md");
            Assert.Equal(@"![...](x.png) 与 ![...](images/y.png)", result);
        }

        [Fact]
        public void ReplaceImagePath_NoImage_ReturnsOriginal()
        {
            var content = "没有任何图片的普通文本";
            var result = content.ReplaceImagePath(@"E:\kb\doc.md");
            Assert.Equal(content, result);
        }

        #endregion

        #region ReplaceDocRefPath

        [Fact]
        public void ReplaceDocRefPath_KnownNodeToken_ConvertsToRelativePath()
        {
            // 构造知识库节点映射：N1 -> "E:\kb\根文档"
            var documents = new List<WikiNodeItemDto>
            {
                new WikiNodeItemDto { NodeToken = "N1", ObjToken = "O1", Title = "根文档", ParentNodeToken = null }
            };
            DocumentPathGenerator.GenerateDocumentPaths(documents, @"E:\kb");

            var content = "[标题](https://xxx.feishu.cn/wiki/N1)";
            var result = content.ReplaceDocRefPath(@"E:\kb\其他.md");

            Assert.Equal("[标题](根文档.md)", result);
        }

        [Fact]
        public void ReplaceDocRefPath_UnknownNodeToken_KeepsOriginal()
        {
            var documents = new List<WikiNodeItemDto>
            {
                new WikiNodeItemDto { NodeToken = "N1", ObjToken = "O1", Title = "根文档", ParentNodeToken = null }
            };
            DocumentPathGenerator.GenerateDocumentPaths(documents, @"E:\kb");

            var content = "[外部文档](https://xxx.feishu.cn/wiki/UnknownToken)";
            var result = content.ReplaceDocRefPath(@"E:\kb\其他.md");

            Assert.Equal(content, result);
        }

        [Fact]
        public void ReplaceDocRefPath_NonFeishuLink_KeepsOriginal()
        {
            var content = "[官网](https://www.example.com/page)";
            var result = content.ReplaceDocRefPath(@"E:\kb\doc.md");

            Assert.Equal(content, result);
        }

        [Fact]
        public void ReplaceDocRefPath_HttpLink_AlsoMatches()
        {
            var documents = new List<WikiNodeItemDto>
            {
                new WikiNodeItemDto { NodeToken = "N1", ObjToken = "O1", Title = "根文档", ParentNodeToken = null }
            };
            DocumentPathGenerator.GenerateDocumentPaths(documents, @"E:\kb");

            var content = "[标题](http://xxx.feishu.cn/wiki/N1)";
            var result = content.ReplaceDocRefPath(@"E:\kb\其他.md");

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
