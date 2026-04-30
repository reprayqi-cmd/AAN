# AAN

Avatar Asset Nexus - avatar资源管理系统。

## 项目简介

Avatar Asset Nexus 是一个 Unity Editor 插件，面向 VRChat / VRM / VTuber 资源整理场景，核心目标是：
- 以 Avatar 为中心做资源追踪
- 快速查看 Prefab 与依赖资源
- 按分类管理常用资源

## 主要功能（简述）

- **Avatar 扫描**：扫描当前 Avatar 引用到的 Prefab。
- **Unused Prefab 检查**：对比项目资源，找出当前 Avatar 未使用的 Prefab。
- **依赖浏览**：查看材质、贴图、动画、模型等依赖资产。
- **自定义分类**：支持创建、重命名、拖拽排序分类。
- **最近导入追踪**：监听导入资源并写入最近导入列表。
- **元数据补充**：可附加 Booth 信息、标签、备注等。

## 核心代码结构（代码块）

```text
AvatarAssetNexus_UnityPlugin_v6_6_cache_hierarchy_booth_fix/
└── AvatarAssetNexus/
    ├── Editor/
    │   ├── AvatarAssetNexusWindow.cs         # 主窗口（UI、交互、列表/分类/详情）
    │   ├── AvatarAssetNexusDatabase.cs       # 数据模型与持久化（ScriptableObject）
    │   └── AvatarAssetNexusImportWatcher.cs  # 资源导入监听（AssetPostprocessor）
    └── Documentation/
        └── README.md                        # 插件详细说明
```

## 当前命名层说明

代码命名层已统一到 **Nexus 风格**（例如命名空间 `AvatarAssetNexus`、窗口类 `AvatarAssetNexusWindow`、数据库类 `AvatarAssetNexusDatabase`）。
