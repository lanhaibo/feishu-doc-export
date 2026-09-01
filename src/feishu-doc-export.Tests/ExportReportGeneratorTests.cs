using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace feishu_doc_export.Tests
{
    /// <summary>
    /// <see cref="ExportReportGenerator"/> 单测：
    /// 1. 零失败+零调整场景：渲染不抛、表头/8 大区块锚点齐全、计数器为 0 展示正确。
    /// 2. 含失败/调整场景：表格行数匹配、失败 Detail 被转义写进 HTML、调整子表有 SavedAs。
    /// 3. 徽标 3 类 class 名必须在 HTML 中显式出现（status-ok / status-failed / status-adjusted）。
    /// </summary>
    public class ExportReportGeneratorTests : IDisposable
    {
        private readonly string _tempDir;

        public ExportReportGeneratorTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "feishu-doc-export-report-tests", Guid.NewGuid().ToString("N"));
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
         * 场景 1：空模型安全
         * ============================================================ */
        [Fact]
        public void Generate_EmptyModel_DoesNotThrow_And_Contains_All_Section_Anchors()
        {
            var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            var end   = start.AddMinutes(1);
            var model = new ReportModel
            {
                StartTimeUtc       = start,
                EndTimeUtc         = end,
                KbName             = "空知识库",
                DocSaveType        = "md",
                EnvType            = "知识库（Wiki）",
                Failures           = Array.Empty<ExportFailure>(),
                Truncations        = Array.Empty<TruncatedRecord>(),
                Dedups             = Array.Empty<DedupRecord>(),
                MdEntries          = Array.Empty<MdDocEntry>(),
                TotalExportedBytes = 0L,
                TotalExportedFiles = 0,
                TotalImageFiles    = 0,
            };

            // Act
            var htmlPath = ExportReportGenerator.Generate(model, _tempDir, "empty-report.html");

            // Assert：落盘 + 可读 + 8 大区块锚点齐全
            Assert.True(File.Exists(htmlPath));
            var html = File.ReadAllText(htmlPath);
            Assert.True(html.Length > 0);
            Assert.Contains("飞书知识库导出报告", html);            // 头部 Banner
            Assert.Contains("文档总数", html);                    // KPI
            Assert.Contains("文档处理状态分布", html);             // 状态条
            Assert.Contains("失败 &amp; 自动调整明细", html);     // 失败/调整表（HTML 编码）
            Assert.Contains("自动处理记录", html);                 // 自动处理两张子表
            Assert.Contains("Markdown 文档按子目录分布", html);    // 目录分布
            Assert.Contains("全量导出文档明细", html);             // 全量明细表
            Assert.Contains("全部状态（0）", html);                // 空场景过滤器文案
            Assert.Contains("BY_PARENT", html);                    // 目录卡 JS 数据变量
            Assert.Contains("foldersBox", html);                   // 目录卡容器 id
            Assert.Contains("id=\"shown\"", html);                 // 过滤器计数 id

            // KPI 数字：文档总数=0、成功=0、失败=0
            Assert.Contains("文档总数", html);
            Assert.Contains("<div class=\"k-value\">0</div>", html, StringComparison.Ordinal);
        }

        /* ============================================================
         * 场景 2：有失败 + 有调整
         * ============================================================ */
        [Fact]
        public void Generate_WithFailuresAndAdjustments_Renders_Match_Rows_And_Escapes_HTML_In_Detail()
        {
            var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            var end   = start.AddMinutes(5);
            // Detail 含 HTML 特殊字符，验证被转义（不直接写 <script> 造成注入）
            var fail = new ExportFailure("AI Coding 01", FailureKind.TypeNotSupported, "objType=19 <script>alert('x')</script> & more")
            {
                Suggestion = "建议"
            };
            var trunc = new TruncatedRecord(
                original:       "Codex Cli启动报错：Failed to load c++ bson extension, using pure JS version really long",
                savedAs:        "Codex Cli启动报错：Failed to load c++ bson extension, ...",
                originalLength: 98,
                showExt:        "md");
            var dedup = new DedupRecord(
                original: @"C:\tmp\同名.md",
                actual:   @"C:\tmp\同名_1.md");
            var mds = new List<MdDocEntry>
            {
                new("子A\\文档1.md",   "子A",      2048,  adjusted: false),
                new("子A\\文档2.md",   "子A",      4096,  adjusted: false),
                new("子B\\长文档.md",  "子B",      8192,  adjusted: true),
            };

            var model = new ReportModel
            {
                StartTimeUtc       = start,
                EndTimeUtc         = end,
                KbName             = "测试知识库",
                DocSaveType        = "md",
                EnvType            = "知识库（Wiki）",
                Failures           = new[] { fail },
                Truncations        = new[] { trunc },
                Dedups             = new[] { dedup },
                MdEntries          = mds,
                TotalExportedBytes = mds.Sum(x => x.SizeBytes) + 100_000,
                TotalExportedFiles = 10,
                TotalImageFiles    = 7,
            };

            // Act
            var htmlPath = ExportReportGenerator.Generate(model, _tempDir, "mixed-report.html");
            var html = File.ReadAllText(htmlPath);

            // 失败明细：FileName、Kind 标签、建议处理 & Detail（含 HTML 转义）都要有
            Assert.Contains("AI Coding 01", html);
            Assert.Contains("类型不支持", html);
            Assert.Contains("objType=19", html);
            // 用户输入的原始字符串（含 <script> 标签）绝不能原样出现在 HTML 里——这是注入安全硬要求。
            // 注意：报告自身的 <script>（渲染目录卡+过滤器）是正常存在的，所以不能用 DoesNotContain("<script>")，要加特征。
            Assert.DoesNotContain("<script>alert('x')</script> & more", html);
            // ⚠ 不要断言 alert(&#39;x&#39;) 不存在——它正表示 HtmlEncode 把单引号 ' 转成了 &#39;，
            // 渲染到页面上就是 "alert('x')" 这段纯文本，不会被当 JS 执行——这才是安全的正确行为。
            Assert.Contains("&lt;script&gt;", html);             // 必须被转义为实体
            Assert.Contains("建议", html);

            // 调整类：截断表的 SavedAs、去重表的 Actual 必须出现
            Assert.Contains(trunc.SavedAs, html);
            Assert.Contains("超长文件名自动截断", html);
            Assert.Contains("同名_1.md", html);
            Assert.Contains("追加 _N 尾号", html);

            // 明细表：3 条 md + 2 正常 + 1 调整（绿色 + 橙色徽章）
            // 报告规范：导出路径统一用 Unix 正斜杠，且加前导 "/" 前缀（无论 Windows/Linux）。
            Assert.Contains("/子A/文档1.md", html);
            Assert.Contains("/子B/长文档.md", html);
            Assert.Contains("仅正常（2）", html);
            Assert.Contains("仅调整（1）", html);

            // 产物规模 KPI：MB + 文件数 + 图片数
            Assert.Contains("3 个 md + 7 张图片", html);                         // 明细区标题
            Assert.Contains("10 个文件", html);                                   // KPI 4 格第 4 块

            // 时间：开始/结束、5 分 0 秒都在
            Assert.Contains("开始 2026-09-01", html);
            Assert.Contains("结束 2026-09-01", html);
            Assert.Contains("5 分 0.0 秒", html);
        }

        /* ============================================================
         * 场景 3：三类状态徽标 CSS 类名都出现
         * ============================================================ */
        [Fact]
        public void Generate_Contains_All_Three_Status_ClassNames()
        {
            var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            var end   = start.AddMinutes(1);
            var model = new ReportModel
            {
                StartTimeUtc       = start,
                EndTimeUtc         = end,
                KbName             = "badge-check",
                Failures           = new[] { new ExportFailure("F1", FailureKind.RequestError, "网络炸了") },
                Truncations        = new[] { new TruncatedRecord("really-long name xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx ...", "ReallyLongNa...", 70, "md") },
                Dedups             = Array.Empty<DedupRecord>(),
                MdEntries          = new[] { new MdDocEntry("ok.md", string.Empty, 123, adjusted: false), new MdDocEntry("cut.md", "(根目录)", 456, adjusted: true) },
                TotalExportedFiles = 3,
                TotalImageFiles    = 0,
            };

            var html = File.ReadAllText(ExportReportGenerator.Generate(model, _tempDir, "badges.html"));

            // 1) 明细表 + 筛选器都使用这 3 个 class
            Assert.Contains("status-ok", html);
            Assert.Contains("status-failed", html);
            Assert.Contains("status-adjusted", html);

            // 2) 状态分布条 3 色 bar 类名也必须存在（CSS 类，不是 inline style）
            Assert.Contains("bar-ok", html);
            Assert.Contains("bar-fail", html);
            Assert.Contains("bar-adj", html);

            // 3) KPI 4 宫格 4 个色类
            Assert.Contains("class=\"card total\"", html);
            Assert.Contains("class=\"card ok\"", html);
            Assert.Contains("class=\"card fail\"", html);
            Assert.Contains("class=\"card adj\"", html);
        }

        /* ============================================================
         * 场景 4：Generate 文件名时间戳默认值（不传 fileName 时自动带时间戳）
         * ============================================================ */
        [Fact]
        public void Generate_DefaultFileName_Format_Is_Timestamped()
        {
            var end = new DateTime(2026, 9, 1, 15, 39, 39, DateTimeKind.Utc);
            var model = new ReportModel
            {
                StartTimeUtc = end.AddMinutes(-10),
                EndTimeUtc   = end,
                KbName       = "时间戳",
            };

            var full = ExportReportGenerator.Generate(model, _tempDir);

            var fn = Path.GetFileName(full);
            // UTC 2026-09-01 15:39:39 → 本地时间 23:39:39（UTC+8）→ 文件名 "export-report-20260901-233939.html"
            // ⚠ 为了跨 CI（UTC）/ 本机（UTC+8）通用：不硬编码时分秒；只校验前缀 / 后缀 / 正则模式。
            Assert.StartsWith("export-report-20260901-", fn, StringComparison.Ordinal);
            Assert.EndsWith(".html", fn, StringComparison.Ordinal);
            Assert.Matches(@"^export-report-\d{8}-\d{6}\.html$", fn);
        }

        /* ============================================================
         * 场景 5：目录分布卡 JSON 注入安全（父目录名含引号 / HTML 特殊字符）
         * ============================================================ */
        [Fact]
        public void Generate_MdEntries_With_Weird_FolderName_Is_Safe_Json_And_Html()
        {
            var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            var model = new ReportModel
            {
                StartTimeUtc = start,
                EndTimeUtc   = start.AddMinutes(1),
                KbName       = "注入测试",
                MdEntries = new[]
                {
                    new MdDocEntry("odd\".md",  "x\" <img=x onerror=alert(1) y=\"z", 1024),
                    new MdDocEntry("normal.md", "普通目录", 2048),
                }
            };

            var html = File.ReadAllText(ExportReportGenerator.Generate(model, _tempDir, "inject.html"));

            // 1) DOM 结构里（<script> 块外）不能出现裸 "<img=x"，必须被 HtmlEncode 成 "&lt;img=x"。
            //    注意：BY_PARENT JSON 在 <script> 块内部的 "parent":"x\" <img=x ..." 是安全的 JS 字符串字面量，
            //    浏览器不会把它当 DOM 解析——所以只能在 <script> 范围之外做负向断言。
            var idxJson = html.IndexOf("var BY_PARENT=", StringComparison.Ordinal);
            Assert.True(idxJson >= 0, "BY_PARENT 赋值语句不存在");
            var endOfJsonScript = html.IndexOf("</script>", idxJson, StringComparison.Ordinal);
            Assert.True(endOfJsonScript > idxJson, "BY_PARENT 脚本块没有闭合 </script>");
            var beforeJson = html.Substring(0, idxJson);
            var afterJson  = html.Substring(endOfJsonScript);
            Assert.DoesNotContain("<img=x", beforeJson);
            Assert.DoesNotContain("<img=x", afterJson);
            Assert.Contains("&lt;img=x", beforeJson + afterJson); // DOM 文本里正确转义

            // 2) BY_PARENT JSON 部分里父目录名双引号必须正确被 JSON 转义（\"），否则切到 JS 语法错误
            //    STJ 的 UnsafeRelaxedJsonEscaping 会保中文、保 < > &，但仍会把 " / \ 转义。
            //    想要的子串是：  x  \  "   → 普通字符串里写 "x\\\""  = (x) + (\\ → \) + (\" → ")   =  x\"
            var idxVar = html.IndexOf("var BY_PARENT=", StringComparison.Ordinal);
            Assert.True(idxVar >= 0, "BY_PARENT 赋值语句不存在");
            var scriptSlice = html.Substring(idxVar, Math.Min(2000, html.Length - idxVar));
            Assert.Contains("x\\\"", scriptSlice); // 期望 JSON 中出现 x\"   （父目录字符串里的 " 被正确转义）
        }
    }
}
