using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using DawnTOD;

namespace DawnTODEditor
{
    /// <summary>
    /// TOD Lighting Editor 主窗口
    /// </summary>
    public class DawnLightingEditorWindow : EditorWindow
    {
        // ========== 常量 ==========
        private const float MIN_OUTLINER_WIDTH = 150f;
        private const float SCROLLBAR_WIDTH = 16f;

        // ========== 状态数据 ==========
        private LightingEditorState _state;

        private TrackManager _trackManager;

        // ========== 目标引用 ==========
        private DawnWeatherController _selectedController;
        private DawnWeatherPreset _activePreset;

        // ========== 布局数据 ==========
        private float _outlinerWidth;
        
        //==========绘制器===========
        private TrackOutlinerDrawer _trackOutlinerDrawer;
        private ModeTabDrawer _modeTabDrawer;
        private KeyframeValueEditorDraw _keyframeValueEditorDrawer;
        private ColorKeyValueEditorDraw _colorKeyValueEditorDrawer;
        private GradientDrawer _gradientDrawer;
        private AnimationCurveDrawer _animationCurveDrawer;
        private GridDrawer _gridDrawer;
        private TimelineRulerDrawer _timelineRulerDrawer;
        private SplitterDrawer _splitterDrawer;

        // ========== 样式 ==========
        private GUIStyle _toolbarStyle;
        private GUIStyle _timeDisplayStyle;
        private bool _stylesInitialized;

        // ========== 交互 ==========
        private CurveInteractionHandler _curveInteractionHandler;
        private GradientInteractionHandler _gradientInteractionHandler;

        // ========== 菜单入口 ==========
        [MenuItem("MagicDawn/TOD/Lighting Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<DawnLightingEditorWindow>();
            window.titleContent = new GUIContent("Lighting Editor", EditorGUIUtility.IconContent("DirectionalLight Icon").image);
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnEnable()
        {
            _state = new LightingEditorState();
            _trackManager = new TrackManager();
            _outlinerWidth = 200f;
            _curveInteractionHandler = new CurveInteractionHandler(_state, _trackManager);
            _gradientInteractionHandler = new GradientInteractionHandler(_state, _trackManager);

            _trackOutlinerDrawer = new TrackOutlinerDrawer();
            _trackOutlinerDrawer.Initialize(_state, _trackManager);
            _trackOutlinerDrawer.RegisterCallbacks(Repaint);
            _modeTabDrawer = new ModeTabDrawer();
            _modeTabDrawer.Initialize(_state, _trackManager);
            _modeTabDrawer.SetModeChangedCallback(() =>
            {
                lastUpdateTime = Time.realtimeSinceStartup;
                Repaint();
            });
            _keyframeValueEditorDrawer = new KeyframeValueEditorDraw();
            _keyframeValueEditorDrawer.Initialize(_state, _trackManager);
            _colorKeyValueEditorDrawer = new ColorKeyValueEditorDraw();
            _colorKeyValueEditorDrawer.Initialize(_state, _trackManager);
            _gradientDrawer = new GradientDrawer();
            _gradientDrawer.Initialize(_state, _trackManager);
            _animationCurveDrawer = new AnimationCurveDrawer();
            _animationCurveDrawer.Initialize(_state, _trackManager);
            _gridDrawer = new GridDrawer();
            _gridDrawer.Initialize(_state, _trackManager);
            _timelineRulerDrawer = new TimelineRulerDrawer();
            _timelineRulerDrawer.Initialize(_state, _trackManager);
            _timelineRulerDrawer.SetOnSetCurrentTimeCallback(SetCurrentTime);
            _splitterDrawer = new SplitterDrawer();
            _splitterDrawer.Initialize(_state, _trackManager);
            _splitterDrawer.SetSplitterConfig(newWidth =>
            {
                _outlinerWidth = newWidth;
                Repaint();
            }, MIN_OUTLINER_WIDTH);

            LoadViewLevelFromPrefs();
            RefreshControllerList();
            EditorApplication.update += OnEditorUpdate;
            Selection.selectionChanged += OnSelectionChanged;
            _trackManager.OnTracksRefreshed += OnTracksRefreshed;

            lastUpdateTime = Time.realtimeSinceStartup;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            Selection.selectionChanged -= OnSelectionChanged;
            _trackManager.OnTracksRefreshed -= OnTracksRefreshed;

            _trackOutlinerDrawer?.Dispose();
            _splitterDrawer?.Dispose();
            _modeTabDrawer?.Dispose();

            SaveViewLevelToPrefs();
        }

        // 窗口事件回调，自动通知绘制器
        private void OnTracksRefreshed()
        {
            Repaint();
        }

        private void OnSelectionChanged()
        {
            // 检查选中的对象是否包含TODController
            var go = Selection.activeGameObject;
            if (go != null)
            {
                var controller = go.GetComponent<DawnWeatherController>();
                if (controller != null && controller != _selectedController)
                {
                    SetSelectedController(controller);
                }
            }
        }

        private void OnEditorUpdate()//每帧更新编辑器画面
        {
            if (_state.Playback.State == PlaybackState.PlayingForward || _state.Playback.State == PlaybackState.PlayingBackward)
            {
                float deltaTime = Time.realtimeSinceStartup - lastUpdateTime;
                lastUpdateTime = Time.realtimeSinceStartup;

                float playDirection = _state.Playback.State == PlaybackState.PlayingForward ? 1f : -1f;
                _state.CurrentTime += _state.Playback.PlaybackSpeed * deltaTime * playDirection;

                // 循环播放
                if (_state.CurrentTime >= 1f)
                {
                    _state.CurrentTime = _state.Playback.IsLooping ? _state.CurrentTime - 1f : 1f;
                    if (!_state.Playback.IsLooping) 
                    {
                        _state.Playback.State = PlaybackState.Stopped;
                    }
                }
                else if (_state.CurrentTime < 0f)
                {
                    _state.CurrentTime = _state.Playback.IsLooping ? _state.CurrentTime + 1f : 0f;
                    if (!_state.Playback.IsLooping) 
                    {
                        _state.Playback.State = PlaybackState.Stopped;
                    }
                }

                if (_selectedController != null)
                {
                    _selectedController.TimeOfDay = _state.CurrentTime * 24f;
                    SceneView.RepaintAll();
                }

                Repaint();
            }
        }
        private float lastUpdateTime;

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _toolbarStyle = LightingEditorToolbarDrawer.CreateToolbarStyle();
            _timeDisplayStyle = LightingEditorToolbarDrawer.CreateTimeDisplayStyle();

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            // 主布局
            EditorGUILayout.BeginVertical();
            {
                // 顶部工具栏
                LightingEditorToolbarDrawer.DrawToolbar(
                    LightingEditorConstants.TOOLBAR_HEIGHT,
                    _toolbarStyle,
                    _timeDisplayStyle,
                    _state,
                    _selectedController,
                    OnControllerSelected,       // 选择控制器回调
                    OnRefreshClicked,           // 刷新按钮回调
                    OnViewLevelChanged,         // 视图级别变更回调
                    OnTimeFormatToggled         // 时间格式切换回调
                );

                EditorGUILayout.Space(1);
                EditorDrawingUtility.DrawHorizontalLine();

                if (_state.CurrentViewLevel == ViewLevel.Level3_CurveEditor)
                {
                    DrawMainEditorArea();
                }

                // 保存当前事件状态，避免曲线区域的事件处理影响 PlaybackBar
                Event currentEvent = Event.current;
                LightingEditorPlaybackBarDrawer.DrawPlaybackBar(
                    _state,
                    JumpToStart,
                    StepBackward,
                    TogglePlayBackward,
                    TogglePlayForward,
                    StepForward,
                    JumpToEnd,
                    SetCurrentTime
                );
            }
            EditorGUILayout.EndVertical();

            // 处理全局键盘事件
            HandleEvents();
        }

        // ========== 主编辑区域 ==========
        private void DrawMainEditorArea()
        {
            Rect mainRect = EditorGUILayout.GetControlRect(false, position.height - LightingEditorConstants.TOOLBAR_HEIGHT - LightingEditorConstants.PLAYBACK_BAR_HEIGHT - 10);

            // 左侧：轨道 Outliner
            Rect outlinerRect = new Rect(mainRect.x, mainRect.y, _outlinerWidth, mainRect.height);

            // 侧边栏分割条
            Rect splitterRect = new Rect(outlinerRect.xMax, mainRect.y, SplitterDrawer.SPLITTER_WIDTH, mainRect.height);

            // 右侧：时间轴 + 曲线区域
            Rect trackAreaRect = new Rect(splitterRect.xMax, mainRect.y, mainRect.width - _outlinerWidth - SplitterDrawer.SPLITTER_WIDTH, mainRect.height);

            // 绘制各区域
            DrawTrackOutliner(outlinerRect);
            DrawSplitter(splitterRect);
            DrawTrackArea(trackAreaRect);
        }

        #region 主编辑绘制区域
        private void DrawTrackOutliner(Rect rect)
        {
            _trackOutlinerDrawer.Draw(rect);

            Event evt = Event.current;
            if (_trackOutlinerDrawer.HandleEvent(rect, evt))
            {
                evt.Use();
            }
        }

        private void DrawSplitter(Rect rect)
        {
            _splitterDrawer.Draw(rect);
            Event evt = Event.current;
            if (_splitterDrawer.HandleEvent(rect, evt))
            {
                evt.Use();
            }
        }

        private void DrawTrackArea(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            // 模式切换标签页
            Rect tabRect = new Rect(rect.x, rect.y, rect.width, 24);
            _modeTabDrawer.Draw(tabRect);

            // 时间轴刻度尺
            float effectiveWidth = _state.CurrentEditorMode == EditorMode.Keyframes
                ? rect.width - SCROLLBAR_WIDTH
                : rect.width;
            Rect rulerRect = new Rect(rect.x, rect.y + 24, effectiveWidth, TrackOutlinerDrawer.TIMELINE_RULER_HEIGHT);
            _timelineRulerDrawer.Draw(rulerRect);
            Event evt = Event.current;
            if (_timelineRulerDrawer.HandleEvent(rulerRect, evt))
            {
                evt.Use();
            }

            //  Keyframes 模式
            Rect contentRect = new Rect(rect.x, rulerRect.yMax, rect.width, rect.height - 24 - TrackOutlinerDrawer.TIMELINE_RULER_HEIGHT);
            if (_state.CurrentEditorMode == EditorMode.Keyframes)
            {
                ClearSelectedKeyframe();
                List<TrackInfo> visibleTracks = _trackOutlinerDrawer?.GetVisibleTracks() ?? new List<TrackInfo>();
                float contentHeight = visibleTracks.Count * 24f;

                var scrollViewRect = new Rect(contentRect.x, contentRect.y, contentRect.width, contentRect.height);
                var viewRect = new Rect(0, 0, contentRect.width - SCROLLBAR_WIDTH, contentHeight);

                Vector2 oldScroll = _trackOutlinerDrawer._scrollPosition;
                _trackOutlinerDrawer._scrollPosition = GUI.BeginScrollView(
                    scrollViewRect,
                    _trackOutlinerDrawer._scrollPosition,
                    viewRect,
                    alwaysShowHorizontal: false,
                    alwaysShowVertical: true
                );

                DrawKeyframeArea(new Rect(0, 0, viewRect.width, viewRect.height), visibleTracks);
                GUI.EndScrollView();

                if (oldScroll != _trackOutlinerDrawer._scrollPosition)
                    Repaint();
            }
            // Curves 模式
            else
            {
                DrawCurveArea(contentRect, null);
            }
        }
        #endregion

        /// <summary>
        /// 清除关键帧选中状态，并重置编辑模式
        /// </summary>
        private void ClearSelectedKeyframe()
        {
            _state.SelectedKeyframe = null;
            // 如果之前是在拖拽或编辑关键帧，回到普通模式
            if (_state.CurrentCurveEditMode == CurveEditMode.ClickedKeyframe ||
                _state.CurrentCurveEditMode == CurveEditMode.MovingKeyframe)
            {
                _state.CurrentCurveEditMode = CurveEditMode.None;
            }

            _state.SelectedGradientKey = null;
            _state.CurrentGradientEditMode = GradientEditMode.None;
        }

        private void DrawKeyframeArea(Rect rect, List<TrackInfo> visibleTracks)
        {
            GUI.Box(rect, GUIContent.none);

            _gridDrawer.Draw(rect);

            float trackHeight = 24f;
            for (int i = 0; i < visibleTracks.Count; i++)
            {
                var track = visibleTracks[i];
                Rect trackRect = new Rect(rect.x, rect.y + i * trackHeight, rect.width, trackHeight);
            }

            // 当前时间指示线（贯穿整个高度）
            float timeX = rect.x + _state.CurrentTime * rect.width;
            EditorGUI.DrawRect(new Rect(timeX - 1, rect.y, 2, rect.height), new Color(1f, 0.3f, 0.3f));

            // 在 Keyframes 窗口下也绘制曲线
            DrawCurveArea(rect, visibleTracks);
        }

        //根据折叠情况 判断轨道是否应该被绘制
        private bool ShouldDrawTrack(TrackInfo track)
        {
            return _trackManager.ShouldDrawTrack(track);
        }

        private void DrawCurveArea(Rect rect, List<TrackInfo> visibleTracks)
        {
            GUI.Box(rect, GUIContent.none);

            _gridDrawer.Draw(rect);

            bool isKeyframesMode = visibleTracks != null;
            List<TrackInfo> tracksToDraw = new List<TrackInfo>();

            if (isKeyframesMode)
            {
                foreach (var track in visibleTracks)
                {
                    if (!ShouldDrawTrack(track)) continue;
                    tracksToDraw.Add(track);
                }
            }
            else
            {
                foreach (int idx in _state.SelectedTrackIndices)
                {
                    var track = _trackManager.GetTracks().Find(t => t.TrackIndex == idx);
                    if (track != null && ShouldDrawTrack(track))
                    {
                        tracksToDraw.Add(track);
                    }
                }
            }

            // 实际绘制曲线
            foreach (var track in tracksToDraw)
            {
                Rect drawRect = isKeyframesMode
                    ? new Rect(rect.x, rect.y + tracksToDraw.IndexOf(track) * 24f, rect.width, 23f)
                    : rect; // Curves 模式则使用整个区域

                if (track.FloatCurve != null)
                {
                    _animationCurveDrawer.DrawAnimationCurve(drawRect, track.FloatCurve, EditorDrawingUtility.GetTrackColor(track.TrackIndex), track.TrackIndex);
                }
                else if (track.ColorGradient != null)
                {
                    _gradientDrawer.DrawGradient(drawRect, track.ColorGradient, isKeyframesMode ? drawRect.height : rect.height / 2);
                }
            }

            Event currentEvent = Event.current;
            // 定义 Curve/Gradient 的有效交互区域（排除下方 PlaybackBar，且限定在当前 rect 内）
            bool isMouseInCurveArea = rect.Contains(currentEvent.mousePosition);
            // 仅当鼠标在区域内，且非 Keyframes 模式时，才交给交互处理器
            if (!isKeyframesMode && isMouseInCurveArea)
            {
                if (_curveInteractionHandler != null)
                {
                    if (_curveInteractionHandler.HandleEvent(rect, currentEvent))
                    {
                        currentEvent.Use();
                        Repaint();
                    }
                }
                if (_gradientInteractionHandler != null)
                {
                    if (_gradientInteractionHandler.HandleEvent(rect, currentEvent))
                    {
                        currentEvent.Use();
                        Repaint();
                    }
                }
            }

            //绘制左上角关键帧设置小面板
            if (!isKeyframesMode)
            {
                _keyframeValueEditorDrawer.Draw(rect);
                _colorKeyValueEditorDrawer.Draw(rect);
            }

            // 时间指示线(贯穿整个高度)
            float timeX = rect.x + _state.CurrentTime * rect.width;
            EditorGUI.DrawRect(new Rect(timeX - 1, rect.y, 2, rect.height), new Color(1f, 0.3f, 0.3f));
        }

        /// <summary>
        /// 刷新轨道（无需同步副本，直接通知绘制器）
        /// </summary>
        private void RefreshTracks()
        {
            _trackManager.RefreshTracks(_activePreset);
        }

        // ========== 时间控制 ==========
        private void SetCurrentTime(float time)
        {
            _state.CurrentTime = Mathf.Clamp01(time);
            if (_selectedController != null)
            {
                _selectedController.TimeOfDay = _state.CurrentTime * 24f;
                SceneView.RepaintAll();
            }
            Repaint();
        }
        
        #region Controller组件管理
        private void RefreshControllerList()
        {
            var controllers = FindObjectsOfType<DawnWeatherController>();
            if (controllers.Length > 0) 
            { 
                SetSelectedController(controllers[0]);
            }
        }

        private void SetSelectedController(DawnWeatherController controller)
        {
            ClearSelectedKeyframe();

            _selectedController = controller;
            _activePreset = controller?.ActivePreset;
            
            _curveInteractionHandler.UpdateActivePreset(_activePreset);
            _gradientInteractionHandler.UpdateActivePreset(_activePreset);
            _keyframeValueEditorDrawer.UpdateActivePreset(_activePreset);
            _colorKeyValueEditorDrawer.UpdateActivePreset(_activePreset);

            if (controller != null)
            {
                _state.CurrentTime = controller.NormalizedTime;
                // 切换控制器时重置播放状态
                _state.Playback.State = PlaybackState.Stopped;
                lastUpdateTime = Time.realtimeSinceStartup;
            }

            // ===== 确保清空选择的关键帧 =====
            _state.SelectedGradientKey = null;
            _state.CurrentGradientEditMode = GradientEditMode.None;
            _state.SelectedKeyframe = null;
            _state.CurrentCurveEditMode = CurveEditMode.None;

            RefreshTracks();
            Repaint();
        }
        #endregion
        
        #region ToolBar回调

        /// <summary>
        /// 控制器选择回调
        /// </summary>
        private void OnControllerSelected(DawnWeatherController controller)
        {
            SetSelectedController(controller);
        }

        /// <summary>
        /// 刷新按钮点击回调
        /// </summary>
        private void OnRefreshClicked()
        {
            RefreshControllerList();
            RefreshTracks();
        }

        /// <summary>
        /// 视图级别变更回调
        /// </summary>
        private void OnViewLevelChanged(ViewLevel level)
        {
            SetViewLevel(level);
        }

        /// <summary>
        /// 时间格式切换回调
        /// </summary>
        private void OnTimeFormatToggled()
        {
            _state.TimeDisplayMode = _state.TimeDisplayMode == TimeDisplayMode.Format24H
                ? TimeDisplayMode.Format12H
                : TimeDisplayMode.Format24H;
        }
        
        #endregion

        #region playbackBar回调
        private void TogglePlayForward()
        {
            if (_state.Playback.State == PlaybackState.PlayingForward)
            {
                _state.Playback.State = PlaybackState.Stopped;
            }
            else
            {
                // 停止其他播放状态，重置时间基准
                _state.Playback.State = PlaybackState.PlayingForward;
                lastUpdateTime = Time.realtimeSinceStartup;
            }
        }

        private void TogglePlayBackward()
        {
            if (_state.Playback.State == PlaybackState.PlayingBackward)
            {
                _state.Playback.State = PlaybackState.Stopped;
            }
            else
            {
                // 停止其他播放状态，重置时间基准
                _state.Playback.State = PlaybackState.PlayingBackward;
                lastUpdateTime = Time.realtimeSinceStartup;
            }
        }

        private void JumpToStart()
        {
            _state.Playback.State = PlaybackState.Stopped;
            SetCurrentTime(0f);
        }

        private void JumpToEnd()
        {
            _state.Playback.State = PlaybackState.Stopped;
            SetCurrentTime(1f);
        }

        private void StepForward()
        {
            _state.Playback.State = PlaybackState.Stopped;
            SetCurrentTime(_state.CurrentTime + 1f / 1440f); // 1分钟
        }

        private void StepBackward()
        {
            _state.Playback.State = PlaybackState.Stopped;
            SetCurrentTime(_state.CurrentTime - 1f / 1440f);
        }

        private void JumpToPreviousKey()
        {
            //实现跳转到上一关键帧
        }

        private void JumpToNextKey()
        {
            //实现跳转到下一关键帧
        }
        
        #endregion

        #region 视图级别ViewLevel
        private void SetViewLevel(ViewLevel level)
        {
            if (_state.CurrentViewLevel != level)
            {
                _state.CurrentViewLevel = level;
                _state.Playback.State = PlaybackState.Stopped;
                lastUpdateTime = Time.realtimeSinceStartup;
                SaveViewLevelToPrefs();
                Repaint();
            }
        }
        #endregion

        #region 数据持久化
        private void SaveViewLevelToPrefs()
        {
            EditorPrefs.SetInt("TOD_ViewLevel", (int)_state.CurrentViewLevel);
        }
        private void LoadViewLevelFromPrefs()
        {
            int level = EditorPrefs.GetInt("TOD_ViewLevel", (int)ViewLevel.Level3_CurveEditor);
            _state.CurrentViewLevel = (ViewLevel)Mathf.Clamp(level, 0, 2);
        }
        #endregion

        #region 事件处理
        private void HandleEvents()
        {
            // Tab 键切换模式
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Tab)
            {
                if (_state.CurrentViewLevel == ViewLevel.Level3_CurveEditor)
                {
                    _state.CurrentEditorMode = _state.CurrentEditorMode == EditorMode.Keyframes
                        ? EditorMode.Curves
                        : EditorMode.Keyframes;
                    lastUpdateTime = Time.realtimeSinceStartup;
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
        #endregion
    }
}