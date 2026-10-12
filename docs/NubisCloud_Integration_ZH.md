# DawnTOD 的 Nubis 云集成

旧的 AABB 体积云 Volume、Renderer Feature、Shader、Inspector、安装菜单和专用纹理已移除。新实现来自宿主工程原有的 NubisCloud：NDF、剖面 LUT、Perlin-Worley、卷云、风暴和时域重建。运行时位于 `Runtime/URP/Nubis`，编辑器位于 `Editor/URP/Nubis`，仅编入 URP 14 的可选程序集。

## 当前场景

`MagicDawn > TOD > Configure Current Nubis Scene (Preserve Clouds)` 在主相机实际使用的 Renderer 上安装 Nubis、Dawn 雾和表面大气光照，并创建大气 Volume 与 TOD 天气控制器；雾由 TOD 的隐藏运行时 Volume 驱动。相机选择的 Renderer 不会被替换。

该入口保留已有云组件、数据资产、太阳、相机机位和后处理。已有云参数不重新套用默认值；不会重新生成已有 NDF、噪声或 LUT。天空背景改由 DawnTOD 的物理大气渲染。

首次配置的 `Drive Directional Lights` 关闭：TOD 读取原太阳供大气使用，不写回其旋转、颜色、强度或日夜激活状态。需要由天气曲线控制日月时再开启该选项。自动时间推进同样关闭。

为保留原有云的颜色和对比度，首次配置开启大气并关闭额外的指数高度雾。需要雾效时开启 Dawn Fog；背景、下层体积云和卷云按各自距离使用同一套指数高度雾。

## 在其他工程使用

- `GameObject > MagicDawn > Nubis Cloud Model` 创建云组件，使用包内默认资源。
- `MagicDawn > TOD > Install URP Nubis Cloud Renderer Feature` 安装到主相机的 Renderer。
- 保留 `Tools > Nubis Clouds` 下的 NDF、卷云、世界地图与天气工具。生成和编辑的项目资源写入 `Assets/NubisCloud`；包内默认资源位于 `Runtime/Resources/DawnTOD/NubisCloud`。
- 外部程序集直接使用 Nubis 类型时引用 `DawnTOD.URP`；使用编辑器工具时引用 `DawnTOD.Editor.URP`。保留了 `NubisStorm` 命名空间和原脚本 GUID。

当前包提供独立默认资源，原场景仍引用其自己的资产；二者拥有不同 GUID。修改项目中的天气场不会修改包内默认数据。

## 渲染契约

透明物体之前先计算体积雾网格，再合成背景高度雾与体积雾，最后合成各云层。三者依次位于 `BeforeRenderingTransparents - 2`、`BeforeRenderingTransparents - 1`、`BeforeRenderingTransparents`。每个相机开始时重置雾状态，避免沿用上一相机的参数。

密度与几何沿用 Nubis；光照按下述日月双光源与大气透射规则计算。指数高度雾在时域重建之后分别作用于下层云、近端卷云与远端卷云，再按原有顺序合成。云直接合成到相机，不再生成供独立体积光使用的云阴影、颜色或距离纹理。

## UE 指数高度雾（URP）

天气预设的雾参数全部为静态值，Lighting Editor 不再显示雾曲线轨道。`Fog Height Falloff`（`fogHeightFalloff`）默认使用 UE 的 0.2，密度每升高 50 米减半；0 为均匀雾。`Affect Sky`、`Base Height`、`Mean Free Path`、旧最高高度/最大积分距离和雾颜色渐变已删除。天空、低层体积云和卷云使用同一套雾计算。

雾统一在 TOD 天气控制器的 `Fog Settings` 调整，包括大气联动。`DawnFogVolume` 从 Volume 的 Add Override 菜单隐藏，当前场景 Profile 中的独立 Fog override 已移除；原有大气联动参数已迁入天气预设。TOD 自动维护隐藏的运行时 Volume，场景安装菜单不再创建可编辑的 Fog override。

主层参数为 `Fog Density`（默认 0.02）和 `Fog Height Falloff`。可选 `Second Fog` 默认关闭，第二层密度默认 **0**（UE 组件构造函数覆盖了 SecondFogData 结构体的 0.02），高度衰减默认 0.2、高度偏移默认 0 米。开启后提供独立 `Fog Density`、`Fog Height Falloff` 和 `Fog Height Offset (m)`。主层参考高度对应 UE Fog Actor 的高度，由 `DawnTODSystem.transform.position.y` 提供；第二层参考高度等于主层高度加偏移。

两层按 UE `SecondFogData` 的方式叠加密度积分，共用固定 `Fog Inscattering Color`、`Fog Max Opacity`（默认 1）、`Start Distance`、`End Distance`、`Fog Cutoff Distance`（距离默认 0）。两层没有独立的距离遮罩：主层可用较小衰减塑造整体及远景雾，第二层用较低高度和较大衰减塑造近地雾，实际仍会作用于穿过该高度范围的远处视线。所有参数不随日内时间变化，但仍参与天气预设混合；第二层关闭时密度按 0 混合，淡入淡出保留有效层的高度与衰减。

`Fog Inscattering Color` 对应 UE 的额外内散射亮度，默认黑色。开启 `Use Sky Atmosphere`（默认开启）后，天空环境光独立叠加，黑色只去掉手动颜色，不会消除大气贡献。`Sky Atmosphere Ambient Contribution Color Scale` 默认白色，对应 UE 同名字段；`Height Fog Contribution` 默认 1，对应 UE SkyAtmosphere 的共享贡献系数，同时缩放天空环境项与透过大气后的日月方向项。参数均在 TOD 中静态编辑并参与天气混合。

按 UE 5.6 默认 `r.SupportExpFogMatchesVolumetricFog=0` 分支，方向内散射使用 `pow(saturate(dot(viewRay, lightDirection)), exponent) / (4π)`，不使用 HG 相函数。`Directional Inscattering Color` 默认黑色（额外手动亮度，乘以主方向光的亮度），`Directional Inscattering Exponent` 默认 4，`Directional Inscattering Start Distance` 默认 **100 米**，对应 UE 的 10000 厘米。方向散射的距离从主雾 Start Distance 截去后的剩余光程开始计算；它使用自己的积分与透射率，不套用 Fog Max Opacity。

最终输出为 `Fog.rgb = AmbientColor × (1 - T) + DirectionalColor × (1 - T_directional)`；`T` 是双层密度积分经过最大不透明度限制后的透射率。关闭大气联动只去掉天空及大气方向贡献，保留两个手动内散射颜色。旧的两个独立强度倍率与 HG Scattering Distribution 已移除，当前场景已迁移并还原为新默认值。

`SceneCore.cpp` 将 UE 艺术参数 density/falloff 除以 1000 后用于厘米空间；在 Unity 米制/Y 轴中相应乘以 0.1。共享 Shader 直接采用 `HeightFogCommon.ush` 的 `CalculateLineIntegralShared`、`exp2` 透射率、0.01 附近的泰勒分支、65536 厘米观察高度限制与起止/截断距离处理。End Distance 限制水平投影距离；Cutoff Distance 排除超距接收点。移除了原来的 MFP 换算和 5000 米积分上限。

背景雾在透明物体和云之前执行。云在时域重建后分别按层距离使用 UE `VolumetricCloud.usf` 的 `L = T_fog * L_cloud + Fog.rgb * (1 - T_cloud)`（Fog.rgb 已包含环境项及独立方向项的积分），保持透射率与历史不变。逐相机重置雾状态，防止禁用后沿用上一相机的数据。

当前 URP 实现对齐 UE 默认解析双层指数高度雾分支的密度积分与方向散射公式。积分前的观察高度限制取所有有效层的最低上限；最大不透明度在两层积分相加之后统一应用。普通透明材质须自行调用共享雾函数。HDRP 仍使用原生单层 Fog 的参数适配，未实现第二层，也不是本 URP Shader。

## 验证范围

Unity 2022.3.62f2、URP 14、D3D11。隔离工程保存替换前后的同机位 HDR 输出，比较云颜色、透射率、距离及完整云合成；检查重复配置、包内默认资源和真实大气输出。宿主工程的报告和截图位于 `CloudCaptures/DawnNubis`。测试源码在验证完成后删除。

双层指数高度雾通过 Unity 编译与运行验证：确认雾参数全部为静态值、Lighting Editor 无雾曲线轨道、时间变化不改变雾参数、第二层关闭后的天气混合保留有效层形状，以及 URP 参数传递正确。240 组 GPU 结果与 UE 原始厘米空间双层公式对照，最大透射率误差约 `4.2×10⁻⁷`，覆盖单层、双层、仅第二层、观察高度限制、零衰减与起止/截断距离。实际云透射率最大差值为 0。报告、日志和截图位于宿主工作目录 `DawnTwoFogResults`，临时测试源码已删除。HDRP 适配未在本次 URP 工程中编译验证。

黑色内散射修正通过 URP 编译与 GPU 验证：分别隔离天空环境光、太阳和月亮，确认黑色手动颜色不再消除大气贡献，非黑色手动颜色按加法叠加，雾透射率及云覆盖率不变。实际场景在 12:00、18:00、00:00 以黑色内散射渲染，并确认大气散射 RGB 随时刻变化。TOD 参数采样、天气混合、隐藏运行时 Volume 的完整参数覆盖、Add Override 隐藏和场景 Profile 迁移均通过。报告位于宿主工作目录 `DawnFogAtmosphereResults`；截图用于功能验证，未调整原场景作者的雾密度或高度。临时测试源码已删除。

Nubis 仍以桌面 URP 透视相机为目标，保留原有内存和时域重建边界；没有新增 HDRP、XR 或目标设备性能保证。



## 2026-10-10：日月、大气颜色与 24 小时场景

宿主 `Assets/NubisCloud/Scenes/SkyClouds.unity` 已单独配置完整 TOD；上文菜单的首次安装策略仍保留原场景参数。

### UE 对齐范围

对照本机 UE 5.6 源码：

- `Engine/Shaders/Private/VolumetricCloud.usf` 652–666、931–948：两盏大气光源分别获取外太空或近地照度，再作用于云内散射；HG 相函数归一化，移除 Nubis 原先额外的 `4π` 亮度放大。
- `Engine/Source/Runtime/Engine/Private/Components/VolumetricCloudComponent.cpp`：`bUsePerSampleAtmosphericLightTransmittance` 默认关闭。`VolumetricCloudComponent.h` 将它明确作为影响外观的云组件选项。
- `Engine/Source/Runtime/Engine/Public/Rendering/SkyAtmosphereCommonData.cpp` 的 `GetTransmittanceAtGroundLevel`：统一光色使用地表上方 500 m 的参考位置。
- `Engine/Shaders/Private/SkyAtmosphereCommon.ush`：星球遮挡与可过滤的大气透射 LUT 分开。Dawn 密度 LUT 不再写入会污染双线性插值的 `1e20` 地影哨兵值。
- `Engine/Shaders/Private/HeightFogCommon.ush`：高度雾使用天空环境光与两盏透过大气后的方向光，保留原指数高度积分与云覆盖率加权。

对齐的是光照来源、角度、颜色传递和透射模式；Dawn 仍使用自己的 Rayleigh/Mie LUT，没有移植 UE 的臭氧吸收和完整大气多次散射，不能视为逐像素复刻 UE。

### Height 为什么会影响染色

`Height` 是下层云厚度，NDF 的上下界乘以该厚度得到实际海拔；厚度较大时，卷云也会被自动抬高，保持在下层云顶部之上至少 256 m。

新增 `Per Sample Atmosphere Transmittance`，两类云以及程序化/自定义路径共用：

- 关闭（本场景与 UE 默认）：统一使用 500 m 参考透射颜色。改变 Height 不会直接让入射大气光色变白；形状、遮挡、相函数及环境光占比仍可能改变最终观感。
- 开启：按每个样本的真实球面海拔计算。海拔越高，大气光程通常越短，较多绿蓝光能透过，因此黄昏染色较弱是预期结果。

固定位置 GPU 测试：厚度分别为 1500、4500、12000 m 时，同一位置的透射结果完全一致。18:00 时，统一模式各海拔 RGB 透射约为 `(0.16012, 0.02066, 0.000113)`；逐采样模式在 7600 m 为 `(0.52931, 0.22770, 0.02660)`，24000 m 为 `(0.92138, 0.82647, 0.62668)`。

### 日月与雾参数

`NubisStormVolume` 支持 `Sun`、`Moon`，可自动取 Dawn TOD 引用。关闭/禁用的灯不会继续照云；同一盏灯不会重复相加。两种云的直射和多次散射均使用灯的线性颜色、强度、色温及大气透射。环境项使用显式发布的天空 SH 朝上方向的辐照，避免全屏绘制读取前一个物体的 UnityPerDraw SH。

场景使用 06:00/18:00 日夜主光切换，交接前后各 0.6 小时允许日月同时贡献，由强度曲线连续过渡。天空背景和环境 SH 同样累加两盏灯。中午 Sun=12、午夜 Moon=0.85；采用统一曝光、ACES、Bloom=0.05，场景运行时 600 秒走完一天。月光的升落角与强度按可读的夜景配置，未模拟真实日期和月相。

云相函数 g=0.3，银边 g=0.7/权重 0.08，直射倍率 1.3、多次散射 0.6、环境光 1。大气顶高 80 km，Rayleigh 标高 8000 m、Mie 标高 1200 m，散射与消光强度均为 1，Mie 系数为 `(0.4,0.4,0.4)×10⁻⁵`。

雾当前使用上文 UE 默认参数：密度 0.02、Height Falloff=0.2、Max Opacity=1、Start/End/Cutoff=0；第二层关闭且密度为 0。两个手动内散射颜色为黑色，天空贡献色为白色，Height Fog Contribution=1，方向指数为 4、方向起始距离为 100 m。TOD 高优先级运行时 Volume 完整覆盖静态雾参数，大气联动也在 TOD 调整。

### 验证

Unity 2022.3.62f2 / URP 14.0.12 / D3D11 编辑器实际相机渲染：

- 0–24 整点 25 帧，加交接附近 15 帧；0/24 周期闭合。
- 四条路径分别检查中午、黄昏、午夜、修改太阳颜色、修改月光颜色、平面几何，共 24 帧。自定义层积云使用已有 SingleCloud 数据的临时克隆隔离，未替换场景原作者数据。
- 三个时刻关闭雾对照，加最终展示帧；共保留 68 个有效采样的测量记录。
- 有效采样未发现 NaN/Inf 云颜色；中午显示亮度 P99=0.808，纯白截断像素比例为 0。18:00 程序化层积云平均预乘 RGB 约 `(0.06795,0.01043,0.00176)`，卷云约 `(0.04522,0.00996,0.00375)`，均明显偏红。
- 缓慢推进 12 → 12.001 → 12.002 时复用时域历史；跳到 18 时重置，下一帧恢复复用。
- C# 编译及四个相关 Shader 无错误；保留原有时域整数运算、pow、Composite 未初始化分析及 URP Shadow 等编译警告。

输出位于宿主 `CloudCaptures/TOD24`：全天总览、24 帧联系表、四路径对照图及 `TOD24-Validation.json`。早期部分离屏采样曾返回全零缓冲，未计入验收；重新初始化编辑器验证通道后四路径复测通过。未做独立 Player 构建或 GPU 性能基准测试。临时验证脚本与 Compute Shader 已在验证后移除。

### URP Lit 与大气光照衔接

`DawnAtmosphereLightingRendererFeature` 在 URP 14 上传灯光数据前，把 TOD Sun/Moon 的可见光颜色乘以 500 m 参考大气透射并除以 π。UE 的 Lambert 漫反射含 1/π，URP 的 `BRDFData.diffuse` 将这一约定留给灯光单位；直接把云使用的光强送入 Lit 会让大面积受光面过亮。此转换同时覆盖主光及附加方向光，不修改 Light 组件，因此天空、云和雾仍取得原始大气外光强。

本场景的 `StormRenderer` 和 `URP-HighFidelity-Renderer` 均已安装该 Feature，覆盖 Game 与 Scene 视图。其他使用 TOD 的 Renderer 也需要安装它。

大气新增 `Ground Diffuse Albedo`，为环境 SH 下半球加入日月与天空照亮的地面漫反射。本场景设为线性中性灰 0.18；默认黑色兼容旧配置。该项随日夜光照变化，区别于既有固定 `Ground Color` 背景辐亮度，不通过抬高整天环境光消除背光面。

实际编辑器验证了 12:00、17:30、18:00、00:00 及两套 Renderer。中午原始 Sun 保持 12，传给 Lit 的主光 RGB 从约 `(12,11.59,11.07)` 变为 `(3.63,3.29,2.67)`；受光面恢复渐变、背光面获得地面反射，黄昏表面偏暖、夜间接收月光。另拍摄原场景相机中午与黄昏画面，云和大气保持正常。球体近景测试背景中的远处地形几何异常，在关闭该 Feature 的对照中同样存在，不属于这次光照修改的验收范围。

结果保留在宿主 `CloudCaptures/LitFix`，包括 `Lit-BeforeAfter.png`、`Lit-Validation.json` 及场景截图。测试后恢复相机、时刻和自动推进设置，并删除临时编辑器脚本；未执行 Player 构建。

## 2026-10-10：删除独立体积光模块

独立方向体积光的 Volume、Renderer Feature、Shader、共享积分文件、Inspector 和安装菜单均已删除。场景配置入口不再创建或安装该模块；宿主的两个 Renderer 及 NubisAtmosphereProfile 已移除对应子资源和引用。Nubis 同时移除仅供该模块使用的世界空间云阴影与云颜色/距离导出通道，保留云自身光照、时域重建、卷云和双层指数高度雾。

已在隔离 Unity 2022.3.62f2 / URP 14.0.12 / D3D11 宿主验证程序集与 Shader 编译、已删除类型和 Shader 不存在、Renderer Feature Map 与子资源一致、Volume Profile 无丢失组件，以及实际云/雾开关渲染。临时验证脚本和程序集定义在验证后删除。


## 2026-10-10：UE 雾默认值与公式复核

核对本地 UE 5.6.0 的 `ExponentialHeightFogComponent.cpp`、`ExponentialHeightFogData.h`、`SceneCore.cpp`、`FogRendering.cpp`、`HeightFogCommon.ush`、`VolumetricCloud.usf`，并确认 `r.SupportExpFogMatchesVolumetricFog=0`、`r.SupportSkyAtmosphereAffectsHeightFog=1` 的默认分支。修正第二层默认密度、方向散射相函数、独立方向起始距离与最大不透明度处理，以及极短射线的 epsilon 单位（`1e-8 cm² = 1e-12 m²`）。

宿主 `Assets/DawnTOD/NubisSceneWeather.asset` 的全部雾参数已还原为 UE 默认值，TOD 根节点高度为 0 米、雾启用。没有重置云、日月、大气、曝光、相机或时间设置。

验证：Unity 2022.3.62f2 / URP 14.0.12 / D3D11 编译与实际相机渲染；539 组 GPU 结果与独立 UE 原始厘米空间公式对照，覆盖双层、仅第二层、观察高度限制、水平 End、Cutoff、主 Start、方向 Start、零最大不透明度、手动/大气内散射与两盏灯。最大 RGB 误差约 `2.98×10⁻⁷`、透射率误差约 `1.19×10⁻⁷`，云覆盖率保持不变。预设默认值、静态字段、采样/混合、隐藏 Volume 和 URP 输出覆盖通过；实际场景 12:00、18:00、00:00 使用默认雾渲染通过。日志、数值报告与截图位于宿主工作目录 `DawnFogDefaultResults`，临时测试源码在验证后删除。

上述解析雾验证不能视为 UE 完整渲染器的逐像素复刻：Dawn 仍采用自己的天空 SH / Rayleigh-Mie LUT，UE 使用 DistantSkyLight LUT 且包含不同的大气模型。无深度天空仍用有限的远距离射线近似。未移植雾色 Cubemap、局部雾、平面反射裁剪与 UE 正交高度调整；普通透明材质需自行接入共享函数。后续体积雾实现见下节。未验证 HDRP、XR 或独立 Player 构建。

## UE 5.6 体积雾（URP）

在 TOD 天气控制器 `Fog Settings > Volumetric Fog` 开启。所有参数为静态值，仍通过天气采样、混合和隐藏运行时 Volume 输出；不增加 Volume 编辑入口或雾曲线。体积雾使用已有双层指数高度密度，不另设 Base Height 或 Mean Free Path。

组件默认值来自 `ExponentialHeightFogComponent.cpp`：

| 参数 | UE 默认值 | Dawn 默认值 |
| --- | --- | --- |
| Enable Volumetric Fog | false | 关闭 |
| Scattering Distribution | 0.2 | 0.2 |
| Albedo / Emissive | 白 / 黑 | 白 / 黑 |
| Extinction Scale | 1 | 1 |
| View Distance | 6000 cm | 60 m |
| Start Distance / Near Fade In Distance | 0 cm / 0 cm | 0 m / 0 m |
| Static Lighting Scattering Intensity | 1 | 1 |
| Override Light Colors With Fog Inscattering Colors | false | 关闭 |

`Grid Pixel Size=16`、`Grid Size Z=64`、深度分布系数 32、时域开启、`History Weight=0.9` 来自 `VolumetricFog.cpp` 的渲染器初始值，UE 的 Scalability 会进一步覆盖网格质量。宿主天气预设显式写入上述默认值；与 UE 一样，体积雾默认关闭。

实现以 `VolumetricFog.usf` 的 MaterialSetup、LightScattering、FinalIntegration 和 `RenderUtils.h::CalculateGridZParams` 为依据：

- 相机视锥对数深度 3D 网格；近平面为 `max(cameraNear, Start Distance) + 0.095 m`，远平面为 View Distance。使用 RGBAHalf 存储散射/消光系数与积分结果。
- 双层密度相加后乘 UE 的 `MatchHeightFogFactor=0.5`；消光为密度乘 Extinction Scale，散射为 Albedo 乘消光，自发光为 Emissive 乘密度。密度和高度衰减均从厘米空间换算为米空间。
- HG 相函数、日月/其他方向光、点光、聚光灯、范围衰减与聚光锥；点光和聚光灯采用体素尺寸距离偏置，最小偏置为 UE 的 1 cm。读取 URP 主光和附加点光/聚光灯的实时阴影图。
- 天空环境光取 L0/L1 SH，按 `-cameraRay*g` 求值；烘焙 Light Probe 按 `cameraRay*g` 求值并乘 Static Lighting Scattering Intensity。Unity Light Probe 以 8³ 视锥网格插值，属于对 UE Volumetric Lightmap 的适配，不是其砖块结构移植。
- Halton 2/3/5 抖动，历史缺失时四次光照采样，按上帧相机重投影散射/消光系数，再进行积分。每个相机独立维护历史；相机跳切、分辨率/投影/雾参数改变和禁用会重置或释放历史。
- 逐片能量守恒积分及 Near Fade In，指数透射使用 `exp`；UE 积分分母的 `1e-5 /cm` 换算为 `1e-3 /m`。这与解析高度雾的 `exp2` 分支不同，保留 UE 两条路径的区别。

按照 `HeightFogCommon.ush`，启用体积雾后，解析高度雾从体积雾远平面以外开始积分，最终 `Fog.rgb = Volume.rgb + Analytic.rgb * Volume.T`、`Fog.T = Volume.T * Analytic.T`。解析雾的 Max Opacity、Start/End/Cutoff 不约束体积雾，体积雾使用自身 Start Distance 和 Near Fade In。Nubis 在云时域重建之后采样同一份体积雾积分，与解析雾一起按云覆盖率合成，不改云的透射率。

已有场景 Light 默认参与体积雾。可选 `Dawn Volumetric Fog Light` 组件提供每盏灯的 `Scattering Intensity`（默认 1）和 `Cast Volumetric Shadow`（默认开启）；强度设为 0 可排除该灯。只有 URP 实际分配的阴影图可用于体积雾，附加方向光无 URP 阴影支持。日月读取原始 Light 强度及地表参考大气透射，不使用表面光照已除以 π 的数值。

天气混合时，启用预设的总权重达到 0.5 才切换为体积雾；启用预设之间连续混合介质参数，网格质量和布尔选项取权重最大的启用预设。这样避免刚开始淡入体积雾就排除整段近处解析雾。开关切换仍是离散的渲染模型变化。

范围：桌面 URP 的全局高度介质、实时灯光/阴影和 Nubis 云受雾。未移植 UE 局部雾、Volume 材质、矩形灯、Light Function、Lumen、VSM/光追阴影及云投射到雾中的阴影（UE 方向光 Cast Cloud Shadows 默认关闭）。天空、烘焙 GI 和灯光单位使用 Dawn/Unity 适配，不能称为完整 UE 渲染器逐像素一致。普通透明材质须接入 `DawnEvaluateHeightFog`；不支持 Compute/3D RGBAHalf 写入或使用立体相机时回退解析雾。未验证 HDRP、XR、独立 Player 和目标设备性能。

验证环境为 Unity 2022.3.62f2 / URP 14.0.12 / D3D11：24,576 个体素与独立 UE 厘米空间密度、HG、光照和积分公式对照，覆盖单/双层、零密度、低消光分母、Near Fade In、正交/透视网格与非整除分辨率，FP16 系数最大相对误差约 0.097%。另外验证 0.9 时域混合、雾色覆盖的 SHAmbientFunction 系数、方向光亮度约定及静态 L0/L1 SH。实际场景中午、黄昏、午夜渲染通过；主方向光、附加点光和聚光灯阴影均降低体积散射且保持透射率不变，单灯散射/阴影开关有效。相机跳切、双相机历史隔离、禁用释放、近处解析雾排除、远处合成与云覆盖率保持通过。报告、日志与截图位于宿主工作目录 `DawnVolumetricFogResults`。临时测试源码在验证后删除；保留 URP Shadows.hlsl 的编译警告，无新增 C# 或 Shader 编译错误。
