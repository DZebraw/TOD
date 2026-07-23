# DawnTOD AI Agent Guide

## 当前软链接宿主工程

插件软链接所在的宿主工程目录：`D:\unity project\2026ShowrReel_URP\Packages\com.tencent.dawn.tod`

这份文件是给后续 AI 代码代理的快速项目地图。开始任务前先判断改动属于 Runtime、Editor、渲染管线还是 Unity 资源；优先阅读对应入口文件，再修改最小范围。

## 项目定位

- 包名：`com.tencent.dawn.tod`
- 显示名：`DawnTOD`
- Unity 基线：`2022.3`
- 包类型：Unity Package Manager（UPM）插件
- 当前仓库是插件包根目录，不是完整 Unity 工程；仓库中没有宿主项目的 `ProjectSettings`、场景和完整 `Assets` 目录。
- `package.json` 当前没有额外包依赖。不要未经必要性评估就引入新的外部依赖。

`.LinkUnity.bat` 用于把本仓库链接到宿主 Unity 工程的 `Packages/com.tencent.dawn.tod`。它会请求管理员权限，并在目标目录下创建目录符号链接；涉及链接或 Unity 编译时应使用一个实际的 Unity 2022.3 宿主工程。

## 目录与程序集

| 路径 | 职责 | 关键注意事项 |
| --- | --- | --- |
| `Runtime/` | 游戏运行时 C#、Shader、Compute Shader、运行时资源 | 不依赖 `UnityEditor`；主要命名空间为 `DawnTOD` |
| `Runtime/Core/` | 时间管理和全局 TOD 事件 | `TimeManager`、`TODEvents` |
| `Runtime/Data/` | 天气预设 ScriptableObject | `DawnWeatherPreset` 的曲线和渐变字段属于序列化数据 |
| `Runtime/Scripts/` | TOD 系统、天气控制器、GPU 雨滴粒子 | `DawnTODSystem` 是场景中心入口 |
| `Runtime/AP/` | URP 运行时天空/大气效果及其 Shader | 受 `USING_URP` 条件编译影响 |
| `Runtime/HDRP/` | HDRP 集成 | 受 `USING_HDRP` 条件编译影响 |
| `Runtime/Resources/`、`Runtime/Shader/` | 降雨粒子计算、默认 Prefab/Material 和渲染 | 改 C# 参数时要同步检查 Shader/Compute Shader 属性 |
| `Runtime/Resources/Textures/`、`Texture/` | 包内运行时纹理和天空/月亮纹理 | Unity 资源必须保留对应 `.meta` |
| `Editor/` | Unity 编辑器 Inspector、Hierarchy 菜单、Lighting Editor | 仅编辑器代码，主要命名空间为 `DawnTODEditor` |
| `Editor/LightingEditor/AI/` | View1 自然语言天气助手、协议、服务、Prompt/Skill/Schema | 仅 Windows Unity Editor；所有字段消费必须保持首版白名单 |
| `package.json` | UPM 包元数据 | 修改包名、版本或 Unity 基线需谨慎评估兼容性 |

程序集边界由以下文件定义：

- `Runtime/DawnTOD.asmdef`：运行时程序集，`autoReferenced` 为 `false`。
- `Editor/DawnTOD.Editor.asmdef`：编辑器程序集，仅包含 `Editor` 平台，`autoReferenced` 为 `false`。

新增或移动 Unity 资产、脚本、Shader、Compute Shader 时，必须检查 `.meta` 文件和 GUID 引用是否保持正确。不要把 `Editor` 程序集类型引用带入 Runtime。

## 核心运行时模型

典型数据流是：

```text
DawnWeatherPreset（曲线/渐变数据）
        ↓
DawnWeatherController（单个天气状态的评估）
        ↓
DawnTODSystem（时间、控制器发现、时间段和天气混合）
        ↓
日光/月光、天空/雾/曝光、降雨 GPU 粒子
```

### 主要类型

- `Runtime/Scripts/DawnTODSystem.cs`：场景中的中心 TOD `MonoBehaviour`，维护当前时间、日光/月光引用、天气控制器列表和时间段，评估并混合有效预设；提供 `Instance` 单例入口。该类使用 `[ExecuteAlways]`，编辑模式和播放模式行为都要考虑。
- `Runtime/Scripts/DawnWeatherController.cs`：单个天气控制器，持有 `ActivePreset`，按照时间评估预设并刷新光源、HDRP Volume 或降雨参数。
- `Runtime/Data/DawnWeatherPreset.cs`：`ScriptableObject`，使用 `AnimationCurve` 和 `Gradient` 保存日月方位/高度/强度/颜色、HDRP 天空/雾/曝光以及降雨参数。
- `Runtime/Core/TimeManager.cs`：封装时间推进和日出、日落、午夜事件。时间以小时表示，并在 `0–24` 范围循环。
- `Runtime/Core/TODEvents.cs`：全局静态事件总线，提供时间变化、日出、日落、午夜和预设切换事件；场景切换或系统销毁时注意监听器清理。
- `Runtime/Scripts/GPUParticlesSystem.cs`：`DawnGPUParticleSystem`，配合 `Runtime/Resources/RainyParticleUpdate.compute`、默认雨水 Prefab/Material 和 `Runtime/Shader/RaindropParticle.shader` 实现 GPU 雨滴。
- `Runtime/AP/Scripts/RuntimeSkySetting.cs`：URP 运行时天空设置；由 `USING_URP` 代码路径使用，修改时要确认其在不同管线项目中的依赖关系。
- `Runtime/HDRP/HDRPIntegration.cs`：HDRP 相关集成；只在 `USING_HDRP` 代码路径下使用。

### 时间与控制约定

- 小时制时间范围：`0–24`，内部通常使用 `Mathf.Repeat` 循环。
- 归一化时间范围：`0–1`，通常等于 `hour / 24`。
- 默认日出时间约为 `6`，默认日落时间约为 `18`；修改边界时要检查跨午夜逻辑。
- 主要控制入口：`SetTime(float hour)`、`Evaluate(float normalizedTime)`、`EvaluateByHour(float hour)`、`AdvanceTime(float deltaTime)` 和 `Refresh()`。
- 自动推进由 `autoAdvanceTime`、`dayLengthInSeconds` 和 `timeScale` 控制；验证时要分别检查编辑模式、播放模式和时间循环边界。
- 预设参数通过曲线/渐变在归一化时间上采样；修改曲线字段、序列化字段名或采样逻辑时，要考虑已有 `.asset` 预设的兼容性。

## 渲染管线与条件编译

`Editor/AutoDefineRenderPipelineSymbols.cs` 会根据当前 `QualitySettings.renderPipeline` 或 `GraphicsSettings.defaultRenderPipeline` 的实际类型，自动维护以下宏：

- `USING_URP`：URP 代码路径。
- `USING_HDRP`：HDRP 代码路径。

修改渲染管线代码时：

1. 保持 `UnityEngine.Rendering.HighDefinition` 等管线专属引用位于对应 `#if` 块内。
2. 同时考虑 URP、HDRP 和未知/内置管线三种情况；不要假设所有宿主项目都有 HDRP 或 URP。
3. 不要随意手工固定或删除 `USING_URP`、`USING_HDRP`；先检查自动检测逻辑和宿主工程的当前 Render Pipeline。
4. 修改 C# 与 Shader/Compute Shader 交互时，核对属性名、资源路径、纹理/缓冲区布局和管线差异。
5. 管线相关改动至少在对应 Unity 管线宿主工程中编译；未覆盖的管线必须在交付说明中明确写出。

## Unity 编辑器入口

当前可用的菜单入口：

- `GameObject/MagicDawn/TOD System with Weather Controller`：创建一个 TOD 系统和一个初始天气控制器。
- `GameObject/MagicDawn/Weather Controller`：创建单独的天气控制器。
- `Assets/Create/MagicDawn/TODPreset`：创建 `DawnWeatherPreset` 资产。
- `MagicDawn/TOD/Lighting Editor`：打开自定义 Lighting Editor 窗口。

相关实现主要位于：

- `Editor/HierarchyMenu/TODHierarchyMenu.cs`
- `Editor/DawnTODSystemEditor.cs`
- `Editor/DawnWeatherControllerEditor.cs`
- `Editor/LightingEditor/DawnLightingEditorWindow.cs`
- `Editor/LightingEditor/Views/`、`Editor/LightingEditor/Interaction/`、`Editor/LightingEditor/Utility/`

自定义 Inspector 和 Lighting Editor 会通过 `SerializedObject`/`SerializedProperty` 编辑曲线、渐变和组件字段，并可能调用 `Refresh()`、`SceneView.RepaintAll()`、`EditorUtility.SetDirty()`。修改这些逻辑时要检查 Undo、编辑器重绘频率、资产保存、Scene View 预览和播放模式行为，避免在每帧产生不必要的 dirty 或刷新。

## AI 自然语言天气模块

该模块嵌入 Lighting Editor View1，使用本地 Windows EXE 代理 DeepSeek；它不属于 Runtime，也不支持 HDRP、macOS、Linux 或运行时构建。当前首版只允许修改时间、太阳和月亮，不应用雾、星空、曝光或降雨字段。

### 目录与数据流

- `Editor/LightingEditor/AI/UI/`：View1 面板、服务按钮、设置窗口和会话历史。
- `Editor/LightingEditor/AI/Client/`：目标快照、请求构建、回环 HTTP、主线程调度和协议常量。
- `Editor/LightingEditor/AI/Application/WeatherPresetPatchApplier.cs`：通过单个 Undo Group 原子应用已校验补丁。
- `Editor/LightingEditor/AI/Validation/`：C# 二次严格解析和首版字段白名单。
- `Editor/LightingEditor/AI/Service/`：服务进程生命周期、当前用户 DPAPI Key 存储及 `Windows/DawnTodAiService.exe`。
- `Editor/LightingEditor/AI/Service/Source/`：Python 服务、Provider、资源校验和测试；`build-service.ps1` 是唯一可复现的 EXE 构建入口。
- `Editor/LightingEditor/AI/{Schemas,Skills,Prompts}/`：版本化 Schema、领域 Skill 和 System Prompt，三者共同约束模型输出。

请求链路为：View1 面板 → Controller/Preset/时间快照 → 带会话令牌的 `127.0.0.1:13296` 服务 → DeepSeek → Python Schema 校验与一次修复 → C# 严格解析 → 单个 Undo Group 应用并刷新 View1/View3/Scene View。

### 不变量与安全边界

- C# `DawnTodAiProtocol.cs` 与 Python `constants.py` 的端口、模式、服务版本、Schema 版本、会话头和支持字段必须同步。当前是 `deepseek`、服务版本 `2.0.0`、Schema `1.0`。
- DeepSeek 调用固定官方 HTTPS 地址、`deepseek-v4-flash`、非 Thinking、JSON Output、`temperature = 0`、非流式；Provider 只对 429/5xx/连接或超时错误重试，401/403 不重试。
- API Key 仅通过设置窗口写入 `%LOCALAPPDATA%\DawnTODAI\config.json` 的当前 Windows 用户 DPAPI 密文；不要把 Key 放入工程、测试夹具、命令行、日志、历史、Unity 请求正文或 Git。需要真实 API 验收时由用户在 Unity 设置窗口自行录入，不要在聊天中索取或回显 Key。
- 本地日志位于 `%LOCALAPPDATA%\DawnTODAI\logs\service.log`，单个最大 5 MB，最多保留 10 个，并对 Key/Bearer 做脱敏。不得为了诊断而记录用户原文、原始模型响应或认证头。
- Schema、Skill 或 Prompt 任何一个缺失、损坏或版本不一致时服务必须保持 Not Ready；不要绕开启动一致性校验或放宽 `additionalProperties = false`。
- 请求必须继续使用捕获时的 Controller/Preset/时间快照；任务取消、目标/Preset 变化、非法或过期响应都不得产生部分修改。
- EXE 使用 PyInstaller `--onefile`，会存在 bootloader 父进程和服务子进程。生命周期代码在确认可执行文件路径后用 `taskkill /T /F` 终止整棵树；不要退化为只调用父进程的 `Process.Kill()`，否则子进程会遗留端口和日志句柄。

### AI 改动的验证

1. Python 或 Prompt/Schema/Skill 改动后，使用 64 位 Python 3.12 执行：

   ```powershell
   .\Editor\LightingEditor\AI\Service\build-service.ps1
   ```

   该脚本在隔离环境运行 Python 测试并重建 EXE；不要手工替换 EXE 或提交 Python 缓存。
2. C#、协议、设置或生命周期改动后，在 Unity 2022.3 Windows URP 宿主运行 `DawnTOD.Editor.Tests` 与 `DawnTOD.Tests` 的 EditMode 测试。当前已验证基线为 Python `44/44`、Unity `86/86`。
3. 真实 DeepSeek 验收需要用户授权 API 调用：选择含 `ActivePreset` 的 Controller，设置并保存 Key，启动服务至 `Ready`，发送“设置为正午。”、“把太阳强度改为 1.5。”等首版支持的指令，验证一次 `Ctrl+Z` 同时恢复 Controller 和 Preset。完成后停止服务；如不需保留 Key，先停止服务再在设置窗口 `Clear`。

## 修改规则

### 代码与序列化

- Runtime 行为放在 `Runtime/`；Unity 编辑器功能放在 `Editor/`。不要为方便而跨程序集引用。
- 沿用命名空间 `DawnTOD`、`DawnTODEditor` 和现有文件/类型命名风格。
- 优先复用现有公开入口和序列化字段。重命名 `[SerializeField]` 字段前，先检查已有预设资产和 Unity 序列化兼容性。
- 修改 `DawnTODSystem` 的单例、`FindObjectsOfType` 场景扫描、`[ExecuteAlways]`、静态事件或编辑器 repaint 逻辑时，额外测试重复对象、对象销毁、场景切换、编辑模式和播放模式。
- 修改天气混合时，检查时间段跨午夜的情况，以及无控制器、无预设、多个有效预设和边界时刻。
- `Runtime/Scripts/DawnTODSystemAdapter.cs` 当前主要是被注释掉的外部适配器草稿，不能当作已经启用或稳定的公共 API。
- 改动 AI 模块时，保持 `DawnTOD.Editor` 程序集边界；不要把 DPAPI、进程、HTTP 或 Python 服务依赖带入 `Runtime/`。
- API Key 保存后需要重启本地服务才会生效；不要把密钥读回 UI，也不要增加明文导出、复制或调试入口。

### Unity 资产与仓库文件

- Unity 资产与对应 `.meta` 文件必须成对保留；不要手工重建 GUID。
- 避免提交 `Library/`、`Temp/`、`Obj/`、`Logs/`、`Build/`、`Builds/`、`.vs/`、IDE 生成工程文件和其他 `.gitignore` 已排除的内容。
- 不要把宿主工程的场景、ProjectSettings 或构建输出复制进这个插件包，除非任务明确要求。
- 新增 Shader、Compute Shader、纹理或 ScriptableObject 时，检查资源路径、引用、`.meta` 和运行时加载方式。

## 验证流程

仓库包含 Python 服务测试和 Unity EditMode 测试；验证应按改动范围运行自动化测试、Unity 编译和必要的手工冒烟测试。

### 每次改动都做

```powershell
git diff --check
git status --short
git diff
```

确认 diff 只包含任务相关文件，没有意外的 Unity 资产或生成文件。

### 有 Unity 2022.3 宿主工程时

1. 通过 `.LinkUnity.bat` 或宿主工程的本地包路径接入本仓库。
2. 等待 Unity 完成导入，确认 `DawnTOD` 和 `DawnTOD.Editor` 程序集无编译错误，Console 没有新增异常。
3. 使用 Hierarchy 菜单创建 TOD 系统和天气控制器，确认组件能正常添加。
4. 创建 `TODPreset`，分配给天气控制器，验证曲线/渐变编辑、Inspector 刷新和资产保存。
5. 拖动 TOD 时间滑杆，检查日光/月光、天气参数和 Scene View 预览；播放模式下验证自动推进、日出/日落和跨午夜行为。
6. 涉及降雨时，检查 `DawnGPUParticleSystem`、Compute Shader、雨滴 Shader、雨滴纹理和密度/速度参数。
7. 涉及 URP/HDRP 时，在对应管线宿主项目分别编译和运行最小场景，并记录实际覆盖的管线。
8. 涉及 AI 时，确认服务能到达 `Ready`、请求历史不含 API Key、非法/取消请求不应用修改，退出 Unity 后服务进程已结束。

如果没有可用的 Unity 宿主工程，应明确报告：已完成源码/文档和 Git 检查，但未运行 Unity 编译及 URP/HDRP 手动冒烟测试。

## 交付前检查

- 说明修改了哪些文件，以及是否触及序列化数据或渲染管线。
- 报告运行过的命令和 Unity 验证范围；不要把未执行的测试写成已通过。
- 保留清晰、最小的 diff；不顺手重构无关代码。
- 涉及 AI 时，额外说明是否重建 EXE、Python/Unity 测试结果，以及是否因缺少用户 API Key 而未执行真实 DeepSeek 手工验收。
