# MultiPlayer-TPS

MultiPlayer-TPS 是一个使用 Unity 6（`6000.3.6f1`）和 C# 开发的多人联机射击项目。项目使用 Mirror 和 KCP 实现联机，包含角色运行时组装、移动与动画同步，以及由服务端处理的开火和伤害判定。构建目标为 Windows 客户端和 Linux 专用服务器。

## 本地体验

用 Unity 打开 [TestScene](Assets/Scenes/TestScene.unity)，进入 Play Mode 后通过 Mirror 面板启动 Host。若本地尚未构建角色资源包，先运行 `Tools > Build AssetBundles`。

## 构建

通过 `Tools > FPSGame-Y1` 构建 Windows 客户端和 Linux Dedicated Server，产物分别位于 `Build/WindowsClient` 和 `Build/LinuxServer`。 [出包与发布流程](项目文档/Skills/出包与发布流程.md)

## 项目文档

- [项目架构与模块介绍](项目文档/项目架构与模块介绍.md)
- [角色生成与运行时组装](项目文档/角色生成与运行时组装.md)
- [出包与发布流程](项目文档/Skills/出包与发布流程.md)
