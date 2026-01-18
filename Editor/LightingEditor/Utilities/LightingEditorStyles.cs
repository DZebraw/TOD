using UnityEngine;
using UnityEditor;

namespace NeuroTODEditor
{
    /// <summary>
    /// Lighting Editor 样式定义
    /// 统一管理编辑器的颜色、样式和图标
    /// </summary>
    public static class LightingEditorStyles
    {
        // ========== 颜色 ==========
        public static class Colors
        {
            // 背景色
            public static readonly Color WindowBackground = new Color(0.22f, 0.22f, 0.22f);
            public static readonly Color PanelBackground = new Color(0.25f, 0.25f, 0.25f);
            public static readonly Color HeaderBackground = new Color(0.2f, 0.2f, 0.2f);
            public static readonly Color TrackBackground = new Color(0.28f, 0.28f, 0.28f);
            public static readonly Color TrackBackgroundAlt = new Color(0.26f, 0.26f, 0.26f);

            // 选中色
            public static readonly Color Selection = new Color(0.24f, 0.48f, 0.9f, 0.5f);
            public static readonly Color SelectionBorder = new Color(0.3f, 0.6f, 1f);
            public static readonly Color Hover = new Color(0.3f, 0.3f, 0.3f, 0.3f);

            // 时间指示器
            public static readonly Color TimeIndicator = new Color(1f, 0.3f, 0.3f);
            public static readonly Color TimeIndicatorHead = new Color(1f, 0.4f, 0.4f);

            // 网格
            public static readonly Color GridMajor = new Color(0.3f, 0.3f, 0.3f);
            public static readonly Color GridMinor = new Color(0.2f, 0.2f, 0.2f);

            // 关键帧
            public static readonly Color KeyframeNormal = new Color(0.8f, 0.8f, 0.8f);
            public static readonly Color KeyframeSelected = new Color(1f, 0.8f, 0.2f);
            public static readonly Color KeyframeHover = new Color(0.9f, 0.9f, 0.9f);

            // 曲线颜色
            public static readonly Color[] CurveColors = {
                new Color(1f, 0.4f, 0.4f),      // 红
                new Color(0.4f, 1f, 0.4f),      // 绿
                new Color(0.4f, 0.4f, 1f),      // 蓝
                new Color(1f, 1f, 0.4f),        // 黄
                new Color(1f, 0.4f, 1f),        // 紫
                new Color(0.4f, 1f, 1f),        // 青
                new Color(1f, 0.7f, 0.4f),      // 橙
                new Color(0.7f, 0.4f, 1f)       // 紫罗兰
            };

            // 组件类型颜色
            public static readonly Color SunColor = new Color(1f, 0.9f, 0.4f);
            public static readonly Color MoonColor = new Color(0.7f, 0.8f, 1f);
            public static readonly Color SkyColor = new Color(0.5f, 0.7f, 1f);
            public static readonly Color FogColor = new Color(0.7f, 0.7f, 0.8f);

            // 按钮状态
            public static readonly Color ButtonActive = new Color(0.3f, 0.6f, 1f);
            public static readonly Color ButtonNormal = Color.white;
        }

        // ========== 尺寸 ==========
        public static class Sizes
        {
            public const float ToolbarHeight = 28f;
            public const float PlaybackBarHeight = 32f;
            public const float TimelineRulerHeight = 24f;
            public const float TrackHeight = 24f;
            public const float IndentWidth = 16f;
            public const float IconSize = 16f;
            public const float ButtonSize = 20f;
            public const float SplitterWidth = 4f;
            public const float MinOutlinerWidth = 150f;
            public const float DefaultOutlinerWidth = 200f;
            public const float KeyframeSize = 8f;
            public const float KeyframeHitSize = 12f;
        }

        // ========== 样式 ==========
        private static GUIStyle _toolbarStyle;
        private static GUIStyle _headerLabelStyle;
        private static GUIStyle _trackLabelStyle;
        private static GUIStyle _groupLabelStyle;
        private static GUIStyle _timeDisplayStyle;
        private static GUIStyle _miniLabelStyle;
        private static bool _initialized;

        public static GUIStyle ToolbarStyle
        {
            get
            {
                EnsureInitialized();
                return _toolbarStyle;
            }
        }

        public static GUIStyle HeaderLabelStyle
        {
            get
            {
                EnsureInitialized();
                return _headerLabelStyle;
            }
        }

        public static GUIStyle TrackLabelStyle
        {
            get
            {
                EnsureInitialized();
                return _trackLabelStyle;
            }
        }

        public static GUIStyle GroupLabelStyle
        {
            get
            {
                EnsureInitialized();
                return _groupLabelStyle;
            }
        }

        public static GUIStyle TimeDisplayStyle
        {
            get
            {
                EnsureInitialized();
                return _timeDisplayStyle;
            }
        }

        public static GUIStyle MiniLabelStyle
        {
            get
            {
                EnsureInitialized();
                return _miniLabelStyle;
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;

            _toolbarStyle = new GUIStyle(EditorStyles.toolbar)
            {
                fixedHeight = Sizes.ToolbarHeight
            };

            _headerLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 0, 0)
            };

            _trackLabelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 0, 0)
            };

            _groupLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 0, 0)
            };

            _timeDisplayStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14
            };

            _miniLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };

            _initialized = true;
        }

        /// <summary>
        /// 获取组件类型对应的颜色
        /// </summary>
        public static Color GetBuiltinTypeColor(BuiltinType type)
        {
            switch (type)
            {
                case BuiltinType.Sun: return Colors.SunColor;
                case BuiltinType.Moon: return Colors.MoonColor;
                case BuiltinType.SkyLight: return Colors.SkyColor;
                case BuiltinType.Fog: return Colors.FogColor;
                default: return Color.white;
            }
        }

        /// <summary>
        /// 获取曲线颜色
        /// </summary>
        public static Color GetCurveColor(int index)
        {
            return Colors.CurveColors[index % Colors.CurveColors.Length];
        }

        /// <summary>
        /// 获取组件类型对应的图标
        /// </summary>
        public static GUIContent GetBuiltinTypeIcon(BuiltinType type)
        {
            switch (type)
            {
                case BuiltinType.Sun:
                    return EditorGUIUtility.IconContent("DirectionalLight Icon");
                case BuiltinType.Moon:
                    return EditorGUIUtility.IconContent("DirectionalLight Icon");
                case BuiltinType.SkyLight:
                    return EditorGUIUtility.IconContent("ReflectionProbe Icon");
                case BuiltinType.Fog:
                    return EditorGUIUtility.IconContent("ParticleSystem Icon");
                default:
                    return EditorGUIUtility.IconContent("Folder Icon");
            }
        }
    }
}
