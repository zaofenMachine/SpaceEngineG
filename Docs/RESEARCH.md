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

## 0.2 银河与天体扩展

本轮另外查阅了以下原始资料。采用模型思想，自行实现代码，没有导入外部资产或代码。

| 原始资料 | 实现取舍 |
| --- | --- |
| [Galaxia 作者项目](https://galaxia.sourceforge.net/) 与 [Sharma et al. (2011)](https://arxiv.org/abs/1101.3561) | 按空间密度生成合成星表；本项目采用简单 Poisson 格准入及有界人口光核，未移植其恒星族合成、年龄/金属丰度及等时线。 |
| [Bland-Hawthorn & Gerhard (2016)](https://www.physics.usyd.edu.au/~jbh/share/Papers/ARAA_Galaxy_2016.pdf) | 分开建模盘、核球/棒和恒星晕。当前参数是可配置合成值，未对银河观测拟合，也未建立引力势。 |
| [McMillan (2011)](https://arxiv.org/abs/1102.4340) 与 [Bissantz & Gerhard (2002)](https://arxiv.org/abs/astro-ph/0110368) | 指数薄/厚盘及棒和旋臂结构；只采用解析分量家族。 |
| [NASA 恒星分类](https://science.nasa.gov/universe/stars/types/) 与 [Fortney & Nettelmann (2010)](https://arxiv.org/abs/0912.0533) | 分开设置恒星残骸、气态巨行星和冰巨星的参数范围；不模拟演化或内部结构方程。 |
| [NASA 土星资料](https://science.nasa.gov/saturn/facts/) | 气态天体无固体降落面，环、卫星和云层分别表示。 |
| [ESO 黑洞研究](https://elt.eso.org/science/blackholes/) | 中央质量取约四百万太阳质量量级，计算几何 Schwarzschild 半径；不将黑色球面当作相对论黑洞渲染完成。 |

GPU 实现额外使用解析频段的像素导数过滤，解决实际截图中的细环摩尔纹；尘埃积分先裁剪至薄层，避免在长达十万光年的视线中完全漏采。它们属于本项目本轮定位后的修复，不是对上述论文精度的声称。

## 0.3 材料、空间积分与天气外观

2026-10-26 至 27 日继续查阅原始实现与论文，代码仍为自行实现，无外部贴图或模型包。

| 原始资料 | 实现取舍 |
| --- | --- |
| [GPU Gems 2: GPU-Based Geometry Clipmaps](https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-2-terrain-rendering-using-gpu-based-geometry) | 将几何细分和材料细节分开、按尺度过滤；当前仍是原 cube-sphere 四叉树，没有声称实现 geometry clipmaps。 |
| [PBRT 4: Transmittance](https://www.pbr-book.org/4ed/Volume_Scattering/Transmittance)、[Equation of Transfer](https://www.pbr-book.org/4ed/Light_Transport_II_Volume_Rendering/The_Equation_of_Transfer) | 银河体积内按 Beer–Lambert 定律积分发光与消光；以解析常系数区间累积，仍是有偏有限步近似，不包含尘埃散射。 |
| [GPU Gems 3: Real-Time Simulation and Rendering of 3D Fluids](https://developer.nvidia.com/gpugems/gpugems3/part-v-physics-simulation/chapter-30-real-time-simulation-and-rendering-3d-fluids) | 低分辨率 HDR 体积积分后合成；未实现文中的流体求解。 |
| [Guerrilla: The Real-Time Volumetric Cloudscapes of Horizon Zero Dawn](https://www.guerrilla-games.com/read/the-real-time-volumetric-cloudscapes-of-horizon-zero-dawn) | 分离天气覆盖、主体形状与细节侵蚀。当前海洋星球只实现廉价轨道薄片，不具有论文的体积天气或同等画面质量。 |
| [NASA: Jupiter's Colorful Cloud Belts](https://www.nasa.gov/image-article/jupiters-colorful-cloud-belts/) | 用连续变形云场替代均匀正弦条带与靶心风暴；不是观测拟合或大气动力学模拟。 |

具体参数、测试设计和边界另见 `Rendering/GALAXY-VOLUME.md`、`Rendering/PLANET-WEATHER.md` 及 `Terrain/README.md`。GPU 哈希与 CPU 高度一致性单独测试，网格密度不变性、实际画面和连续降落分别验收。
