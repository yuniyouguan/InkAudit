# 墨鉴（InkAudit）

基于 **WinUI 3 (.NET 8)** 打造的极简小说 AI 成分审校桌面端工具，接入朱雀 AI 文本检测接口，支持逐段风险定位、全文上下文合并复核以及原地改稿重检闭环。

## 核心特性

- **一体化极简稿纸工作台**：无繁琐模式切换、无 1px 杂乱边框，采用纯中性微明度色阶（Surface 0 → 3）与自适应暗色字重，专注正文审校体验。
- **逐段 + 全文双视角检测**：
  - `is_merge: false` 逐段定位每段分类（人工 / 疑似 / AI）与置信度，超长段被拆分为多子片段时自动按字长加权聚合三项占比；
  - `is_merge: true` 全文合并复核（默认开启，可一键开关以节省 50% Token），并实时展示 Makers 免费额度实际扣减 Token。
- **原地改写与单段重检**：单击段落展开上下文操作条，双击正文原地编辑，支持「保存并重检」并直接展示改稿前后置信度变化幅度。
- **短段跳过策略**：逐段检测时可按非空白字数阈值自动跳过极短对话段，避免误报与额度浪费；全文复核始终保留完整章节上下文。
- **数据即筛选 & 风险热力图**：点击右侧「人工 / 疑似 / AI」占比卡片即可直接筛选对应段落；支持热力方块点击定位与 `Alt+↑` / `Alt+↓` 快捷跳转。
- **原生系统多语言**：内置 WinUI 3 MRT Core（`Strings/zh-CN` 与 `Strings/en-US`），随 Windows 系统语言自动切换中英文。
- **本地加密凭据存储**：API Key 仅保存在本机 `%LocalAppData%\InkAudit\settings.json`，并通过 Windows DPAPI（`LOCAL=user`）加密保护。

## 目录结构

```text
InkAudit/
├── Assets/                     # 应用图标与启动图资源
├── Models/                     # 领域与视图数据模型
│   ├── AppSettings.cs          # 应用配置模型
│   ├── AuditSummary.cs         # 全文检测汇总模型
│   ├── ParagraphItem.cs        # 单段落审校视图模型
│   └── ZhuqueApiModels.cs      # 朱雀接口请求/响应 DTO
├── Services/                   # 业务与基础设施服务
│   ├── LocalizationService.cs  # WinUI 3 原生 MRT Core 本地化封装
│   ├── ParagraphMapper.cs      # 章节切段、短段过滤与 [start, length] 映射
│   ├── SettingsService.cs      # DPAPI 加密本地配置持久化
│   └── ZhuqueApiService.cs     # 朱雀 HTTP 客户端与统一异常处理
├── Strings/                    # WinUI 3 内置多语言资源表
│   ├── zh-CN/Resources.resw    # 简体中文资源
│   └── en-US/Resources.resw    # 英文资源
├── Theming/                    # 视觉令牌与自适应字重
│   ├── AdaptiveTypography.cs   # 暗色字重自动降档（400→360 / 500→460）
│   └── SemanticPalette.cs      # 浅色/暗色动态语义画刷
├── Views/                      # 子视图与弹窗
│   ├── SettingsDialog.xaml     # 设置弹窗界面
│   └── SettingsDialog.xaml.cs  # 设置弹窗交互逻辑
├── App.xaml / App.xaml.cs      # 应用入口与全局 Surface 色阶资源
├── MainWindow.xaml(.cs)        # 主工作台窗口
├── InkAudit.csproj             # .NET 8 / WinUI 3 项目文件
├── .editorconfig               # 代码风格与格式规范
├── AGENTS.md                   # AI 编码代理开发指南
└── LICENSE                     # CC BY-NC 4.0 许可协议
```

## 环境要求与构建

- **操作系统**：Windows 10 1809（Build 17763）及以上 / Windows 11
- **开发工具**：Visual Studio 2022+（安装「Windows 应用 SDK C# 模板」工作负载）或 .NET 8 SDK

### 命令行构建

```powershell
dotnet build -p:Platform=x64
```

或在 Visual Studio 中打开 `InkAudit.slnx`，选择 `x64` 平台直接按 `F5` 运行。

## 接口配置

首次运行后点击右上角 **设置** 图标：

1. 填入你的朱雀网关 Bearer **API Key**；
2. 默认接口地址为 `https://ai-gateway.edgeone.link/v1/providers/zhuque-text/classify`；
3. 点击 **测试** 验证连通性后保存即可开始使用。

## 开源许可

本项目采用 **知识共享 署名-非商业性使用 4.0 国际许可协议（CC BY-NC 4.0）** 发布，详见 [LICENSE](./LICENSE)：

- **禁止商业化使用**；
- **引用、二次开发或分发必须注明原作者 `yuniyouguan` 并附带本仓库链接**。
