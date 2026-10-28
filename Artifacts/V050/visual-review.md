# v0.5.0 最终 Windows Player 图形复核

本记录来自实际逐张打开的 26 张原始 PNG：`PlayerFlight` 六张、`PlayerGalaxy` 二十张，均为 1600×900。最终复核对象为源提交 `e450ec1`、构建 GUID `01e6f56554854f339b6e50bc10846e10`、Seed `20261026`、第 3 代生成器。运行报告分别为 [Flight](PlayerFlight/runtime-verification.json)（UTC `2026-10-28T05:50:17.9907781Z`）和 [Galaxy](PlayerGalaxy/galaxy-verification.json)（UTC `2026-10-28T05:50:35.8835901Z`），硬件记录为 RTX 4070 / Direct3D12。

最终构建生成上述文件后，已重新对这 26 张当前原图逐张调用图像查看工具，确认下面每一行仍符合最终截图；并非只替换前一轮记录的 GUID。最终图中未发现相较上一轮的新渲染故障，近地细节、云层硬边和银河形态不足仍然存在。

以下结论来自图像观察，不以报告中的 `passed` 字段代替视觉判断。复核未启动 Unity、未改渲染代码、未处理或替换截图。本记录也未测量性能；右侧 HUD 的瞬时帧率不能代表冷启动或整段帧时间。

## 观察结论

Gen3 轨道布局改变后，这组截图中的行星仍有完整轮廓，岩石、冰、巨行星及第二恒星系海洋行星仍能看到方向一致的明暗变化，星环前后遮挡和投影仍存在。未在这 26 张图中看到洋红材质、整颗目标消失、明显撕裂成背景的地形洞，或大块异常彩色像素。PNG 是有限显示结果，不能证明所有着色器中间数值都有限，也不能排除截图之间的一帧空场或闪烁。

画质仍有明确不足：近地画面几乎纯绿色，188 千米俯视云层存在硬多边形轮廓；银河旋臂过于规则、盘内尘纹形成明显闭合环与管状结构，弥散发光仍偏软。此次不将这些限制描述为已经修复，也不据静态图宣称达到 SpaceEngine 的视觉质量。

## PlayerFlight：6/6 张实际查看

| 原始截图 | 实际观察 |
| --- | --- |
| [01-interstellar](PlayerFlight/01-interstellar.png) | 0.030 光年处，目标未达到可分辨球面尺度，标签聚集在系统方向；背景银河带可见。不是近地空场证据，也不能用于验证地表覆盖。 |
| [02-orbit](PlayerFlight/02-orbit.png) | 海洋行星轮廓连续，海陆、云层与海面高光可辨，未见贴图缺面或天空穿透。正面整体很亮、地形层次偏平，但未整球过曝成白色。 |
| [03-atmosphere](PlayerFlight/03-atmosphere.png) | 高度 188.44 千米，视线被大面积灰白云层/雾覆盖，仍有浅色陆地差异；云遮罩呈明显硬多边形缺口，近距离外观不足。单图不能区分云层网格/遮罩硬边与下方几何问题，未将其自动判为地形缺洞。 |
| [04-low-altitude](PlayerFlight/04-low-altitude.png) | 高度 1.37 千米，绿色斜坡与蓝色天空边界连续，坡面可见低对比细纹；没有明显背景洞，但岩土层次弱，斜视纹理显得拉长、柔软。 |
| [05-surface](PlayerFlight/05-surface.png) | 高度 2.00 米，顶部仍能看到连续起伏地平线与天空，主体几乎单一绿色，材质与微地形辨识度很低。没有可见洋红/黑洞式缺面；此图只能支持地表存在，不能支持近地视觉细节已经达标。 |
| [06-second-system](PlayerFlight/06-second-system.png) | 第二红矮星系统海洋行星可见海陆、暖色云层、高光及右侧暗面，球面完整。未看到 Gen3 布局后失光、整球发黑或异常发光的明显回归。 |

## PlayerGalaxy：20/20 张实际查看

| 原始截图 | 实际观察 |
| --- | --- |
| [01-galaxy-oblique](PlayerGalaxy/01-galaxy-oblique.png) | 暖核、蓝白年轻星带、灰褐尘带可辨，银河外为空黑，无巨大星粒子遮挡。旋臂多圈近似同心，外盘弥散发光仍软，尘团尺度与重复感明显。 |
| [02-galaxy-edge](PlayerGalaxy/02-galaxy-edge.png) | 薄盘、中央隆起和中平面暗带仍存在，画面上下主要为空黑；中央亮带宽而柔软，近白曝光压缩了核心层次。没有看见全屏灰雾或上下翻转的合成异常。 |
| [03-disk-from-within](PlayerGalaxy/03-disk-from-within.png) | 中央暖色银河带与上下暗天保留；尘埃形成大块闭合环/管状轮廓，并有细水平层纹，整体仍模糊。这是明显形态/频谱限制，不能由 native 分辨率或 GPU 检查通过消除。 |
| [04-oceanic](PlayerGalaxy/04-oceanic.png) | 海陆、云与高光完整，标签仅指向当前海洋行星，未见此前背后天体标签透到球面。正面亮、云层覆盖较厚，海岸细节可辨。 |
| [05-rocky](PlayerGalaxy/05-rocky.png) | 灰褐球面、少量凹坑和右侧暗面完整；材质仍以柔软斑驳为主，缺少清晰多尺度地质结构。未见缺面。 |
| [06-ice](PlayerGalaxy/06-ice.png) | 浅冰色主体、蓝灰斑块及细冰面纹理可辨，右上暗面正常；地貌起伏仍弱，未见异常色块或贴图接缝。 |
| [07-gasgiant](PlayerGalaxy/07-gasgiant.png) | 棕色云纹、星环层次及右上环阴影正常，环后部被球体遮挡。云带仍偏块状，环线过于均匀；下缘与 HUD 相接属于取景范围。 |
| [08-icegiant](PlayerGalaxy/08-icegiant.png) | 蓝绿色云顶、环前后遮挡与球面上的窄暗环影可辨；整体偏暗但仍有层次，未见实体地表穿出云顶或环整体透过球体。 |
| [09-lava-moon](PlayerGalaxy/09-lava-moon.png) | 暗壳和橙色发光裂缝完整；裂缝像均匀闭合等高线，明显风格化，不能当作真实熔融流动或已解温度分布。 |
| [13-cloud-top-approach](PlayerGalaxy/13-cloud-top-approach.png) | 10 千米云顶悬停视角，连续云顶地平线与斜上方星环存在；云顶近景近乎平坦，细节和立体云层不足。未见固体山体错误露出或画面空黑。 |
| [14-central-horizon](PlayerGalaxy/14-central-horizon.png) | 中央黑洞几何视界呈完整黑圆，明亮核球背景可见尘纹与星点。背景过亮且平，黑洞仅遮挡形状；没有引力透镜/吸积结构，不能以此图声称相对论黑洞渲染。 |
| [15-star-reddwarf](PlayerGalaxy/15-star-reddwarf.png) | 暖橙圆盘、细颗粒光球与完整边缘；没有缺材质，恒星外层结构仍简化。 |
| [16-star-mainsequence](PlayerGalaxy/16-star-mainsequence.png) | 浅暖色光球、细颗粒和边缘层次可辨；没有异常发黑、洋红或过曝整屏。 |
| [17-star-bluemainsequence](PlayerGalaxy/17-star-bluemainsequence.png) | 蓝白光球完整，与低温恒星存在色差；形态仍主要为共用颗粒圆盘，图中没有显著日冕/耀斑。 |
| [18-star-redgiant](PlayerGalaxy/18-star-redgiant.png) | 暖色光球完整，距离显示与大半径的取景相容；视觉形态与红矮星仍十分相似，仅凭图像不能验证恒星演化数据。 |
| [19-star-whitedwarf](PlayerGalaxy/19-star-whitedwarf.png) | 蓝白紧致星圆盘完整，显示半径 8,124.28 千米；纹理仍沿用光球颗粒，没有额外紧致星特征。 |
| [20-star-neutronstar](PlayerGalaxy/20-star-neutronstar.png) | 蓝白圆盘完整，显示半径 12.10 千米；没有可见喷流/脉冲/相对论表面效果，不把共用光球视作完整中子星外观。 |
| [21-star-blackhole](PlayerGalaxy/21-star-blackhole.png) | 黑色几何视界遮挡背景，轮廓在银河带前可辨；没有假发光光球。系统第一颗原海洋体已显示冰封分类；此图不验证温度或零光度系统整套能量账。 |
| [22-desert](PlayerGalaxy/22-desert.png) | 橙褐球面与暗边连续，无洋红或缺面；斑驳噪声较均一，仍缺少能明确识别的盆地、山脉或沙漠层次。 |
| [23-galaxy-moving-view](PlayerGalaxy/23-galaxy-moving-view.png) | 旋转性能采集后的保留帧仍有完整暖核、旋臂和外盘，无残留天体标签/轨道线覆盖银河。规则环状旋臂与软尘团同 01；单帧不能验证运动缓存每帧正确、闪烁或连续视差。 |

## 后续验收边界

本记录完成了 26 张截图的逐张人工观察，不是原生录像逐帧人工评审。连续下降、世界切换空场、冻结及短暂 LOD 弹变仍应结合该构建的原生录像、精确截图 marker、自动全帧像素筛查和人工关键帧复核。详情页的 21 张截图由另一份独立复核覆盖，本记录不重复其结论。

若后续继续优化视觉，优先处理 `PlayerFlight/05-surface` 的近地材质缺少层次与 `03-atmosphere` 的云层硬多边形边界；银河需要改变规则旋臂/闭合尘纹的空间结构和频谱，而非只提高分辨率、提高曝光或加入更大的亮点。
