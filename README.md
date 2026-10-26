# SpaceEngineG

Unity 6 / URP 程序化宇宙探索器。使用真实米制半径与轨道距离、高精度扇区坐标和自适应球面地形，可从星际空间连续飞到程序化地表。

这是可运行的早期探索版本；基础系统与图形验证证据随源码维护，尚未达到 SpaceEngine 的天体种类、画面质量和科学完整度。

当前版本 **0.2.0**：新增六分量三维银河、七类恒星、七类行星、卫星、程序化云带、行星环和熔岩裂隙。**102 项 Unity 测试、17 项 GPU 像素检查**通过；独立 Windows 程序分别完成降落与银河巡检。详细结果、性能与限制见 [0.2 实测记录](Docs/VALIDATION-V020.md)。

![独立 Windows 程序实际银河总览](Artifacts/V020/PlayerGalaxy/01-galaxy-oblique.png)

![程序化气态巨行星及环](Artifacts/V020/PlayerGalaxy/07-gasgiant.png)

## 启动

使用 Unity Hub 打开仓库中的 **`SpaceEngineG/`** 子目录，版本 **6000.5.7f1**。载入 `Assets/Scenes/Universe.unity` 后按 Play。项目已包含入口场景、资源材质和默认配置，无需下载美术包。

若需重建场景/默认资源，运行菜单 `Space Engine > Setup Explorer Scene`。Windows 打包菜单为 `Space Engine > Build Windows Explorer`，输出 `Builds/Windows-v0.2.0/SpaceEngineG.exe`。

本地交付包为 `Builds/SpaceEngineG-0.2.0-windows-x64.zip`（40.15 MB），解压后运行其中的 `SpaceEngineG.exe`，保留同目录数据和 DLL。二进制不提交 Git；源码用户按上述菜单构建。[分发包清单与 SHA-256](Artifacts/V020/package-manifest.json) 记录实际验收版本。旧版 0.1.0 程序、ZIP 和证据均保留。

## 操作

| 操作 | 按键 |
| --- | --- |
| 前后左右飞行 | WASD |
| 上下移动 / 翻滚 | E、Q / Z、C |
| 转动视角 | 按住鼠标右键 |
| 调整速度 / 加速 | 滚轮 / Shift |
| 连续降落或接近云顶 / 连续上升 | G / R |
| 选中行星的轨道观察位置 | F |
| 下一颗行星 / 下一个恒星系快捷跳转 | Tab / N |
| 定位当前恒星 / 银河概览 | T / M |
| 返回起始系统 / 中央黑洞参考面 | Home / B |
| 取消自动导航 / 暂停时间 | Esc / Space |
| 保存 / 恢复飞行状态 | F5 / F9 |
| 显示或隐藏界面 | F1 |

自由飞行和 G/R 导航连续更新同一观察者坐标；F、Tab、N、T、M、Home、B 是明确的定位/传送快捷方式。气态/冰巨星没有固体降落面，接近停在可配置的云顶安全净空。探索器允许超光速移动，未实现相对论飞行动力学。

银河拥有薄盘、厚盘、旋臂、核球、棒和恒星晕；默认盘直径 104,000 光年。远景的 60,000 个三维光核代表未解析的恒星人口，局部目录按同一个密度模型准入。它们具有空间位置和视差，不是天空盒，也不是一千亿颗独立恒星实例。黑洞目前显示几何参考面，尚无透镜或吸积盘。

游戏内右上角 `SETTINGS` 支持调整曝光、视场、星星亮度、地形预算及宇宙 Seed。银河、云层细分、地形、大气和安全净空参数位于 `Assets/SpaceEngine/Resources/UniverseSettings.asset`；细环、裂隙和 Shader 细节参数位于同目录材质。参数在启动时复制，游戏内调整不会覆盖源配置。

生成版本 2 使用 `flight-v2.json`，保存银河参数与当前参考视角。旧 `flight.json` 保留，使用 0.1.0 程序打开；不会把新世界地形套在旧存档上。

## 验证

Unity Test Runner 包含 EditMode 数学/网格测试和 PlayMode 集成测试。菜单还提供：

- `Space Engine > Validate Shaders`：实际图形设备上的 Shader Pass 与变体编译检查。
- `Space Engine > Validate Atmosphere Pixels`：HDR GPU 像素读回，分别验证真空、天空、尺度不变和近地深度终止。
- `Space Engine > Validate Celestial Pixels`：验证云层 Seed、旋转、环影、云顶细节和熔岩网格密度不变性。
- `Space Engine > Run Rendered Flight Acceptance`：真实渲染从 0.03 光年连续下降至约 2 米地表，保存六个高度/星系截图、运行时断言和性能 JSON。
- `Space Engine > Run Galaxy And Body Acceptance`：银河三个视角、全部行星/恒星类别、卫星、云顶接近和中央参考面，默认 Seed 共 19 张实际截图。

0.2 证据输出到 `Artifacts/V020/`；独立程序证据位于其中的 `PlayerFlight/` 和 `PlayerGalaxy/`。`Iteration01/`、`Iteration02/` 保留了断言通过但视觉未通过的截图。截图审查不能由 Shader 编译或网格计数代替；旧版证据不覆盖。

如果已在 Unity 启动项目附带的 Unity MCP，可以通过 Python 标准库脚本复现测试：

```powershell
$env:UNITY_MCP_URL = 'http://127.0.0.1:8055/mcp' # 改为当前 Editor 的本地服务端口
python Scripts/validate.py --tests --graphics --flight --galaxy
```

Windows 程序也能独立运行验证航线：

```powershell
.\Builds\Windows-v0.2.0\SpaceEngineG.exe --verify-flight --quit-after-verification --evidence-path C:\Temp\SpaceEngineFlight
.\Builds\Windows-v0.2.0\SpaceEngineG.exe --verify-galaxy --quit-after-verification --evidence-path C:\Temp\SpaceEngineGalaxy
```

验证航线用固定虚拟时间步快速驱动导航，帧耗时统计衡量渲染与流式地形吞吐，不能当作实际旅行时长。性能报告区分 Editor 和独立程序，不含 GPU 时间戳测量。

独立程序截图要求实际可见窗口。完成独立程序验证并记录退出码后，运行 `python Scripts/package_windows.py` 可生成分发 ZIP、完整性检查和哈希清单；打包脚本要求两条路线使用相同构建 GUID。`source-revision.txt` 记录构建前的源码提交。

## 实现说明

[开发阶段与验收标准](Docs/PLAN.md) · [系统架构与已知边界](Docs/ARCHITECTURE.md) · [原始论文和开源方案调研](Docs/RESEARCH.md)

当前实现整数扇区加 double 局部坐标、空间银河和恒星种群、层级开普勒轨道、六面四叉树地形、近地真实几何起伏、单次 Rayleigh/Mie 大气、程序化云层/环/熔岩、自由飞行、参考系切换和原子存档。后续重点为地形细节与 LOD 过渡、气态近景解析几何、GPU/Burst 生成、多次散射、星团/星云、星图及长时间飞行验证。
