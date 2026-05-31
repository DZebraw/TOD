// using UnityEngine;
// using DawnTOD;
// using GPUBaking;
//
// public class DawnTODSystemAdapter : DawnTimeOfDayProvider
// {
//     /// <summary>
//     /// "烘焙时模拟的一天总时长（秒），用于 BakeEnvironment 遍历所有帧"
//     /// </summary>
//     private const float bakeDuration = 240.0f;
//
//     private DawnTODSystem todSystem;
//
//     /// <summary>
//     /// DawnTODSystem 使用 0-24 小时制，这里将其映射为秒数。
//     /// 运行时 DawnRuntimeManager 用 CurrentTime 来索引 SH 帧数据，
//     /// 所以这里返回的值需要与烘焙时 SetTimeAndEvaluate 传入的 time 参数一致。
//     /// 
//     /// 映射关系：currentTime(秒) = (timeOfDay / 24) * bakeDuration
//     /// </summary>
//     public override float CurrentTime
//     {
//         get
//         {
//             EnsureInitialized();
//             if (todSystem == null) return 0f;
//             float normalizedTime = todSystem.NormalizedTime;
//             return (float)(normalizedTime * bakeDuration);
//         }
//     }
//
//     public override float Duration => bakeDuration;
//
//     public override bool IsValid
//     {
//         get
//         {
//             EnsureInitialized();
//             return todSystem != null;
//         }
//     }
//
//     private void Awake()
//     {
//         todSystem = GetComponent<DawnTODSystem>();
//     }
//
//     private void EnsureInitialized()
//     {
//         if (todSystem == null)
//         {
//             todSystem = GetComponent<DawnTODSystem>();
//         }
//     }
//
//     /// <summary>
//     /// 烘焙时由 DawnRuntimeManager 调用。
//     /// 将秒数时间反算为 0-24 小时制，然后调用 DawnTODSystem.Evaluate 驱动场景更新。
//     /// </summary>
//     /// <param name="time">时间（秒），范围 [0, bakeDuration]</param>
//     public override void SetTimeAndEvaluate(float time)
//     {
//         EnsureInitialized();
//         if (todSystem == null)
//         {
//             Debug.LogError("DawnTODSystemAdapter: SetTimeAndEvaluate 失败，DawnTODSystem 为空。");
//             return;
//         }
//
//         float normalizedTime = bakeDuration > 0 ? Mathf.Clamp01((float)(time / bakeDuration)) : 0f;
//         todSystem.Evaluate(normalizedTime);
//     }
//
//     private void OnValidate()
//     {
//         if (todSystem == null)
//         {
//             todSystem = GetComponent<DawnTODSystem>();
//         }
//     }
//
//     public override Light GetMainDirectionalLight()
//     {
//         return todSystem == null ? null : todSystem.GetMainDirectionalLight();
//     }
// }
