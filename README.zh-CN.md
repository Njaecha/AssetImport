# AssetImport

[English](README.md) | [简体中文](README.zh-CN.md)

将外部 3D 模型导入 **Koikatsu（KK）** 和 **Koikatsu Sunshine（KKS）**，用作 Studio 物件或角色饰品。常规模型格式通过 [Assimp](https://assimp.org/) 读取，PMX 2.0 使用专用读取器。

本项目基于 [Njaecha/AssetImport](https://github.com/Njaecha/AssetImport)，增加了 KK 支持、PMX 导入、MMDD 动作映射和 DynamicBone 集成。

## 功能

- 导入 FBX、OBJ、glTF、PMX 等格式的网格、材质、贴图、骨架及支持的 BlendShape。
- 将导入资源随 Studio 场景或角色饰品保存。
- 为 PMX 骨骼动画和顶点表情生成 MMDD 3 映射。
- 根据支持的 PMX 元数据创建 DynamicBone 骨链与身体碰撞体，并在导入面板控制物理。
- 按物理骨链调整向下受力和保形强度，并应用支持的 PMX 转角限制、地面碰撞和初始稳定。
- 可选在物理分支末端启用实验性胸部接触，支持手动标记物理链、调整球形或胶囊形体积，以及镜像调整和预览。
- 解码 BMP、TGA 和常见 DDS 贴图，可选用 `.cube` LUT 转换主贴图颜色。
- 在后台处理贴图，通过 MaterialEditor 应用到模型。

导入结果是物件或饰品，不是带服装槽和换装功能的游戏原生角色。

## 运行要求

Windows 64 位系统，以及对应游戏的前置插件：

| 依赖 | KK | KKS |
| --- | --- | --- |
| BepInEx | 5.4.22 或更新的 5.x 版本 | 5.4.22 或更新的 5.x 版本 |
| 游戏 API | KKAPI 1.45.1+ | KKSAPI 1.43.0+ |
| MaterialEditor | 4.0.3+ | 3.13.5+ |
| LoadFileLimitedFix | KK 版 | KKS 版 |

仅播放 VMD 时需要 MMDD 3。DynamicBoneEditor / KKPE / KKSPE 为可选编辑联动。前置插件需单独安装。

## 安装

1. 从 [Releases](https://github.com/PoeraWork/AssetImport/releases) 下载对应游戏的 `KK_AssetImport_*_Packed.zip` 或 `KKS_AssetImport_*_Packed.zip`，也可以[从源码构建](CONTRIBUTING.md)。
2. 退出游戏和 Studio。
3. 将包内的 `BepInEx` 和 `runtimes` 一起解压到游戏根目录，保留目录结构。
4. 启动游戏，按 **左 Alt + I** 打开面板。角色编辑器中需先选中一个已有饰品。

升级时覆盖已有安装，并将多余的旧插件副本移出 `BepInEx/plugins`。KK 与 KKS 使用不同运行库，请使用对应游戏的包。不要把 ZIP 放进 `mods`。

包内安装说明：[KK](docs/INSTALL-KK.txt) · [KKS](docs/INSTALL-KKS.txt)。

开发构建可从成功的 [Actions 运行](https://github.com/PoeraWork/AssetImport/actions/workflows/build.yml)中下载 `packages-…` 产物，保留 30 天；下载需登录 GitHub。构建与发布方式见 [CI 和发布说明](CONTRIBUTING.md#ci-and-releases)。

## 使用

在插件设置列表（BepInEx Configuration Manager）中，展开 **KK_AssetImport / KKS_AssetImport → _General_ → Language**，可选择 **English** 或 **简体中文**。默认英文，切换后立即生效，已有状态提示也会更新。无需安装翻译插件；也可修改 `BepInEx/config/org.njaecha.plugins.assetimport.cfg` 中的同一设置。诊断日志保留原有标识与技术详情。

选择模型，调整导入选项和比例，检查预览后完成导入。模型配套贴图应保留原来的相对目录。保存、复制物件或切换坐标前，请等待面板显示 **Textures ready**。

PMX 建议先使用 **PMX / VMD preset**，比例设为 **1**。在 Studio 中，通过 MMDD 的 **Load VMD Advanced** 选择生成的 **AssetImport PMX Auto** 定义。映射、物理、贴图转色及排错方法见 [PMX 使用指南](docs/PMX-SUPPORT.md)。

PMX 支持尚不完整：未实现 PMX 2.1、完整 MMD 刚体/关节模拟、toon/sphere 着色和骨骼/UV/材质表情；SDEF 使用线性蒙皮近似。无效的可选约束可能被跳过并记录诊断，因此导入成功不代表与 MMD 表现完全一致。

## 开发与问题反馈

构建环境、自动检查和打包步骤见 [CONTRIBUTING.md](CONTRIBUTING.md)。

主面板的 **Debug** 默认关闭，统一控制详细日志、物理可视化、诊断报告和资源转储。插件配置中的 **_General_ / Debug** 是同一个开关。调整碰撞体积或排查问题时可开启；关闭后物理功能和必要错误提示仍正常工作。报告位置见[诊断说明](docs/PMX-SUPPORT.md#debug-mode)。

请通过 [Issues](https://github.com/PoeraWork/AssetImport/issues) 报告问题，附上游戏名称、`BUILD.txt` 或 Debug 面板中的构建标识、复现步骤，以及 `BepInEx/LogOutput.log` 中的相关日志（或 `output_log.txt`）。动作问题请补充帧数范围和导入设置。分享模型或动作前请确认其许可允许。

## 致谢

- [Njaecha/AssetImport](https://github.com/Njaecha/AssetImport)：原插件。
- [Assimp](https://github.com/assimp/assimp)、AssimpNet 和 [AssimpNetter](https://github.com/Njaecha/AssimpNetter)：模型读取。
- [IllusionMods](https://github.com/IllusionMods)：游戏 API 和插件集成。

第三方依赖的许可声明见 [docs/licenses](docs/licenses) 和安装包。
