using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using DawnTOD;

namespace DawnTODEditor
{
    public class KeyframeValueEditorDraw : IEditorDrawer
    {
        private LightingEditorState _state;
        private TrackManager _trackManager;
        private DawnWeatherPreset _activePreset;

        private (int trackIndex, int keyIndex)? _lastKeyframeForEditor;

        public void UpdateActivePreset(DawnWeatherPreset activePreset)
        {
            this._activePreset = activePreset;
        }

        /// <summary>
        /// 初始化（传入必要的状态和 TrackManager）
        /// </summary>
        public void Initialize(LightingEditorState state, TrackManager trackManager)
        {
            _state = state;
            _trackManager = trackManager;
            _lastKeyframeForEditor = null;
        }

        /// <summary>
        /// 绘制关键帧数值编辑窗口
        /// </summary>
        public void Draw(Rect drawRect)
        {
            // 修改：判空改为 _trackManager
            if (_state == null || _trackManager == null) return;

            DrawKeyframeValueEditor(drawRect);
        }

        /// <summary>
        /// 事件处理（当前逻辑无额外事件，返回 false 不拦截事件）
        /// </summary>
        public bool HandleEvent(Rect drawRect, Event evt)
        {
            return false;
        }

        private void DrawKeyframeValueEditor(Rect curveDrawRect)
        {
            bool readOnly = _state.IsDraggingKeyframe;

            if (readOnly || _state.SelectedKeyframe != _lastKeyframeForEditor)
            {
                _lastKeyframeForEditor = _state.SelectedKeyframe;

                GUIUtility.keyboardControl = 0;
                GUIUtility.hotControl = 0;
                EditorGUIUtility.editingTextField = false;
            }

            if (_state.SelectedKeyframe == null)
                return;

            var (trackIndex, keyIndex) = _state.SelectedKeyframe.Value;
            var track = _trackManager.GetTracks().Find(t => t.TrackIndex == trackIndex);
            if (track == null || track.FloatCurve == null || keyIndex >= track.FloatCurve.keys.Length)
                return;

            AnimationCurve curve = track.FloatCurve;
            Keyframe key = curve.keys[keyIndex];
            bool isEdgeKey = (keyIndex == 0 || keyIndex == curve.keys.Length - 1);

            // ===== 绘制窗口 =====
            float windowHeight = isEdgeKey ? 30 : 70;
            Rect windowRect = new Rect(curveDrawRect.x + 10, curveDrawRect.y + 10, 160, windowHeight);
            EditorGUI.DrawRect(windowRect, new Color(0.2f, 0.2f, 0.2f, 0.9f));
            GUI.Box(windowRect, GUIContent.none);

            // ===== 进入只读模式 =====
            bool prevGUIEnabled = GUI.enabled;
            GUI.enabled = !readOnly;

            // ===== Value 输入 =====
            Rect valueRect = new Rect(windowRect.x + 5, windowRect.y + 5, windowRect.width - 10, 18);
            EditorGUI.LabelField(new Rect(valueRect.x, valueRect.y, 50, valueRect.height), "Value:");
            float newValue = EditorGUI.FloatField(
                new Rect(valueRect.x + 60, valueRect.y, 70, valueRect.height),
                key.value
            );

            // ===== Time 输入（Hour / Min）=====
            float newTime = key.time;
            if (!isEdgeKey)
            {
                Rect timeRect = new Rect(windowRect.x + 5, windowRect.y + 32, windowRect.width - 10, 50);

                float totalHours = key.time * 24f;
                int currentHour = Mathf.RoundToInt(totalHours);
                float minuteFloat = Mathf.Abs((totalHours - currentHour) * 60f);
                int currentMin = Mathf.RoundToInt(minuteFloat);

                EditorGUI.LabelField(new Rect(timeRect.x, timeRect.y, 40, 18), "Hour:");
                string hourInput = EditorGUI.TextField(
                    new Rect(timeRect.x + 40, timeRect.y, 50, 18),
                    currentHour.ToString()
                );
                int newHour = currentHour; 
                if (!string.IsNullOrEmpty(hourInput) && !string.IsNullOrWhiteSpace(hourInput))
                {
                    if (int.TryParse(hourInput, out int parsedHour))
                    {
                        newHour = parsedHour;
                    }
                }

                EditorGUI.LabelField(new Rect(timeRect.x, timeRect.y + 22, 30, 18), "Min:");
                string minInput = EditorGUI.TextField(
                    new Rect(timeRect.x + 40, timeRect.y + 22, 50, 18),
                    currentMin.ToString()
                );
                int newMin = currentMin;
                if (!string.IsNullOrEmpty(minInput) && !string.IsNullOrWhiteSpace(minInput))
                {
                    if (int.TryParse(minInput, out int parsedMin))
                    {
                        newMin = parsedMin;
                    }
                }

                newHour = Mathf.Clamp(newHour, 0, 23);
                newMin = Mathf.Clamp(newMin, 0, 59);

                newTime = (newHour + newMin / 60f) / 24f;
            }

            // ===== 恢复 GUI 状态 =====
            GUI.enabled = prevGUIEnabled;

            // ===== 拖拽期间：只显示，不应用修改 =====
            if (readOnly)
                return;

            // ===== 应用修改（仅点击态）=====
            if (_state.CurrentCurveEditMode == CurveEditMode.ClickedKeyframe ||
                _state.CurrentCurveEditMode == CurveEditMode.ClickedTangent)
            {
                bool valueChanged = !Mathf.Approximately(newValue, key.value);
                bool timeChanged = !isEdgeKey && !Mathf.Approximately(newTime, key.time);

                if (!valueChanged && !timeChanged)
                    return;

                // ---- Value 修改（含首尾同步）----
                if (valueChanged)
                {
                    EditorUtility.SetDirty(_activePreset);
                    
                    Keyframe currentKey = curve.keys[keyIndex];
                    currentKey.value = newValue;
                    curve.MoveKey(keyIndex, currentKey);
                }

                // ---- Time 修改（中间帧）----
                if (timeChanged)
                {
                    EditorUtility.SetDirty(_activePreset);
                    
                    Keyframe modifiedKey = new Keyframe(
                        newTime,
                        key.value,
                        key.inTangent,
                        key.outTangent
                    );

                    curve.MoveKey(keyIndex, modifiedKey);

                    // 更新索引（防止排序后指错帧）
                    float targetTime = modifiedKey.time;
                    for (int i = 0; i < curve.keys.Length; i++)
                    {
                        if (Mathf.Approximately(curve.keys[i].time, targetTime))
                        {
                            _state.SelectedKeyframe = (trackIndex, i);
                            break;
                        }
                    }
                }
            }
        }
    }

    public class ColorKeyValueEditorDraw : IEditorDrawer
    {
        private LightingEditorState _state;
        private TrackManager _trackManager;
        private DawnWeatherPreset _activePreset;

        // 原有私有状态变量
        private (int trackIndex, int keyIndex, GradientKeyType keyType)? _lastColorKeyForEditor;

        public void UpdateActivePreset(DawnWeatherPreset activePreset)
        {
            this._activePreset = activePreset;
        }

        /// <summary>
        /// 初始化（传入必要的状态和 TrackManager）
        /// </summary>
        public void Initialize(LightingEditorState state, TrackManager trackManager)
        {
            _state = state;
            _trackManager = trackManager;
            _lastColorKeyForEditor = null;
        }

        /// <summary>
        /// 绘制渐变关键帧（颜色/透明度）编辑窗口
        /// </summary>
        public void Draw(Rect drawRect)
        {
            if (_state == null || _trackManager == null) return;

            DrawColorKeyValueEditor(drawRect);
        }

        /// <summary>
        /// 事件处理（当前逻辑无额外事件，返回 false 不拦截事件）
        /// </summary>
        public bool HandleEvent(Rect drawRect, Event evt)
        {
            return false;
        }

        // ========== 核心绘制逻辑（迁移自原窗口）==========
        private void DrawColorKeyValueEditor(Rect colorDrawRect)
        {
            // ===== 是否处于拖拽只读态 =====
            bool readOnly = _state.IsDraggingColorKey;

            // ===== 拖拽 / 关键帧切换 / 轨道切换时，强制清空 GUI 焦点 =====
            if (readOnly || _state.SelectedGradientKey != _lastColorKeyForEditor)
            {
                _lastColorKeyForEditor = _state.SelectedGradientKey;

                GUIUtility.keyboardControl = 0;
                GUIUtility.hotControl = 0;
                EditorGUIUtility.editingTextField = false;
            }

            // ===== 状态检查 =====
            if (_state.SelectedGradientKey == null)
                return;

            var (trackIndex, keyIndex, keyType) = _state.SelectedGradientKey.Value;
            var track = _trackManager.GetTracks().Find(t => t.TrackIndex == trackIndex);

            if (track == null || track.ColorGradient == null)
                return;

            bool hasKeys = false;
            float keyTime = 0f;
            Color keyColor = Color.white;
            float keyAlpha = 1f;
            bool isEdgeKey = false;

            if (keyType == GradientKeyType.Color)
            {
                var colorKeys = track.ColorGradient.colorKeys;
                if (colorKeys.Length == 0 || keyIndex >= colorKeys.Length)
                {
                    // 没有关键帧时，隐藏编辑窗口
                    _state.SelectedGradientKey = null;
                    return;
                }

                hasKeys = true;
                keyTime = colorKeys[keyIndex].time;
                keyColor = colorKeys[keyIndex].color;
                isEdgeKey = (keyIndex == 0 || keyIndex == colorKeys.Length - 1);
            }
            else // Alpha 关键帧
            {
                var alphaKeys = track.ColorGradient.alphaKeys;
                if (alphaKeys.Length == 0 || keyIndex >= alphaKeys.Length)
                {
                    // 没有关键帧时，隐藏编辑窗口
                    _state.SelectedGradientKey = null;
                    return;
                }

                hasKeys = true;
                keyTime = alphaKeys[keyIndex].time;
                keyAlpha = alphaKeys[keyIndex].alpha;
                isEdgeKey = (keyIndex == 0 || keyIndex == alphaKeys.Length - 1);
            }

            if (!hasKeys)
                return;

            // ===== 绘制窗口 =====
            float windowHeight = isEdgeKey
                ? (keyType == GradientKeyType.Color ? 60 : 40)
                : (keyType == GradientKeyType.Color ? 90 : 70);
            Rect windowRect = new Rect(colorDrawRect.x + 10, colorDrawRect.y + 10, 180, windowHeight);
            EditorGUI.DrawRect(windowRect, new Color(0.2f, 0.2f, 0.2f, 0.9f));
            GUI.Box(windowRect, GUIContent.none);

            // ===== 进入只读模式=====
            bool prevGUIEnabled = GUI.enabled;
            GUI.enabled = !readOnly;

            // ===== 关键帧类型标题 =====
            Rect typeLabelRect = new Rect(windowRect.x + 5, windowRect.y + 5, windowRect.width - 10, 18);
            string typeText = keyType == GradientKeyType.Color ? "Color Key" : "Alpha Key";
            EditorGUI.LabelField(typeLabelRect, typeText, EditorStyles.boldLabel);

            // ===== 颜色/透明度编辑 =====
            Rect valueRect = new Rect(windowRect.x + 5, windowRect.y + 25, windowRect.width - 10, 20);
            Color newColor = keyColor;
            float newAlpha = keyAlpha;

            if (keyType == GradientKeyType.Color)
            {
                EditorGUI.LabelField(new Rect(valueRect.x, valueRect.y, 50, valueRect.height), "Color:");
                newColor = EditorGUI.ColorField(
                    new Rect(valueRect.x + 60, valueRect.y, 90, valueRect.height),
                    GUIContent.none,
                    keyColor
                );

                // 应用颜色修改
                if (!newColor.Equals(keyColor) && !readOnly)
                {
                    EditorUtility.SetDirty(_activePreset);
                    var colorKeys = track.ColorGradient.colorKeys.ToList();
                    colorKeys[keyIndex] = new GradientColorKey(newColor, keyTime);
                    track.ColorGradient.colorKeys = colorKeys.ToArray();
                }
            }
            else
            {
                EditorGUI.LabelField(new Rect(valueRect.x, valueRect.y, 50, valueRect.height), "Alpha:");
                newAlpha = EditorGUI.Slider(
                    new Rect(valueRect.x + 60, valueRect.y, 90, valueRect.height),
                    keyAlpha,
                    0f,
                    1f
                );

                // 应用透明度修改
                if (!Mathf.Approximately(newAlpha, keyAlpha) && !readOnly)
                {
                    EditorUtility.SetDirty(_activePreset);
                    var alphaKeys = track.ColorGradient.alphaKeys.ToList();
                    alphaKeys[keyIndex] = new GradientAlphaKey(newAlpha, keyTime);
                    track.ColorGradient.alphaKeys = alphaKeys.ToArray();
                }
            }

            // ===== Time 输入（Hour / Min）=====
            float newTime = keyTime;
            bool timeUserModified = false;

            if (!isEdgeKey && !readOnly)
            {
                Rect timeRect = new Rect(windowRect.x + 5,
                    windowRect.y + (keyType == GradientKeyType.Color ? 50 : 40), windowRect.width - 10, 50);

                float totalHours = keyTime * 24f;
                int currentHour = Mathf.RoundToInt(totalHours);
                float minuteFloat = Mathf.Abs((totalHours - currentHour) * 60f);
                int currentMin = Mathf.RoundToInt(minuteFloat);

                EditorGUI.LabelField(new Rect(timeRect.x, timeRect.y, 40, 18), "Hour:");
                string hourInput = EditorGUI.TextField(
                    new Rect(timeRect.x + 40, timeRect.y, 50, 18),
                    currentHour.ToString()
                );
                int newHour = currentHour;
                if (!string.IsNullOrEmpty(hourInput) && !string.IsNullOrWhiteSpace(hourInput))
                {
                    if (int.TryParse(hourInput, out int parsedHour))
                    {
                        newHour = parsedHour;
                    }
                }

                EditorGUI.LabelField(new Rect(timeRect.x, timeRect.y + 22, 30, 18), "Min:");
                string minInput = EditorGUI.TextField(
                    new Rect(timeRect.x + 40, timeRect.y + 22, 50, 18),
                    currentMin.ToString()
                );
                int newMin = currentMin;
                if (!string.IsNullOrEmpty(minInput) && !string.IsNullOrWhiteSpace(minInput))
                {
                    if (int.TryParse(minInput, out int parsedMin))
                    {
                        newMin = parsedMin;
                    }
                }

                bool hourChanged = newHour != currentHour;
                bool minChanged = newMin != currentMin;
                if (hourChanged || minChanged)
                {
                    newHour = Mathf.Clamp(newHour, 0, 23);
                    newMin = Mathf.Clamp(newMin, 0, 59);
                    newTime = (newHour + newMin / 60f) / 24f;
                    timeUserModified = true;
                }
                else
                {
                    newTime = keyTime;
                }
            }


            // ===== 应用时间修改 =====
            if (timeUserModified && !Mathf.Approximately(newTime, keyTime) && !readOnly)
            {
                EditorUtility.SetDirty(_activePreset);
                if (keyType == GradientKeyType.Color)
                {
                    var colorKeys = track.ColorGradient.colorKeys.ToList();
                    colorKeys[keyIndex] = new GradientColorKey(keyColor, newTime);
                    colorKeys.Sort((a, b) => a.time.CompareTo(b.time));
                    track.ColorGradient.colorKeys = colorKeys.ToArray();

                    // 更新选中索引
                    int newIndex = colorKeys.FindIndex(k => Mathf.Approximately(k.time, newTime));
                    if (newIndex != -1)
                    {
                        _state.SelectedGradientKey = (trackIndex, newIndex, GradientKeyType.Color);
                    }
                }
                else
                {
                    var alphaKeys = track.ColorGradient.alphaKeys.ToList();
                    alphaKeys[keyIndex] = new GradientAlphaKey(keyAlpha, newTime);
                    alphaKeys.Sort((a, b) => a.time.CompareTo(b.time));
                    track.ColorGradient.alphaKeys = alphaKeys.ToArray();

                    int newIndex = alphaKeys.FindIndex(k => Mathf.Approximately(k.time, newTime));
                    if (newIndex != -1)
                    {
                        _state.SelectedGradientKey = (trackIndex, newIndex, GradientKeyType.Alpha);
                    }
                }
            }

            // ===== 恢复 GUI 状态 =====
            GUI.enabled = prevGUIEnabled;

            // ===== 拖拽期间：只显示，不应用修改 =====
            if (readOnly)
                return;
        }
    }
}