using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace NeuroTODEditor
{
    /// <summary>
    /// 轨道列表视图
    /// 支持层级显示、展开/折叠、选择和可见性控制
    /// </summary>
    public class TrackListView
    {
        // ========== 常量 ==========
        private const float TRACK_HEIGHT = 24f;
        private const float INDENT_WIDTH = 16f;
        private const float ICON_SIZE = 16f;
        private const float BUTTON_SIZE = 20f;

        // ========== 状态 ==========
        private List<TrackInfo> tracks;
        private HashSet<int> selectedIndices;
        private Vector2 scrollPosition;

        // ========== 事件 ==========
        public event Action<TrackInfo> OnTrackSelected;
        public event Action<TrackInfo> OnTrackDoubleClicked;
        public event Action<TrackInfo, bool> OnTrackVisibilityChanged;
        public event Action<TrackInfo> OnTrackDeleted;
        public event Action<BuiltinType> OnAddComponentRequested;

        // ========== 样式 ==========
        private GUIStyle trackLabelStyle;
        private GUIStyle groupLabelStyle;
        private bool stylesInitialized;

        public TrackListView()
        {
            tracks = new List<TrackInfo>();
            selectedIndices = new HashSet<int>();
        }

        /// <summary>
        /// 设置轨道列表
        /// </summary>
        public void SetTracks(List<TrackInfo> trackList)
        {
            tracks = trackList ?? new List<TrackInfo>();
        }

        /// <summary>
        /// 获取选中的轨道索引
        /// </summary>
        public HashSet<int> GetSelectedIndices() => selectedIndices;

        /// <summary>
        /// 设置选中的轨道索引
        /// </summary>
        public void SetSelectedIndices(HashSet<int> indices)
        {
            selectedIndices = indices ?? new HashSet<int>();
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;

            trackLabelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 0, 0)
            };

            groupLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 0, 0)
            };

            stylesInitialized = true;
        }

        /// <summary>
        /// 绘制轨道列表
        /// </summary>
        public void Draw(Rect rect)
        {
            InitStyles();

            // 背景
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            // 标题栏
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, TRACK_HEIGHT);
            DrawHeader(headerRect);

            // 轨道列表区域
            Rect listRect = new Rect(rect.x, rect.y + TRACK_HEIGHT, rect.width, rect.height - TRACK_HEIGHT);
            DrawTrackList(listRect);
        }

        private void DrawHeader(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));

            // 标题
            Rect labelRect = new Rect(rect.x + 4, rect.y, rect.width - 28, rect.height);
            GUI.Label(labelRect, "TOD Components", EditorStyles.boldLabel);

            // 添加按钮
            Rect addButtonRect = new Rect(rect.xMax - 24, rect.y + 2, 20, 20);
            if (GUI.Button(addButtonRect, EditorGUIUtility.IconContent("Toolbar Plus"), GUIStyle.none))
            {
                ShowAddComponentMenu();
            }
        }

        private void DrawTrackList(Rect rect)
        {
            // 计算可见轨道
            List<TrackInfo> visibleTracks = GetVisibleTracks();
            float contentHeight = visibleTracks.Count * TRACK_HEIGHT;

            // 滚动视图
            Rect viewRect = new Rect(0, 0, rect.width - 16, contentHeight);
            scrollPosition = GUI.BeginScrollView(rect, scrollPosition, viewRect);
            {
                for (int i = 0; i < visibleTracks.Count; i++)
                {
                    Rect trackRect = new Rect(0, i * TRACK_HEIGHT, rect.width - 16, TRACK_HEIGHT);
                    DrawTrackItem(trackRect, visibleTracks[i]);
                }
            }
            GUI.EndScrollView();
        }

        private List<TrackInfo> GetVisibleTracks()
        {
            List<TrackInfo> visible = new List<TrackInfo>();
            HashSet<int> collapsedParents = new HashSet<int>();

            foreach (var track in tracks)
            {
                // 检查父级是否折叠
                if (track.ParentIndex >= 0 && collapsedParents.Contains(track.ParentIndex))
                {
                    continue;
                }

                visible.Add(track);

                // 如果是折叠的分组，记录下来
                if (track.IsGroup && !track.IsExpanded)
                {
                    collapsedParents.Add(track.TrackIndex);
                }
            }

            return visible;
        }

        private void DrawTrackItem(Rect rect, TrackInfo track)
        {
            bool isSelected = selectedIndices.Contains(track.TrackIndex);

            // 背景
            if (isSelected)
            {
                EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.9f, 0.5f));
            }
            else if (track.IsGroup)
            {
                EditorGUI.DrawRect(rect, new Color(0.25f, 0.25f, 0.25f));
            }
            else if (rect.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f, 0.3f));
            }

            // 分隔线
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), new Color(0.15f, 0.15f, 0.15f));

            // 缩进
            float indent = track.Depth * INDENT_WIDTH;
            float currentX = rect.x + indent;

            // 展开/折叠按钮（仅分组）
            if (track.IsGroup)
            {
                Rect foldoutRect = new Rect(currentX, rect.y + 4, ICON_SIZE, ICON_SIZE);
                bool newExpanded = EditorGUI.Foldout(foldoutRect, track.IsExpanded, GUIContent.none);
                if (newExpanded != track.IsExpanded)
                {
                    track.IsExpanded = newExpanded;
                }
                currentX += ICON_SIZE + 2;
            }
            else
            {
                currentX += ICON_SIZE + 2;
            }

            // 图标
            Rect iconRect = new Rect(currentX, rect.y + 4, ICON_SIZE, ICON_SIZE);
            GUIContent icon = GetTrackIcon(track);
            if (icon != null)
            {
                GUI.Label(iconRect, icon);
            }
            currentX += ICON_SIZE + 4;

            // 轨道名称
            float labelWidth = rect.width - currentX - 50;
            Rect labelRect = new Rect(currentX, rect.y, labelWidth, rect.height);
            GUIStyle style = track.IsGroup ? groupLabelStyle : trackLabelStyle;
            GUI.Label(labelRect, track.DisplayName, style);

            // 可见性按钮
            Rect visRect = new Rect(rect.xMax - 44, rect.y + 2, BUTTON_SIZE, BUTTON_SIZE);
            GUIContent visIcon = track.IsVisible
                ? EditorGUIUtility.IconContent("animationvisibilitytoggleon")
                : EditorGUIUtility.IconContent("animationvisibilitytoggleoff");
            if (GUI.Button(visRect, visIcon, GUIStyle.none))
            {
                track.IsVisible = !track.IsVisible;
                OnTrackVisibilityChanged?.Invoke(track, track.IsVisible);
            }

            // 删除按钮（仅分组）
            if (track.IsGroup)
            {
                Rect deleteRect = new Rect(rect.xMax - 22, rect.y + 2, BUTTON_SIZE, BUTTON_SIZE);
                if (GUI.Button(deleteRect, EditorGUIUtility.IconContent("TreeEditor.Trash"), GUIStyle.none))
                {
                    if (EditorUtility.DisplayDialog("Delete Component",
                        $"Are you sure you want to delete '{track.DisplayName}'?", "Delete", "Cancel"))
                    {
                        OnTrackDeleted?.Invoke(track);
                    }
                }
            }

            // 处理点击事件
            HandleTrackClick(rect, track);
        }

        private GUIContent GetTrackIcon(TrackInfo track)
        {
            if (track.IsGroup)
            {
                switch (track.BuiltinType)
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
            else
            {
                if (track.Type == TrackType.ColorGradient)
                {
                    return EditorGUIUtility.IconContent("ColorPicker.ColorCycle");
                }
                return EditorGUIUtility.IconContent("AnimationClip Icon");
            }
        }

        private void HandleTrackClick(Rect rect, TrackInfo track)
        {
            Event e = Event.current;

            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                // 双击
                if (e.clickCount == 2)
                {
                    OnTrackDoubleClicked?.Invoke(track);
                    e.Use();
                    return;
                }

                // 单击选择
                if (e.control || e.command)
                {
                    // Ctrl/Cmd + 点击：切换选择
                    if (selectedIndices.Contains(track.TrackIndex))
                        selectedIndices.Remove(track.TrackIndex);
                    else
                        selectedIndices.Add(track.TrackIndex);
                }
                else if (e.shift && selectedIndices.Count > 0)
                {
                    // Shift + 点击：范围选择
                    int lastSelected = -1;
                    foreach (int idx in selectedIndices)
                    {
                        lastSelected = idx;
                    }

                    if (lastSelected >= 0)
                    {
                        int start = Mathf.Min(lastSelected, track.TrackIndex);
                        int end = Mathf.Max(lastSelected, track.TrackIndex);
                        for (int i = start; i <= end; i++)
                        {
                            selectedIndices.Add(i);
                        }
                    }
                }
                else
                {
                    // 普通点击：单选
                    selectedIndices.Clear();
                    selectedIndices.Add(track.TrackIndex);
                }

                OnTrackSelected?.Invoke(track);
                e.Use();
            }

            // 右键菜单
            if (e.type == EventType.ContextClick && rect.Contains(e.mousePosition))
            {
                ShowTrackContextMenu(track);
                e.Use();
            }
        }

        private void ShowAddComponentMenu()
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Sun"), false, () => OnAddComponentRequested?.Invoke(BuiltinType.Sun));
            menu.AddItem(new GUIContent("Moon"), false, () => OnAddComponentRequested?.Invoke(BuiltinType.Moon));
            menu.AddItem(new GUIContent("Sky Light"), false, () => OnAddComponentRequested?.Invoke(BuiltinType.SkyLight));
            menu.AddItem(new GUIContent("Fog"), false, () => OnAddComponentRequested?.Invoke(BuiltinType.Fog));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Scene Light..."), false, () => { /* TODO: 打开场景灯光选择器 */ });
            menu.ShowAsContext();
        }

        private void ShowTrackContextMenu(TrackInfo track)
        {
            GenericMenu menu = new GenericMenu();

            if (track.IsGroup)
            {
                menu.AddItem(new GUIContent("Expand All"), false, () => ExpandAll(track));
                menu.AddItem(new GUIContent("Collapse All"), false, () => CollapseAll(track));
                menu.AddSeparator("");
            }

            menu.AddItem(new GUIContent(track.IsVisible ? "Hide" : "Show"), false, () =>
            {
                track.IsVisible = !track.IsVisible;
                OnTrackVisibilityChanged?.Invoke(track, track.IsVisible);
            });

            if (track.IsGroup)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Delete"), false, () =>
                {
                    if (EditorUtility.DisplayDialog("Delete Component",
                        $"Are you sure you want to delete '{track.DisplayName}'?", "Delete", "Cancel"))
                    {
                        OnTrackDeleted?.Invoke(track);
                    }
                });
            }

            menu.ShowAsContext();
        }

        private void ExpandAll(TrackInfo parentTrack)
        {
            parentTrack.IsExpanded = true;
            foreach (int childIndex in parentTrack.ChildIndices)
            {
                if (childIndex >= 0 && childIndex < tracks.Count)
                {
                    var child = tracks[childIndex];
                    if (child.IsGroup)
                    {
                        ExpandAll(child);
                    }
                }
            }
        }

        private void CollapseAll(TrackInfo parentTrack)
        {
            parentTrack.IsExpanded = false;
            foreach (int childIndex in parentTrack.ChildIndices)
            {
                if (childIndex >= 0 && childIndex < tracks.Count)
                {
                    var child = tracks[childIndex];
                    if (child.IsGroup)
                    {
                        CollapseAll(child);
                    }
                }
            }
        }

        /// <summary>
        /// 选择所有轨道
        /// </summary>
        public void SelectAll()
        {
            selectedIndices.Clear();
            foreach (var track in tracks)
            {
                selectedIndices.Add(track.TrackIndex);
            }
        }

        /// <summary>
        /// 清除选择
        /// </summary>
        public void ClearSelection()
        {
            selectedIndices.Clear();
        }

        /// <summary>
        /// 滚动到指定轨道
        /// </summary>
        public void ScrollToTrack(int trackIndex)
        {
            if (trackIndex < 0 || trackIndex >= tracks.Count) return;

            float targetY = trackIndex * TRACK_HEIGHT;
            scrollPosition.y = targetY;
        }
    }
}
