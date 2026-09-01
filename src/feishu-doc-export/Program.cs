
using DocSharp.Docx;
using feishu_doc_export.Dtos;
using feishu_doc_export.Helper;
using feishu_doc_export.HttpApi;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using WebApiClientCore;
using WebApiClientCore.Exceptions;

namespace feishu_doc_export
{
    internal class Program
    {
        static IFeiShuHttpApiCaller feiShuApiCaller;

        static async Task Main(string[] args)
        {
            // 设置控制台输出为 UTF-8，避免重定向到文件时中文乱码
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            GlobalConfig.Init(args);

            if (!Directory.Exists(GlobalConfig.ExportPath))
            {
                LogHelper.LogWarnExit($"指定的导出目录({GlobalConfig.ExportPath})不存在！！！");
            }

            IOC.Init();
            feiShuApiCaller = IOC.IoContainer.GetService<IFeiShuHttpApiCaller>();

            Stopwatch stopwatch = new();
            DateTime exportStartTimeUtc = DateTime.UtcNow;  // 兜底值，真正的在 wiki/cloudDoc 分支里 stopwatch.Start() 时覆写

            // ============ HTML 报告用的 4 个共享集合 ============
            // 真正的"硬失败"（objType 不支持 / HTTP 失败 / 导出任务失败 / 未知异常）
            List<ExportFailure> failures = new();
            // 文件名超长截断（SafeName 自动修复，本次已成功导出但算"调整"）
            List<TruncatedRecord> nameTruncated = new();
            // 去重尾号事件（SafeName 后重名，DeduplicateOnExist 触发，这里记录）
            List<DedupRecord> dedupRecords = new();
            object dedupLock = new();
            // —— 报告 KbName 显示用中文名（分支里请求 folderMeta / wikiSpaceInfo 时顺手写入，出作用域后仍可用）
            //    留空时 kbName 回退为 token，防止 meta 请求异常时报告崩。
            string friendlyFolderName   = string.Empty;
            string friendlyWikiSpaceName = string.Empty;
            FileHelper.Deduplicated += (orig, actual) =>
            {
                lock (dedupLock) dedupRecords.Add(new DedupRecord(orig, actual));
            };

            if (GlobalConfig.Type == "cloudDoc")
            {

                if (string.IsNullOrWhiteSpace(GlobalConfig.CloudDocFolder))
                {
                    LogHelper.LogWarnExit("导出对象为个人空间云文档时，请填写【folderToken】参数");
                }

                var folderMeta = await feiShuApiCaller.GetFolderMeta(GlobalConfig.CloudDocFolder);
                friendlyFolderName = folderMeta == null ? string.Empty : folderMeta.Name ?? string.Empty;

                Console.WriteLine($"正在加载个人空间云文档【{folderMeta.Name}】文件夹下的所有文档信息，请耐心等待...");

                stopwatch.Start();
                exportStartTimeUtc = DateTime.UtcNow;

                // 获取个人空间下的所有文档
                var selfDocs = await feiShuApiCaller.GetFolderAllCloudDoc(GlobalConfig.CloudDocFolder);

                // 文档路径映射字典
                CloudDocPathGenerator.GenerateDocumentPaths(selfDocs, GlobalConfig.ExportPath);

                // 记录导出的文档数量
                int count = 1;
                foreach (var item in selfDocs)
                {
                    if (item.Type == "folder")
                    {
                        continue;
                    }

                    var isSupport = GlobalConfig.GetFileExtension(item.Type, out string fileExt);

                    // 如果该文件类型不支持导出
                    if (!isSupport)
                    {
                        failures.Add(new ExportFailure(item.Name, FailureKind.TypeNotSupported, $"objType={item.Type} 不在 allow-list（docx/sheet/bitable/file/pdf）"));
                        LogHelper.LogWarn($"文档【{item.Name}】不支持导出，已忽略。如有需要请手动下载。");
                        continue;
                    }

                    // 文档为文件类型则直接下载文件
                    if (fileExt == "file")
                    {
                        try
                        {
                            Console.WriteLine($"正在导出文档————————{count++}.【{item.Name}】");

                            await DownLoadFile(item.Token);

                            continue;
                        }
                        catch (HttpRequestException ex)
                        {
                            failures.Add(new ExportFailure(item.Name, FailureKind.RequestError, ex.Message));
                            LogHelper.LogError($"下载文档【{item.Name}】时出现请求异常！！！异常信息：{ex.Message}，堆栈信息：{ex.StackTrace}");
                        }
                        catch (Exception ex)
                        {
                            failures.Add(new ExportFailure(item.Name, FailureKind.UnknownError, ex.Message));
                            LogHelper.LogError($"下载文档【{item.Name}】时出现未知异常，已忽略。请手动下载。异常信息：{ex.Message}，堆栈信息：{ex.StackTrace}");
                        }
                    }

                    // 用于展示的文件后缀名称
                    var showFileExt = fileExt;
                    // 用于指定文件下载类型
                    var fileExtension = fileExt;

                    // 只有当飞书文档类型为docx时才支持使用自定义文档保存类型
                    if (fileExt == "docx")
                    {
                        showFileExt = GlobalConfig.DocSaveType;

                        if (GlobalConfig.DocSaveType == "pdf")
                        {
                            fileExtension = GlobalConfig.DocSaveType;
                        }
                    }

                    // 文件名超长：根因已由 DocumentPathGenerator/CloudDocPathGenerator 的 SafeName() 修复，
                    // 不会再导致 PathTooLongException；此处仅打提示便于排障，不中断导出流程
                    if (item.Name.Length > FileHelper.SafeNameMaxLength)
                    {
                        var safe = FileHelper.SafeName(item.Name);
                        nameTruncated.Add(new TruncatedRecord(item.Name, safe, item.Name.Length, showFileExt));
                        Console.WriteLine($"文档【{item.Name}.{showFileExt}】文件名超长（{item.Name.Length} > {FileHelper.SafeNameMaxLength}），已自动截断为【{safe}.{showFileExt}】继续导出");
                    }

                    Console.WriteLine($"正在导出文档————————{count++}.【{item.Name}.{showFileExt}】");

                    try
                    {
                        await DownLoadDocument(fileExtension, item.Token, item.Type, displayName: item.Name, failures: failures);
                    }
                    catch (HttpRequestException ex)
                    {
                        failures.Add(new ExportFailure(item.Name, FailureKind.RequestError, ex.Message));
                        LogHelper.LogError($"下载文档【{item.Name}】时出现请求异常，异常信息：{ex.Message}，堆栈信息：{ex.StackTrace}");
                    }
                    catch (CustomException ex)
                    {
                        failures.Add(new ExportFailure(item.Name, FailureKind.JobError, ex.Message));
                        LogHelper.LogWarn($"文档【{item.Name}】{ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        failures.Add(new ExportFailure(item.Name, FailureKind.UnknownError, ex.Message));
                        LogHelper.LogError($"下载文档【{item.Name}】时出现未知异常，已忽略，请手动下载。异常信息：{ex.Message}，堆栈信息：{ex.StackTrace}");
                    }
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(GlobalConfig.WikiSpaceId))
                {
                    var wikiSpaces = await feiShuApiCaller.GetWikiSpaces();
                    var wikiSpaceDict = wikiSpaces.Items
                        .Select((x, i) => new { Index = i + 1, WikiSpace = x })
                        .ToDictionary(x => x.Index, x => x.WikiSpace);

                    if (wikiSpaceDict.Any())
                    {
                        Console.WriteLine($"以下是所有支持导出的知识库：");

                        foreach (var item in wikiSpaceDict)
                        {
                            Console.WriteLine($"【{item.Key}.】{item.Value.Name}");
                        }
                        Console.WriteLine("请选择知识库（输入知识库的序号）：");
                        var index = int.Parse(Console.ReadLine());
                        GlobalConfig.WikiSpaceId = wikiSpaceDict[index].Spaceid;
                    }
                    else
                    {
                        LogHelper.LogWarnExit("没有可支持导出的知识库！！！");
                    }
                }

                var wikiSpaceInfo = await feiShuApiCaller.GetWikiSpaceInfo(GlobalConfig.WikiSpaceId);
                friendlyWikiSpaceName = (wikiSpaceInfo == null || wikiSpaceInfo.Space == null)
                    ? string.Empty
                    : wikiSpaceInfo.Space.Name ?? string.Empty;

                Console.WriteLine($"正在加载知识库【{wikiSpaceInfo.Space.Name}】的所有文档信息，请耐心等待...");

                stopwatch.Start();
                exportStartTimeUtc = DateTime.UtcNow;

                // 获取知识库下的所有文档
                var wikiNodes = await feiShuApiCaller.GetAllWikiNode(GlobalConfig.WikiSpaceId);

                // 文档路径映射字典
                DocumentPathGenerator.GenerateDocumentPaths(wikiNodes, GlobalConfig.ExportPath);

                // 记录导出的文档数量
                int count = 1;
                foreach (var item in wikiNodes)
                {

                    var isSupport = GlobalConfig.GetFileExtension(item.ObjType, out string fileExt);

                    // 如果该文件类型不支持导出
                    if (!isSupport)
                    {
                        failures.Add(new ExportFailure(item.Title, FailureKind.TypeNotSupported, $"objType={item.ObjType} 不在 allow-list（docx/sheet/bitable/file/pdf）"));
                        LogHelper.LogWarn($"文档【{item.Title}】不支持导出，已忽略。如有需要请手动下载。");
                        continue;
                    }

                    // 文档为文件类型则直接下载文件
                    if (fileExt == "file")
                    {
                        try
                        {
                            Console.WriteLine($"正在导出文档————————{count++}.【{item.Title}】");

                            await DownLoadFile(item.ObjToken);

                            continue;
                        }
                        catch (HttpRequestException ex)
                        {
                            failures.Add(new ExportFailure(item.Title, FailureKind.RequestError, ex.Message));
                            LogHelper.LogError($"下载文档【{item.Title}】时出现请求异常！！！异常信息：{ex.Message}，堆栈信息：{ex.StackTrace}");
                        }
                        catch (Exception ex)
                        {
                            failures.Add(new ExportFailure(item.Title, FailureKind.UnknownError, ex.Message));
                            LogHelper.LogWarn($"下载文档【{item.Title}】时出现未知异常，已忽略。请手动下载。异常信息：{ex.Message}");
                        }
                    }

                    // 用于展示的文件后缀名称
                    var showFileExt = fileExt;
                    // 用于指定文件下载类型
                    var fileExtension = fileExt;

                    // 只有当飞书文档类型为docx时才支持使用自定义文档保存类型
                    if (fileExt == "docx")
                    {
                        showFileExt = GlobalConfig.DocSaveType;

                        if (GlobalConfig.DocSaveType == "pdf")
                        {
                            fileExtension = GlobalConfig.DocSaveType;
                        }
                    }

                    // 文件名超长：根因已由 DocumentPathGenerator/CloudDocPathGenerator 的 SafeName() 修复，
                    // 不会再导致 PathTooLongException；此处仅打提示便于排障，不中断导出流程
                    if (item.Title.Length > FileHelper.SafeNameMaxLength)
                    {
                        var safe = FileHelper.SafeName(item.Title);
                        nameTruncated.Add(new TruncatedRecord(item.Title, safe, item.Title.Length, showFileExt));
                        Console.WriteLine($"文档【{item.Title}.{showFileExt}】文件名超长（{item.Title.Length} > {FileHelper.SafeNameMaxLength}），已自动截断为【{safe}.{showFileExt}】继续导出");
                    }

                    Console.WriteLine($"正在导出文档————————{count++}.【{item.Title}.{showFileExt}】");

                    try
                    {
                        await DownLoadDocument(fileExtension, item.ObjToken, item.ObjType, displayName: item.Title, failures: failures);
                    }
                    catch (HttpRequestException ex)
                    {
                        failures.Add(new ExportFailure(item.Title, FailureKind.RequestError, ex.Message));
                        LogHelper.LogError($"下载文档【{item.Title}】时出现请求异常！！！异常信息：{ex.Message}，堆栈信息：{ex.StackTrace}");
                    }
                    catch (CustomException ex)
                    {
                        failures.Add(new ExportFailure(item.Title, FailureKind.JobError, ex.Message));
                        LogHelper.LogWarn($"文档【{item.Title}】{ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        failures.Add(new ExportFailure(item.Title, FailureKind.UnknownError, ex.Message));
                        LogHelper.LogError($"下载文档【{item.Title}】时出现未知异常，已忽略，请手动下载。异常信息：{ex.Message}，堆栈信息：{ex.StackTrace}");
                    }
                }
            }

            

            stopwatch.Stop();
            DateTime exportEndTimeUtc = DateTime.UtcNow;
            TimeSpan elapsedTime = stopwatch.Elapsed;
            // 输出执行时间（以秒为单位）
            double seconds = elapsedTime.TotalSeconds;

            // ============ 控制台：导出结束清单（保持原格式兼容，打印 FileName） ============
            Console.WriteLine("—————————————————————————————文档已全部导出—————————————————————————————");
            if (failures.Any())
            {
                Console.WriteLine("以下是所有无法导出的文档（包含不支持导出、导出异常的文档）");
                for (int i = 0; i < failures.Count; i++)
                {
                    Console.WriteLine($"{i + 1}.【{failures[i].FileName}】  ({failures[i].KindLabel}: {failures[i].Detail})");
                }
            }

            // ============ 扫描产物：总数 / MD 条目 / 大小 ============
            string exportDir = GlobalConfig.ExportPath;
            List<MdDocEntry> mdEntries = new();
            long totalBytes = 0;
            int totalFiles = 0;
            int totalImages = 0;
            HashSet<string> truncatedStems = new(StringComparer.OrdinalIgnoreCase);
            foreach (var t in nameTruncated)
            {
                var key = t.SavedAs.Trim();
                if (key.Length > 0) truncatedStems.Add(key);
            }
            if (Directory.Exists(exportDir))
            {
                string exportDirFull = Path.GetFullPath(exportDir);
                int rootLen = exportDirFull.Length + (exportDirFull.EndsWith(Path.DirectorySeparatorChar.ToString()) ? 0 : 1);
                foreach (var fullPath in Directory.EnumerateFiles(exportDirFull, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        FileInfo fi = new(fullPath);
                        totalBytes += fi.Length;
                        totalFiles++;

                        string ext = fi.Extension.ToLowerInvariant();
                        if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg") totalImages++;

                        if (ext == ".md")
                        {
                            string rel       = fullPath.Substring(rootLen);
                            string folder    = Path.GetDirectoryName(rel);
                            string stemOnly  = Path.GetFileNameWithoutExtension(fi.Name);
                            bool adjusted    = truncatedStems.Contains(stemOnly);
                            // 图片目录下的 md 概率极低，不过保险起见过滤 images/ 目录
                            if (!string.IsNullOrEmpty(folder) && folder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(s => s.Equals("images", StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }
                            mdEntries.Add(new MdDocEntry(rel, folder ?? string.Empty, fi.Length, adjusted));
                        }
                    }
                    catch
                    {
                        // 单个文件信息读不到，跳过，不中断报告
                    }
                }
            }

            // ============ 知识库名 / 模式 ============
            // 优先用"文件夹/知识库中文名"（上面请求 folderMeta / wikiSpaceInfo 时已经写入 friendlyXXX），
            // 中文名空时才回退到 token（CloudDocFolder / WikiSpaceId），最后兜底为"个人空间云文档"/"知识库"。
            string kbName, envType;
            if (GlobalConfig.Type == "cloudDoc")
            {
                if (!string.IsNullOrWhiteSpace(friendlyFolderName))
                    kbName = $"云文档 {friendlyFolderName}";
                else if (!string.IsNullOrWhiteSpace(GlobalConfig.CloudDocFolder))
                    kbName = $"云文档 {GlobalConfig.CloudDocFolder}";
                else
                    kbName = "个人空间云文档";
                envType = "个人空间云文档";
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(friendlyWikiSpaceName))
                    kbName = $"知识库 {friendlyWikiSpaceName}";
                else if (!string.IsNullOrWhiteSpace(GlobalConfig.WikiSpaceId))
                    kbName = $"知识库 {GlobalConfig.WikiSpaceId}";
                else
                    kbName = "知识库";
                envType = "知识库（Wiki）";
            }

            var model = new ReportModel
            {
                StartTimeUtc       = exportStartTimeUtc,
                EndTimeUtc         = exportEndTimeUtc,
                KbName             = kbName,
                DocSaveType        = GlobalConfig.DocSaveType ?? "md",
                EnvType            = envType,
                Failures           = failures.AsReadOnly(),
                Truncations        = nameTruncated.AsReadOnly(),
                Dedups             = dedupRecords.AsReadOnly(),
                MdEntries          = mdEntries.AsReadOnly(),
                TotalExportedBytes = totalBytes,
                TotalExportedFiles = totalFiles,
                TotalImageFiles    = totalImages,
            };

            string reportPath = ExportReportGenerator.Generate(model, exportDir);
            Console.WriteLine($"✅ 导出报告已生成：{reportPath}");
            string reportSize = new FileInfo(reportPath).Length >= 1024
                ? $"{new FileInfo(reportPath).Length / 1024d:0.1} KB"
                : $"{new FileInfo(reportPath).Length} B";
            Console.WriteLine($"   报告大小：{reportSize}  （离线可直接双击打开，无需联网）");

            if (GlobalConfig.Quit)
            {
                Console.WriteLine($"程序执行结束，总耗时{seconds:0.###}（秒）。已自动退出程序！");
                return;
            }

            Console.WriteLine($"程序执行结束，总耗时{seconds:0.###}（秒）。请按任意键退出！");
            Console.ReadKey();
        }

        /// <summary>
        /// 下载文档到本地
        /// </summary>
        /// <param name="fileExtension">文档导出的文件类型（docx）</param>
        /// <param name="objToken"></param>
        /// <param name="type"></param>
        /// <param name="displayName">展示名（objToken 对应的标题/文件名，仅用于 JobError 失败记录）</param>
        /// <param name="failures">失败记录列表：当飞书 JobErrorMsg != "success" 时在此追加一条 <see cref="FailureKind.JobError"/>。</param>
        /// <returns></returns>
        static async Task DownLoadDocument(string fileExtension, string objToken, string type, string displayName, List<ExportFailure> failures)
        {
            if (failures == null) throw new ArgumentNullException(nameof(failures));

            var exportTaskDto = await feiShuApiCaller.CreateExportTask(fileExtension, objToken, type);

            if (exportTaskDto == null)
            {
                failures.Add(new ExportFailure(displayName, FailureKind.JobError, "CreateExportTask 返回 null（飞书未生成导出任务）"));
                return;
            }

            int maxRetryCount = 10; // 最大重试次数
            var exportTaskResult = new ExportTaskResultDto();
            for (int i = 0; i < maxRetryCount; i++)
            {
                try
                {
                    exportTaskResult = await feiShuApiCaller.QueryExportTaskResult(exportTaskDto.Ticket, objToken);
                    break;
                }
                catch (HttpRequestException) when (i < maxRetryCount - 1)
                {
                    await Task.Delay(1000);
                }
            }
            
            var taskInfo = exportTaskResult.Result;

            if (taskInfo.JobErrorMsg == "success")
            {
                var bytes = await feiShuApiCaller.DownLoad(taskInfo.FileToken);

                string filePath;
                if (GlobalConfig.Type == "cloudDoc")
                {
                    filePath = CloudDocPathGenerator.GetDocumentPath(objToken) + "." + fileExtension;
                }
                else
                {
                    filePath = DocumentPathGenerator.GetDocumentPath(objToken) + "." + fileExtension;
                }

                if (fileExtension == "docx" && GlobalConfig.DocSaveType == "md")
                {
                    await SaveToMarkdownFile(bytes, filePath);
                    return;
                }

                _ = await filePath.Save(bytes);
            }
            else
            {
                // 飞书导出任务未成功：打印 JobErrorMsg + 追加到 failures
                string detail = $"JobStatus={taskInfo.JobStatus}, JobErrorMsg={taskInfo.JobErrorMsg}, FileToken={taskInfo.FileToken}, objToken={objToken}";
                failures.Add(new ExportFailure(displayName, FailureKind.JobError, detail));
                LogHelper.LogError($"导出任务未成功，{detail}");
            }
        }

        /// <summary>
        /// 下载文件到本地
        /// </summary>
        /// <param name="objToken"></param>
        /// <returns></returns>
        static async Task DownLoadFile(string objToken)
        {
            var bytes = await feiShuApiCaller.DownLoadFile(objToken);

            string filePath = GlobalConfig.Type == "cloudDoc" ? CloudDocPathGenerator.GetDocumentPath(objToken) : DocumentPathGenerator.GetDocumentPath(objToken);

            await filePath.Save(bytes);
        }

        /// <summary>
        /// 保存为Markdown文件（使用 DocSharp.Docx 转换，MIT 无水印）
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="fileSavePath"></param>
        static async Task SaveToMarkdownFile(byte[] bytes, string fileSavePath)
        {
            // 文件保存的文件夹路径
            var saveDirPath = Path.GetDirectoryName(fileSavePath);
            // 图片保存目录（<md同级目录>/images）
            var imagesFolder = Path.Combine(saveDirPath, "images");
            Directory.CreateDirectory(imagesFolder);

            // 重构文件名
            var fileName = Path.GetFileNameWithoutExtension(fileSavePath) + ".md";
            // Markdown 文件最终保存路径
            var mdFileSavePath = Path.Combine(saveDirPath, fileName);

            using MemoryStream stream = new(bytes);
            var converter = new DocxToMarkdownConverter
            {
                ImagesOutputFolder = imagesFolder,
                ImagesBaseUriOverride = "./images"  // md 文件里用 ./images/xxx.png 相对引用
            };
            converter.Convert(stream, mdFileSavePath);

            // 处理 Markdown 文件，替换图片和文档的引用路径为相对路径
            var markdownContent = await File.ReadAllTextAsync(mdFileSavePath);
            var replacedContent = markdownContent.ReplaceImagePath(mdFileSavePath).ReplaceDocRefPath(mdFileSavePath).ReplaceCodeToMdFormat();
            await File.WriteAllTextAsync(mdFileSavePath, replacedContent);
        }
        
    }
}