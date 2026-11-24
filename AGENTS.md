# AGENTS.md — 墨鉴（InkAudit）开发指南

本文件面向在本仓库协作的 AI 编码代理，约定项目架构、设计约束、构建发布方式与代码规范。

## 项目概览

- **项目名称**：墨鉴（InkAudit）
- **技术栈**：C# / .NET 8 (`net8.0-windows10.0.19041.0`) / WinUI 3 (Windows App SDK)
- **最低系统版本**：Windows 10 1809 (`10.0.17763.0`)
- **依赖策略**：零第三方 NuGet 依赖，仅使用 .NET BCL、WinRT 与 Windows App SDK 原生能力

## 目录结构约定

- `Models/`：数据实体、接口 DTO、筛选枚举与视图模型（单文件单一职责：`AppSettings`、`AuditSegmentItem`、`AuditSummary`、`SegmentFilterKind`、`ZhuqueApiModels`）
- `Services/`：无界面耦合的服务层（`ZhuqueApiService` HTTP 客户端、`SettingsService` DPAPI 配置存储、`ParagraphMapper` 精确字符区间映射、`ReportExportService` Markdown 报告导出、`LocalizationService` MRT Core 本地化封装）
- `Theming/`：语义色画刷与 `conf` 强度映射（`SemanticPalette`）、暗色自适应字重（`AdaptiveTypography`）
- `Strings/zh-CN/`、`Strings/en-US/`：WinUI 3 原生 `.resw` 多语言资源文件
- `Views/`：组件化视图与弹窗（`ManuscriptCanvasView` 连续高亮纸面、`AuditSidebarView` 全文诊断与片段分布、`SettingsDialog` 设置弹窗）
- 根目录：`App.xaml(.cs)`、`MainWindow.xaml(.cs)`（纯窗口协调壳）及工程/配置文件

## 构建与发布命令

```powershell
# 完整构建（x64）
dotnet build -p:Platform=x64

# 离线/沙箱无还原构建
dotnet build --no-restore -p:Platform=x64

# 免环境依赖便携发布（x64 Release，自包含 .NET 8 与 Windows App SDK 运行时）
dotnet publish -c Release -p:Platform=x64 -p:PublishProfile=win-x64
```

每次代码改动后必须确保构建 **0 警告、0 错误**。
发布配置约束：`PublishTrimmed` 保持 `False`（避免裁剪 `System.Text.Json` 反射序列化元数据），`PublishSingleFile` 保持 `False`（WinUI 3 `WindowsAppSDKSelfContained` 不支持单文件）。

## 核心业务与接口约束

1. **朱雀分类接口协议**：
   - Endpoint：`POST https://ai-gateway.edgeone.link/v1/providers/zhuque-text/classify`
   - 鉴权：`Authorization: Bearer <API_KEY>`（**禁止在任何源码、文档或测试文件中硬编码真实 API Key**）
   - 全文原样送检，**不做短段过滤或前端人为切段**；支持 `is_merge: false`（精确切片定位）与可选并发 `is_merge: true`（全文上下文合并复核）。
   - `position` 字段为 **`[start, length]`**（Unicode 码点起始偏移 + 片段长度），**不是** `[start, end]`；修改 `ParagraphMapper.cs` 时务必保持该约定并做好 Unicode 码点到 UTF-16 下标换算，确保与 `RichEditBox` 单字符 `\r` 换行 1:1 对齐。
   - `conf` 字段始终表示 **AI 风险概率（0~1）**，人工概率对应 `1 - conf`；正文背景色分红（AI）、黄（疑似）、绿（人工），鲜艳度与混合深度由 `SemanticPalette.GetConfidenceStrength` 换算（红/黄为 `conf` 越高越鲜明，绿为 `conf` 越低越鲜明）。
   - Makers 免费额度核算以响应中的 `makers_models_usage.total_tokens` 为准。
2. **连续编辑器与悬停气泡约束**：
   - 仅支持整篇检测；在 `RichEditBox` 刷完高亮背景色后必须调用 `Document.ClearUndoRedoHistory()` 锁定撤销基线，防止 `Ctrl+Z` 误撤回检测高亮或检测结果。
   - `RichEditBox.TextChanged` 在修改格式时会异步触发，必须通过纯文本快照比对过滤仅格式变更事件，避免误判改稿或锁死悬停气泡。
   - 悬停定位调用 `ITextRange.GetRect` 时必须携带 `PointOptions.AllowOffClient`，避免滚出首屏时抛出异常。
3. **多语言约定**：
   - 仅支持 `zh-CN` 与 `en-US`，由 WinUI 3 MRT Core 按 Windows 本机系统语言自动选择，**不添加手动语言切换控件**。
   - 新增任何面向用户的可见文案（含 Markdown 导出报告模板），必须同步补齐 `Strings/zh-CN/Resources.resw`、`Strings/en-US/Resources.resw` 及 `Services/LocalizationService.cs`。

## UI / UX 设计红线

- **极简无噪音**：禁止添加多余说明标签、帮助文案、占位教学语或装饰性角标，全局仅保留顶部原生 `TitleBar` 中的**唯一主检测按钮**（检测中原位切换为取消）与必要校验/错误提示。
- **纯色微明度层次**：使用 `App.xaml` 定义的 `Surface0Brush`（底）→ `Surface1Brush`（右侧诊断栏）→ `Surface2Brush`（最亮正文纸面）→ `Surface3Brush`（悬停/胶囊）表达层级，**不使用 1px 边框**，**禁用纯黑 `#000000`**（近黑用 `#1A1A19`），并覆盖 WinUI 原生亮蓝强调色。
- **规避 WinUI `BrushTransition` 透明插值缺陷**：禁止在透明/半透明背景画刷上使用 `BrushTransition`，避免切换态发灰或卡帧。
- **暗色字重与数字排版**：暗色模式通过 `AdaptiveTypography` 下调字重一档（`400→360`、`500→460`、`600→540`）；所有数字必须设置 `Typography.NumeralAlignment="Tabular"`。
- **列表控件**：热力方块矩阵与高风险片段列表使用 `ItemsRepeater`，避免在 `ListView` 上强行魔改厚重模板。

## 代码格式与注释规范

- 遵循根目录 `.editorconfig`（UTF-8、CRLF、4 空格缩进、文件级命名空间、Allman 大括号）。
- 所有 C# 类、公开/私有成员属性与字段、方法必须编写简洁准确的中文 XML 文档注释（`/// <summary>`、`<param>`、`<returns>`）。
- XAML 文件须在各主要布局区块上方添加简明中文注释说明该区块用途。
