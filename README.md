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
3. 确认上采样类型为 DLSS（运行时值 2）
4. 触发引擎重建渲染目标 —— 这一步才会真正重建升采样器，让 DLSS（及 OptiScaler 等外部工具）生效
5. 全程有窗口守卫：拦截游戏在重建时去掉 `WS_CAPTION` 的动作，保证窗口边框与分辨率不受影响

> 注：不能通过 `IGameConfig.Set` 改上采样类型——它会触发游戏的配置变更回调并完整重应用图像设置，在窗口化模式下会破坏窗口状态（边框消失、分辨率异常，参见 goatcorp/Dalamud#2964）。现在走的是「FFXIVClientStructs 直接写 `GraphicsConfig` + 受保护的引擎重建」。
>
> 另注意：**结构体里的枚举与配置文件不同**——配置文件 `0=FSR, 1=DLSS`，运行时结构体 `0=无, 1=FSR, 2=DLSS`。写错值不会报错，只会静静地留在 FSR。

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
