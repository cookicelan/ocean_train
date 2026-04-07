# BRC Marker

BRC Marker 是一款集数据标注、模型训练与推理于一体的桌面端标注工具软件。

## 功能模块

| 模块 | 说明 |
|------|------|
| **数据预处理** | 对原始音频/信号数据生成频谱图，支持参数配置与预览 |
| **标注** | 在图像上进行目标框选与标签标注，生成 LabelMe 格式标注文件 |
| **训练** | 基于标注数据启动目标检测模型训练，实时可视化训练进度 |
| **推理** | 加载训练好的模型，对新数据进行批量推理预测 |

## 技术栈

- **UI 框架:** [Avalonia](https://avaloniaui.net/) 11.x + [Semi.Avalonia](https://github.com/irihitech/Semi.Avalonia) 主题
- **MVVM:** CommunityToolkit.Mvvm
- **运行时:** .NET 10

## 项目结构

```
BRC.Marker.slnx                  # 解决方案文件
src/
  BRC.Marker/                    # 主应用程序库
    Pages/                       # 各功能页面
    Views/                       # 主窗口与主视图
    ViewModels/                  # 视图模型
    Assets/                      # 图标等资源
    Themes/                      # 自定义主题样式
  BRC.Marker.Desktop/            # 桌面平台入口
  Directory.Packages.props       # NuGet 包版本集中管理
```

## 构建与运行

### 环境要求

- .NET 10 SDK

### 构建

```bash
dotnet build src/BRC.Marker.Desktop/BRC.Marker.Desktop.csproj
```

### 运行

```bash
dotnet run --project src/BRC.Marker.Desktop/BRC.Marker.Desktop.csproj
```

### 发布

```bash
# 单文件发布
dotnet publish src/BRC.Marker.Desktop/BRC.Marker.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Native AOT 发布
dotnet publish src/BRC.Marker.Desktop/BRC.Marker.Desktop.csproj -c Release -r win-x64 -p:PublishAot=true
```

## 许可证

Copyright (c) 2026 BRC. All Rights Reserved.

本软件为 BRC 专有财产。详见 [LICENSE](LICENSE)。

第三方开源组件许可信息见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
