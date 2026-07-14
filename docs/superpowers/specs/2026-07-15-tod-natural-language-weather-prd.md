# DawnTOD LightingEditor View1 自然语言天气助手 PRD

## 1. 文档信息

| 项目 | 内容 |
| --- | --- |
| 文档状态 | 已确认 |
| 日期 | 2026-07-15 |
| 目标仓库 | `com.tencent.dawn.tod` |
| Unity 基线 | Unity 2022.3 |
| 首版平台 | Windows Unity Editor + URP |
| 首版视图 | `LightingEditor` 的 `View1 / Level1_Playback` |

## 2. 产品摘要

在 DawnTOD 的 `LightingEditor` View1 中增加一个常驻自然语言天气助手。用户选择场景中的 `DawnWeatherController`，输入“设置为正午”“让太阳暗一点”等自然语言指令，工具通过本地 Windows 服务调用 LLM，把文本转换为版本化、固定结构的 JSON 稀疏补丁。Unity C# 对 JSON 进行二次校验后，立即把非空字段原子应用到当前 Controller 及其 `ActivePreset`，并支持一次 `Ctrl+Z` 完整撤销。

产品采用分阶段交付。第一阶段不调用真实 LLM，而是完整走通 `View1 → 本地 EXE → 固定 JSON → C# 校验 → Preset/Controller → Undo` 的端到端冒烟链路。第二阶段接入 DeepSeek 官方 API，并用版本化 Skill、System Prompt 和 JSON Schema 约束模型输出。

## 3. 背景与问题

当前 `LightingEditor` 的 View1 中部为空，只保留顶部工具栏和底部播放条；View3 则用于曲线编辑。TOD 参数以 `DawnWeatherPreset` 中的 `AnimationCurve` 和 `Gradient` 存储，用户需要理解方位角、仰角、强度、关键帧和颜色渐变等概念才能完成天气或光照设置。

目标是给非专业用户提供一个低门槛入口，同时保持现有曲线资产、Undo、编辑器刷新和资产保存流程不变。模型不能直接控制 Unity，也不能输出任意脚本或任意字段；它只能产生受 Schema 和能力白名单约束的数据补丁。

参考实现为 `UI_Replacer`/`UIAutoKit` 的 Unity C# → 本地 EXE/FastAPI → LLM 调用模式。DawnTOD 使用独立服务、独立端口和独立配置，不依赖或修改 `UiVerse.exe`。

## 4. 目标与非目标

### 4.1 产品目标

1. 在 View1 内提供自然语言输入、目标 Controller 选择、服务控制和操作记录。
2. 用固定、版本化的全量 JSON 结构表达稀疏修改；未指定字段始终为 JSON `null`。
3. 首版只修改时间、太阳和月亮，并在 URP 中得到可见结果。
4. 所有修改必须在完整校验后一次性应用，并由单个 Undo Group 撤销。
5. 通过独立 Python/EXE 隔离 LLM SDK、Prompt、Skill、Schema、网络重试和日志逻辑。
6. API Key 只保存在用户本机，不进入工程、Git、日志或请求历史。
7. 保持 Unity Editor 主线程响应，服务启动和 HTTP 请求均不得同步阻塞 OnGUI。

### 4.2 首版非目标

- 不支持多轮对话上下文。
- 不支持 HDRP、macOS、Linux 或运行时构建。
- 不应用雾、星空、曝光和降雨字段。
- 不自动创建或复制 `DawnWeatherPreset`。
- 不自动调用 `AssetDatabase.SaveAssets()`。
- 不提供预览确认步骤；有效结果返回后立即应用。
- 不支持语音、图片或场景截图输入。
- 不允许 LLM 输出或执行 C#、Python、Shader、命令行或文件操作。

## 5. 用户与核心场景

### 5.1 目标用户

- 希望快速调整 TOD 光照但不熟悉曲线编辑的美术、策划和关卡设计人员。
- 需要通过自然语言快速建立光照基线，再到 View3 精细调整的技术美术。

### 5.2 首版支持的输入示例

- “设置为正午。”
- “把太阳强度改为 1.5。”
- “保持当前时间，让太阳更亮一点。”
- “太阳方位角 240 度，仰角 35 度。”
- “把月光颜色调成偏冷的蓝色。”

“更亮一点”等相对表达不依赖对话历史。Unity 会把目标时刻的当前 Preset 参数快照与用户文本一并发送，模型相对于该快照计算实际值。

## 6. 分阶段范围

### 6.1 阶段 1：端到端冒烟版

- 完成 View1 常驻面板、Controller/Preset 选择、服务控制、操作历史、多行输入、取消和 Undo。
- 提供 Windows 专用 EXE、对应 Python 源码和可复现构建脚本。
- 服务报告 `mode = smoke`，任意非空输入都返回固定 URP 正午 JSON。
- C# 完成严格解析、二次校验和时间/太阳/月亮消费。
- Smoke 模式不调用 DeepSeek，也不要求 API Key。

### 6.2 阶段 2：真实 LLM 版

- 接入 DeepSeek 官方 OpenAI 兼容 API。
- 固定使用 `deepseek-v4-flash`，关闭 Thinking，启用 JSON Output，`temperature = 0`，使用非流式响应。
- 加载版本化 `SKILL.md`、System Prompt 和 JSON Schema。
- 请求包含用户文本、URP 能力白名单、捕获时间和当前参数快照。
- 实现一次模型修复、网络重试、API Key 配置和本机日志。
- 仍只应用时间、太阳和月亮。

### 6.3 后续扩展

1. 开放降雨参数。
2. 为 URP 增加雾、星空和曝光的真实运行时消费路径，再开放对应 JSON 字段。
3. 增加多轮上下文，使“再亮一点”可以引用上一轮结果。
4. 评估 HDRP 与其他编辑器平台。

雾字段的未来语义必须与运行时一致：`fog.mean_free_path_m` 控制雾的浓淡，数值越小雾越浓；`fog.base_height_m` 只表示雾层基准高度。不得把现有 `fogHeightCurve` 的错误“雾密度”注释当作实际运行时语义。

## 7. 用户流程

1. 用户打开 `MagicDawn/TOD/Lighting Editor` 并切换到 View1。
2. 用户从 View1 顶部下拉框选择 `DawnWeatherController`；下拉框默认跟随 Hierarchy 当前选择。
3. View1 显示所选 Controller 的 `ActivePreset`。Controller 或 Preset 为空时禁止发送。
4. 用户可打开“设置”弹窗录入 API Key。Smoke 模式不要求 Key。
5. 用户手动点击“启动服务”。服务启动并通过健康检查后进入 Ready。
6. 用户输入自然语言并点击发送或按 `Ctrl+Enter`。
7. LightingEditor 自动暂停播放，捕获 Controller、Preset、当前时间和当前参数快照，并锁定本次任务目标。
8. 服务返回 JSON 后，Python 和 C# 分别完成校验。
9. C# 在主线程用一个 Undo Group 修改 Controller 和 Preset，随后刷新轨道、LightingEditor 和 Scene View。
10. 历史区显示实际应用字段、耗时、结果及可折叠原始 JSON，并提示可以 `Ctrl+Z` 撤销。

请求期间只允许一个任务。用户可取消；取消后任何迟到响应都必须根据 `request_id` 丢弃，不能修改 Unity 对象。

## 8. View1 UI 需求

### 8.1 布局

View1 中部新增常驻面板，保留现有顶部工具栏和底部播放条。

```text
┌ Controller: [WeatherController ▼]  Preset: ClearDay
│ Service: ● Ready   [启动] [停止] [重启] [设置]
├────────────────────────────────────────────────────
│ 12:01  用户：设置为正午
│        ✓ 已应用 7 个字段，可通过 Ctrl+Z 撤销
│        [展开原始 JSON]
│
│ 12:03  用户：让太阳暗一点
│        正在分析…  [取消]
├────────────────────────────────────────────────────
│ [多行自然语言输入框                         ]
│                                     [发送]
└────────────────────────────────────────────────────
```

### 8.2 交互状态

任务状态机为：

```text
Ready → Analyzing → Validating → Applying → Success / Error / Cancelled → Ready
```

服务状态为：

```text
Stopped → Starting → Ready / Error → Stopping → Stopped
```

### 8.3 发送条件

以下条件全部满足时才允许发送：

- 当前为 Windows Unity Editor。
- 服务状态为 Ready。
- 已选择 Controller，且 `ActivePreset` 非空。
- 输入不为空白。
- 当前没有进行中的请求。
- DeepSeek 模式下 API Key 已配置；Smoke 模式不要求 Key。

### 8.4 操作历史

历史记录包含：用户文本、捕获时间、目标 Controller/Preset、请求状态、实际应用字段、耗时、错误摘要和原始 JSON。原始 JSON 默认折叠。UI 历史只保留当前 LightingEditor 会话；持久化服务日志保存在本机日志目录，且不包含 API Key。

## 9. 系统架构

```text
LightingEditor View1
  └─ TOD AI Drawer
      ├─ Service Controller
      ├─ HTTP Client / Request Coordinator
      ├─ JSON Parser + Validator
      └─ Weather Preset Patch Applier
             │
             ▼
http://127.0.0.1:13296
             │
             ▼
DawnTodAiService.exe
  ├─ FastAPI endpoints
  ├─ Skill / Prompt / Schema loader
  ├─ Smoke provider
  ├─ DeepSeek provider
  ├─ JSON Schema validator / one-shot repair
  └─ Local rotating logger
```

### 9.1 建议目录边界

```text
Editor/LightingEditor/AI/
  UI/
  Client/
  Validation/
  Application/
  Service/
    Windows/DawnTodAiService.exe
    Source/
  Skills/weather-intent/SKILL.md
  Prompts/weather-intent-system.md
  Schemas/weather-intent-v1.schema.json
```

具体类名可在实施计划中调整，但 UI、进程管理、HTTP、校验和 Preset 写入必须保持独立边界，避免继续膨胀 `DawnLightingEditorWindow.cs`。

### 9.2 双层校验职责

Python 负责：

- 调用模型并要求 JSON Output。
- 依据 JSON Schema 检查结构、类型、必填字段、`null` 和范围。
- 检查首版能力白名单。
- 在首次响应无效时，携带原响应和校验错误修复一次。
- 只向 Unity 返回有效数据或结构化错误。

Unity C# 负责：

- 校验传输信封、`request_id`、`schema_version`、服务版本和 Skill 哈希。
- 使用相同能力白名单和数值范围进行二次校验。
- 校验 Controller/Preset 生命周期和请求快照。
- 在主线程原子应用数据，并负责 Undo、Dirty 和刷新。

## 10. JSON 技术选型

### 10.1 Unity 解析库

沿用参考工程 `UIAutoKit/Common` 的方案，在 DawnTOD `package.json` 中增加 Unity 官方依赖：

```json
"com.unity.nuget.newtonsoft-json": "3.2.1"
```

C# 使用 `Newtonsoft.Json.Linq` 的 `JObject/JToken` 做严格结构、类型和 `JTokenType.Null` 判断，再转换为内部 DTO。不得使用 `JsonUtility` 把 `null` 静默转换为数值类型默认值，也不在包内维护自定义 JSON 解析器。

### 10.2 Schema 版本

首个版本固定为字符串 `"1.0"`。Python 与 C# 都内置支持的 Schema 版本，并在 `/status` 握手时比较。版本不一致时服务不可用，不做猜测兼容。

## 11. 请求与响应协议

### 11.1 请求示例

```json
{
  "request_id": "8b482eef-5512-4d79-baba-a95adba31424",
  "schema_version": "1.0",
  "pipeline": "URP",
  "user_input": "让太阳再亮一点",
  "capabilities": {
    "supported_non_null_fields": [
      "time",
      "sun.azimuth_deg",
      "sun.elevation_deg",
      "sun.intensity",
      "sun.color",
      "moon.azimuth_deg",
      "moon.elevation_deg",
      "moon.intensity",
      "moon.color"
    ]
  },
  "snapshot": {
    "time_hour": 10.5,
    "sun": {
      "azimuth_deg": 247.5,
      "elevation_deg": 52.0,
      "intensity": 1.6,
      "color": { "r": 1.0, "g": 0.9, "b": 0.8, "a": 1.0 }
    },
    "moon": {
      "azimuth_deg": 67.5,
      "elevation_deg": -52.0,
      "intensity": 0.2,
      "color": { "r": 0.7, "g": 0.8, "b": 1.0, "a": 1.0 }
    }
  }
}
```

用户文本必须作为不可信数据与 System Prompt、Skill 和 Schema 隔离。用户文本中的“忽略以上规则”“输出脚本”等内容不能改变模型职责。

### 11.2 模型数据对象

模型只返回数据对象，不返回 Markdown、代码块或解释文字。结构固定如下：

```json
{
  "schema_version": "1.0",
  "time": {
    "mode": "explicit",
    "hour": 12.0
  },
  "sun": {
    "azimuth_deg": 270.0,
    "elevation_deg": 60.0,
    "intensity": 2.0,
    "color": { "r": 1.0, "g": 0.98, "b": 0.92, "a": 1.0 }
  },
  "moon": {
    "azimuth_deg": 90.0,
    "elevation_deg": -60.0,
    "intensity": 0.2,
    "color": { "r": 0.7, "g": 0.8, "b": 1.0, "a": 1.0 }
  },
  "sky": {
    "star_emission": null
  },
  "fog": {
    "mean_free_path_m": null,
    "base_height_m": null,
    "color": null
  },
  "exposure": {
    "compensation_ev": null
  },
  "rain": {
    "enabled": null,
    "fall_speed": null,
    "density": null,
    "wind_z_rotation_deg": null
  }
}
```

该对象也是阶段 1 的固定 Smoke 响应。方位角使用规范化 `0–360°`；C# 写入曲线前负责角度展开。

### 11.3 稀疏补丁规则

- 所有定义的字段必须始终存在。
- 用户未要求修改的叶子字段必须为 JSON `null`。
- 用户未指定时间时固定输出：

```json
"time": { "mode": "current", "hour": null }
```

- 用户指定时间时输出 `mode = explicit` 和具体 `hour`。
- 只有用户明确要求修改的字段才能非空；不得自行联动或补写其他参数。
- 首版 `sky/fog/exposure/rain` 的全部叶子字段必须为 `null`。
- 没有任何可应用字段时返回“无有效修改”，不创建 Undo。
- 用户只请求首版尚未开放的能力时，传输层返回稳定错误码 `NO_SUPPORTED_CHANGE`，不得用其他字段替代用户意图。

例如只修改太阳强度：

```json
{
  "schema_version": "1.0",
  "time": { "mode": "current", "hour": null },
  "sun": {
    "azimuth_deg": null,
    "elevation_deg": null,
    "intensity": 2.2,
    "color": null
  },
  "moon": {
    "azimuth_deg": null,
    "elevation_deg": null,
    "intensity": null,
    "color": null
  },
  "sky": { "star_emission": null },
  "fog": {
    "mean_free_path_m": null,
    "base_height_m": null,
    "color": null
  },
  "exposure": { "compensation_ev": null },
  "rain": {
    "enabled": null,
    "fall_speed": null,
    "density": null,
    "wind_z_rotation_deg": null
  }
}
```

### 11.4 传输信封

服务在模型数据对象外增加传输信封。模型不生成信封字段。

```json
{
  "request_id": "8b482eef-5512-4d79-baba-a95adba31424",
  "status": "ok",
  "mode": "smoke",
  "data": {
    "schema_version": "1.0",
    "time": { "mode": "explicit", "hour": 12.0 },
    "sun": {
      "azimuth_deg": 270.0,
      "elevation_deg": 60.0,
      "intensity": 2.0,
      "color": { "r": 1.0, "g": 0.98, "b": 0.92, "a": 1.0 }
    },
    "moon": {
      "azimuth_deg": 90.0,
      "elevation_deg": -60.0,
      "intensity": 0.2,
      "color": { "r": 0.7, "g": 0.8, "b": 1.0, "a": 1.0 }
    },
    "sky": { "star_emission": null },
    "fog": {
      "mean_free_path_m": null,
      "base_height_m": null,
      "color": null
    },
    "exposure": { "compensation_ev": null },
    "rain": {
      "enabled": null,
      "fall_speed": null,
      "density": null,
      "wind_z_rotation_deg": null
    }
  },
  "error": null
}
```

错误响应中 `status = error`、`data = null`，并返回稳定错误码和可读错误信息。

## 12. 数值与能力校验

### 12.1 首版合法范围

| 字段 | 单位 | 合法范围 |
| --- | --- | --- |
| `time.hour` | 小时 | `0 ≤ value < 24` |
| `sun.azimuth_deg` | 度 | `0 ≤ value < 360` |
| `sun.elevation_deg` | 度 | `-90 ≤ value ≤ 90` |
| `sun.intensity` | URP Light intensity | `0 ≤ value ≤ 8` |
| `sun.color.*` | Unity Color RGBA 分量 | `0 ≤ value ≤ 1` |
| `moon.azimuth_deg` | 度 | `0 ≤ value < 360` |
| `moon.elevation_deg` | 度 | `-90 ≤ value ≤ 90` |
| `moon.intensity` | URP Light intensity | `0 ≤ value ≤ 8` |
| `moon.color.*` | Unity Color RGBA 分量 | `0 ≤ value ≤ 1` |

所有数字必须为有限值，禁止 `NaN`、正无穷和负无穷。C# 不静默 Clamp 非法值；任何越界都使整次响应失败。

### 12.2 首版能力白名单

首版只允许以下字段非空：

- `time.mode` 与显式时间下的 `time.hour`
- `sun.azimuth_deg`
- `sun.elevation_deg`
- `sun.intensity`
- `sun.color`
- `moon.azimuth_deg`
- `moon.elevation_deg`
- `moon.intensity`
- `moon.color`

保留字段出现非空值时，Python 首次校验失败并进入一次修复；C# 仍检测到时视为协议错误，整次不应用。

## 13. JSON 到 TOD 的应用规则

### 13.1 字段映射

| JSON 字段 | Unity 目标 |
| --- | --- |
| `time` | `DawnWeatherController.TimeOfDay` 与 LightingEditor 当前时间 |
| `sun.azimuth_deg` | `ActivePreset.sunAzimuthCurve` |
| `sun.elevation_deg` | `ActivePreset.sunElevationCurve` |
| `sun.intensity` | `ActivePreset.sunIntensityCurve` |
| `sun.color` | `ActivePreset.sunColorGradient` |
| `moon.azimuth_deg` | `ActivePreset.moonAzimuthCurve` |
| `moon.elevation_deg` | `ActivePreset.moonElevationCurve` |
| `moon.intensity` | `ActivePreset.moonIntensityCurve` |
| `moon.color` | `ActivePreset.moonColorGradient` |

其他字段在首版没有 Unity 写入目标。

### 13.2 目标时间

- `time.mode = explicit`：目标时间为 `hour / 24`，同时把 Controller 和 LightingEditor 时间设置为该小时。
- `time.mode = current`：目标时间为点击发送时捕获的时间，不使用响应返回时的时间。
- 发送时自动暂停播放，避免请求期间时间继续变化。

### 13.3 曲线写入

- 目标时间已有关键帧时，更新值并保留原切线。
- 目标时间没有关键帧时新增关键帧，并沿用现有 LightingEditor 的平滑切线行为。
- 使用统一时间容差判断同一关键帧，避免浮点误差产生重复 Key。
- 其他时刻的 Key 不得修改、移动或删除。

### 13.4 渐变写入

- RGBA 非空时，在目标时间同时新增或更新 Color Key 和 Alpha Key。
- 其他 Color Key 和 Alpha Key 保持不变。
- 同一时间只能保留一组由本次写入产生的颜色/透明度 Key。

### 13.5 方位角展开

模型输出规范化方位角。C# 以目标时间当前曲线值为基准，选择与模型值等价且距离最近的 `value + 360 × n`。例如模型输出 `90°`、当前曲线附近为 `450°`，写入值为 `450°`，避免跨越 0/360 度时反向插值。

### 13.6 原子事务与刷新

1. 完成所有解析、版本、字段、范围、能力和对象生命周期校验。
2. 再次确认捕获的 Controller 存活且仍引用捕获的 `ActivePreset`。
3. 创建一个命名 Undo Group，同时记录 Controller 与 Preset。
4. 应用所有非空字段。
5. 发生异常时回滚整个 Undo Group。
6. 成功后标记相关对象 Dirty，刷新 TrackManager、LightingEditor 与 Scene View。
7. 不调用 `AssetDatabase.SaveAssets()`。

## 14. 本地服务设计

### 14.1 服务身份

| 项目 | 固定值 |
| --- | --- |
| EXE 名称 | `DawnTodAiService.exe` |
| 绑定地址 | `127.0.0.1` |
| 端口 | `13296` |
| 官方 API 地址 | `https://api.deepseek.com` |
| 模型 | `deepseek-v4-flash` |
| 推理模式 | Thinking disabled |
| 输出模式 | JSON Output |

模型 ID 和地址依据 DeepSeek 官方文档固定：

- <https://api-docs.deepseek.com/>
- <https://api-docs.deepseek.com/api/list-models>

### 14.2 HTTP 接口

| 接口 | 方法 | 作用 |
| --- | --- | --- |
| `/status` | GET | 返回健康状态、运行模式、服务版本、Schema 版本和 Skill 哈希 |
| `/analyze` | POST | 提交单轮自然语言解析任务并返回传输信封 |
| `/tasks/{request_id}/cancel` | POST | 标记任务取消，并禁止其结果被采用 |
| `/shutdown` | POST | 请求服务正常退出 |

### 14.3 手动生命周期

- 打开 View1 不自动启动服务。
- 用户通过 View1 的启动、停止、重启按钮管理服务。
- 关闭 LightingEditor 窗口不停止服务。
- Unity 正常退出时结束由插件启动的进程。
- Unity 启动服务时传入父进程 PID；父进程异常消失时服务自行退出。
- Unity 程序集重载时使用 `SessionState` 保存 PID 和会话令牌并尝试重新健康检查；不能安全重连时重置为 Stopped。

### 14.4 路径定位

C# 必须通过当前包位置解析 EXE 和 Skill/Prompt/Schema 路径，不使用开发机绝对路径。启动前校验 EXE 与所需文件存在。结束进程前校验 PID 对应的可执行文件位于当前包的服务目录下，避免误杀同名进程。

## 15. API Key 与本地安全

- View1 设置弹窗只开放 API Key，不开放 `base_url`、模型、Thinking、temperature 或端口。
- API Key 使用当前 Windows 用户的 DPAPI 加密后写入 `%LOCALAPPDATA%\DawnTODAI\config.json`。
- API Key 不随每次分析请求从 Unity 传给服务；服务以同一 Windows 用户身份读取并解密本地配置。
- 启动服务时由 Unity 生成随机会话令牌，通过进程环境变量传入。
- `/status`、`/analyze`、`/cancel`、`/shutdown` 都要求会话令牌请求头。
- 服务只绑定 Loopback，不监听局域网或公网地址。
- 日志、异常、UI 历史和 Unity Console 中不得输出 API Key 或 Authorization Header。
- 用户文本和模型 JSON 除发送到 DeepSeek 官方 API 外，不上传到任何其他服务。

## 16. Prompt、Skill 与 Schema

### 16.1 文件职责

- `SKILL.md`：领域知识、参数含义、实际单位、合法范围、时间词汇、相对修改规则和示例。
- System Prompt：模型角色、输出限制、Prompt 注入防护、能力白名单和调用策略。
- JSON Schema：固定字段、必填项、可空类型、枚举、数值范围和 `additionalProperties = false`。

三者均随包版本控制，由服务启动时加载。任一文件缺失、无法解析或版本不一致时 `/status` 不得报告 Ready。

### 16.2 输出约束

- 只输出一个 JSON 对象。
- 不输出 Markdown、解释、思考过程或代码块。
- 顶层与所有定义字段必须存在。
- 未提及字段必须为 JSON `null`。
- 不允许额外字段。
- 只允许首版能力白名单字段非空。
- 模糊时间词转换为小时；未出现时间意图时使用 `current`。
- 相对表达基于请求中的当前参数快照，结果仍使用 TOD 实际单位。

### 16.3 一次修复

首次模型响应未通过 Python 校验时，服务发起一次修复请求。修复请求包含原始响应、结构化校验错误、相同 Schema 和能力白名单，不增加用户新意图。修复仍失败时返回错误，不返回部分结果。

## 17. 错误处理

### 17.1 服务启动错误

服务启动超过 5 秒仍未通过健康检查时进入 Error，并区分：EXE 缺失、端口占用、进程提前退出、Skill/Schema 缺失、版本不匹配和会话鉴权失败。

### 17.2 DeepSeek 错误

- `401/403`：不重试，提示 API Key 无效。
- `429/5xx/连接错误`：指数退避，最多重试 2 次。
- 单次请求超时 60 秒。
- 用户取消后，任务即使完成也不能返回可应用结果。

### 17.3 数据错误

- Python 校验失败：修复一次；仍失败则结构化报错。
- C# 二次校验失败：视为协议或版本错误，不再次调用模型。
- 任一错误均不允许部分应用。

### 17.4 Unity 对象错误

Controller/Preset 被删除、Preset 被更换、请求已取消或请求 ID 已过期时，结果只进入历史，不创建 Undo，不修改对象。

## 18. 非功能需求

### 18.1 响应性

- OnGUI 和 Unity 主线程不得同步等待进程或网络。
- Smoke 模式健康检查后，固定分析响应目标为 1 秒内。
- DeepSeek 单次调用上限 60 秒，UI 必须持续可取消。

### 18.2 可靠性

- 双层校验。
- 单任务模式。
- 单 Undo Group 原子修改。
- 迟到响应防护。
- 父进程监控和安全进程结束。

### 18.3 日志

日志保存在 `%LOCALAPPDATA%\DawnTODAI\logs`，按 `request_id` 记录服务生命周期、耗时、重试、校验结果和异常。日志滚动保存，最多 10 个文件，每个文件不超过 5 MB。不得记录 API Key。

### 18.4 分发

仓库同时保存：

- Python 源码。
- 依赖锁定文件。
- 可复现构建脚本。
- 已打包 Windows EXE。
- Unity `.meta` 文件。
- Skill、Prompt 和 JSON Schema。

首版不从远端下载可执行文件。

## 19. 测试策略

### 19.1 C# EditMode 自动化测试

- 完整 Schema 正常解析。
- 数值、对象与 `null` 的严格区分。
- 缺失字段、额外字段和错误类型被拒绝。
- 首版能力白名单拒绝保留字段非空。
- 时间、角度、强度和颜色范围校验。
- 方位角最近等价展开。
- 目标时间已有 Key 时更新且不重复。
- 目标时间无 Key 时新增，其他 Key 不变。
- Gradient Color/Alpha Key 同步更新。
- 单个 Undo 恢复 Controller 与 Preset。
- 校验失败和应用异常不产生部分修改。
- 取消与迟到响应不应用。
- Controller/Preset 生命周期变化使请求过期。

### 19.2 Python 自动化测试

- `/status` 的模式、版本和哈希。
- Smoke 模式任意非空输入返回同一固定 JSON。
- 空输入被拒绝。
- Schema、Skill 或 Prompt 缺失时健康检查失败。
- JSON Schema 对缺失、额外、错误类型、越界和非法非空字段的拒绝。
- 会话令牌鉴权。
- 取消标记。
- 日志脱敏和滚动。
- DeepSeek Provider 使用固定地址、模型、非 Thinking 和 JSON Output。
- 非法模型响应只修复一次。

### 19.3 Unity 手工冒烟

1. 在 Windows URP 宿主工程中打开 LightingEditor View1。
2. 选择拥有 `ActivePreset` 的 Controller。
3. 手动启动服务并确认 UI 显示 Smoke/Ready。
4. 输入任意非空文字并发送。
5. 验证 Controller 变为 12:00。
6. 验证正午位置的太阳/月亮角度、强度和颜色 Key 被新增或更新。
7. 验证 Scene View 和 View3 曲线立即刷新。
8. 执行一次 `Ctrl+Z`，确认 Controller 与 Preset 完整恢复。
9. 测试取消、目标切换、Preset 更换、服务停止和非法响应场景。
10. 退出 Unity，确认服务进程结束。

## 20. 验收标准

1. 任意非空输入可完整经过 `View1 → EXE → 固定 JSON → C# → Controller/Preset`。
2. Smoke 响应把 Controller 和 LightingEditor 时间设置为 12:00，并在正午新增或更新太阳/月亮的角度、强度与颜色 Key。
3. 只产生一个 Undo 操作；一次 `Ctrl+Z` 完整恢复 Controller 和 Preset。
4. 不自动保存资产，其他时刻关键帧保持不变。
5. 取消、非法 JSON、越界值、版本不一致和目标失效均不得产生部分修改。
6. 请求期间 Unity Editor 保持响应。
7. 服务未启动、目标未选择或 Preset 为空时不能发送。
8. 非 Windows 平台明确显示不支持，不尝试启动服务。
9. API Key 不进入工程、Git、日志、操作历史或请求正文。
10. C# 与 Python 自动化测试通过，并完成 Windows URP Unity 手工冒烟。

## 21. 风险与缓解

| 风险 | 缓解措施 |
| --- | --- |
| 模型输出漂移 | JSON Output、temperature 0、版本化 Skill/Schema、一次修复、C# 二次校验 |
| Python/C# Schema 漂移 | `/status` 版本与 Skill 哈希握手；不匹配时拒绝服务 |
| 方位角跨 360 度导致反向插值 | C# 最近等价角展开 |
| 异步响应应用到错误目标 | 捕获 Controller/Preset、请求 ID、对象生命周期复核和迟到响应丢弃 |
| 部分写入污染资产 | 先完整校验，单 Undo Group，异常回滚 |
| EXE 被安全软件误报 | 保留可复现源码、依赖锁与构建脚本；发布前做签名和安全扫描 |
| Unity 崩溃遗留进程 | 父进程 PID 监控，服务自退出 |
| API Key 泄露 | Windows DPAPI、Loopback、会话令牌、日志脱敏 |
| Newtonsoft 依赖缺失 | DawnTOD `package.json` 显式声明官方 `3.2.1` 依赖 |
| URP 不消费雾/星空/曝光 | 首版能力白名单禁止这些字段非空；完成 URP 运行时适配后再开放 |

## 22. 已确认的关键决策

- 有效 JSON 返回后立即应用，不提供确认预览。
- 直接修改当前 Controller 的 `ActivePreset`，不复制 Preset。
- 使用独立 Python 服务和独立 EXE，不复用 UiVerse。
- 只允许用户配置 API Key；模型和地址固定。
- 模型输出 TOD 实际单位，不使用 C# 归一化天气映射。
- JSON 为固定全量结构的稀疏补丁，未指定字段为 `null`。
- 时间默认 `current`，明确时间使用 `explicit + hour`。
- 首版只支持 Windows、URP、时间、太阳和月亮。
- 首版是完整端到端 Smoke，不调用真实 LLM。
- 单轮、单任务、可取消，发送时暂停并捕获当前时间。
- 修改进入单个 Undo，不自动保存资产。
- 日志仅本机保存，不额外上传。
- Python/C# 双层校验。
- Unity JSON 使用官方 Newtonsoft JSON `3.2.1`。
