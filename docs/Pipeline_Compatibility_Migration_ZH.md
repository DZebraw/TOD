# 双渲染管线兼容迁移说明

本次兼容层重构面向 Unity 2022.3 与 SRP 14.x。核心程序集不再依赖宿主工程的
`USING_URP` / `USING_HDRP` 全局宏，也不会自动修改 Scripting Define Symbols。
URP、HDRP 适配器分别由各自程序集的 `versionDefines` 编译和注册。

## 自定义程序集引用

只使用管线中立类型（例如 `DawnTODSystem`、`DawnWeatherController`、
`DawnWeatherPreset`）的程序集继续引用 `DawnTOD`。

直接使用下列管线专属类型的自定义 `.asmdef` 需要增加对应引用：

- `RuntimeSkySetting`、Dawn URP Volume、URP Renderer Feature：引用
  `DawnTOD.URP`。
- `HDRPIntegration` 或其他 HDRP 专属类型：引用 `DawnTOD.HDRP`。
- 编辑器代码直接使用管线专属编辑器工具时，再分别引用
  `DawnTOD.Editor.URP` 或 `DawnTOD.Editor.HDRP`。

Scene、Prefab 和资产的脚本 GUID 保持不变；这项迁移只影响编译期程序集引用。

## 程序化创建天气预设

`Assets/Create/MagicDawn/TODPreset` 菜单会按当前实际渲染管线创建正确量纲的
默认曲线。

程序化创建时应显式指定目标管线：

```csharp
DawnWeatherPreset preset = DawnWeatherPreset.CreateWithDefaults(
    WeatherRenderPipelineKind.HighDefinition);
```

直接调用 `ScriptableObject.CreateInstance<DawnWeatherPreset>()` 仍可用，但现在
采用确定性的核心回退值（URP 量纲），不再在 ScriptableObject 字段初始化阶段
查询当前渲染管线。HDRP 工具代码应改用上面的工厂方法。

## Built-in Render Pipeline

当前管线输出策略只实现 URP 与 HDRP。核心仍保留
`UnityEngine.Rendering.Volume` 类型和共享 SRP 资源，以兼容现有 Scene、
Prefab 与公开字段；因此未安装 `com.unity.render-pipelines.core` 的纯
Built-in 工程不在本次完整导入保证范围内。
