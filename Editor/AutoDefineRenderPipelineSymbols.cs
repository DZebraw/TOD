using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
[ExecuteInEditMode]
public static class AutoDefineRenderPipelineSymbols
{
    private const string DEFINE_URP = "USING_URP";
    private const string DEFINE_HDRP = "USING_HDRP";

    // 管线类型名称通过反射匹配
    private const string URP_PIPELINE_ASSET_TYPE_NAME = "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset";
    private const string HDRP_PIPELINE_ASSET_TYPE_NAME = "UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset";

    static AutoDefineRenderPipelineSymbols()
    {
        // 延迟一帧执行，确保编辑器资源加载完成
        EditorApplication.delayCall += () =>
        {
            try
            {
                UpdatePipelineDefines();
            }
            catch (Exception e)
            {
                Debug.LogError($"[管线宏检测] 执行失败：{e.Message}\n{e.StackTrace}");
            }
        };
    }

    /// <summary>
    /// 核心逻辑：通过反射检测管线类型并更新宏定义
    /// </summary>
    private static void UpdatePipelineDefines()
    {
        RenderPipelineAsset currentPipeline = QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline;
        BuildTargetGroup targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;

        string existingSymbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
        var symbolsList = existingSymbols.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

        symbolsList.Remove(DEFINE_URP);
        symbolsList.Remove(DEFINE_HDRP);

        // 通过反射判断管线类型
        if (currentPipeline != null)
        {
            string pipelineAssetTypeFullName = currentPipeline.GetType().FullName;

            // 匹配URP类型
            if (pipelineAssetTypeFullName == URP_PIPELINE_ASSET_TYPE_NAME)
            {
                symbolsList.Add(DEFINE_URP);
                //Debug.Log($"[管线宏检测] 检测到{pipelineType}，已添加 {DEFINE_URP} 宏定义。");
            }
            // 匹配HDRP类型
            else if (pipelineAssetTypeFullName == HDRP_PIPELINE_ASSET_TYPE_NAME)
            {
                symbolsList.Add(DEFINE_HDRP);
                //Debug.Log($"[管线宏检测] 检测到{pipelineType}，已添加 {DEFINE_HDRP} 宏定义。");
            }
            else
            {
                Debug.Log($"[管线宏检测] 当前使用未知管线类型：{pipelineAssetTypeFullName}，不添加宏定义。");
            }
        }

        string newSymbols = string.Join(";", symbolsList);
        PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, newSymbols);
    }

    /// <summary>
    /// 手动触发检测的菜单选项
    /// </summary>
    //[MenuItem("Tools/刷新管线宏定义")]
    //public static void ManualRefreshDefines()
    //{
    //    UpdatePipelineDefines();
    //    EditorUtility.DisplayDialog("成功", "已根据当前渲染管线刷新宏定义！", "确定");
    //}
}