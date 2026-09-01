using feishu_doc_export.Dtos;
using Xunit;

namespace feishu_doc_export.Tests
{
    /// <summary>
    /// 共享 DocumentPathGenerator 静态字典状态的测试类归入同一 collection 并禁用并行，避免跨类竞争
    /// </summary>
    [CollectionDefinition("DocumentPathGeneratorSequential", DisableParallelization = true)]
    public class DocumentPathGeneratorSequentialCollection
    {
    }

    /// <summary>
    /// 知识库目录树到本地路径映射测试
    /// </summary>
    [Collection("DocumentPathGeneratorSequential")]
    public class PathGeneratorTests
    {
        private static List<WikiNodeItemDto> BuildNodeTree() => new List<WikiNodeItemDto>
        {
            new WikiNodeItemDto { NodeToken = "N1", ObjToken = "O1", Title = "根文档", ParentNodeToken = null },
            new WikiNodeItemDto { NodeToken = "N2", ObjToken = "O2", Title = "子文档1", ParentNodeToken = "N1" },
            new WikiNodeItemDto { NodeToken = "N3", ObjToken = "O3", Title = "子文档2", ParentNodeToken = "N1" },
            new WikiNodeItemDto { NodeToken = "N4", ObjToken = "O4", Title = "孙文档", ParentNodeToken = "N2" },
        };

        [Fact]
        public void GenerateDocumentPaths_MapsTopLevelToRoot()
        {
            DocumentPathGenerator.GenerateDocumentPaths(BuildNodeTree(), @"E:\kb");

            Assert.Equal(Path.Combine(@"E:\kb", "根文档"), DocumentPathGenerator.GetDocumentPath("O1"));
        }

        [Fact]
        public void GenerateDocumentPaths_MapsChildToParentFolder()
        {
            DocumentPathGenerator.GenerateDocumentPaths(BuildNodeTree(), @"E:\kb");

            Assert.Equal(
                Path.Combine(@"E:\kb", "根文档", "子文档1"),
                DocumentPathGenerator.GetDocumentPath("O2"));
            Assert.Equal(
                Path.Combine(@"E:\kb", "根文档", "子文档2"),
                DocumentPathGenerator.GetDocumentPath("O3"));
        }

        [Fact]
        public void GenerateDocumentPaths_MapsGrandchildRecursively()
        {
            DocumentPathGenerator.GenerateDocumentPaths(BuildNodeTree(), @"E:\kb");

            Assert.Equal(
                Path.Combine(@"E:\kb", "根文档", "子文档1", "孙文档"),
                DocumentPathGenerator.GetDocumentPath("O4"));
        }

        [Fact]
        public void GenerateDocumentPaths_NodeTokenLookup_MatchesObjToken()
        {
            DocumentPathGenerator.GenerateDocumentPaths(BuildNodeTree(), @"E:\kb");

            Assert.Equal(
                DocumentPathGenerator.GetDocumentPath("O4"),
                DocumentPathGenerator.GetDocumentPathByNodeToken("N4"));
        }

        [Fact]
        public void GenerateDocumentPaths_UnknownToken_ReturnsNull()
        {
            DocumentPathGenerator.GenerateDocumentPaths(BuildNodeTree(), @"E:\kb");

            Assert.Null(DocumentPathGenerator.GetDocumentPath("UNKNOWN"));
            Assert.Null(DocumentPathGenerator.GetDocumentPathByNodeToken("UNKNOWN"));
        }

        [Theory]
        [InlineData("a/b", "a-b")]
        [InlineData("a:b*c", "a-b-c")]
        [InlineData("a?d\"e<f>g|h", "a-d-e-f-g-h")]
        [InlineData("正常标题", "正常标题")]
        public void GenerateDocumentPaths_SanitizesIllegalFileNameChars(string title, string expected)
        {
            var documents = new List<WikiNodeItemDto>
            {
                new WikiNodeItemDto { NodeToken = "N1", ObjToken = "O1", Title = title, ParentNodeToken = null }
            };

            DocumentPathGenerator.GenerateDocumentPaths(documents, @"E:\kb");

            Assert.Equal(Path.Combine(@"E:\kb", expected), DocumentPathGenerator.GetDocumentPath("O1"));
        }

        [Fact]
        public void CloudDocPathGenerator_MapsParentAndChild()
        {
            var documents = new List<CloudDocDto>
            {
                new CloudDocDto { Token = "T1", Name = "父文件夹", ParentToken = null },
                new CloudDocDto { Token = "T2", Name = "子文件", ParentToken = "T1" },
            };

            CloudDocPathGenerator.GenerateDocumentPaths(documents, @"E:\cloud");

            Assert.Equal(Path.Combine(@"E:\cloud", "父文件夹"), CloudDocPathGenerator.GetDocumentPath("T1"));
            Assert.Equal(Path.Combine(@"E:\cloud", "父文件夹", "子文件"), CloudDocPathGenerator.GetDocumentPath("T2"));
        }

        [Fact]
        public void CloudDocPathGenerator_UnknownToken_ReturnsNull()
        {
            var documents = new List<CloudDocDto>
            {
                new CloudDocDto { Token = "T1", Name = "父文件夹", ParentToken = null },
            };

            CloudDocPathGenerator.GenerateDocumentPaths(documents, @"E:\cloud");

            Assert.Null(CloudDocPathGenerator.GetDocumentPath("UNKNOWN"));
        }
    }
}
