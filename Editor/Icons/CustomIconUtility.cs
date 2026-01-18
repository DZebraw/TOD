using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NeuroTODEditor
{
    public static class CustomIconUtility
    {
        public static GUIContent Icon(string iconName)
        {
            var path = $"Assets/Unity-TOD/Editor/Icons/{iconName}.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            return new GUIContent(tex ?? EditorGUIUtility.IconContent("console.warnicon").image);
        }
    }
}
