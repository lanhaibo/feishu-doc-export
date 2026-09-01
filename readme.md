# feishu-doc-export

一个支持Windows、Mac、Linux系统的飞书文档一键导出服务，仅需一行命令即可将飞书知识库的全部文档同步到本地电脑。支持导出`markdown`，`docx`，`pdf`三种格式。导出速度嘎嘎快，实测**700**多个文档导出只需**25**分钟，且程序是后台挂机运行，不影响正常工作。版本更新记录见 [change.log](change.log)。

## 动机

最近也是公司办公软件从飞书切换回了企业微信，自然就产生了一些文档要迁移的问题，由于文档量过多（大概有700多个），无论是从飞书手动下载为`Word`或`PDF`格式的文档，还是将内容复制到本地新建`Markdown`文件都是一件极为繁琐的事情。于是便找到了两个GitHub上已有的飞书文档导出工具`Feishu2MD`和`feishu-backup`，但是他们都有一些问题不太满足我的需求。

### 现有方案的不满足

**feishu-backup：**

官方地址：[dicarne/feishu-backup: 用于备份飞书文档，可以将飞书文档转成markdown下载。 (github.com)](https://github.com/dicarne/feishu-backup)

1. 因为它是网页版，下载速度太慢。有一次使用线上版选择了其中一个飞书文档节点下的所有文档（大概200-300个），下载了1个多小时还没有好，可能是卡死了。
2. 因为它的下载方式是把选择的全部文档打包成压缩包后才会在浏览器返回给你，如果这个等待的过程中途断网或者电脑卡顿要重启，那你就白等那么长时间了。
3. 因为它不支持下载表格类型的文档。

**feishu2md：**

官方地址：[Wsine/feishu2md: 一键命令下载飞书文档为 Markdown (github.com)](https://github.com/Wsine/feishu2md)

我虽然没用实际使用过它，但我阅读它的官方文档后发现它的核心问题是一次只能下载一个文档。

### 我的需求

- 一次导出知识库下的所有文档，包含文档和表格
- 导出的文档目录结构保持和原飞书文档一致
- 导出速度不要太慢
- 对于文档导出的格式没有要求，`docx`和`xlsx`即可

基于以上的种种原因呢，我决定自己动手写一个满足自己需求的程序来解决这个问题。这里我使用的是支持跨平台的.net core进行开发，最终打包程序可支持在`windows`、`linux`、`mac`系统上运行。这里将不赘述具体的实现过程，直接展示最终的效果图吧。

## 如何使用

### 获取AppId和AppSecret

- 进入飞书[开发者后台](https://open.feishu.cn/app)，创建企业自建应用，信息随意填写。进入应用的后台管理页
- （重要）打开权限管理，开通需要的权限：云文档>开通以下权限（注意有分页）
  - 查看新版文档
  - 查看、评论和下载云空间中所有文件
  - 查看、评论和导出文档
  - 查看、评论、编辑和管理云空间中所有文件
  - 查看、评论、编辑和管理多维表格
  - 查看、编辑和管理知识库
  - 查看、评论、编辑和管理电子表格
  - 导出云文档
- 打开添加应用能力，添加机器人
- 版本管理与发布中创建一个版本，并申请发布上线
  - 等待企业管理员审核通过
  - 如果只是为了测试，可以选择测试企业和人员，创建测试企业，绑定应用，切换至测试版本
    - 进入测试企业创建知识库和文档
- 为机器人添加知识库的访问权限，具体步骤如下：
  - 在飞书桌面客户端中创建一个新的群组或直接使用已有的群组
  - 为群组添加群机器人，选择上面步骤中自己创建的应用作为群机器人
  - 打开知识库，如果你是知识库管理员，则可以看见知识空间设置。打开知识空间设置>成员管理>添加管理员，选择刚刚建立的群组
- 回到开发者平台，打开凭证与基础信息，获取 `App ID` 和 `App Secret`

### 如何获取知识库ID

![image](https://github.com/lanhaibo/feishu-doc-export/assets/84184815/ba45e7c8-ff76-4591-bda1-366f6234a6d0)
![image](https://github.com/lanhaibo/feishu-doc-export/assets/84184815/8be655df-9168-4c2a-90d6-81dff8e1676a)

### 下载程序

> 下载地址：[（Releases）feishu-doc-export](https://github.com/lanhaibo/feishu-doc-export/releases)，请选择最新版本下载

- windows-x64系统，下载`feishu-doc-export-win-x64.zip`
- mac-osx-x64系统，下载`feishu-doc-export-mac-osx-x64.zip`
- linux-x64系统，下载`feishu-doc-export-linux-x64.tar.gz`

下载并解压即可得到程序可执行文件：
- Windows：`feishu-doc-export.exe`（双击或命令行运行）
- macOS / Linux：`feishu-doc-export`（无后缀，解压后需 `chmod +x` 授权首次执行）

### 命令行执行

在可执行文件的目录打开终端，命令行所有参数如下：

```
请填写以下所有参数：
  --appId           飞书自建应用的AppId.【必填项】
  --appSecret       飞书自建应用的AppSecret.【必填项】
  --exportPath      文档导出的目录位置.【必填项】
  --spaceId         飞书导出的知识库Id（可为空，或者不传此参数）.
  --type            知识库（wiki）或个人空间云文档（cloudDoc）（可选值：cloudDoc、wiki，为空则默认为wiki）.
  --saveType        文档导出的文件类型（可选值：docx、md、pdf，为空或其他非可选值则默认为docx）.
  --folderToken     当type为个人空间云文档时，该项必填.
  --apiEndpoint     可以指定 API 的路径，如https://open.larksuite.com ，以支持Lark 环境
```

- win环境
  ```powershell
  # 指定知识库导出
  ./feishu-doc-export.exe --appId=111111 --appSecret=2222222  --spaceId=333333 --exportPath=E:\temp\test
  # 不指定知识库导出
  ./feishu-doc-export.exe --appId=111111 --appSecret=222222 --exportPath=E:\temp\test
  # win 不指定知识库 将文档保存为markdown文档
  ./feishu-doc-export.exe --appId=xxx --appSecret=xxx --saveType=md --exportPath=E:\temp\test
  # win 导出个人空间文档 将文档保存为markdown文档
  ./feishu-doc-export.exe --appId=xxx --appSecret=xxx --saveType=md --exportPath=E:\temp\test --type=cloudDoc --folderToken=xxx
  ```
- Linux / macOS 环境

  \*\*注意！！！\*\*首次使用时需要将文件授权为可执行文件
  ```shell
  # Linux 解压并授权
  tar -xzf feishu-doc-export-linux-x64.tar.gz
  chmod +x ./feishu-doc-export

  # macOS 解压并授权
  unzip feishu-doc-export-mac-osx-x64.zip
  chmod +x ./feishu-doc-export
  ```
  执行命令（**不需要 sudo**，除非你导出到系统受保护目录如 `/var/log` 等）：
  ```shell
  # Linux / macOS 执行（指定知识库）
  ./feishu-doc-export --appId=111111 --appSecret=222222 --spaceId=333333 --exportPath=./doc --saveType=md
  # Linux / macOS 执行（不指定知识库）
  ./feishu-doc-export --appId=111111 --appSecret=222222 --exportPath=./doc
  ```

执行效果图如下：

![image-20230706105636270](https://github.com/lanhaibo/feishu-doc-export/assets/84184815/aea85f4b-51bc-4e77-a047-1b52b1a75c23)

### 逐步执行

1. 第一步，（win，mac）双击运行程序，输入飞书自建应用的配置，并输入文档要导出的目录位置。

   `mac`和`linux`仍需执行命令`chmod +x ./feishu-doc-export`来将文件设置为可执行文件。

   `mac`可能会出现不受信任的执行程序，需要手动覆盖“隐私与安全性”设置中的设置。`linux`可通过命令行输入`./feishu-doc-export`而不带参数的方式执行

   ![feishuexport\_1](https://github.com/lanhaibo/feishu-doc-export/assets/84184815/cd8b8ab1-ec46-4d19-8844-794e58c305e8)
2. 第二步，选择知识库后自动导出

   ![2](https://github.com/lanhaibo/feishu-doc-export/assets/84184815/c1a09804-1d9c-414e-94f4-9a5be7230b22)
3. 第三步，对比飞书原文档的目录结构

   ![feishu\_wiki](https://github.com/lanhaibo/feishu-doc-export/assets/84184815/ddc6f0c0-3ace-4498-8bc4-02effc5ee5ea)

## 本地编译

### 环境依赖

- .NET SDK 6.0（实测 6.0.428 可用）
- NuGet 需可访问 nuget.org（本机默认无源时，restore/publish 需显式指定 `--source`）

### 编译命令（Windows）

在 `src/feishu-doc-export` 目录执行：

```powershell
dotnet publish -c Release -r win-x64 -o dist/run --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false --source https://api.nuget.org/v3/index.json
```

> 说明：
>
> - 产物在 `src/feishu-doc-export/dist/run`，可执行文件为 `feishu-doc-export.exe`
> - 单文件发布（`-p:PublishSingleFile=true`）在本机可能出现 apphost 找不到 dll 的启动问题，日常使用推荐上面的非单文件方式
> - 跨平台发布（win-x64 / linux-x64 / osx-x64）与裁剪配置详见 `src/feishu-doc-export/readme.md`

## 单元测试

测试项目为 `src/feishu-doc-export.Tests`（xUnit + net6.0），已加入解决方案。

### 环境依赖

- .NET 6 SDK
- NuGet 源（首次运行需还原 xunit 等测试包）

### 运行测试

在 `src` 目录执行：

```powershell
# 首次运行需先还原依赖（dotnet test 不支持 --source 开关）
dotnet restore .\feishu-doc-export.Tests\feishu-doc-export.Tests.csproj --source https://api.nuget.org/v3/index.json

# 运行全部测试
dotnet test .\feishu-doc-export.Tests\feishu-doc-export.Tests.csproj --no-restore

# 运行指定测试类（如配置合并）
dotnet test .\feishu-doc-export.Tests\feishu-doc-export.Tests.csproj --no-restore --filter FullyQualifiedName~GlobalConfigMergeTests
```

在 IDE（Visual Studio / Rider / VS Code + C# 扩展）中也可直接通过测试资源管理器运行。

### 当前测试结果

- **37/37 全部通过**（普通模式与覆盖率收集模式均验证；并发修复后连跑 3 轮稳定）
- 单轮执行耗时约 46-70ms
- 测试间无外部依赖（不访问网络、不依赖飞书凭证），可在任意环境离线执行

### 测试覆盖范围

| 测试文件 | 覆盖内容 |
| --- | --- |
| `DocxToMdFormatHelperTests` | md 后处理：图片相对/绝对路径替换、飞书文档引用转本地相对路径、docx 表格式代码块转 md 语法 |
| `PathGeneratorTests` | 知识库目录树到本地路径映射（三层树、双 token 查询、非法文件名字符清洗）、云文档路径映射 |
| `GlobalConfigMergeTests` | 配置三源合并：`FirstNonEmpty` 语义、命令行参数解析、config.json / credentials.local.json 应用规则及优先级链 |

### 新增用例约定

- 纯逻辑优先直测（字符串变换、路径计算、配置合并）；飞书 API 调用与 DOCX→MD 转换不做单测（依赖外部服务，属集成测试范畴）
- 涉及 `GlobalConfig` 静态状态的用例，需在用例内先重置状态（参考 `GlobalConfigMergeTests.ResetGlobalConfig`），避免用例间污染
- 共享静态状态的测试类必须归入同一 collection 并禁并行（参考 `DocumentPathGeneratorSequentialCollection`），否则 xUnit 跨类并行会引发用例间竞争
- 访问主项目 `internal` 成员已通过 `InternalsVisibleTo` 授权，新增 internal 成员无需额外配置

### 代码覆盖率

使用 coverlet.collector（已内置于测试项目）收集、ReportGenerator 生成 HTML 报表。

在 `src` 目录执行：

```powershell
# 1. 运行测试并收集覆盖率（生成 coverage.cobertura.xml）
dotnet test .\feishu-doc-export.Tests\feishu-doc-export.Tests.csproj --no-restore --collect:"XPlat Code Coverage"

# 2. （首次）安装 ReportGenerator
dotnet tool install --global dotnet-reportgenerator-globaltool --version 5.3.8 --add-source https://api.nuget.org/v3/index.json

# 3. 生成 HTML 报表（coverage-report.ps1 已封装 2、3 两步）
$coverageXml = (Get-ChildItem .\feishu-doc-export.Tests\TestResults -Recurse -Filter coverage.cobertura.xml | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
reportgenerator "-reports:$coverageXml" "-targetdir:.\feishu-doc-export.Tests\coveragereport" "-reporttypes:Html;TextSummary"

# 打开报表
start .\feishu-doc-export.Tests\coveragereport\index.html
```

当前覆盖率基线（37 个用例）：

| 指标 | 数值 |
| --- | --- |
| 行覆盖率 | 19.0%（184/966，主程序集整体） |
| 分支覆盖率 | 21.4%（46/214） |
| 方法覆盖率 | 30.9%（43/139） |

核心被测类覆盖率：

| 类 | 行覆盖率 |
| --- | --- |
| `DocxToMdFormatHelper`（md 后处理） | 100% |
| `DocumentPathGenerator`（知识库路径映射） | 100% |
| `CloudDocPathGenerator`（云文档路径映射） | 100% |
| `GlobalConfig`（配置合并） | 34.3% |

> 说明：整体 19% 偏低属预期——`Program` 主流程、`FeiShuHttpApiCaller`（飞书 API）、DOCX→MD 转换均依赖外部服务，不在单测范围（靠真实导出集成验证）；`Dtos` 多为属性容器。评估单测价值应看核心逻辑类，不看整体数字。

覆盖率产物（`TestResults/`、`coveragereport/`）已加入 `.gitignore`，不入库。

## 飞书应用权限清单

程序实际调用的飞书 API 与所需权限对应如下（在[开发者后台](https://open.feishu.cn/app) → 权限管理中搜索名称开通）：

| 程序功能（对应 API）                    | 权限管理页名称                         | scope 标识                                              | 必需性                 |
| ------------------------------- | ------------------------------- | ----------------------------------------------------- | ------------------- |
| 获取 tenant\_access\_token        | 无需业务权限                          | —                                                     | 自动                  |
| 列举/读取知识库节点（wiki 模式）             | 查看知识库                           | `wiki:wiki:readonly`                                  | **必需**              |
| 创建/查询导出任务（docx/md/pdf 全部走此接口）   | 导出云文档                           | `drive:export:readonly` 或 `docs:document:export`（二选一） | **必需**，缺失报 99991672 |
| 下载导出文件、下载文件类型文档（pdf/image 等）    | 查看、评论和下载云空间中所有文件                | `drive:drive:readonly`                                | **必需**              |
| 查看新版文档（docx 内容读取与 md 转换）        | 查看新版文档                          | `docx:document:readonly`                              | 推荐                  |
| 个人空间文件夹 meta 与文件列举（cloudDoc 模式） | 查看、评论和下载云空间中所有文件                | `drive:drive:readonly`                                | cloudDoc 模式必需       |
| 多维表格类文档导出                       | 查看、评论、编辑和管理多维表格（只读版：`bitable:app:readonly`） | `bitable:app`                              | 导出多维表格时需要           |
| 电子表格类文档导出                       | 查看、评论、编辑和管理电子表格（只读版：`sheets:spreadsheet:readonly`） | `sheets:spreadsheet`                       | 导出电子表格时需要           |

**最小权限集（仅导出知识库为 md/docx/pdf）**：

- 查看知识库（`wiki:wiki:readonly`）
- 导出云文档（`drive:export:readonly`）
- 查看、评论和下载云空间中所有文件（`drive:drive:readonly`）

> 提醒：scope 标识以「二选一」标注的，任开一个即可满足导出任务的权限校验（来自实测 99991672 错误的官方提示）。
> 权限开通后不会立即生效，需走完整流程：创建版本提交发布 → 管理员审批 → 机器人添加为知识库成员，详见下方「使用注意事项」第 1 条。

### 权限开通直达链接

在浏览器打开以下地址可一次性勾选开通（把 `cli_xxx` 替换为你的 AppId）：

```text
https://open.feishu.cn/app/cli_xxx/auth?q=wiki:wiki:readonly,drive:export:readonly,drive:drive:readonly,docx:document:readonly
```

### 常见权限错误排查

| 错误码 | 典型信息 | 原因与解决 |
|---|---|---|
| 99991672 | 应用尚未开通所需的应用身份权限 | 缺 scope（开通权限）或版本未发布（创建版本提交发布并审批通过）或机器人未加入知识库（设为管理员/编辑成员），三者缺一不可 |
| 1310213 | Permission Fail | API 权限已开通但**目标文档未授权给应用**：在电子表格/文档页面右上角「···」→「…更多」→「添加文档应用」，搜索并添加你的应用 |
| 1254004 | 表格未授权 | 同上，多维表格需在「···」菜单中添加文档应用 |

> 注：「添加文档应用」入口只有在应用至少开通一个云文档 API 权限后才能搜索到目标应用。
>
> 程序运行时遇到上述错误码（99991672 / 1310213 / 1254004）会**自动打印对应排查指引**：99991672 附带按当前 AppId 生成的权限开通直达链接，无需手动拼接。

## 凭证管理与一键运行

凭证与运行参数分离，避免敏感信息入库：

| 文件                                             | 是否上库           | 用途                                    |
| ---------------------------------------------- | -------------- | ------------------------------------- |
| `src/feishu-doc-export/config.json`            | 是              | 运行参数模板，appId / appSecret / spaceId 留空 |
| `src/feishu-doc-export/credentials.local.json` | 否（已 gitignore） | 本地真实凭证：appId / appSecret / spaceId    |
| `src/feishu-doc-export/run.ps1`                | 是              | 可选脚本：脱敏预检（-DryRun）、日志捕获、免交互启动         |

**程序原生支持配置文件**：无参数直接运行 `feishu-doc-export.exe` 时，自动从「exe 所在目录 → 工作目录」读取 `config.json` 与 `credentials.local.json`，必填项（appId / appSecret / exportPath）齐全则跳过交互直接导出；不齐全则回退手动输入模式。

参数覆盖优先级：**命令行参数 > credentials.local.json > config.json > 交互式输入**。

`credentials.local.json` 格式（首次使用：复制 config.json 改名后填入真实值）：

```json
{
  "appId": "cli_xxx",
  "appSecret": "你的AppSecret",
  "spaceId": "知识库ID"
}
```

使用示例：

```powershell
cd src/feishu-doc-export

# 方式一：exe 原生运行（编译后，自动读取 config.json + credentials.local.json）
.\dist\run\feishu-doc-export.exe

# 方式二：脚本运行（带脱敏预检与日志捕获）
.\run.ps1

# 仅校验配置，不实际执行（打印参数，appSecret/spaceId 自动脱敏为 ****）
.\run.ps1 -DryRun

# 临时覆盖凭证运行（不落盘，命令行优先级最高）
.\run.ps1 -AppSecret "新的secret"
.\run.ps1 -SpaceId "其他知识库ID"
```

## 使用注意事项

1. **飞书权限生效流程**（缺一步会返回 99991672 权限错误）：权限管理开通权限（如 `drive:export:readonly`）→ 版本管理与发布创建版本并提交发布 → 企业管理员审批通过 → 在飞书客户端将应用机器人添加为知识库成员（管理员或编辑）
2. **DOCX→Markdown 转换引擎**：当前使用 **DocSharp.Docx 0.16.0**（MIT 协议，**无水印**、无 License 文件要求、最后一版支持 .NET 6）。支持简单二维表格（GFM Markdown 语法）；代码块直接输出 ``` 围栏格式。无需额外配置，零成本使用。替换前使用的 Aspose.Words（评估版会加页眉/页脚水印、截断长文档）已在 2026-09-01 版本移除，历史记录见 [change.log](change.log)
   > **⚠ md 表格能力说明（重要）**：DocSharp 只生成 GFM 语法的简单二维表，**不支持合并单元格（`rowspan` / `colspan`）、嵌套表格、单元格内换行/复杂排版**；也不会像商业组件那样降级为 Raw HTML `<table colspan=...>` 兜底。若你的飞书文档里存在「表头跨列 / 行跨区 / 子表格」，导出后的 md 会出现列数对不齐、合并内容丢失、跨区被平铺等情况。**建议**：① 日常知识库（培训/操作手册/FAQ，表格基本都是简单二维）不受影响，正常使用 md；② 若有大量复杂表格，可临时把 `--saveType` 切到 `docx` / `pdf`，保留 Word/PDF 的原貌；③ 未来如升级到 .NET 8+，可升级 DocSharp 到最新版以获得表格增强。
3. **导出失败类型**：两类失败都会在日志末尾统一列出「以下是所有无法导出的文档」清单：
   - **文件类型不支持 API 导出**（如飞书「AI Coding 培训」等特定格式文档，`objType` 不在 allow-list 中）：请在飞书客户端手动下载
   - ~~**文件名超出 64 字符**（已修复）~~：2026-09-01 起，文件名 / 文件夹名**单组件超过 60 字符时会自动按「前 60 字符 + `...`」截断落盘**，并在控制台输出带原始字符数的提示信息「文件名超长（N > 60），已自动截断为【XX...md】继续导出」——导出**不会再因文件名过长失败**。若截断后与同目录其他文件发生重名，会自动追加 `_1`/`_2`/… 后缀，避免互相覆盖（该保护也对 docx/pdf 直接导出、SaveToMarkdownFile 产物生效）。飞书标题的非法字符 `\/:*?"<>|` 会统一替换为 `-`
4. **`--quit`** **参数**：追加后导出完成自动退出，适合脚本/无人值守（run.ps1 已默认携带）
5. **日志**：程序控制台输出已设 UTF-8，重定向到文件中文不乱码；run.ps1 运行日志在 `src/feishu-doc-export/run.log`（已被 gitignore）
6. **NuGet 包源**：当前机器默认 NuGet 无配置，在本机执行 `dotnet restore`/`publish`/`test` 必须追加 `--source https://api.nuget.org/v3/index.json`
7. **HTML 导出报告**：每次导出结束自动在 `--exportPath` 根目录生成 `export-report-yyyyMMdd-HHmmss.html`，列出每个文档的 🟢 成功 / 🔴 失败（含失败原因枚举）/ 🟠 自动调整（超长文件名截断 / 截断后同名追加 `_N` 尾号）明细，以及 KPI 4 宫格、状态分布条、目录分布卡、可搜索 / 筛选的 200+ md 明细表、开始/结束/耗时、总产物大小、md / 图片数量。**CSS + JS 全部内联，零 CDN、零联网依赖**，直接双击 HTML 即可离线查看。

## 个人空间文档导出

操作步骤请参考 [change.log](change.log) 中 v0.0.4 版本记录。

## 耗时测试

**700**多个文件导出到本地总耗时**25分钟**

![image](https://github.com/lanhaibo/feishu-doc-export/assets/84184815/77c97483-8c32-4de0-97d8-a0ef9211cab8)

