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
| Login Delay (s) | 0.5 | 登录后等待多久开始触发重建 |

## 工作原理

1. 检测玩家登录
2. 等待 `Login Delay` 秒
3. 启动窗口守卫（拦截游戏在重应用显示设置时抹掉窗口边框的动作）
4. 通过 `IGameConfig.Set` 把上采样类型设为 FSR（配置枚举 0），等待 3 秒
5. 再设回 DLSS（配置枚举 1）—— 这一步会让游戏真正重应用显示设置、**重建 DLSS 特性**，从而让 DLSS 生效（含 OptiScaler 等外部工具接管）

> 为什么必须是配置路径：实测「直接写 `GraphicsConfig` 结构体」和「软性 `RequestResolutionChange`」都只跑到 ~60 帧，而配置路径能到 ~84 帧——两种情况下的 `GraphicsConfig`/`Device`/cfg 状态**完全一致**，差别在渲染器内部的 DLSS 特性创建。结构体写法不会触发这一步。
>
> 另注意：**两套枚举不能混用**——配置文件/`IGameConfig` 是 `0=FSR, 1=DLSS`，运行时 `GraphicsConfig` 是 `0=无, 1=FSR, 2=DLSS`；写错会静默地落在错误的上采样器上。

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
