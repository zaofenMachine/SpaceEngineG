# 技术调研与取舍

调研日期：2026-10-26。下列为原作者、论文或官方实现；本项目的代码自行实现，未直接拷贝外部工程。

| 原始资料 | 对本项目的启发 | 采用范围 |
| --- | --- | --- |
| [Unity High Precision Framework](https://github.com/Unity-Technologies/com.unity.gis.high-precision-framework) | Unity Transform 使用 float，需要在模型坐标和渲染坐标之间设立明确边界 | 使用整数扇区加 double 局部坐标，比单一银河绝对 double 更适合保留近地表精度；不引入整个 GIS 包 |
| [Sean O'Neil: A Real-Time Procedural Universe, Part Two](https://www.gamedeveloper.com/programming/a-real-time-procedural-universe-part-two-rendering-planetary-bodies) | 球面连续程序化高度场与自适应地形 | 采用方向域噪声和 cube-sphere 四叉树；不复制旧版 ROAM 实现 |
| [Filip Strugar: CDLOD](https://github.com/fstrugar/CDLOD) | 基于距离的有界地形细分 | 四叉树分块、分级预算和接缝覆盖；当前裙边方案不是 CDLOD 的完整连续形变 |
| [Bruneton & Neyret: Precomputed Atmospheric Scattering](https://ebruneton.github.io/precomputed_atmospheric_scattering/) | 地表至空间的光学路径、密度积分和可测试渲染 | 首版直接数值积分单次 Rayleigh/Mie 散射；不声称已经实现论文多次散射 LUT |
| [NASA/JPL: Approximate Positions of the Planets](https://ssd.jpl.nasa.gov/planets/approx_pos.html) | 开普勒方程和轨道坐标旋转 | 用确定性生成的轨道根数作解析双体运动；不是 N 体天体力学或真实星表 |
| [Ken Perlin: Improved Noise](https://cs.nyu.edu/~perlin/noise/) | 连续梯度噪声与分形叠加 | Seed 驱动的三维连续高度域，球面不使用经纬贴图接缝 |

需要分别验证模型误差、视觉表现和运行性能；物理尺寸真实不等于所有天体统计和光度都达到科研精度。
