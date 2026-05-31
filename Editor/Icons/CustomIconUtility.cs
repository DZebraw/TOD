using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DawnTODEditor
{
    public static class CustomIconUtility
    {
        // 缓存脚本所在目录，避免重复计算
        private static string _scriptDirectory;

        /// <summary>
        /// 获取同目录下的图标，返回GUIContent（图标不存在时返回默认警告图标）
        /// </summary>
        /// <param name="iconName">图标文件名（不带后缀，默认读取.png）</param>
        public static GUIContent Icon(string iconName)
        {
            // 1. 获取脚本所在的绝对目录，再转换为Unity工程相对路径（AssetDatabase识别的路径）
            string scriptFolderPath = GetScriptDirectory();
            if (string.IsNullOrEmpty(scriptFolderPath))
            {
                return EditorGUIUtility.IconContent("console.warnicon");
            }

            // 2. 拼接完整的图标路径（默认后缀.png，如需其他格式可扩展参数）
            string iconPath = Path.Combine(scriptFolderPath, $"{iconName}.png");
            // 统一路径分隔符（Unity工程内使用/，而非系统默认的\）
            iconPath = iconPath.Replace("\\", "/");

            // 3. 加载纹理资源
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);

            // 4. 返回GUIContent（图标不存在时返回默认警告图标）
            return new GUIContent(tex ?? EditorGUIUtility.IconContent("console.warnicon").image);
        }

        /// <summary>
        /// 准确获取当前脚本（CustomIconUtility.cs）所在的目录（Unity工程相对路径，如：Packages/com.neuro.tod/Editor/LightingEditor/）
        /// </summary>
        private static string GetScriptDirectory()
        {
            // 避免重复查找目录，提升性能
            if (!string.IsNullOrEmpty(_scriptDirectory))
            {
                return _scriptDirectory;
            }

            try
            {
                // 1. 获取当前脚本的类型信息
                Type scriptType = typeof(CustomIconUtility);
                // 2. 查找该脚本对应的AssetPath（Unity内的资源路径）
                string scriptAssetPath = AssetDatabase.FindAssets(scriptType.Name + " t:Script")[0];
                scriptAssetPath = AssetDatabase.GUIDToAssetPath(scriptAssetPath);

                // 3. 提取脚本所在的目录路径（去掉脚本文件名本身）
                _scriptDirectory = Path.GetDirectoryName(scriptAssetPath);

                return _scriptDirectory;
            }
            catch (Exception e)
            {
                Debug.LogError($"获取脚本目录失败：{e.Message}");
                return string.Empty;
            }
        }
    }
}