using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace feishu_doc_export
{
    /// <summary>
    /// 失败 / 自动调整 明细的种类。
    /// </summary>
    public enum FailureKind
    {
        /// <summary>objType 不在 allow-list（docx/sheet/bitable/file/pdf 等）</summary>
        TypeNotSupported,
        /// <summary>HttpRequestException 之类的 HTTP 传输异常</summary>
        RequestError,
        /// <summary>FeiShu 导出任务 JobErrorMsg != "success"（由 CreateExportTask/QueryExportTaskResult 返回）</summary>
        JobError,
        /// <summary>其他未分类的 Exception</summary>
        UnknownError
    }

    /// <summary>
    /// 单次导出流程里的一条失败/异常记录。
    /// </summary>
    public sealed class ExportFailure
    {
        public string FileName { get; }
        public FailureKind Kind { get; }
        public string Detail { get; }
        public string Suggestion { get; set; } = string.Empty;

        public ExportFailure(string fileName, FailureKind kind, string detail)
        {
            FileName = fileName ?? string.Empty;
            Kind = kind;
            Detail = detail ?? string.Empty;
        }

        /// <summary>
        /// 给前端报告用的「kind 展示名」。
        /// </summary>
        public string KindLabel => Kind switch
        {
            FailureKind.TypeNotSupported => "类型不支持（飞书 API 无法导出）",
            FailureKind.RequestError     => "网络 / HTTP 请求异常",
            FailureKind.JobError         => "飞书导出任务失败（JobErrorMsg）",
            _                            => "未知异常"
        };

        /// <summary>
        /// 给前端报告用的默认建议处理。
        /// </summary>
        public string DefaultSuggestion => Kind switch
        {
            FailureKind.TypeNotSupported => "飞书客户端手动下载（CreateExportTask 接口对该格式无导出能力）",
            FailureKind.RequestError     => "稍后重试；检查网络与飞书令牌有效性",
            FailureKind.JobError         => "飞书后台导出任务失败，可稍后重试或通过客户端直接导出",
            _                            => "检查本机日志；若持续失败请通过飞书客户端手动下载"
        };

        /// <summary>
        /// 前端 CSS 子类（配色用）。
        /// </summary>
        public string KindClass => Kind == FailureKind.TypeNotSupported ? "type-unsupported" :
                                   Kind == FailureKind.RequestError     ? "request-error" :
                                   Kind == FailureKind.JobError         ? "job-error" :
                                                                          "unknown-error";
    }

    /// <summary>
    /// 一条 SafeName 超长截断记录（属于"调整"类，导出成功但文件名被修改）。
    /// </summary>
    public sealed class TruncatedRecord
    {
        public string Original { get; }
        public string SavedAs  { get; }
        public int OriginalLength { get; }
        public string ShowExt { get; }

        public TruncatedRecord(string original, string savedAs, int originalLength, string showExt)
        {
            Original       = original       ?? string.Empty;
            SavedAs        = savedAs        ?? string.Empty;
            OriginalLength = originalLength;
            ShowExt        = showExt        ?? string.Empty;
        }
    }

    /// <summary>
    /// 一条 DeduplicateOnExist 去重记录（SafeName 截断后前 60 字符重名 → _1/_2 尾号）。
    /// </summary>
    public sealed class DedupRecord
    {
        public string Original { get; }
        public string Actual   { get; }

        public DedupRecord(string original, string actual)
        {
            Original = original ?? string.Empty;
            Actual   = actual   ?? string.Empty;
        }
    }

    /// <summary>
    /// 一条成功导出的 Markdown 文档条目，用于全量明细表与按目录分布卡。
    /// </summary>
    public sealed class MdDocEntry
    {
        public string RelativePath { get; }
        public string ParentFolder { get; }
        public long SizeBytes { get; }
        /// <summary>true 表示文件名是经过 SafeName 超长截断后落盘的（用于明细表 badge）。</summary>
        public bool Adjusted { get; set; }

        public MdDocEntry(string relativePath, string parentFolder, long sizeBytes, bool adjusted = false)
        {
            RelativePath = relativePath ?? string.Empty;
            ParentFolder = string.IsNullOrWhiteSpace(parentFolder) ? "(根目录)" : parentFolder;
            SizeBytes    = sizeBytes;
            Adjusted     = adjusted;
        }
    }

    /// <summary>
    /// 给 <see cref="ExportReportGenerator"/> 用的数据聚合对象。
    /// </summary>
    public sealed class ReportModel
    {
        public DateTime StartTimeUtc { get; init; }
        public DateTime EndTimeUtc   { get; init; }
        public DateTime StartTimeLocal => StartTimeUtc.ToLocalTime();
        public DateTime EndTimeLocal   => EndTimeUtc.ToLocalTime();

        public string KbName       { get; init; } = string.Empty;
        public string DocSaveType  { get; init; } = "md";
        public string EnvType      { get; init; } = "知识库（Wiki）";

        public IReadOnlyList<ExportFailure>  Failures      { get; init; } = Array.Empty<ExportFailure>();
        public IReadOnlyList<TruncatedRecord> Truncations   { get; init; } = Array.Empty<TruncatedRecord>();
        public IReadOnlyList<DedupRecord>     Dedups        { get; init; } = Array.Empty<DedupRecord>();
        public IReadOnlyList<MdDocEntry>      MdEntries     { get; init; } = Array.Empty<MdDocEntry>();

        /// <summary>所有导出物（md + 图片 + 原文件等）的总字节数。</summary>
        public long TotalExportedBytes { get; init; }
        /// <summary>所有导出物的文件总个数（含 md / 图片 / 附件 / PDF 等）。</summary>
        public int  TotalExportedFiles { get; init; }
        /// <summary>图片文件数量（png/jpg/gif 等）。</summary>
        public int  TotalImageFiles    { get; init; }
    }

    /// <summary>
    /// 零依赖 HTML 导出报告生成器。所有 CSS / JS 全部 inline，离线可开。
    /// <para>颜色 / 类名 / 布局 与 export_report_template.html（SSoT）一一对应。</para>
    /// </summary>
    public static class ExportReportGenerator
    {
        // ------------------------------------------------------------
        // 公共入口
        // ------------------------------------------------------------

        /// <summary>
        /// 根据模型渲染 HTML 并写入 <c>exportDir</c>，文件名形如 <c>export-report-20260901-153939.html</c>。
        /// </summary>
        /// <returns>最终写入的 HTML 绝对路径。</returns>
        public static string Generate(ReportModel model, string exportDir, string fileName = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (string.IsNullOrWhiteSpace(exportDir)) throw new ArgumentException("导出目录不能为空", nameof(exportDir));
            Directory.CreateDirectory(exportDir);

            fileName ??= $"export-report-{model.EndTimeLocal:yyyyMMdd-HHmmss}.html";
            var fullPath = Path.Combine(exportDir, fileName);

            var html = Render(model);
            File.WriteAllText(fullPath, html, new UTF8Encoding(false));
            return fullPath;
        }

        // ------------------------------------------------------------
        // 核心渲染
        // ------------------------------------------------------------

        private static string Render(ReportModel m)
        {
            var sb = new StringBuilder(128 * 1024); // 128 KB 预分配
            sb.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head>")
              .Append("<meta charset=\"UTF-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1.0\">")
              .Append("<title>飞书知识库导出报告 · ").Append(HtmlEsc(m.KbName)).Append(" · ")
              .Append(HtmlEsc(m.EndTimeLocal.ToString("yyyy-MM-dd HH:mm:ss"))).Append("</title>")
              .Append("<style>").Append(CssBlock).Append("</style></head><body><div class=\"wrap\">");

            RenderHeader(sb, m);
            RenderKpi(sb, m);
            RenderDistBar(sb, m);
            RenderFailuresAndAdjustments(sb, m);
            RenderAutoProcess(sb, m);
            RenderFolders(sb, m);
            RenderDetailTable(sb, m);

            sb.Append("</div>").Append(FolderRenderScriptBlock).Append(FilterScriptBlock).Append("</body></html>");
            return sb.ToString();
        }

        // ------------------------------------------------------------
        // 8 大区块
        // ------------------------------------------------------------

        private static void RenderHeader(StringBuilder sb, ReportModel m)
        {
            var duration = (m.EndTimeUtc - m.StartTimeUtc).Duration();
            var durationHuman = HumanizeDuration(duration);

            sb.Append("<header><div>")
              .Append("<h1>✦ 飞书知识库导出报告</h1>")
              .Append("<div class=\"sub\">")
              .Append("<div><span class=\"label\">知识库：</span><strong>").Append(HtmlEsc(m.KbName)).Append("</strong></div>")
              .Append("<div><span class=\"label\">导出类型：</span><strong>").Append(HtmlEsc(m.DocSaveType.ToUpperInvariant())).Append("</strong></div>")
              .Append("<div><span class=\"label\">模式：</span>").Append(HtmlEsc(m.EnvType)).Append("</div>")
              .Append("<div><span class=\"label\">转换引擎：</span>DocSharp.Docx（MIT · 无水印）</div>")
              .Append("</div></div>")
              .Append("<div style=\"display:flex;flex-direction:column;gap:8px;align-items:flex-end\">")
              .Append("<span class=\"pill\">报告生成：").Append(HtmlEsc(m.EndTimeLocal.ToString("yyyy-MM-dd HH:mm:ss"))).Append("</span>")
              .Append("<span class=\"pill\">耗时：").Append(HtmlEsc(durationHuman)).Append("</span>")
              .Append("<span class=\"pill\">开始 ").Append(HtmlEsc(m.StartTimeLocal.ToString("yyyy-MM-dd HH:mm:ss")))
              .Append(" → 结束 ").Append(HtmlEsc(m.EndTimeLocal.ToString("yyyy-MM-dd HH:mm:ss"))).Append("</span>")
              .Append("</div></header>");
        }

        private static void RenderKpi(StringBuilder sb, ReportModel m)
        {
            int mdCount = m.MdEntries.Count;
            int totalDocs = mdCount + m.Failures.Count; // "文档总数" = md 成功 + 硬失败
            int ok = mdCount;
            int fail = m.Failures.Count;
            int adj = m.Truncations.Count + m.Dedups.Count;
            double coverage = totalDocs == 0 ? 100d : Math.Round(ok * 100d / totalDocs, 1, MidpointRounding.AwayFromZero);
            double totalMb = Math.Round(m.TotalExportedBytes / (1024d * 1024d), 2, MidpointRounding.AwayFromZero);

            sb.Append("<div class=\"kpi\">")
              .Append("<div class=\"card total\"><div class=\"k-label\">文档总数</div><div class=\"k-value\">").Append(totalDocs.ToString(CultureInfo.InvariantCulture))
              .Append("</div><div class=\"k-extra\">成功 ").Append(ok).Append(" + 失败 ").Append(fail).Append(" · 覆盖率 <strong>").Append(coverage.ToString("0.0", CultureInfo.InvariantCulture))
              .Append("%</strong></div></div>")
              .Append("<div class=\"card ok\"><div class=\"k-label\">成功导出</div><div class=\"k-value\">").Append(ok.ToString(CultureInfo.InvariantCulture))
              .Append("</div><div class=\"k-extra\">含 ").Append(adj).Append(" 个自动调整文档（超长截断 / 重名去重）</div></div>")
              .Append("<div class=\"card fail\"><div class=\"k-label\">失败文档</div><div class=\"k-value\">").Append(fail.ToString(CultureInfo.InvariantCulture))
              .Append("</div><div class=\"k-extra\">见「失败 &amp; 自动调整明细」，按类给了处理建议</div></div>")
              .Append("<div class=\"card adj\"><div class=\"k-label\">产物规模</div><div class=\"k-value\">").Append(totalMb.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" MB</div><div class=\"k-extra\">").Append(m.TotalExportedFiles.ToString(CultureInfo.InvariantCulture)).Append(" 个文件（")
              .Append(mdCount.ToString(CultureInfo.InvariantCulture)).Append(" md + ").Append(m.TotalImageFiles.ToString(CultureInfo.InvariantCulture)).Append(" 图片 + 其他）</div></div>")
              .Append("</div>");
        }

        private static void RenderDistBar(StringBuilder sb, ReportModel m)
        {
            int ok   = m.MdEntries.Count;
            int fail = m.Failures.Count;
            int adj  = m.Truncations.Count + m.Dedups.Count;
            int total = Math.Max(1, ok + fail + adj); // 调整也按比例占一小条
            var pctOk   = Math.Round(ok   * 100d / total, 1, MidpointRounding.AwayFromZero);
            var pctFail = Math.Round(fail * 100d / total, 1, MidpointRounding.AwayFromZero);
            var pctAdj  = Math.Round(adj  * 100d / total, 1, MidpointRounding.AwayFromZero);
            // 浮点累积误差兜底
            if (pctOk + pctFail + pctAdj > 100) pctAdj = Math.Max(0, 100 - pctOk - pctFail);

            sb.Append("<section class=\"card\"><h2>文档处理状态分布</h2>")
              .Append("<div class=\"distbar\">")
              .Append("<div class=\"bar-ok\"   style=\"width:").Append(Pct(pctOk))  .Append("\">").Append(Pct(pctOk))  .Append(" 成功</div>")
              .Append("<div class=\"bar-fail\" style=\"width:").Append(Pct(pctFail)).Append("\">").Append(Pct(pctFail)).Append(" 失败</div>")
              .Append("<div class=\"bar-adj\"  style=\"width:").Append(Pct(pctAdj)) .Append("\">").Append(Pct(pctAdj)) .Append(" 调整</div>")
              .Append("</div><div class=\"dist-legend\">")
              .Append("<span><i style=\"background:var(--ok)\"></i>正常导出 <strong>").Append(ok.ToString(CultureInfo.InvariantCulture))  .Append("</strong></span>")
              .Append("<span><i style=\"background:var(--fail)\"></i>导出失败 <strong>").Append(fail.ToString(CultureInfo.InvariantCulture)).Append("</strong></span>")
              .Append("<span><i style=\"background:var(--adjust)\"></i>调整（自动截断/重名去重）<strong>").Append(adj.ToString(CultureInfo.InvariantCulture)).Append("</strong></span>")
              .Append("</div></section>");
        }

        private static void RenderFailuresAndAdjustments(StringBuilder sb, ReportModel m)
        {
            sb.Append("<section class=\"card\"><h2>失败 &amp; 自动调整明细</h2>")
              .Append("<table class=\"wide\"><thead><tr><th style=\"width:60px\">#</th><th>文档名</th><th style=\"width:160px\">状态</th>")
              .Append("<th>失败/调整原因</th><th style=\"width:280px\">建议处理</th></tr></thead><tbody>");

            int idx = 0;
            // 1) 真正的失败
            foreach (var f in m.Failures)
            {
                idx++;
                var reason = $"{f.KindLabel}";
                var detail = string.IsNullOrWhiteSpace(f.Detail) ? f.KindLabel : f.Detail;
                var suggestion = string.IsNullOrWhiteSpace(f.Suggestion) ? f.DefaultSuggestion : f.Suggestion;

                sb.Append("<tr><td>").Append(idx).Append("</td>")
                  .Append("<td class=\"rel-path\">").Append(HtmlEsc(f.FileName)).Append("</td>")
                  .Append("<td><span class=\"badge status-failed\">失败</span></td>")
                  .Append("<td>").Append(HtmlEsc(reason)).Append("<div class=\"sub\">").Append(HtmlEsc(detail)).Append("</div></td>")
                  .Append("<td>").Append(HtmlEsc(suggestion)).Append("</td></tr>");
            }
            // 2) 调整类（截断 + 去重）
            foreach (var t in m.Truncations)
            {
                idx++;
                var ext = string.IsNullOrWhiteSpace(t.ShowExt) ? string.Empty : "." + t.ShowExt;
                var detail = $"原标题 {t.OriginalLength} 字符 > SafeNameMaxLength({Helper.FileHelper.SafeNameMaxLength}) → 自动「前 60 + ...」截断";
                sb.Append("<tr><td>").Append(idx).Append("</td>")
                  .Append("<td class=\"rel-path\">").Append(HtmlEsc(t.Original + ext)).Append("</td>")
                  .Append("<td><span class=\"badge status-adjusted\">调整（已自动修复）</span></td>")
                  .Append("<td>").Append("文件名超长（已自动修复，本次已成功导出）").Append("<div class=\"sub\">").Append(HtmlEsc(detail)).Append("</div></td>")
                  .Append("<td>无需额外操作；若飞书标题方便建议简短化更易读。</td></tr>");
            }
            foreach (var d in m.Dedups)
            {
                idx++;
                var detail = $"SafeName 截断后与已有文件重名 → 自动追加 _1/_2 尾号，不覆盖原文件，不中断导出。实际落盘：{d.Actual}";
                var name = Path.GetFileName(d.Original);
                sb.Append("<tr><td>").Append(idx).Append("</td>")
                  .Append("<td class=\"rel-path\">").Append(HtmlEsc(name)).Append("</td>")
                  .Append("<td><span class=\"badge status-adjusted\">调整（已自动去重）</span></td>")
                  .Append("<td>").Append("SafeName 后重名（已自动修复，本次已成功导出）").Append("<div class=\"sub\">").Append(HtmlEsc(detail)).Append("</div></td>")
                  .Append("<td>无需额外操作；若介意尾号可手动改名后再跑一次增量。</td></tr>");
            }

            if (idx == 0)
            {
                sb.Append("<tr><td>—</td><td colspan=\"4\" class=\"sub\" style=\"text-align:center;padding:20px;color:#9ca3af\">（本次全部成功，无失败也无调整）</td></tr>");
            }

            sb.Append("</tbody></table></section>");
        }

        private static void RenderAutoProcess(StringBuilder sb, ReportModel m)
        {
            sb.Append("<section class=\"card\"><h2>自动处理记录</h2>");

            // 5.1 截断
            sb.Append("<h3>（1）超长文件名自动截断（本次 ")
              .Append(m.Truncations.Count.ToString(CultureInfo.InvariantCulture)).Append(" 个）</h3>")
              .Append("<table class=\"wide\"><thead><tr><th style=\"width:60px\">#</th><th>原标题（飞书侧）</th>")
              .Append("<th>本地落盘文件名</th><th style=\"width:220px\">处理方式</th></tr></thead><tbody>");
            int adi = 0;
            foreach (var t in m.Truncations)
            {
                adi++;
                var ext = string.IsNullOrWhiteSpace(t.ShowExt) ? string.Empty : "." + t.ShowExt;
                sb.Append("<tr><td>").Append(adi).Append("</td>")
                  .Append("<td>").Append(HtmlEsc(t.Original + ext)).Append("（").Append(t.OriginalLength).Append(" 字符）</td>")
                  .Append("<td class=\"rel-path\">").Append(HtmlEsc(t.SavedAs + ext)).Append("</td>")
                  .Append("<td><span class=\"badge status-adjusted\">超长文件名自动截断（前 60 + ...）</span></td></tr>");
            }
            if (adi == 0)
            {
                sb.Append("<tr><td>—</td><td colspan=\"3\" class=\"sub\" style=\"text-align:center;padding:16px;color:#9ca3af\">（本次空，无超长文件名）</td></tr>");
            }
            sb.Append("</tbody></table>");

            // 5.2 去重
            sb.Append("<h3>（2）重名去重（本次 ").Append(m.Dedups.Count.ToString(CultureInfo.InvariantCulture)).Append(" 个）</h3>")
              .Append("<div class=\"sub\">SafeName 截断后前 60 字符若与已有文件冲突，自动追加 _1 / _2 … 尾号直到不覆盖。下表记录「原路径 → 实际落盘路径」。</div>")
              .Append("<table class=\"wide\"><thead><tr><th style=\"width:60px\">#</th><th>原落盘路径</th>")
              .Append("<th>实际落盘路径（去重尾号）</th><th style=\"width:160px\">处理方式</th></tr></thead><tbody>");
            int di = 0;
            foreach (var d in m.Dedups)
            {
                di++;
                sb.Append("<tr><td>").Append(di).Append("</td>")
                  .Append("<td class=\"rel-path\">").Append(HtmlEsc(d.Original)).Append("</td>")
                  .Append("<td class=\"rel-path\">").Append(HtmlEsc(d.Actual)).Append("</td>")
                  .Append("<td><span class=\"badge status-adjusted\">重名自动追加 _N 尾号</span></td></tr>");
            }
            if (di == 0)
            {
                sb.Append("<tr><td>—</td><td colspan=\"3\" class=\"sub\" style=\"text-align:center;padding:16px;color:#9ca3af\">（本次空，无需去重）</td></tr>");
            }
            sb.Append("</tbody></table></section>");
        }

        private static void RenderFolders(StringBuilder sb, ReportModel m)
        {
            var folders = m.MdEntries
                .GroupBy(e => e.ParentFolder, StringComparer.Ordinal)
                .Select(g => new
                {
                    Parent = g.Key ?? "(根目录)",
                    Count  = g.Count(),
                    SizeKB = Math.Round(g.Sum(x => x.SizeBytes) / 1024d, 1, MidpointRounding.AwayFromZero)
                })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Parent, StringComparer.Ordinal)
                .ToList();

            sb.Append("<section class=\"card\"><h2>Markdown 文档按子目录分布（共 ")
              .Append(folders.Count.ToString(CultureInfo.InvariantCulture)).Append(" 个分组）</h2>")
              .Append("<div class=\"folders\" id=\"foldersBox\"></div>")
              .Append("<script>var BY_PARENT=");

            // 手写 JSON，省得引 System.Text.Json 还要配置（但直接用 STJ 更稳，net6 自带）
            var opts = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            sb.Append(JsonSerializer.Serialize(folders, opts));

            sb.Append(";</script></section>");
        }

        private static void RenderDetailTable(StringBuilder sb, ReportModel m)
        {
            int ok   = m.MdEntries.Count(x => !x.Adjusted);
            int adj  = m.MdEntries.Count(x => x.Adjusted);
            int totalDocs = ok + adj;

            sb.Append("<section class=\"card\"><h2>全量导出文档明细（")
              .Append(totalDocs.ToString(CultureInfo.InvariantCulture)).Append(" 个 md + ")
              .Append(m.TotalImageFiles.ToString(CultureInfo.InvariantCulture)).Append(" 张图片）</h2>")

              .Append("<div class=\"toolbar\">")
              .Append("<input id=\"q\" placeholder=\"🔍 按 路径 / 文件名 / 文件夹 关键词过滤…\">")
              .Append("<select id=\"f\">")
              .Append("<option value=\"all\">全部状态（").Append(totalDocs.ToString(CultureInfo.InvariantCulture)).Append("）</option>")
              .Append("<option value=\"status-ok\">仅正常（").Append(ok.ToString(CultureInfo.InvariantCulture)).Append("）</option>")
              .Append("<option value=\"status-adjusted\">仅调整（").Append(adj.ToString(CultureInfo.InvariantCulture)).Append("）</option>")
              .Append("</select>")
              .Append("<span style=\"color:#6b7280;font-size:13px\">显示 <strong id=\"shown\">0</strong> / ")
              .Append(totalDocs.ToString(CultureInfo.InvariantCulture)).Append(" 条</span>")
              .Append("</div>")

              .Append("<div style=\"max-height:620px;overflow:auto;border:1px solid var(--line);border-radius:10px\">")
              .Append("<table class=\"wide\" id=\"dt\">")
              .Append("<thead><tr>")
              .Append("<th style=\"width:120px;position:sticky;top:0;background:#f9fafb\">状态</th>")
              .Append("<th style=\"position:sticky;top:0;background:#f9fafb\">导出路径</th>")
              .Append("<th style=\"width:240px;position:sticky;top:0;background:#f9fafb\">所属目录</th>")
              .Append("<th class=\"num\" style=\"width:120px;position:sticky;top:0;background:#f9fafb\">大小</th>")
              .Append("<th style=\"width:70px;position:sticky;top:0;background:#f9fafb\">类型</th>")
              .Append("</tr></thead><tbody id=\"dt-body\">");

            foreach (var e in m.MdEntries)
            {
                var sClass = e.Adjusted ? "status-adjusted" : "status-ok";
                var sText  = e.Adjusted ? "调整（自动截断）" : "正常";
                var sz = FormatSize(e.SizeBytes);
                var rel = e.RelativePath;
                // 统一 Unix 风格相对路径 + 前置 /
                var relUnix = "/" + rel.Replace('\\', '/').TrimStart('/');
                sb.Append("<tr><td class=\"").Append(sClass).Append("\"><span class=\"badge ").Append(sClass).Append("\">").Append(sText).Append("</span></td>")
                  .Append("<td class=\"rel-path\">").Append(HtmlEsc(relUnix)).Append("</td>")
                  .Append("<td>").Append(HtmlEsc(e.ParentFolder)).Append("</td>")
                  .Append("<td class=\"num\">").Append(HtmlEsc(sz)).Append("</td>")
                  .Append("<td>md</td></tr>");
            }

            sb.Append("</tbody></table></div></section>");
        }

        // ------------------------------------------------------------
        // 辅助
        // ------------------------------------------------------------

        private static string Pct(double d) => d.ToString("0.0", CultureInfo.InvariantCulture) + "%";

        private static string HtmlEsc(string s) => string.IsNullOrEmpty(s) ? string.Empty : WebUtility.HtmlEncode(s);

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return (bytes / (1024d * 1024d)).ToString("0.00", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= 1024)        return (bytes / 1024d).ToString("0.1", CultureInfo.InvariantCulture) + " KB";
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        private static string HumanizeDuration(TimeSpan ts)
        {
            var parts = new List<string>(4);
            if (ts.Days    > 0) parts.Add($"{ts.Days} 天");
            if (ts.Hours   > 0) parts.Add($"{ts.Hours} 小时");
            if (ts.Minutes > 0) parts.Add($"{ts.Minutes} 分");
            var sec = ts.TotalSeconds - Math.Floor(ts.TotalSeconds / 60) * 60;
            parts.Add(sec.ToString("0.0", CultureInfo.InvariantCulture) + " 秒");
            return string.Join(" ", parts) + $" （{ts.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s）";
        }

        // ------------------------------------------------------------
        // 常量区块（避免 C# 插值与 CSS 大括号冲突）
        // ------------------------------------------------------------

        private const string CssBlock = @"
*,*::before,*::after{box-sizing:border-box}
:root{--ok:#22c55e;--ok-bg:#dcfce7;--fail:#ef4444;--fail-bg:#fee2e2;--adjust:#f59e0b;--adjust-bg:#fef3c7;--ink:#111827;--sub:#6b7280;--line:#e5e7eb;--card:#fff;--surface:#f9fafb;--primary:#2563eb}
html,body{margin:0;padding:0;background:var(--surface);color:var(--ink);font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",""PingFang SC"",""Hiragino Sans GB"",""Microsoft YaHei"",sans-serif;font-size:14px;line-height:1.55}
.wrap{max-width:1200px;margin:0 auto;padding:32px 24px 96px}
header{display:flex;justify-content:space-between;align-items:flex-start;gap:24px;padding:28px 32px;background:linear-gradient(135deg,#1e3a8a,#2563eb);color:#fff;border-radius:16px;box-shadow:0 10px 30px rgba(37,99,235,.25);margin-bottom:28px}
header h1{margin:0 0 12px;font-size:24px}
header .sub{color:#dbeafe;display:flex;flex-wrap:wrap;gap:8px 20px;font-size:13.5px}
header .sub .label{opacity:.8;margin-right:4px}
header .pill{display:inline-block;padding:6px 14px;background:rgba(255,255,255,.14);border-radius:999px;font-size:12.5px;border:1px solid rgba(255,255,255,.22)}
.kpi{display:grid;grid-template-columns:repeat(4,1fr);gap:16px;margin-bottom:24px}
.kpi .card{background:var(--card);border-radius:14px;padding:20px 22px;border:1px solid var(--line)}
.kpi .k-label{color:var(--sub);font-size:13px;margin-bottom:6px}
.kpi .k-value{font-size:28px;font-weight:700}
.kpi .k-extra{font-size:12.5px;color:var(--sub);margin-top:4px}
.kpi .ok .k-value{color:var(--ok)}.kpi .fail .k-value{color:var(--fail)}.kpi .adj .k-value{color:var(--adjust)}.kpi .total .k-value{color:var(--primary)}
section.card{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:24px 28px;margin-bottom:24px}
h2{margin:0 0 16px;font-size:18px;display:flex;align-items:center;gap:10px}
h2::before{content:"";display:inline-block;width:4px;height:18px;background:var(--primary);border-radius:3px}
h3{font-size:15px;color:var(--sub);margin:16px 0 8px;font-weight:600}
.distbar{display:flex;height:30px;border-radius:999px;overflow:hidden;border:1px solid var(--line)}
.distbar div{display:flex;align-items:center;justify-content:center;color:#fff;font-size:12px;font-weight:600}
.distbar .bar-ok{background:var(--ok)}.distbar .bar-fail{background:var(--fail)}.distbar .bar-adj{background:var(--adjust)}
.dist-legend{display:flex;gap:22px;flex-wrap:wrap;margin-top:12px;color:var(--ink);font-size:13px}
.dist-legend i{display:inline-block;width:14px;height:14px;border-radius:4px;margin-right:6px;vertical-align:-2px}
.badge{display:inline-flex;align-items:center;padding:3px 10px;border-radius:999px;font-size:12px;font-weight:600;white-space:nowrap}
.badge.status-ok{color:#166534;background:var(--ok-bg);border:1px solid #86efac}
.badge.status-failed{color:#991b1b;background:var(--fail-bg);border:1px solid #fecaca}
.badge.status-adjusted{color:#92400e;background:var(--adjust-bg);border:1px solid #fde68a}
td.status-ok,td.status-failed,td.status-adjusted{text-align:center}
.sub{color:var(--sub);font-size:12px;margin-top:4px}
table.wide{width:100%;border-collapse:collapse;font-size:13.5px}
table.wide th{background:var(--surface);color:#374151;text-align:left;padding:10px 12px;border-bottom:1px solid var(--line);font-weight:600;position:sticky;top:0;z-index:1}
table.wide td{padding:10px 12px;border-bottom:1px solid var(--line);vertical-align:top}
table.wide tr:hover td{background:#f3f4f6}
td.num,th.num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}
td.rel-path{font-family:""JetBrains Mono"",""Cascadia Code"",Menlo,Consolas,monospace;font-size:12.5px;color:#334155;word-break:break-all}
.toolbar{display:flex;gap:12px;flex-wrap:wrap;margin-bottom:14px;align-items:center}
.toolbar input,.toolbar select{padding:8px 12px;border-radius:8px;border:1px solid var(--line);background:#fff;font-size:13.5px;outline:none}
.toolbar input:focus,.toolbar select:focus{border-color:var(--primary);box-shadow:0 0 0 3px rgba(37,99,235,.1)}
.toolbar input{min-width:280px;flex:1}
.folders{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:12px}
.folders .fc{padding:12px 14px;border:1px solid var(--line);border-radius:10px;background:#fff}
.folders .fc-top{display:flex;justify-content:space-between;align-items:baseline;margin-bottom:6px}
.folders .fc-name{font-weight:600;font-size:13.5px;color:#1f2937;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:200px}
.folders .fc-count{font-size:12px;color:var(--sub)}
.folders .fc-bar{height:6px;border-radius:999px;background:#e5e7eb;overflow:hidden}
.folders .fc-bar i{display:block;height:100%;background:linear-gradient(90deg,#60a5fa,#2563eb)}
.folders .fc-size{font-size:11.5px;color:var(--sub);margin-top:5px;text-align:right}
@media(max-width:900px){.kpi{grid-template-columns:repeat(2,1fr)} header{flex-direction:column} table.wide{display:block;overflow-x:auto;white-space:nowrap}}
";

        // 目录卡片渲染脚本（数据在 RenderFolders 里嵌入在 BY_PARENT 变量）
        private const string FolderRenderScriptBlock = "\n<script>\n" +
"(function(){\n" +
"  var data = (typeof BY_PARENT !== 'undefined') ? BY_PARENT : [];\n" +
"  var max = data.reduce(function(m,x){return Math.max(m,x.count||0)},0) || 1;\n" +
"  var html = data.map(function(x){\n" +
"    var w = ((x.count||0)/max*100).toFixed(1) + '%';\n" +
"    var sizeKB = (x.sizeKB == null) ? '0' : x.sizeKB;\n" +
"    var parent = x.parent == null ? '(根目录)' : String(x.parent);\n" +
// C# 里 ' 是字符串边界，JS 里也用 ' 作为字符串，不能在逐字字符串里用 "" 转义。
// 统一把 JS 字符串切到 " 来做，在 C# 中 "" 就是一个 "。
"    return \"<div class=\\\"fc\\\"><div class=\\\"fc-top\\\"><span class=\\\"fc-name\\\" title=\\\"\" + parent.replace(/\"/g,'&quot;') + \"\\\">\" + parent + \"</span>\" +\n" +
"           \"<span class=\\\"fc-count\\\">\"+(x.count||0)+\" 篇</span></div>\" +\n" +
"           \"<div class=\\\"fc-bar\\\"><i style=\\\"width:\"+w+\"\\\"></i></div>\" +\n" +
"           \"<div class=\\\"fc-size\\\">\"+sizeKB+\" KB</div></div>\";\n" +
"  }).join('');\n" +
"  var box = document.getElementById('foldersBox');\n" +
"  if (box) box.innerHTML = html;\n" +
"})();\n" +
"</script>\n";

        // 明细表搜索 + 状态过滤
        private const string FilterScriptBlock = @"
<script>
(function(){
  var q = document.getElementById('q');
  var f = document.getElementById('f');
  var shown = document.getElementById('shown');
  var tbody = document.getElementById('dt-body');
  var rows = tbody ? [].slice.call(tbody.querySelectorAll('tr')) : [];
  function apply(){
    var kw = q ? q.value.trim().toLowerCase() : '';
    var fl = f ? f.value : 'all';
    var c = 0;
    for (var i=0;i<rows.length;i++){
      var tr = rows[i];
      var text = (tr.innerText || tr.textContent || '').toLowerCase();
      var statusTd = tr.firstElementChild;
      var matchKw = !kw || text.indexOf(kw) >= 0;
      var matchFl = fl === 'all' || (statusTd && statusTd.classList && statusTd.classList.contains(fl));
      var ok = matchKw && matchFl;
      tr.style.display = ok ? '' : 'none';
      if (ok) c++;
    }
    if (shown) shown.textContent = String(c);
  }
  if (q) q.addEventListener('input', apply);
  if (f) f.addEventListener('change', apply);
  apply();
})();
</script>
";
    }
}
