# 墨鉴（InkAudit）

基于 **WinUI 3 (.NET 8)** 打造的极简 AI 文本成分审校桌面端工具，接入朱雀 AI 文本检测接口，提供连续纸面精确字符级红/黄/绿高亮、悬停占比气泡、全文合并复核以及 Markdown 报告导出。

## 核心特性

- **一体化纯纸面连续工作台**：采用暖灰石底与纯白纸面微明度层次（无 1px 杂乱边框、全站禁用纯黑、暗色自适应字重），粘贴、阅读、改写与高亮全部在同一连续文档内完成，顶部原生标题栏仅保留唯一主检测按钮（支持 `Ctrl+Enter` 检测、`Esc` 取消）。
- **精确字符级红 / 黄 / 绿动态高亮**：严格按朱雀返回的字符位置 `position = [start, length]` 为正文着色（红 = AI、黄 = 疑似、绿 = 人工），背景饱和度与浓度随 `conf` 确信强度连续渐变，浅色与暗色主题均保持高对比可读性。
- **鼠标悬停占比气泡**：鼠标移至任意高亮片段上方，即时浮现深色占比气泡，明确展示该片段分类、序号、字数以及 **AI / 人工双项占比**（如 `AI 74.6% · 人工 25.4%`）。
- **全文合并复核 + 片段热力导航**：支持 `is_merge: false`（精确切片定位）与 `is_merge: true`（全文上下文合并复核）并发执行；点击右侧「人工 / 疑似 / AI」图例可单色聚焦高亮对应文字，点击片段热力方块或使用 `Alt+↑` / `Alt+↓` 可直接滚动选中风险原文。
- **Markdown 检测报告导出**：检测完成后点击页眉导出按钮（或按 `Ctrl+S`），即可导出结构化 GFM Markdown 报告，包含元信息、全文总览表、朱雀片段明细表及逐片标注正文。
- **安全撤销保护**：检测着色完成后自动锁定撤销基线，`Ctrl+Z` 仅撤销用户后续手动编辑，不会误撤回高亮或检测结果；撤回至原检测正文时自动复原高亮与新鲜状态。
- **原生系统多语言与本地加密**：基于 WinUI 3 MRT Core（`zh-CN` / `en-US`）随 Windows 系统语言自动切换；API Key 仅保存在本机 `%LocalAppData%\InkAudit\settings.json`，并通过 Windows DPAPI（`LOCAL=user`）加密保护。

## 目录结构

```text
InkAudit/
├── Assets/                          # 应用图标与启动图资源
├── Models/                          # 领域与视图数据模型
│   ├── AppSettings.cs               # 应用配置模型
│   ├── AuditSegmentItem.cs          # 朱雀精确字符片段视图模型
│   ├── AuditSummary.cs              # 全文检测汇总模型
│   ├── SegmentFilterKind.cs         # 正文高亮聚焦筛选枚举
│   └── ZhuqueApiModels.cs           # 朱雀接口请求/响应 DTO
├── Properties/PublishProfiles/      # 免环境依赖自包含发布配置（win-x64 等）
├── Services/                        # 无界面耦合的业务服务
│   ├── LocalizationService.cs       # WinUI 3 原生 MRT Core 本地化封装
│   ├── ParagraphMapper.cs           # 换行归一化与 [start, length] 精确偏移映射
│   ├── ReportExportService.cs       # Markdown 检测报告生成服务
│   ├── SettingsService.cs           # DPAPI 加密本地配置持久化
│   └── ZhuqueApiService.cs          # 朱雀 HTTP 客户端与统一异常处理
├── Strings/                         # WinUI 3 内置多语言资源表
│   ├── zh-CN/Resources.resw         # 简体中文资源
│   └── en-US/Resources.resw         # 英文资源
├── Theming/                         # 视觉令牌与自适应字重
│   ├── AdaptiveTypography.cs        # 暗色字重自动降档（400→360 / 500→460）
│   └── SemanticPalette.cs           # 浅/暗语义画刷与 conf 强度高亮色阶
├── Views/                           # 组件化视图与弹窗
│   ├── ManuscriptCanvasView.xaml(.cs) # 左侧连续高亮纸面、悬停气泡与轻提示组件
│   ├── AuditSidebarView.xaml(.cs)   # 右侧全文总览与片段热力导航组件
│   └── SettingsDialog.xaml(.cs)     # 设置弹窗
├── App.xaml / App.xaml.cs           # 应用入口与全局主题色阶/滚动条样式
├── MainWindow.xaml(.cs)             # 主窗口协调壳与检测流程编排
├── InkAudit.csproj                  # .NET 8 / WinUI 3 项目文件
├── .editorconfig                    # 代码风格与格式规范
├── AGENTS.md                        # AI 编码代理开发指南
└── LICENSE                          # CC BY-NC 4.0 许可协议
```

## 环境要求与构建

- **运行环境**：Windows 10 1809（Build 17763）及以上 / Windows 11（免环境便携版无需预装 `.NET` 或 `Windows App SDK`）
- **开发环境**：Visual Studio 2022+（安装「Windows 应用 SDK C# 模板」工作负载）或 .NET 8 SDK

### 开发构建

```powershell
dotnet build -p:Platform=x64
```

或在 Visual Studio 中打开 `InkAudit.slnx`，选择 `x64` 平台直接按 `F5` 运行。

### 打包免环境依赖便携版（x64）

```powershell
dotnet publish -c Release -p:Platform=x64 -p:PublishProfile=win-x64
```

发布产物输出至 `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\`，已完整内置 `.NET 8` 与 `Windows App SDK` 运行时，将整个目录打包分发后双击 `InkAudit.exe` 即可运行。

### 构建 Windows x64 安装包

在已安装 WiX Toolset 3.14 的 Windows 上，从仓库根目录运行以下 PowerShell 7 命令。

```powershell
./installer/Build.ps1 -WixBin 'C:\Program Files (x86)\WiX Toolset v3.14\bin'
```

脚本在 `bin/installer/v<版本>/` 生成中文 MSI 和同版本便携 ZIP。MSI 安装时会在桌面与开始菜单各创建一个“墨鉴”快捷方式。已安装旧版 MSI 的用户可直接运行新版安装包升级。

## 接口配置

首次运行后点击右上角 **设置** 图标：

1. 填入你的朱雀网关 Bearer **API Key**；
2. 默认接口地址为 `https://ai-gateway.edgeone.link/v1/providers/zhuque-text/classify`；
3. 点击 **测试** 验证连通性后保存即可开始使用。

## 开源许可

本项目采用 **知识共享 署名-非商业性使用 4.0 国际许可协议（CC BY-NC 4.0）** 发布，详见 [LICENSE](./LICENSE)：

- **禁止商业化使用**；
- **引用、二次开发或分发必须注明原作者 `yuniyouguan` 并附带本仓库链接**。
