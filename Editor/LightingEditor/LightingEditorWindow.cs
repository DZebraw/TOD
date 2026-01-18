using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using NeuroTOD;
using UnityEngine.Rendering.VirtualTexturing;

namespace NeuroTODEditor
{
    /// <summary>
    /// NeuroTOD Lighting Editor 主窗口
    /// 参考 Unreal Sequencer 风格设计
    /// </summary>
    public class LightingEditorWindow : EditorWindow
    {
        // ========== 常量 ==========
        private const float TOOLBAR_HEIGHT = 28f;//顶部视口栏高度
        private const float PLAYBACK_BAR_HEIGHT = 32f;
        private const float TIMELINE_RULER_HEIGHT = 24f;
        private const float TRACK_OUTLINER_WIDTH_RATIO = 0.25f;
        private const float MIN_OUTLINER_WIDTH = 150f;
        private const float SPLITTER_WIDTH = 6f;
        private const float SCROLLBAR_WIDTH = 16f;

        // ========== 状态数据 ==========
        private LightingEditorState state;
        private List<TrackInfo> tracks; // 原始的所有轨道信息

        // ========== 目标引用 ==========
        private NeuroTODController selectedController;
        private TODPreset activePreset;

        // ========== 布局数据 ==========
        private float outlinerWidth;
        private bool isDraggingSplitter;
        private Vector2 trackScrollPosition;
        private Vector2 curveScrollPosition;

        // ========== 样式 ==========
        private GUIStyle toolbarStyle;
        private GUIStyle trackLabelStyle;
        private GUIStyle timeDisplayStyle;
        private bool stylesInitialized;

        // ========== 菜单入口 ==========
        [MenuItem("Window/NeuroTOD/Lighting Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<LightingEditorWindow>();
            window.titleContent = new GUIContent("Lighting Editor", EditorGUIUtility.IconContent("DirectionalLight Icon").image);
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnEnable()
        {
            state = new LightingEditorState();
            tracks = new List<TrackInfo>();
            outlinerWidth = 200f;//左侧视口栏

            LoadViewLevelFromPrefs();
            RefreshControllerList();
            EditorApplication.update += OnEditorUpdate;
            Selection.selectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            Selection.selectionChanged -= OnSelectionChanged;
            SaveViewLevelToPrefs();
        }

        private void OnSelectionChanged()
        {
            // 检查选中的对象是否包含 NeuroTODController
            var go = Selection.activeGameObject;
            if (go != null)
            {
                var controller = go.GetComponent<NeuroTODController>();
                if (controller != null && controller != selectedController)
                {
                    SetSelectedController(controller);
                }
            }
        }

        private void OnEditorUpdate()//每帧更新编辑器画面
        {
            if (state.Playback.IsPlaying)
            {
                float deltaTime = Time.realtimeSinceStartup - lastUpdateTime;
                lastUpdateTime = Time.realtimeSinceStartup;

                state.CurrentTime += state.Playback.PlaybackSpeed * deltaTime * state.Playback.PlayDirection;

                // 循环播放
                if (state.CurrentTime >= 1f)
                {
                    state.CurrentTime = state.Playback.IsLooping ? state.CurrentTime - 1f : 1f;
                    if (!state.Playback.IsLooping) state.Playback.State = PlaybackState.Stopped;
                }
                else if (state.CurrentTime < 0f)
                {
                    state.CurrentTime = state.Playback.IsLooping ? state.CurrentTime + 1f : 0f;
                    if (!state.Playback.IsLooping) state.Playback.State = PlaybackState.Stopped;
                }

                // 更新控制器时间
                if (selectedController != null)
                {
                    selectedController.TimeOfDay = state.CurrentTime * 24f;
                }

                Repaint();
            }
        }
        private float lastUpdateTime;

        private void InitStyles()
        {
            if (stylesInitialized) return;

            toolbarStyle = new GUIStyle(EditorStyles.toolbar)
            {
                fixedHeight = TOOLBAR_HEIGHT
            };

            trackLabelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(1, 1, 0, 0)
            };

            timeDisplayStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14
            };

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            // 主布局
            EditorGUILayout.BeginVertical();
            {
                // 顶部工具栏
                DrawToolbar();

                // 分隔线
                EditorGUILayout.Space(1);
                DrawHorizontalLine();

                // Level 3: 曲线编辑器区域
                if (state.CurrentViewLevel == ViewLevel.Level3_CurveEditor)
                {
                    DrawMainEditorArea();
                }

                // 底部播放控制条
                DrawPlaybackBar();
            }
            EditorGUILayout.EndVertical();

            // 处理事件
            HandleEvents();
        }

        // ========== 工具栏 ==========
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(toolbarStyle, GUILayout.Height(TOOLBAR_HEIGHT));
            {
                // Controller 选择（仅 Level 3 显示）
                if (state.CurrentViewLevel == ViewLevel.Level3_CurveEditor)
                {
                    GUILayout.Label("Controller:", GUILayout.Width(70));
                    DrawControllerSelector();
                    GUILayout.Space(8);
                }

                // 刷新按钮
                if (GUILayout.Button(EditorGUIUtility.IconContent("Refresh"), GUILayout.Width(28), GUILayout.Height(20)))
                {
                    RefreshControllerList();
                    RefreshTracks();
                }

                GUILayout.FlexibleSpace();

                // 时间显示
                GUILayout.Label($"Time: {state.GetFormattedTime()}", timeDisplayStyle, GUILayout.Width(120));

                // 时间格式切换
                if (GUILayout.Button(state.TimeDisplayMode == TimeDisplayMode.Format24H ? "24H" : "12H", GUILayout.Width(40)))
                {
                    state.TimeDisplayMode = state.TimeDisplayMode == TimeDisplayMode.Format24H
                        ? TimeDisplayMode.Format12H
                        : TimeDisplayMode.Format24H;
                }

                GUILayout.FlexibleSpace();

                // 视图级别按钮
                DrawViewLevelButtons();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawControllerSelector()
        {
            string currentName = selectedController != null ? selectedController.name : "None";
            if (GUILayout.Button(currentName, EditorStyles.popup, GUILayout.Width(150)))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("None"), selectedController == null, () => SetSelectedController(null));
                menu.AddSeparator("");

                var controllers = FindObjectsOfType<NeuroTODController>();
                foreach (var controller in controllers)
                {
                    var c = controller;
                    menu.AddItem(new GUIContent(c.name), c == selectedController, () => SetSelectedController(c));
                }
                menu.ShowAsContext();
            }
        }

        private void DrawViewLevelButtons()
        {
            GUILayout.Label("View:", GUILayout.Width(35));

            Color defaultColor = GUI.backgroundColor;
            Color selectedColor = new Color(0.3f, 0.6f, 1f);

            GUI.backgroundColor = state.CurrentViewLevel == ViewLevel.Level1_Playback ? selectedColor : defaultColor;
            if (GUILayout.Button("1", GUILayout.Width(24)))
            {
                SetViewLevel(ViewLevel.Level1_Playback);
            }

            GUI.backgroundColor = state.CurrentViewLevel == ViewLevel.Level2_LightControl ? selectedColor : defaultColor;
            if (GUILayout.Button("2", GUILayout.Width(24)))
            {
                SetViewLevel(ViewLevel.Level2_LightControl);
            }

            GUI.backgroundColor = state.CurrentViewLevel == ViewLevel.Level3_CurveEditor ? selectedColor : defaultColor;
            if (GUILayout.Button("3", GUILayout.Width(24)))
            {
                SetViewLevel(ViewLevel.Level3_CurveEditor);
            }

            GUI.backgroundColor = defaultColor;
        }

        // ========== 主编辑区域 ==========
        private void DrawMainEditorArea()
        {
            Rect mainRect = EditorGUILayout.GetControlRect(false, position.height - TOOLBAR_HEIGHT - PLAYBACK_BAR_HEIGHT - 10);

            // 左侧：轨道 Outliner
            Rect outlinerRect = new Rect(mainRect.x, mainRect.y, outlinerWidth, mainRect.height);

            // 侧边栏分割条
            Rect splitterRect = new Rect(outlinerRect.xMax, mainRect.y, SPLITTER_WIDTH, mainRect.height);

            // 右侧：时间轴 + 曲线区域
            Rect trackAreaRect = new Rect(splitterRect.xMax, mainRect.y, mainRect.width - outlinerWidth - SPLITTER_WIDTH, mainRect.height);

            // 绘制各区域
            DrawTrackOutliner(outlinerRect);
            DrawSplitter(splitterRect);
            DrawTrackArea(trackAreaRect);
        }

        // 递归函数：计算展开后的可见轨道数量
        private int CountVisibleTracks(List<TrackInfo> allTracks, int parentIndex = -1, bool parentIsExpanded = true)
        {
            int count = 0;
            foreach (var track in allTracks)
            {
                // 如果是根节点 (parentIndex == -1) 或者是当前节点的子节点
                if (track.ParentIndex == parentIndex)
                {
                    // 如果父节点是展开的，则该节点可见
                    if (parentIsExpanded)
                    {
                        count++; // 计数自身
                        // 如果是组且已展开，递归计算其子节点
                        if (track.IsGroup && track.IsExpanded)
                        {
                            count += CountVisibleTracks(allTracks, track.TrackIndex, true);
                        }
                        // 如果是组但未展开，也递归计算其子节点，但传入 parentIsExpanded = false
                        else if (track.IsGroup)
                        {
                            count += CountVisibleTracks(allTracks, track.TrackIndex, false);
                        }
                        // 非组项目没有子项，无需递归
                    }
                    // 如果父节点未展开，则该节点及其所有子节点都不可见
                    else
                    {
                        // 即使父节点未展开，我们也需要检查这个节点是否有自己的子节点，
                        // 因为它可能是一个独立的组，其子节点依赖于它自身的 IsExpanded 状态。
                        // 但是，由于其父节点未展开，它本身不会被绘制，所以它的子节点也不会被绘制。
                        // 所以这里我们只需要继续遍历，但不计数。
                        if (track.IsGroup)
                        {
                            count += CountVisibleTracks(allTracks, track.TrackIndex, false);
                        }
                    }
                }
            }
            return count;
        }

        // 获取展开后的可见轨道列表
        private void GetVisibleTracks(List<TrackInfo> allTracks, List<TrackInfo> visibleTracks, int parentIndex = -1, bool parentIsExpanded = true)
        {
            foreach (var track in allTracks)
            {
                if (track.ParentIndex == parentIndex)
                {
                    if (parentIsExpanded)
                    {
                        visibleTracks.Add(track); // 添加自身
                        // 如果是组且已展开，递归添加其子节点
                        if (track.IsGroup && track.IsExpanded)
                        {
                            GetVisibleTracks(allTracks, visibleTracks, track.TrackIndex, true);
                        }
                        // 如果是组但未展开，递归调用但 parentIsExpanded = false
                        else if (track.IsGroup)
                        {
                            GetVisibleTracks(allTracks, visibleTracks, track.TrackIndex, false);
                        }
                    }
                    // 如果父节点未展开，则该节点及其所有子节点都不应被添加到可见列表
                    else
                    {
                        if (track.IsGroup)
                        {
                            GetVisibleTracks(allTracks, visibleTracks, track.TrackIndex, false);
                        }
                    }
                }
            }
        }

        private void DrawTrackOutliner(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            // 标题栏
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, TIMELINE_RULER_HEIGHT + 24);
            EditorGUI.DrawRect(headerRect, new Color(0.2f, 0.2f, 0.2f));
            GUI.Label(headerRect, "  TOD Components", EditorStyles.boldLabel);

            // 获取当前可见的轨道列表
            List<TrackInfo> visibleTracks = new List<TrackInfo>();
            GetVisibleTracks(tracks, visibleTracks); // 从根节点开始 (parentIndex = -1)

            // 轨道列表区域
            Rect listRect = new Rect(rect.x, rect.y + (TIMELINE_RULER_HEIGHT+24), rect.width,rect.height - (TIMELINE_RULER_HEIGHT+24)); // 使用常量
            // 计算滚动视图的内容高度
            int visibleCount = visibleTracks.Count;
            float contentHeight = visibleCount * 24f; // 假设每个项目高 24px

            trackScrollPosition = GUI.BeginScrollView(listRect, trackScrollPosition, new Rect(0, 0, rect.width - 16, contentHeight)); // 内容宽度减去滚动条
            {
                for (int i = 0; i < visibleTracks.Count; i++)
                {
                    // 使用可见列表中的索引和轨道信息
                    TrackInfo currentTrack = visibleTracks[i];
                    // 传递当前在滚动视图中绘制的位置
                    DrawTrackItem(new Rect(0, i * 24, rect.width - 16, 24), currentTrack);
                }
            }
            GUI.EndScrollView();
        }


        private void DrawTrackItem(Rect rect, TrackInfo track)
        {
            bool isSelected = state.SelectedTrackIndices.Contains(track.TrackIndex);

            // 选中时背景颜色
            if (isSelected)
            {
                EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.9f, 0.5f));
            }
            else if (track.IsGroup)
            {
                EditorGUI.DrawRect(rect, new Color(0.25f, 0.25f, 0.25f));
            }

            // 缩进
            float indent = track.Depth * 16f;
            Rect labelRect = new Rect(rect.x + indent + 20, rect.y, rect.width - indent - 60, rect.height);

            // 展开/折叠按钮（仅分组）
            if (track.IsGroup && track.ChildIndices.Count > 0)
            {
                Rect foldoutRect = new Rect(rect.x + indent, rect.y, 20, rect.height);
                // 关键修改：更新 track.IsExpanded 状态
                track.IsExpanded = EditorGUI.Foldout(foldoutRect, track.IsExpanded, GUIContent.none);
            }

            // 轨道名称
            GUI.Label(labelRect, track.DisplayName, trackLabelStyle);

            // 可见性按钮
            Rect visRect = new Rect(rect.xMax - 40, rect.y + 2, 20, 20);
            var visIcon = track.IsVisible
                ? EditorGUIUtility.IconContent("animationvisibilitytoggleon")
                : EditorGUIUtility.IconContent("animationvisibilitytoggleoff");
            if (GUI.Button(visRect, visIcon, GUIStyle.none))
            {
                track.IsVisible = !track.IsVisible;
                OnTrackVisibilityChanged(track);
            }

            // 点击选择
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (Event.current.control || Event.current.command)
                {
                    if (isSelected)
                        state.SelectedTrackIndices.Remove(track.TrackIndex);
                    else
                        state.SelectedTrackIndices.Add(track.TrackIndex);
                }
                else
                {
                    state.SelectedTrackIndices.Clear();
                    state.SelectedTrackIndices.Add(track.TrackIndex);
                }
                Event.current.Use();
                Repaint(); // 确保选择状态更新
            }
        }

        private void DrawSplitter(Rect rect)
        {
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                isDraggingSplitter = true;
                Event.current.Use();
            }

            if (isDraggingSplitter)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    outlinerWidth = Mathf.Clamp(Event.current.mousePosition.x, MIN_OUTLINER_WIDTH, position.width * 0.5f);
                    Event.current.Use();
                    Repaint();
                }
                else if (Event.current.type == EventType.MouseUp)
                {
                    isDraggingSplitter = false;
                    Event.current.Use();
                }
            }
        }

        private void DrawTrackArea(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            // 模式切换标签页
            Rect tabRect = new Rect(rect.x, rect.y, rect.width, 24);
            DrawModeTab(tabRect);

            // 时间轴刻度尺
            float effectiveWidth = state.CurrentEditorMode == EditorMode.Keyframes 
                ? rect.width - SCROLLBAR_WIDTH 
                : rect.width;

            Rect rulerRect = new Rect(rect.x, rect.y + 24, effectiveWidth, TIMELINE_RULER_HEIGHT);
            DrawTimelineRuler(rulerRect);

            // 曲线/关键帧区域
            Rect contentRect = new Rect(rect.x, rulerRect.yMax, rect.width, rect.height - 24 - TIMELINE_RULER_HEIGHT);

            if (state.CurrentEditorMode == EditorMode.Keyframes)
            {
                List<TrackInfo> visibleTracks = new List<TrackInfo>();
                GetVisibleTracks(tracks, visibleTracks);
                float contentHeight = visibleTracks.Count * 24f;

                // ScrollView 占据整个 contentRect，但内容宽度要减去滚动条
                var scrollViewRect = new Rect(contentRect.x, contentRect.y, contentRect.width, contentRect.height);
    
                // 关键：viewRect 的宽度 = 可用宽度（不含滚动条）
                var viewRect = new Rect(0, 0, contentRect.width - SCROLLBAR_WIDTH, contentHeight);

                Vector2 oldScroll = trackScrollPosition;
                trackScrollPosition = GUI.BeginScrollView(
                    scrollViewRect,
                    trackScrollPosition,
                    viewRect,
                    alwaysShowHorizontal: false,
                    alwaysShowVertical: true // 或 false，如果你之后想隐藏
                );

                // 注意：传给 DrawKeyframeArea 的 rect 宽度应为 viewRect.width
                DrawKeyframeArea(new Rect(0, 0, viewRect.width, viewRect.height), visibleTracks);

                GUI.EndScrollView();

                if (oldScroll != trackScrollPosition)
                    Repaint();
            }
            else
            {
                // === Curves 模式：保持原样，无滚动 ===
                DrawCurveArea(contentRect, null); // 不传递 visibleTracks，使用原始逻辑
            }
        }

        //Keyframes和Curves视图切换
        private void DrawModeTab(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));

            float tabWidth = 80;
            Rect keyframesTab = new Rect(rect.x + 4, rect.y + 2, tabWidth, rect.height - 4);
            Rect curvesTab = new Rect(keyframesTab.xMax + 4, rect.y + 2, tabWidth, rect.height - 4);

            Color defaultColor = GUI.backgroundColor;
            Color selectedColor = new Color(0.3f, 0.5f, 0.8f);

            GUI.backgroundColor = state.CurrentEditorMode == EditorMode.Keyframes ? selectedColor : defaultColor;
            if (GUI.Button(keyframesTab, "Keyframes"))
            {
                state.CurrentEditorMode = EditorMode.Keyframes;
            }

            GUI.backgroundColor = state.CurrentEditorMode == EditorMode.Curves ? selectedColor : defaultColor;
            if (GUI.Button(curvesTab, "Curves"))
            {
                state.CurrentEditorMode = EditorMode.Curves;
            }

            GUI.backgroundColor = defaultColor;
        }

        private void DrawTimelineRuler(Rect rect)
        {
            //时间刻度背景
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));

            // 绘制刻度
            int numTicks = 24;
            float tickSpacing = rect.width / numTicks;

            for (int i = 0; i <= numTicks; i++)
            {
                float x = rect.x + i * tickSpacing;
                float tickHeight = (i % 6 == 0) ? 12 : 6;

                // 刻度线
                EditorGUI.DrawRect(new Rect(x, rect.yMax - tickHeight, 1, tickHeight), new Color(0.5f, 0.5f, 0.5f));

                // 时间标签（每6小时）
                if (i % 6 == 0)
                {
                    GUI.Label(new Rect(x - 10, rect.y, 30, 12), $"{i}:00", EditorStyles.miniLabel);
                }
            }

            // 当前时间指示线
            float timeX = rect.x + state.CurrentTime * rect.width;
            EditorGUI.DrawRect(new Rect(timeX - 1, rect.y, 2, rect.height), new Color(1f, 0.3f, 0.3f));

            // 点击设置时间
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                float newTime = (Event.current.mousePosition.x - rect.x) / rect.width;
                SetCurrentTime(Mathf.Clamp01(newTime));
                Event.current.Use();
            }
        }

        private void DrawKeyframeArea(Rect rect, List<TrackInfo> visibleTracks)
        {
            GUI.Box(rect, GUIContent.none);

            // 绘制网格背景（覆盖整个可视区域）
            DrawGrid(rect);

            float trackHeight = 24f;
            for (int i = 0; i < visibleTracks.Count; i++)
            {
                var track = visibleTracks[i];
                Rect trackRect = new Rect(rect.x, rect.y + i * trackHeight, rect.width, trackHeight);
                DrawTrackKeyframes(trackRect, track);
            }

            // 当前时间指示线（贯穿整个高度）
            float timeX = rect.x + state.CurrentTime * rect.width;
            EditorGUI.DrawRect(new Rect(timeX - 1, rect.y, 2, rect.height), new Color(1f, 0.3f, 0.3f));

            // 在 Keyframes 窗口下也绘制曲线
            DrawCurveArea(rect, visibleTracks);
        }

        // 辅助函数：判断轨道是否应该被绘制
        private bool ShouldDrawTrack(TrackInfo track)
        {
            // 如果是根节点
            if (track.ParentIndex == -1)
            {
                return true; // 根节点总是可见的，但其子节点受其 IsExpanded 影响
            }
            // 如果不是根节点，需要检查其父节点链
            int parentIdx = track.ParentIndex;
            while (parentIdx != -1)
            {
                TrackInfo parentTrack = tracks.Find(t => t.TrackIndex == parentIdx);
                if (parentTrack == null || !parentTrack.IsExpanded)
                {
                    return false; // 如果任何一个父节点不存在或未展开，则该节点不应绘制
                }
                parentIdx = parentTrack.ParentIndex;
            }
            return true; // 所有父节点都存在且已展开
        }


        private void DrawTrackKeyframes(Rect rect, TrackInfo track)
        {
            if (track.FloatCurve == null) return;

            var keys = track.FloatCurve.keys;
            foreach (var key in keys)
            {
                float x = rect.x + key.time * rect.width;
                Rect keyRect = new Rect(x - 4, rect.y + 4, 8, 16);

                // 菱形关键帧
                Color keyColor = state.SelectedKeyframes.Contains(new KeyframeHandle(track.TrackIndex, 0))
                    ? new Color(1f, 0.8f, 0.2f)
                    : new Color(0.8f, 0.8f, 0.8f);
                EditorGUI.DrawRect(keyRect, keyColor);
            }
        }

        private void DrawCurveArea(Rect rect, List<TrackInfo> visibleTracks)
        {
            GUI.Box(rect, GUIContent.none);

            // 绘制网格背景
            DrawGrid(rect);

            // 判断当前是否为 Keyframes 模式（通过 visibleTracks 是否为 null）
            bool isKeyframesMode = visibleTracks != null;

            if (isKeyframesMode)
            {
                // Keyframes 模式：绘制所有可见的 FloatCurve 和 ColorGradient 轨道
                foreach (var track in visibleTracks)
                {
                    if (!track.IsVisible) continue;

                    int indexInVisible = visibleTracks.IndexOf(track);
                    Rect trackRect = new Rect(rect.x, rect.y + indexInVisible * 24f, rect.width, 23f);

                    if (track.FloatCurve != null && ShouldDrawTrack(track))
                    {
                        DrawCurve(trackRect, track.FloatCurve, GetTrackColor(track.TrackIndex));
                    }
                    else if (track.ColorGradient != null && ShouldDrawTrack(track))
                    {
                        DrawGradient(trackRect, track.ColorGradient, trackRect.height); 
                    }
                }
            }
            else
            {
                // Curves 模式：仅绘制选中的轨道
                foreach (int trackIndex in state.SelectedTrackIndices)
                {
                    var track = tracks.Find(t => t.TrackIndex == trackIndex);
                    if (track == null || !track.IsVisible) continue;

                    if (track.FloatCurve != null && ShouldDrawTrack(track))
                    {
                        DrawCurve(rect, track.FloatCurve, GetTrackColor(trackIndex));
                    }
                    else if (track.ColorGradient != null && ShouldDrawTrack(track))
                    {
                        DrawGradient(rect, track.ColorGradient, rect.height); // 使用整个区域的高度
                    }
                }
            }

            // 当前时间指示线（贯穿整个高度）
            float timeX = rect.x + state.CurrentTime * rect.width;
            EditorGUI.DrawRect(new Rect(timeX - 1, rect.y, 2, rect.height), new Color(1f, 0.3f, 0.3f));
        }
        
        //可编辑的渐变
        // private void DrawGradient(Rect rect, Gradient gradient, float height)
        // {
        //     // 调整绘制区域：居中显示在轨道内（通常 height == 23～24）
        //     Rect drawRect = new Rect(rect.x, rect.y + (rect.height - height) * 0.5f, rect.width, height);
        //
        //     // 使用 Unity 内置的 GradientField 绘制渐变（只绘制背景，不响应编辑）
        //     EditorGUI.GradientField(drawRect,gradient);
        // }
        
        //不可编辑的渐变
        private void DrawGradient(Rect rect, Gradient gradient, float height)
        {
            // 创建渐变纹理（只创建一次或缓存更好，但这里简单处理）
            Texture2D gradientTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            Color[] colors = new Color[256];
            for (int i = 0; i < 256; i++)
            {
                colors[i] = gradient.Evaluate(i / 255f);
            }
            gradientTexture.SetPixels(colors);
            gradientTexture.Apply();

            // 调整绘制区域
            Rect drawRect = new Rect(
                rect.x,
                rect.y + (rect.height - height) * 0.5f,
                rect.width,
                height
            );

            EditorGUI.DrawPreviewTexture(drawRect, gradientTexture, null, ScaleMode.StretchToFill, 0f);

            // 注意：在 Editor 中频繁创建/销毁纹理可能影响性能，
            // 但在 OnGUI 中偶尔调用可接受。若需优化，可考虑缓存。
            DestroyImmediate(gradientTexture);
        }

        private void DrawCurve(Rect rect, AnimationCurve curve, Color color)
        {
            if (curve.length < 2) return;

            Handles.BeginGUI();
            Handles.color = color;

            // 计算曲线的值范围
            float minVal = float.MaxValue, maxVal = float.MinValue;
            foreach (var key in curve.keys)
            {
                minVal = Mathf.Min(minVal, key.value);
                maxVal = Mathf.Max(maxVal, key.value);
            }
            float range = (maxVal - minVal)*1.1f;
            if (range < 0.001f) range = 1f;

            // 绘制曲线
            int segments = 100;
            Vector3 prevPoint = Vector3.zero;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float value = curve.Evaluate(t);
                float normalizedValue = (value - minVal) / range;

                float x = rect.x + t * rect.width;
                float y = rect.yMax - normalizedValue * rect.height;

                Vector3 point = new Vector3(x, y, 0);
                if (i > 0)
                {
                    Handles.DrawLine(prevPoint, point);
                }
                prevPoint = point;
            }

            Handles.EndGUI();
        }

        private void DrawGrid(Rect rect)
        {
            // 垂直线（时间刻度）
            int numVertical = 24;
            for (int i = 0; i <= numVertical; i++)
            {
                float x = rect.x + (i / (float)numVertical) * rect.width;
                Color lineColor = (i % 6 == 0) ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.2f, 0.2f, 0.2f);
                EditorGUI.DrawRect(new Rect(x, rect.y, 1, rect.height), lineColor);
            }

            // 水平线：每 24px 一条（匹配轨道高度）
            float trackHeight = 24f;
            int numHorizontal = Mathf.CeilToInt(rect.height / trackHeight);
            for (int i = 0; i <= numHorizontal; i++)
            {
                float y = rect.y + i * trackHeight;
                if (y <= rect.yMax)
                {
                    EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, 1), new Color(0.1f, 0.1f, 0.1f));
                }
            }
        }

        // ========== 播放控制条 ==========
        private void DrawPlaybackBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(PLAYBACK_BAR_HEIGHT));
            {
                // Level 3 显示完整控制
                if (state.CurrentViewLevel == ViewLevel.Level3_CurveEditor)
                {
                    // 跳转到开头
                    if (GUILayout.Button(EditorGUIUtility.IconContent("Animation.FirstKey"), GUILayout.Width(28)))
                    {
                        JumpToStart();
                    }

                    // 上一关键帧
                    //if (GUILayout.Button(EditorGUIUtility.IconContent("Animation.PrevKey"), GUILayout.Width(28)))
                    //{
                    //    JumpToPreviousKey();
                    //}

                    // 上一帧
                    if (GUILayout.Button(EditorGUIUtility.IconContent("Animation.PrevKey"), GUILayout.Width(28)))
                    {
                        StepBackward();
                    }
                }

                // 后退播放
                var backIcon = state.Playback.State == PlaybackState.PlayingBackward
                    ? EditorGUIUtility.IconContent("PauseButton")
                    : CustomIconUtility.Icon("LightingEditorPlayBack");
                if (GUILayout.Button(backIcon, GUILayout.Width(28)))
                {
                    TogglePlayBackward();
                }

                // 前进播放
                var playIcon = state.Playback.State == PlaybackState.PlayingForward
                    ? EditorGUIUtility.IconContent("PauseButton")
                    : CustomIconUtility.Icon("LightingEditorPlay");
                if (GUILayout.Button(playIcon, GUILayout.Width(28)))
                {
                    TogglePlayForward();
                }


                if (state.CurrentViewLevel == ViewLevel.Level3_CurveEditor)
                {
                    // 下一帧
                    if (GUILayout.Button(EditorGUIUtility.IconContent("Animation.NextKey"), GUILayout.Width(28)))
                    {
                        StepForward();
                    }

                    // 下一关键帧
                    //if (GUILayout.Button(EditorGUIUtility.IconContent("Animation.NextKey"), GUILayout.Width(28)))
                    //{
                    //    JumpToNextKey();
                    //}

                    // 跳转到结尾
                    if (GUILayout.Button(EditorGUIUtility.IconContent("Animation.LastKey"), GUILayout.Width(28)))
                    {
                        JumpToEnd();
                    }
                }

                GUILayout.Space(8);

                // 时间滑块
                EditorGUI.BeginChangeCheck();
                float newTime = GUILayout.HorizontalSlider(state.CurrentTime, 0f, 1f, GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck())
                {
                    SetCurrentTime(newTime);
                }

                GUILayout.Space(8);

                // 时间显示
                GUILayout.Label(state.GetFormattedTime(), GUILayout.Width(60));
            }
            EditorGUILayout.EndHorizontal();
        }

        // ========== 辅助方法 ==========
        private void DrawHorizontalLine()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
        }

        private Color GetTrackColor(int index)
        {
            Color[] colors = {
                new Color(1f, 0.4f, 0.4f),
                new Color(0.4f, 1f, 0.4f),
                new Color(0.4f, 0.4f, 1f),
                new Color(1f, 1f, 0.4f),
                new Color(1f, 0.4f, 1f),
                new Color(0.4f, 1f, 1f)
            };
            return colors[index % colors.Length];
        }

        // ========== 控制器和轨道管理 ==========
        private void RefreshControllerList()//查找组件
        {
            if (selectedController == null)
            {
                var controllers = FindObjectsOfType<NeuroTODController>();
                if (controllers.Length > 0)
                {
                    SetSelectedController(controllers[0]);
                }
            }
        }

        private void SetSelectedController(NeuroTODController controller)
        {
            selectedController = controller;
            activePreset = controller?.ActivePreset;

            if (controller != null)
            {
                state.CurrentTime = controller.NormalizedTime;
            }

            RefreshTracks();
            Repaint();
        }

    private void RefreshTracks()
    {
        tracks.Clear();
        if (activePreset == null) return;

        int trackIndex = 0;

        CreateTrackGroup("Sun", BuiltinType.Sun, ref trackIndex,
            floatChildren: new[] {
                ("Intensity", activePreset.sunIntensityCurve),
                ("Azimuth", activePreset.sunAzimuthCurve),
                ("Elevation", activePreset.sunElevationCurve)
            },
            gradientChildren: new[]
            {
                ("SunColor", activePreset.sunColorGradient)
            });

        CreateTrackGroup("Moon", BuiltinType.Moon, ref trackIndex,
            floatChildren: new[] {
                ("Intensity", activePreset.moonIntensityCurve),
                ("Azimuth", activePreset.moonAzimuthCurve),
                ("Elevation", activePreset.moonElevationCurve)
            },
            gradientChildren: new[]
            {
                ("MoonColor", activePreset.moonColorGradient)
            });

        CreateTrackGroup("Sky", BuiltinType.SkyLight, ref trackIndex,
            floatChildren: new[] {
                ("Intensity", activePreset.skyLightIntensityCurve),
                ("Star Emission", activePreset.starEmissionCurve)
            },
            gradientChildren: new[] {
                ("SkyLightColor", activePreset.skyLightColorGradient)
            });

        CreateTrackGroup("Fog", BuiltinType.Fog, ref trackIndex,
            floatChildren: new[] {
                ("Density", activePreset.fogDensityCurve),
                ("Distance", activePreset.fogDistanceCurve)
            },
            gradientChildren: new[] {
                ("FogColor", activePreset.fogColorGradient)
            });
    }

    private void CreateTrackGroup(
        string groupName,
        BuiltinType builtinType,
        ref int trackIndex,
        (string name, AnimationCurve curve)[] floatChildren,
        (string name, Gradient gradient)[] gradientChildren)
    {
        int groupIndex = trackIndex;
        var group = new TrackInfo
        {
            TrackIndex = trackIndex++,
            DisplayName = groupName,
            FullName = groupName,
            Type = TrackType.Group,
            BuiltinType = builtinType,
            Depth = 0,
            IsExpanded = true
        };
        tracks.Add(group);

        // Add float curve children
        foreach (var (name, curve) in floatChildren)
        {
            if (curve == null) continue;
            var child = new TrackInfo
            {
                TrackIndex = trackIndex++,
                DisplayName = name,
                FullName = $"{groupName}.{name}",
                Type = TrackType.FloatCurve,
                BuiltinType = builtinType,
                Depth = 1,
                ParentIndex = groupIndex,
                FloatCurve = curve
            };
            group.ChildIndices.Add(child.TrackIndex);
            tracks.Add(child);
        }

        // Add gradient children
        foreach (var (name, gradient) in gradientChildren)
        {
            if (gradient == null) continue;
            var child = new TrackInfo
            {
                TrackIndex = trackIndex++,
                DisplayName = name,
                FullName = $"{groupName}.{name}",
                Type = TrackType.ColorGradient, // 设置正确的类型
                BuiltinType = builtinType,
                Depth = 1,
                ParentIndex = groupIndex,
                ColorGradient = gradient // 初始化 ColorGradient 字段
            };
            group.ChildIndices.Add(child.TrackIndex);
            tracks.Add(child);
        }
    }

        private void OnTrackVisibilityChanged(TrackInfo track)
        {
            // TODO: 实现轨道可见性变更逻辑
            Debug.Log($"[NeuroTOD] Track visibility changed: {track.DisplayName} = {track.IsVisible}");
        }

        // ========== 时间控制 ==========
        private void SetCurrentTime(float time)
        {
            state.CurrentTime = Mathf.Clamp01(time);
            if (selectedController != null)
            {
                selectedController.TimeOfDay = state.CurrentTime * 24f;
            }
            Repaint();
        }

        private void TogglePlayForward()
        {
            if (state.Playback.State == PlaybackState.PlayingForward)
            {
                state.Playback.State = PlaybackState.Stopped;
            }
            else
            {
                state.Playback.State = PlaybackState.PlayingForward;
                lastUpdateTime = Time.realtimeSinceStartup;
            }
        }

        private void TogglePlayBackward()
        {
            if (state.Playback.State == PlaybackState.PlayingBackward)
            {
                state.Playback.State = PlaybackState.Stopped;
            }
            else
            {
                state.Playback.State = PlaybackState.PlayingBackward;
                lastUpdateTime = Time.realtimeSinceStartup;
            }
        }

        private void JumpToStart()
        {
            state.Playback.State = PlaybackState.Stopped;
            SetCurrentTime(0f);
        }

        private void JumpToEnd()
        {
            state.Playback.State = PlaybackState.Stopped;
            SetCurrentTime(1f);
        }

        private void StepForward()
        {
            state.Playback.State = PlaybackState.Stopped;
            SetCurrentTime(state.CurrentTime + 1f / 1440f); // 1分钟
        }

        private void StepBackward()
        {
            state.Playback.State = PlaybackState.Stopped;
            SetCurrentTime(state.CurrentTime - 1f / 1440f);
        }

        private void JumpToPreviousKey()
        {
            // TODO: 实现跳转到上一关键帧
        }

        private void JumpToNextKey()
        {
            // TODO: 实现跳转到下一关键帧
        }

        // ========== 视图级别 ==========
        private void SetViewLevel(ViewLevel level)
        {
            if (state.CurrentViewLevel != level)
            {
                state.CurrentViewLevel = level;
                SaveViewLevelToPrefs();
                Repaint();
            }
        }

        //========== 数据持久化 ==========
        private void SaveViewLevelToPrefs()
        {
            EditorPrefs.SetInt("NeuroTOD_ViewLevel", (int)state.CurrentViewLevel);
        }
        private void LoadViewLevelFromPrefs()
        {
            int level = EditorPrefs.GetInt("NeuroTOD_ViewLevel", (int)ViewLevel.Level3_CurveEditor);
            state.CurrentViewLevel = (ViewLevel)Mathf.Clamp(level, 0, 2);
        }

        // ========== 事件处理 ==========
        private void HandleEvents()
        {
            // Tab 键切换模式
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Tab)
            {
                if (state.CurrentViewLevel == ViewLevel.Level3_CurveEditor)
                {
                    state.CurrentEditorMode = state.CurrentEditorMode == EditorMode.Keyframes
                        ? EditorMode.Curves
                        : EditorMode.Keyframes;
                    Event.current.Use();
                    Repaint();
                }
            }

            // 空格键播放/暂停
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Space)
            {
                TogglePlayForward();
                Event.current.Use();
            }
        }
    }
}