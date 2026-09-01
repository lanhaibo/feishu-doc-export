using Xunit;
using Xunit.Abstractions;

namespace feishu_doc_export.Tests
{
    /// <summary>
    /// 配置合并逻辑测试（FirstNonEmpty / GetCommandLineArg / ApplyFileConfig / ApplyCredentialConfig）
    /// 注意：GlobalConfig 为静态状态，本测试类内的用例必须串行执行并重置状态；
    /// xUnit 同一测试类内默认串行，满足要求；其他测试类不触碰 GlobalConfig，无并行冲突。
    /// </summary>
    public class GlobalConfigMergeTests
    {
        private readonly ITestOutputHelper _output;

        public GlobalConfigMergeTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// 重置 GlobalConfig 静态状态，避免用例间污染
        /// </summary>
        private static void ResetGlobalConfig()
        {
            GlobalConfig.AppId = null;
            GlobalConfig.AppSecret = null;
            GlobalConfig.WikiSpaceId = null;
            GlobalConfig.Type = "wiki";
            GlobalConfig.CloudDocFolder = null;
            GlobalConfig.DocSaveType = "docx";
            GlobalConfig.ExportPath = null;
            GlobalConfig.ApiEndpoint = null;
            GlobalConfig.Quit = false;
        }

        #region FirstNonEmpty

        [Fact]
        public void FirstNonEmpty_AllEmpty_ReturnsNull()
        {
            Assert.Null(GlobalConfig.FirstNonEmpty(null, "", "  "));
        }

        [Fact]
        public void FirstNonEmpty_ReturnsFirstNonEmptyValue()
        {
            Assert.Equal("a", GlobalConfig.FirstNonEmpty(null, "", "a", "b"));
        }

        [Fact]
        public void FirstNonEmpty_WhitespaceOnly_IsTreatedAsEmpty()
        {
            Assert.Equal("x", GlobalConfig.FirstNonEmpty("   ", "x"));
        }

        #endregion

        #region GetCommandLineArg

        [Fact]
        public void GetCommandLineArg_MatchingPrefix_ReturnsValue()
        {
            var args = new[] { "--appId=cli_123", "--saveType=md" };

            Assert.Equal("cli_123", GlobalConfig.GetCommandLineArg(args, "--appId=", true));
            Assert.Equal("md", GlobalConfig.GetCommandLineArg(args, "--saveType=", true));
        }

        [Fact]
        public void GetCommandLineArg_NotFound_CanNull_ReturnsEmpty()
        {
            var args = new[] { "--appId=cli_123" };

            Assert.Equal(string.Empty, GlobalConfig.GetCommandLineArg(args, "--spaceId=", true));
        }

        [Fact]
        public void GetCommandLineArg_DuplicatePrefix_TakesLastOccurrence()
        {
            var args = new[] { "--appId=first", "--appId=second" };

            Assert.Equal("second", GlobalConfig.GetCommandLineArg(args, "--appId=", true));
        }

        [Fact]
        public void GetCommandLineArg_ValueContainingEquals_PreservesRest()
        {
            var args = new[] { "--exportPath=E:\\doc=a=b" };

            Assert.Equal("E:\\doc=a=b", GlobalConfig.GetCommandLineArg(args, "--exportPath=", true));
        }

        [Fact]
        public void GetCommandLineArg_PartialPrefixMatch_DoesNotMatchOtherParams()
        {
            // --appSecretExtra 不应命中 --appSecret 前缀（前缀包含等号）
            var args = new[] { "--appSecretExtra=xyz" };

            Assert.Equal(string.Empty, GlobalConfig.GetCommandLineArg(args, "--appSecret=", true));
        }

        #endregion

        #region ApplyFileConfig / ApplyCredentialConfig

        [Fact]
        public void ApplyFileConfig_FillsAllProvidedFields()
        {
            ResetGlobalConfig();

            var config = new GlobalConfig.FileConfig
            {
                AppId = "file_app",
                AppSecret = "file_secret",
                SpaceId = "file_space",
                Type = "wiki",
                SaveType = "md",
                ExportPath = @"E:\out",
                Quit = true
            };

            GlobalConfig.ApplyFileConfig(config);

            Assert.Equal("file_app", GlobalConfig.AppId);
            Assert.Equal("file_secret", GlobalConfig.AppSecret);
            Assert.Equal("file_space", GlobalConfig.WikiSpaceId);
            Assert.Equal("md", GlobalConfig.DocSaveType);
            Assert.Equal(@"E:\out", GlobalConfig.ExportPath);
            Assert.True(GlobalConfig.Quit);
        }

        [Fact]
        public void ApplyFileConfig_EmptyFields_DoNotOverwriteExistingValues()
        {
            ResetGlobalConfig();
            GlobalConfig.AppId = "preset_app";
            GlobalConfig.ExportPath = @"E:\preset";

            var config = new GlobalConfig.FileConfig
            {
                AppId = "",
                ExportPath = null,
                SaveType = "pdf"
            };

            GlobalConfig.ApplyFileConfig(config);

            Assert.Equal("preset_app", GlobalConfig.AppId);
            Assert.Equal(@"E:\preset", GlobalConfig.ExportPath);
            Assert.Equal("pdf", GlobalConfig.DocSaveType);
        }

        [Fact]
        public void ApplyCredentialConfig_OverridesOnlySensitiveFields()
        {
            ResetGlobalConfig();

            // 先应用 config.json 基底
            GlobalConfig.ApplyFileConfig(new GlobalConfig.FileConfig
            {
                AppId = "base_app",
                AppSecret = "base_secret",
                SpaceId = "base_space",
                SaveType = "md",
                ExportPath = @"E:\out"
            });

            // credentials.local.json 仅覆盖敏感三项
            GlobalConfig.ApplyCredentialConfig(new GlobalConfig.FileConfig
            {
                AppId = "cred_app",
                AppSecret = "cred_secret",
                SpaceId = "cred_space",
                SaveType = "pdf",
                ExportPath = @"E:\should_not_apply"
            });

            Assert.Equal("cred_app", GlobalConfig.AppId);
            Assert.Equal("cred_secret", GlobalConfig.AppSecret);
            Assert.Equal("cred_space", GlobalConfig.WikiSpaceId);
            // 非敏感项不被 credentials 覆盖
            Assert.Equal("md", GlobalConfig.DocSaveType);
            Assert.Equal(@"E:\out", GlobalConfig.ExportPath);
        }

        [Fact]
        public void ApplyFileConfig_NullConfig_DoesNothing()
        {
            ResetGlobalConfig();
            GlobalConfig.AppId = "keep";

            GlobalConfig.ApplyFileConfig(null);
            GlobalConfig.ApplyCredentialConfig(null);

            Assert.Equal("keep", GlobalConfig.AppId);
        }

        #endregion

        #region 三源合并优先级（模拟 Init 中的合并链）

        [Fact]
        public void MergePriority_CommandLine_OverCredential_OverFile()
        {
            ResetGlobalConfig();

            // 1. config.json 基底
            GlobalConfig.ApplyFileConfig(new GlobalConfig.FileConfig
            {
                AppId = "file_app",
                AppSecret = "file_secret",
                SpaceId = "file_space",
                SaveType = "docx"
            });

            // 2. credentials.local.json 覆盖敏感项
            GlobalConfig.ApplyCredentialConfig(new GlobalConfig.FileConfig
            {
                AppSecret = "cred_secret",
                SpaceId = "cred_space"
            });

            // 3. 命令行覆盖（非空才生效）
            var args = new[] { "--saveType=md" };
            GlobalConfig.AppId = GlobalConfig.FirstNonEmpty(
                GlobalConfig.GetCommandLineArg(args, "--appId=", true),
                GlobalConfig.AppId);
            GlobalConfig.DocSaveType = GlobalConfig.FirstNonEmpty(
                GlobalConfig.GetCommandLineArg(args, "--saveType=", true),
                GlobalConfig.DocSaveType);

            // 断言优先级：命令行 > credentials > config.json
            Assert.Equal("file_app", GlobalConfig.AppId);              // 仅 config.json 提供
            Assert.Equal("cred_secret", GlobalConfig.AppSecret);       // credentials 覆盖
            Assert.Equal("cred_space", GlobalConfig.WikiSpaceId);      // credentials 覆盖
            Assert.Equal("md", GlobalConfig.DocSaveType);              // 命令行覆盖
        }

        [Fact]
        public void MergePriority_EmptyCommandLineValue_KeepsFileValue()
        {
            ResetGlobalConfig();
            GlobalConfig.ApplyFileConfig(new GlobalConfig.FileConfig { SpaceId = "file_space" });

            var args = new[] { "--quit" }; // 无 --spaceId=
            GlobalConfig.WikiSpaceId = GlobalConfig.FirstNonEmpty(
                GlobalConfig.GetCommandLineArg(args, "--spaceId=", true),
                GlobalConfig.WikiSpaceId);

            Assert.Equal("file_space", GlobalConfig.WikiSpaceId);
        }

        #endregion
    }
}
