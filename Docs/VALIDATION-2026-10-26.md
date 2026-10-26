# 0.1.0 实测与交付记录

日期：2026-10-26（Asia/Singapore）。Unity 6000.5.7f1，URP 17.5.0，Windows 10，NVIDIA GeForce RTX 4070，Direct3D 12。

## 分阶段结果

| 阶段 | 结果 | 原始证据 |
| --- | --- | --- |
| 高精度与程序生成 | 坐标跨界、负数、银河位置毫米位移、Seed 重现、轨道求根/周期、方向地形连续性与范围通过 | `Artifacts/Latest/editmode-tests.json`，Core/Tests 源码 |
| 近地形与 LOD | 原生生命周期、L20、双面裙边、粗细边缘覆盖、轨道最低细分和米级几何回归通过 | 共 42 个 EditMode 用例全部通过 |
| 探索集成 | 真实生成网格、地表高度约束、参考体/星系无位移切换、精度、原子存档、损坏存档拒绝、实际 Update 键盘 W/G 接入通过 | 共 7 个 PlayMode 用例全部通过；`Artifacts/Latest/playmode-tests.json` |
| Shader | 4 个自定义 Shader，所有 Pass 和两种大气变体通过；无 Shader 编译消息 | `Artifacts/Latest/shaders.json` |
| 实际 GPU 像素 | 真空黑、蓝色天空、停用后去除散射、×0.001 尺度一致、独立 Unlit 对照、10 m 地表深度终止通过 | 6 项检查；64×64 HDR 浮点读回；`Artifacts/Latest/atmosphere-pixels.json` |
| Editor 完整航线 | 317 个固定导航步，自 0.03 ly 连续下降到约 2 m；6 张截图已实际逐张审查 | `Artifacts/Latest/runtime-verification.json` 及 PNG |
| Windows 构建 | 成功，27.25 s，105,857,383 bytes，0 错误/0 警告 | `Artifacts/Latest/build.txt` |
| Windows 独立运行 | 完整航线通过，6 张实际截图、0 运行时错误，正常退出码 0；已查看截图联系表并与 Editor 观察一致 | `Artifacts/Player/runtime-verification.json`、`exit-code.txt`、PNG |

## 独立程序性能

1600×900，设定帧率上限 120 FPS、VSync 关闭。317 个移动帧平均 8.43 ms，P50 8.33 ms，P95 8.73 ms，最大 16.81 ms。数据是 Unity 实际帧间隔，包含运行时与地形流式构建开销；不是单独 GPU 时间，也不是解除上限后的极限吞吐。冷启动首个星际截图时平滑帧间隔仍约 20.57 ms；不得将下面稳定帧或移动帧均值当作全部启动帧性能。

| 观察点 | 净地表高度 | 选中行星最大 LOD | 全系统可见块 | 全系统三角形（含裙边） | Unity 分配内存 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 星际 | 0.03 ly | 0 | 35 | 16,800 | 153.64 MiB |
| 轨道 | 12,642.58 km | 3 | 205 | 98,400 | 160.07 MiB |
| 接近大气 | 188.44 km | 5 | 61 | 29,280 | 160.16 MiB |
| 低空 | 1.37 km | 12 | 111 | 53,280 | 161.84 MiB |
| 地表 | 2.007 m | 20 | 189 | 90,720 | 163.46 MiB |
| 第二恒星系轨道 | 11,998.39 km | 3 | 186 | 89,280 | 160.03 MiB |

内存来自 `Profiler.GetTotalAllocatedMemoryLong`，包含引擎对象、材质等，不能当作纯网格成本。独立 CPU Release 探针中，100,000 次完整地形采样约 267 ms，res12 网格构建均值约 1.61 ms；这是非 Unity 渲染线程微基准，与上述独立程序帧耗时分开使用。

## 实际看图发现并修复的问题

1. 独立 C# 编译未发现 `MaterialPropertyBlock` 在 MonoBehaviour 字段初始化中创建原生对象的错误；Unity EditMode 暴露后，已将原生资源初始化移到 Configure。
2. 首轮航线断言通过，但大气过曝且近景地形平滑。实际截图审查判为不通过，修正太阳辐亮度归一化并加入真实米制高度频段；保存于 `Artifacts/Iteration01/`。
3. 第二轮仍出现黑色细缝。修复单面裙边剔除和裙边深度，增加真实粗细边缘覆盖测试，并提高轨道细分；保存于 `Artifacts/Iteration02/`。第三轮及独立程序所审查的视野中未再见这些黑缝。
4. 首个 GPU 测试错误地把两个仍带大气的帧当作开/关对照。修复 Editor 生命周期，采用独立 Unlit 夹具、色彩空间正确的期望值及真正的关闭后黑色检查。
5. 隐藏 Windows 窗口时截图接口失败，验证器按错误数将运行判为失败。保留 `Artifacts/PlayerHiddenFailure/`；使用真实可见窗口后，6 张截图成功且退出码为 0。
6. 追加的键盘测试暴露 Input System 替换默认设置会销毁旧设置的问题。测试改为临时修改并恢复同一设置对象的两项属性；最终 7/7 通过。

## 已覆盖与未覆盖

实际图形航线使用默认宇宙 Seed 20261026，首恒星系首行星完成全程，第二恒星系验证了流式位置保持、Seed 匹配和轨道画面。多个 Seed、极端位置和偏心率由数值测试覆盖，不等于它们都完成了实际地表视觉验收。

仍需长时间浸泡飞行、更多 Seed/落点、其他 GPU/API 和分辨率验收。当前画面保持早期原型质量，颜色插值和 LOD 变化仍可见；裙边通过不等于完整几何形变无弹变。模型尚无气态巨行星、双星、星系密度结构、多次散射、云、侵蚀、刚体重力或相对论。它已能实际探索与降落，但不声称达到 SpaceEngine 的完整科学模型或视觉水准。

代码与运行证据可分别复现；操作入口见根目录 README。Windows 分发包不提交二进制进 Git，使用本地 `Builds/` 交付并在 `Artifacts/Player/package-manifest.json` 记录源版本与 SHA-256。
