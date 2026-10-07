# AGENTS.md

## 构建 / 运行
- **不要自行运行 `dotnet build` 或其他构建/运行命令。** 作者使用 Rider 构建与运行，编译和测试由作者负责。
- 改完代码后说明改了什么即可，让作者在 Rider 里编译验证。

## 图标
- 一律使用 **RemixIcons**（`MahApps.Metro.IconPacks` 的 `PackIconRemixIcon` / `PackIconRemixIconKind`）。
- **禁止使用 Segoe MDL2 Assets / `IconGlyphs`**（历史遗留，作者反感）。

## ClassIsland2-Plugins
- `ClassIsland2-Plugins/` 是作者为 **ClassIsland 2（CI2）** 开发的插件，因为用到了 LegacyIsland 的部分核心技术，所以和本仓库放在一起。
- **不要改动该目录**（命名、`classisland://` 协议、命名空间、字符串等一律保持原样），它面向 CI2 而不是 LegacyIsland。

## Git
- 仅在被明确要求时提交。
- 提交身份使用：`git -c user.name="douxiba" -c user.email="kriastans@protonmail.com"`。
- 不要提交 `.idea/`。
