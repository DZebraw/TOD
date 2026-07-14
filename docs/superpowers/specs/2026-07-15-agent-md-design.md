# AGENT.md 设计说明

## 目标

在仓库根目录建立一份面向 AI 代码代理的 `AGENT.md`，让代理在开始任务前能快速理解 DawnTOD Unity 插件的定位、目录结构、运行时架构、编辑器工作流、渲染管线约定和验证方式。

文档应优先回答以下问题：

1. 这个仓库是什么，以及它不是一个什么项目。
2. 修改某类功能时应该先看哪些目录和入口类。
3. Runtime、Editor、资源文件和渲染管线代码之间如何分工。
4. 修改 Unity 资产或 C# 脚本时有哪些必须遵守的约定。
5. 没有自动化测试时，代理应该如何验证改动。

## 选定方案

采用单文件、项目专用的工程指南方案。所有快速入门信息放在根目录 `AGENT.md`，不再把信息拆到多个新文档中，减少代理首次读取时的跳转成本。

文档使用中文说明，保留代码符号、菜单路径、文件路径和 Unity API 的原始英文名称。结构以短段落、表格和分层列表为主，方便 AI 定位信息，也方便开发者人工浏览。

## 内容结构

### 1. 项目身份与边界

记录 `package.json` 中的包名 `com.tencent.dawn.tod`、Unity 版本基线 `2022.3`、UPM 包定位，以及 `.LinkUnity.bat` 将本仓库链接到宿主项目 `Packages/com.tencent.dawn.tod` 的用途。

### 2. 目录和程序集地图

说明 `Runtime` 是游戏运行时功能，`Editor` 是仅编辑器代码，`Texture` 是包级纹理资源；说明 `Runtime/DawnTOD.asmdef` 和 `Editor/DawnTOD.Editor.asmdef` 的边界，以及 `.meta` 文件必须与 Unity 资产一起保留。

### 3. 核心运行时模型

用简短的数据流描述以下组件：

- `DawnTODSystem`：场景中的中心 TOD 系统，管理时间、日月光源、天气控制器发现与混合，并驱动不同渲染管线的环境效果。
- `DawnWeatherController`：单个天气控制器，持有当前 `DawnWeatherPreset` 并按时间评估天气参数。
- `DawnWeatherPreset`：ScriptableObject，通过 AnimationCurve 和 Gradient 保存日月、天空、雾、曝光、降雨等参数。
- `TimeManager`：封装 0–24 小时制时间推进和日出、日落、午夜事件。
- `TODEvents`：静态事件总线，提供全局 TOD 事件订阅和场景切换时的监听器清理。
- `DawnGPUParticleSystem`：降雨 GPU 粒子运行时组件，配合 Compute Shader 和雨滴 Shader 使用。

明确时间约定：小时值范围为 `0–24`，归一化时间范围为 `0–1`；当前默认日出约为 `6` 点、日落约为 `18` 点；公开入口通常通过 `SetTime`、`Evaluate`、`EvaluateByHour`、`AdvanceTime` 和 `Refresh` 进行控制。

### 4. 渲染管线与条件编译

说明 `Editor/AutoDefineRenderPipelineSymbols.cs` 会根据当前 Render Pipeline 设置 `USING_URP` 或 `USING_HDRP`，Runtime 与 Editor 中的条件编译代码依赖这些宏。代理修改管线相关代码时必须同时考虑 URP、HDRP 和无匹配管线三种情况，不应随意把某个管线的类型引用移出条件编译块。

### 5. 编辑器入口和资源工作流

记录可从 Unity 使用的入口：

- `GameObject/MagicDawn/TOD System with Weather Controller`
- `GameObject/MagicDawn/Weather Controller`
- `Assets/Create/MagicDawn/TODPreset`
- `MagicDawn/TOD/Lighting Editor`

说明自定义 Inspector 会直接编辑组件和预设中的曲线、渐变，并可能触发 `Refresh`、`SceneView.RepaintAll` 和资产 dirty 标记；编辑器代码不得被 Runtime 程序集依赖。

### 6. 修改约定

包含以下可执行规则：

- 新增运行时行为放在 `Runtime`，新增 Unity 编辑器功能放在 `Editor`，不要混用程序集边界。
- 保持现有命名空间 `DawnTOD` 和 `DawnTODEditor`，优先沿用现有公开类型和序列化字段命名。
- 移动、重命名或新增 Unity 资产时保留并检查对应 `.meta` 文件和 GUID 引用。
- 不提交 `Library`、`Temp`、`Logs`、`Obj`、构建输出和 IDE 生成文件；遵循现有 `.gitignore`。
- 修改 `ExecuteAlways`、单例、场景扫描、静态事件和编辑器 repaint 逻辑时，额外检查编辑模式、播放模式、重复对象和场景切换行为。
- `DawnTODSystemAdapter.cs` 当前是注释掉的外部适配器草稿，不能把它当作已启用的稳定 API。

### 7. 验证流程

由于仓库当前没有自动化测试，文档要求代理在条件允许时使用 Unity `2022.3` 宿主项目完成编译和手动冒烟测试：确认 Runtime/Editor 程序集无编译错误，验证创建菜单、TOD 时间滑杆、预设曲线编辑、播放模式自动推进、天气刷新和至少一个目标渲染管线。涉及管线宏或 HDRP/URP 特有代码时，应在对应管线项目中验证，并在最终说明未覆盖的管线。

代码或文档改动仍应先运行 `git diff --check`，再检查 `git status` 和最终 diff，确保没有意外生成文件或 Unity 资产变化。

## 非目标

- 不在本次工作中重构 TOD 运行时架构。
- 不补充自动化测试框架或示例场景。
- 不修改现有 C#、Shader、Compute Shader、Unity 资产和包元数据。
- 不把通用 Unity 教程或完整 API 参考复制进 `AGENT.md`。

## 验收标准

`AGENT.md` 完成后，未阅读源码的 AI 代理应能根据它：

1. 判断一个任务属于 Runtime、Editor、渲染管线还是资源工作流。
2. 找到对应的入口文件和主要类型。
3. 知道时间、预设、事件和渲染宏的关键约定。
4. 知道哪些 Unity 资产和生成文件不能误提交。
5. 按文档给出的流程完成基本编译和手动验证，并报告未覆盖的环境。
