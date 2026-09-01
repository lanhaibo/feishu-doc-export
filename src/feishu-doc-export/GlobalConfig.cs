using Aspose.Words;
using feishu_doc_export.Helper;
using System.Text.Json;

namespace feishu_doc_export
{
    public static class GlobalConfig
    {
        public static string AppId { get; set; } 

        public static string AppSecret { get; set; } 

        public static string ExportPath { get; set; } 

        public static string ApiEndpoint { get; set; } 

        public static string WikiSpaceId { get; set; }

        public static string CloudDocFolder { get; set; } 

        public static bool Quit { get; set; }

        public static string Type { get; set; } = "wiki";

        private static string _docSaveType = "docx";

        public static string DocSaveType { 
            get { return _docSaveType; }
            set
            {
                var options = new string[] { "pdf", "docx", "md" };

                _docSaveType = options.Contains(value) ? value : "docx";
            } 
        }

        /// <summary>
        /// 飞书支持导出的文件类型和导出格式
        /// </summary>
        static readonly Dictionary<string, string> fileExtensionDict = new()
        {
            {"doc","docx" },
            {"docx","docx" },
            {"sheet","xlsx" },
            {"bitable","xlsx" },
            {"file","file" },
        };

        /// <summary>
        /// 获取飞书支持导出的文件格式
        /// </summary>
        /// <param name="objType"></param>
        /// <param name="fileExt"></param>
        /// <returns></returns>
        public static bool GetFileExtension(string objType, out string fileExt)
        {
            return fileExtensionDict.TryGetValue(objType, out fileExt);
        }

        private static void InitAsposeLicense()
        {
            // 候选 License 文件路径：兼容 Windows（程序目录）与 Linux（/private/tmp）原约定
            string[] candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "License.lic"),
                Path.Combine(Directory.GetCurrentDirectory(), "License.lic"),
                "/private/tmp/License.lic"
            };

            string licenseFile = candidates.FirstOrDefault(File.Exists);
            if (licenseFile == null)
            {
                LogHelper.LogWarn("未找到 Aspose License 文件（License.lic），将以评估模式运行。");
                return;
            }

            License license = new();
            license.SetLicense(licenseFile);
        }

        /// <summary>
        /// 初始化全局配置信息
        /// 配置来源优先级：命令行参数 > credentials.local.json（敏感项）> config.json > 交互式输入
        /// </summary>
        /// <param name="args"></param>
        public static void Init(string[] args)
        {
            var fileConfig = LoadJsonConfig("config.json");
            var credConfig = LoadJsonConfig("credentials.local.json");

            if (args.Length > 0)
            {
                InitFromArgs(args, fileConfig, credConfig);
            }
            else
            {
                ApplyFileConfig(fileConfig);
                ApplyCredentialConfig(credConfig);

                bool fromFile = !string.IsNullOrWhiteSpace(AppId)
                    && !string.IsNullOrWhiteSpace(AppSecret)
                    && !string.IsNullOrWhiteSpace(ExportPath);

                if (!fromFile)
                {
                    InitInteractive();
                }
            }

            InitAsposeLicense();
        }

        /// <summary>
        /// 从命令行参数 + 配置文件初始化（有参模式）
        /// </summary>
        private static void InitFromArgs(string[] args, FileConfig fileConfig, FileConfig credConfig)
        {
            // 1. 文件配置作为基底（config.json 填充 → credentials.local.json 覆盖敏感项）
            ApplyFileConfig(fileConfig);
            ApplyCredentialConfig(credConfig);

            // 2. 命令行参数优先级最高（非空才覆盖）
            AppId = FirstNonEmpty(GetCommandLineArg(args, "--appId=", true), AppId);
            AppSecret = FirstNonEmpty(GetCommandLineArg(args, "--appSecret=", true), AppSecret);
            ExportPath = FirstNonEmpty(GetCommandLineArg(args, "--exportPath=", true), ExportPath);
            Type = FirstNonEmpty(GetCommandLineArg(args, "--type=", true), Type);
            CloudDocFolder = FirstNonEmpty(GetCommandLineArg(args, "--folderToken=", true), CloudDocFolder);
            WikiSpaceId = FirstNonEmpty(GetCommandLineArg(args, "--spaceId=", true), WikiSpaceId);
            DocSaveType = FirstNonEmpty(GetCommandLineArg(args, "--saveType=", true), DocSaveType);
            ApiEndpoint = FirstNonEmpty(
                GetCommandLineArg(args, "--apiEndpoint=", true),
                ApiEndpoint,
                FeiShuConsts.DefaultOpenApiEndPoint);
            Quit = Quit || args.Contains("--quit");

            // 3. 必填校验（可由配置文件满足）
            if (string.IsNullOrWhiteSpace(AppId)
                || string.IsNullOrWhiteSpace(AppSecret)
                || string.IsNullOrWhiteSpace(ExportPath))
            {
                Console.WriteLine("appId / appSecret / exportPath 为必填项，可通过以下任一方式提供：");
                Console.WriteLine("  1. 命令行参数：--appId / --appSecret / --exportPath 等");
                Console.WriteLine("  2. 程序目录或工作目录下的 credentials.local.json / config.json");
                Environment.Exit(0);
            }
        }

        /// <summary>
        /// 交互式手动输入所有必填项（无参且配置文件不完整时）
        /// </summary>
        private static void InitInteractive()
        {
            Console.WriteLine("未在程序目录找到完整配置（config.json / credentials.local.json），进入手动输入模式：");

            Console.WriteLine("请输入飞书自建应用的AppId：");
            AppId = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(AppId))
            {
                LogHelper.LogWarnExit("AppId是必填参数");
            }

            Console.WriteLine("请输入飞书自建应用的AppSecret：");
            AppSecret = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(AppSecret))
            {
                LogHelper.LogWarnExit("AppSecret是必填参数");
            }

            Console.WriteLine("请输入文档导出的文件类型（可选值：docx、md、pdf，为空或其他非可选值则默认为docx）：");
            DocSaveType = Console.ReadLine();

            Console.WriteLine("请选择云文档类型（可选值：wiki、cloudDoc）");
            Type = Console.ReadLine();
            if (Type == "cloudDoc")
            {
                Console.WriteLine("请输入云文档文件夹Token（必填项！）");
                CloudDocFolder = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(CloudDocFolder))
                {
                    LogHelper.LogWarnExit("文件夹Token是必填参数");
                }
            }
            else
            {
                Console.WriteLine("请输入要导出的知识库Id（为空代表从所有知识库中选择）：");
                WikiSpaceId = Console.ReadLine();
            }

            Console.WriteLine("请输入文档导出的目录位置：");
            ExportPath = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(ExportPath))
            {
                LogHelper.LogWarnExit("文档导出的目录是必填参数");
            }
        }

        /// <summary>
        /// 配置文件模型（字段与 config.json / credentials.local.json 的 camelCase 键对应）
        /// </summary>
        internal sealed class FileConfig
        {
            public string AppId { get; set; }
            public string AppSecret { get; set; }
            public string SpaceId { get; set; }
            public string Type { get; set; }
            public string SaveType { get; set; }
            public string FolderToken { get; set; }
            public string ExportPath { get; set; }
            public string ApiEndpoint { get; set; }
            public bool Quit { get; set; }
        }

        /// <summary>
        /// 从程序目录或工作目录加载 JSON 配置文件，不存在返回 null，解析失败忽略并记录日志
        /// </summary>
        private static FileConfig LoadJsonConfig(string fileName)
        {
            string[] candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, fileName),
                Path.Combine(Directory.GetCurrentDirectory(), fileName)
            };

            string path = candidates.FirstOrDefault(File.Exists);
            if (path == null)
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<FileConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                });
                LogHelper.LogInfo($"已加载配置文件：{path}");
                return config;
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"配置文件 {path} 解析失败，已忽略：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 应用 config.json 配置（非空字段才覆盖全局属性）
        /// </summary>
        internal static void ApplyFileConfig(FileConfig config)
        {
            if (config == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(config.AppId)) AppId = config.AppId;
            if (!string.IsNullOrWhiteSpace(config.AppSecret)) AppSecret = config.AppSecret;
            if (!string.IsNullOrWhiteSpace(config.SpaceId)) WikiSpaceId = config.SpaceId;
            if (!string.IsNullOrWhiteSpace(config.Type)) Type = config.Type;
            if (!string.IsNullOrWhiteSpace(config.SaveType)) DocSaveType = config.SaveType;
            if (!string.IsNullOrWhiteSpace(config.FolderToken)) CloudDocFolder = config.FolderToken;
            if (!string.IsNullOrWhiteSpace(config.ExportPath)) ExportPath = config.ExportPath;
            if (!string.IsNullOrWhiteSpace(config.ApiEndpoint)) ApiEndpoint = config.ApiEndpoint;
            if (config.Quit) Quit = true;
        }

        /// <summary>
        /// 应用 credentials.local.json 配置（仅覆盖 appId/appSecret/spaceId 敏感项，优先级高于 config.json）
        /// </summary>
        internal static void ApplyCredentialConfig(FileConfig config)
        {
            if (config == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(config.AppId)) AppId = config.AppId;
            if (!string.IsNullOrWhiteSpace(config.AppSecret)) AppSecret = config.AppSecret;
            if (!string.IsNullOrWhiteSpace(config.SpaceId)) WikiSpaceId = config.SpaceId;
        }

        /// <summary>
        /// 返回第一个非空字符串，全空返回 null
        /// </summary>
        internal static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        /// <summary>
        /// 获取命令行参数值
        /// </summary>
        /// <param name="args"></param>
        /// <param name="parameterName"></param>
        /// <returns></returns>
        public static string GetCommandLineArg(string[] args, string parameterName, bool canNull = false)
        {
            // 参数值
            string paraValue = string.Empty;
            // 是否有匹配的参数
            bool found = false;
            foreach (string arg in args)
            {
                if (arg.StartsWith(parameterName))
                {
                    paraValue = arg[parameterName.Length..];
                    found = true;
                }
            }

            if (!canNull)
            {
                if (!found)
                {
                    Console.WriteLine($"没有找到参数：{parameterName}");
                    Console.WriteLine("请填写以下所有参数：");
                    Console.WriteLine("  --appId           飞书自建应用的AppId.【必填项】");
                    Console.WriteLine("  --appSecret       飞书自建应用的AppSecret.【必填项】");
                    Console.WriteLine("  --exportPath      文档导出的目录位置.【必填项】");
                    Console.WriteLine("  --type            知识库（wiki）或个人空间云文档（cloudDoc）（可选值：cloudDoc、wiki，为空则默认为wiki）");
                    Console.WriteLine("  --saveType        文档导出的文件类型（可选值：docx、md、pdf，为空或其他非可选值则默认为docx）.");
                    Console.WriteLine("  --folderToken     当type为个人空间云文档时，该项必填");
                    Console.WriteLine("  --spaceId         飞书导出的知识库Id.");
                    Environment.Exit(0);
                }

                // 参数值为空
                if (string.IsNullOrWhiteSpace(paraValue))
                {
                    Console.WriteLine($"参数{parameterName}不能为空");
                    Environment.Exit(0);
                }
            }

            return paraValue;
        }
    }
}