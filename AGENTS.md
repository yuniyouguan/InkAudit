# AGENTS.md — 墨鉴（InkAudit）开发指南

本文件面向在本仓库协作的 AI 编码代理，约定项目架构、设计约束、构建方式与代码规范。

## 项目概览

- **项目名称**：墨鉴（InkAudit）
- **技术栈**：C# / .NET 8 (`net8.0-windows10.0.19041.0`) / WinUI 3 (Windows App SDK)
- **最低系统版本**：Windows 10 1809 (`10.0.17763.0`)
- **依赖策略**：零第三方 NuGet 依赖，仅使用 .NET BCL、WinRT 与 Windows App SDK 原生能力

## 目录结构约定

- `Models/`：数据实体、接口 DTO 与实现 `INotifyPropertyChanged` 的视图模型（单文件单一职责）
- `Services/`：无界面耦合的服务层（HTTP 客户端、DPAPI 设置存储、段落映射算法、MRT Core 本地化封装）
- `Theming/`：语义色画刷（`SemanticPalette`）与暗色自适应字重（`AdaptiveTypography`）
- `Strings/zh-CN/`、`Strings/en-US/`：WinUI 3 原生 `.resw` 多语言资源文件
- `Views/`：对话框与独立视图（如 `SettingsDialog`）
- 根目录：`App.xaml(.cs)`、`MainWindow.xaml(.cs)` 及工程/配置文件

## 构建与验证命令

```powershell
# 完整构建（x64）
dotnet build -p:Platform=x64

# 离线/沙箱无还原构建
dotnet build --no-restore -p:Platform=x64
```

每次代码改动后必须确保构建 **0 警告、0 错误**。

## 核心业务与接口约束

1. **朱雀分类接口协议**：
   - Endpoint：`POST https://ai-gateway.edgeone.link/v1/providers/zhuque-text/classify`
   - 鉴权：`Authorization: Bearer <API_KEY>`（**禁止在任何源码、文档或测试文件中硬编码真实 API Key**）
   - `position` 字段为 **`[start, length]`**（起始偏移 + 片段长度），**不是** `[start, end]`；修改 `ParagraphMapper.cs` 时务必保持该约定。
   - `is_merge: false` 在服务端仍会对短段做最小窗口打包，因此一个返回 `segment` 可能覆盖多个自然段；超长段也可能被拆分为多个子片段，需按字符长度加权聚合。
   - Makers 免费额度核算以响应中的 `makers_models_usage.total_tokens` 为准。
2. **多语言约定**：
   - 仅支持 `zh-CN` 与 `en-US`，由 WinUI 3 MRT Core 按 Windows 本机系统语言自动选择，**不添加手动语言切换控件**。
   - 新增任何面向用户的可见文案，必须同步补齐 `Strings/zh-CN/Resources.resw`、`Strings/en-US/Resources.resw` 及 `Services/LocalizationService.cs`。

## UI / UX 设计红线

- **极简无噪音**：禁止添加多余说明标签、帮助文案、占位教学语或装饰性角标，仅保留核心交互控件与必要校验/错误提示。
- **纯色微明度层次**：使用 `App.xaml` 定义的 `Surface0Brush` → `Surface3Brush` 表达层级，**不使用 1px 边框**，**禁用纯黑 `#000000`**（近黑用 `#1A1A19`），聚焦态不加刺眼亮色 outline。
- **暗色字重与数字排版**：暗色模式通过 `AdaptiveTypography` 下调字重一档（`400→360`、`500→460`、`600→540`）；所有数字必须设置 `Typography.NumeralAlignment="Tabular"`。
- **列表控件**：自定义段落列表使用 `ItemsRepeater`，避免在 `ListView` 上强行魔改厚重模板。

## 代码格式与注释规范

- 遵循根目录 `.editorconfig`（UTF-8、4 空格缩进、文件级命名空间、Allman 大括号）。
- 所有 C# 类、公开/私有成员属性与字段、方法必须编写简洁准确的中文 XML 文档注释（`/// <summary>`、`<param>`、`<returns>`）。
- XAML 文件须在各主要布局区块上方添加简明中文注释说明该区块用途。
