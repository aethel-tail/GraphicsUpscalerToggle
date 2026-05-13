# Graphics Upscaler Toggle

[English](README.en.md)

FFXIV Dalamud 插件。登录时自动将图形上采样从 DLSS 切换为 FSR 再切回 DLSS，修复 DLSS 在初次加载时未正确生效的问题。

## 安装

将 `GraphicsUpscalerToggle` 文件夹放入 Dalamud 的 `installedPlugins` 目录。

## 使用方法

使用 `/pupscaler` 命令：

| 命令 | 说明 |
|------|------|
| `/pupscaler on` / `enable` | 启用自动切换 |
| `/pupscaler off` / `disable` | 禁用自动切换 |
| `/pupscaler status` | 查看当前状态 |

## 配置

通过 `/pupscaler` → 设置窗口，或直接在 Dalamud 插件设置中打开。

| 设置项 | 默认值 | 说明 |
|--------|--------|------|
| Enable Auto-Toggle | 开启 | 是否在登录时自动执行切换 |
| FSR Delay (s) | 0.5 | 登录后等待多久开始切换 |
| DLSS Delay (s) | 3.0 | 切换到 FSR 后等待多久再切回 DLSS |

## 工作原理

1. 检测玩家登录
2. 等待 `FSR Delay` 秒
3. 读取当前 `GraphicsRezoUpscaleType` 值
4. 设置为 FSR（0）
5. 等待 `DLSS Delay` 秒
6. 设置为 DLSS（1）

## 构建

```bash
dotnet build                 # Debug
dotnet build -c Release      # Release
```

依赖：Dalamud SDK 15.0.0，目标 .NET 10，x64。

## 许可

AGPL-3.0-or-later

## 致谢

本插件由 DeepSeek V4 Pro 协助开发。
