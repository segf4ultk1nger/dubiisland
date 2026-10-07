<!--markdownlint-disable MD001 MD033 MD041 MD051-->

<div align="center">

# <image src="ClassIsland/Assets/DubiIsland-AppLogo.png" height="72" width="72"/> <br/> LegacyIsland

**基于 ClassIsland 1.7 分支打造，持续演进的桌面课表岛。**

ClassIsland 是一款在 Windows 桌面上显示课表、提醒等信息的工具。
LegacyIsland 在 1.7 的基础上继续开发，并向下兼容 Windows 7 SP1。

[官网](https://sfkgr.me/a/legacyisland) · [文档](https://docs.classisland.tech) · [SIFWARE QQ 群](https://qm.qq.com/q/5Szl0Vtqa4)

</div>

## 特点

- **自绘课表岛**：主界面重构为纯自绘的灵动岛式课表窗口，支持停靠回弹 / 果冻形变、组件逐个入场、超椭圆圆角与界面边距等细节。
- **脚本化自动化**：自动化与规则集改用 JavaScript，提供 `lessons` / `window` / `weather` 等只读 API、`on.*` 触发器与行动函数；旧配置可一键导入。
- **对齐 ClassIsland 2**：组件配置与档案模型迁移到 CI2 格式，可直接导入 ClassIsland 的档案与组件配置。
- **重做的设置体验**：设置页搜索（模糊 + 拼音）、组件页「树 + 属性 + 预览」、存储 / 外观 / 天气页重做、字体按需下载。
- **安全增强**：TOTP 认证、DLL 注入检测、进程守护与自身伪装；UIAccess 超级置顶、以管理员身份重启 / 开机启动。
- **轻量**：迁移到 .NET Framework 4.7.2，发布为轻量单文件 exe，移除 gRPC / 遥测 / 自动更新 / 主题市场等。

## 运行要求

- Windows 7 SP1 及以上（推荐 Windows 10+），需本机安装 .NET Framework 4.7.2
- 解压到**独立、无中文路径**的文件夹后运行，勿放网盘同步目录或「下载」文件夹

## 开发

作者使用 Rider，参考[配置 ClassIsland 开发环境](https://docs.classisland.tech/dev/get-started/devlopment.html)（本项目目标框架为 net472）。

## 致谢与许可

由 segf4ultk1nger 与 Xavo Industries 联合出品，受 [DuguSand/class_form](https://github.com/DuguSand/class_form) 启发。
本体基于 [GPL-3.0](LICENSE.txt)，部分库基于 LGPL-3.0，详见 [LICENSE](LICENSE.txt)。
