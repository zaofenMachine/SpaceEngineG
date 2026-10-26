# SpaceEngineG

Unity 6 / URP 程序化宇宙探索器。使用真实米制半径与轨道距离、高精度扇区坐标和自适应球面地形，可从星际空间连续飞到程序化地表。

这是可运行的早期探索版本；基础系统与图形验证证据随源码维护，尚未达到 SpaceEngine 的天体种类、画面质量和科学完整度。

## 启动

使用 Unity Hub 打开仓库中的 **`SpaceEngineG/`** 子目录，版本 **6000.5.7f1**。载入 `Assets/Scenes/Universe.unity` 后按 Play。项目已包含入口场景、资源材质和默认配置，无需下载美术包。

若需重建场景/默认资源，运行菜单 `Space Engine > Setup Explorer Scene`。Windows 打包菜单为 `Space Engine > Build Windows Explorer`，输出 `Builds/Windows/SpaceEngineG.exe`。

## 操作

| 操作 | 按键 |
| --- | --- |
| 前后左右飞行 | WASD |
| 上下移动 / 翻滚 | E、Q / Z、C |
| 转动视角 | 按住鼠标右键 |
| 调整速度 / 加速 | 滚轮 / Shift |
| 连续降落 / 连续上升 | G / R |
| 选中行星的轨道观察位置 | F |
| 下一颗行星 / 下一个恒星系快捷跳转 | Tab / N |
| 取消自动导航 / 暂停时间 | Esc / Space |
| 保存 / 恢复飞行状态 | F5 / F9 |
| 显示或隐藏界面 | F1 |

自由飞行和 G/R 导航连续更新同一观察者坐标；F、Tab、N 是明确的定位/传送快捷方式。探索器允许超光速移动，未实现相对论飞行动力学。

游戏内右上角 `SETTINGS` 支持调整曝光、视场、星星亮度、地形预算及宇宙 Seed。全部地形、大气采样与光学参数位于 `Assets/SpaceEngine/Resources/UniverseSettings.asset`。参数在启动时复制，游戏内调整不会覆盖源配置。

## 验证

Unity Test Runner 包含 EditMode 数学/网格测试和 PlayMode 集成测试。菜单还提供：

- `Space Engine > Validate Shaders`：实际图形设备上的 Shader Pass 与变体编译检查。
- `Space Engine > Validate Atmosphere Pixels`：HDR GPU 像素读回，分别验证真空、天空、尺度不变和近地深度终止。
- `Space Engine > Run Rendered Flight Acceptance`：真实渲染从 0.03 光年连续下降至约 2 米地表，保存六个高度/星系截图、运行时断言和性能 JSON。

证据输出到 `Artifacts/Latest/`。`Artifacts/Iteration01/` 保留了首轮自动断言通过但视觉未通过的截图，具体审查见目录说明。截图审查不能由 Shader 编译或网格计数代替。

如果已在 Unity 启动项目附带的 Unity MCP，可以通过 Python 标准库脚本复现测试：

```powershell
$env:UNITY_MCP_URL = 'http://127.0.0.1:8055/mcp' # 改为当前 Editor 的本地服务端口
python Scripts/validate.py --tests --graphics --flight
```

Windows 程序也能独立运行验证航线：

```powershell
.\Builds\Windows\SpaceEngineG.exe --verify-flight --quit-after-verification --evidence-path C:\Temp\SpaceEngineEvidence
```

验证航线用固定虚拟时间步快速驱动导航，帧耗时统计衡量渲染与流式地形吞吐，不能当作实际旅行时长。性能报告区分 Editor 和独立程序，不含 GPU 时间戳测量。

## 实现说明

[开发阶段与验收标准](Docs/PLAN.md) · [系统架构与已知边界](Docs/ARCHITECTURE.md) · [原始论文和开源方案调研](Docs/RESEARCH.md)

当前实现整数扇区加 double 局部坐标、Seed 恒星种群和开普勒轨道、六面四叉树地形、近地真实几何起伏、单次 Rayleigh/Mie 大气、自由飞行、参考系切换和原子存档。后续重点为 LOD 过渡、GPU/Burst 生成、更多天体类型、多次散射、星图及长时间飞行验证。
