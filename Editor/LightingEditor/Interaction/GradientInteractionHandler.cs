using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using DawnTOD;
using UnityEditor;

namespace DawnTODEditor
{
    // 标记 Gradient 关键帧类型：颜色键或透明度键
    public enum GradientKeyType
    {
        Color,
        Alpha
    }

    // 用于记录拖拽开始时的关键帧状态
    public struct GradientKeySnapshot
    {
        public float time;
        public GradientKeyType type;

        public GradientKeySnapshot(float t, GradientKeyType ty)
        {
            time = t;
            type = ty;
        }
    }

    public class GradientInteractionHandler
    {
        private LightingEditorState state;
        private TrackManager _trackManager;
        private DawnWeatherPreset _activePreset;

        private (int trackIndex, int keyIndex, GradientKeySnapshot originalKey, Vector2 startPos, Vector2 keyScreenPos)? draggingKeyData;
        private const float KEYFRAME_DRAG_MAX_DISTANCE = 15f;
        private const float KEYFRAME_CLICK_RADIUS = 10f;
        private const float DRAG_ACTIVATION_THRESHOLD = 8f;
        private bool isDragInitiated = false;

        public GradientInteractionHandler(LightingEditorState state, TrackManager trackManager)
        {
            this.state = state;
            this._trackManager = trackManager;
        }

        public void UpdateActivePreset(DawnWeatherPreset activePreset)
        {
            this._activePreset = activePreset;
        }

        public bool HandleEvent(Rect fullCurveRect, Event e)
        {
            if (!fullCurveRect.Contains(e.mousePosition))
            {
                return false;
            }

            bool used = false;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                isDragInitiated = false;
                used = HandleMouseDown(fullCurveRect, e.mousePosition);
            }
            else if (e.type == EventType.MouseDrag)
            {
                used = HandleMouseDrag(fullCurveRect, e.delta);
            }
            else if (e.type == EventType.MouseUp)
            {
                HandleMouseUp();
                used = true;
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete)
            {
                used = HandleKeyDelete();
                if (used) e.Use();
            }

            return used;
        }

        #region 核心逻辑：鼠标按下处理
        private bool HandleMouseDown(Rect fullCurveRect, Vector2 mousePos)
        {
            if (state.SelectedTrackIndices.Count != 1)
                return false;

            int selectedTrackIndex = state.SelectedTrackIndices.First();
            TrackInfo track = _trackManager.GetTracks().Find(t => t.TrackIndex == selectedTrackIndex);
            if (track == null || track.ColorGradient == null || track.Type != TrackType.ColorGradient)
                return false;

            float gradientHeight = fullCurveRect.height * 0.5f;
            Rect gradientDrawRect = new Rect(
                fullCurveRect.x,
                fullCurveRect.y + (fullCurveRect.height - gradientHeight) * 0.5f,
                fullCurveRect.width,
                gradientHeight
            );

            bool clickedOnKey = CheckClickOnGradientKeys(track, gradientDrawRect, mousePos);

            if (!clickedOnKey && IsPointOverGradientArea(gradientDrawRect, mousePos))
            {
                state.CurrentGradientEditMode = GradientEditMode.ClickedGradient;
                float normalizedTime = Mathf.Clamp01((mousePos.x - gradientDrawRect.x) / gradientDrawRect.width);
                AddNewColorKeyframe(track, normalizedTime);
                //Debug.Log($"[Gradient] Clicked on gradient area at time {normalizedTime:F3}, created new color key");
                return true;
            }

            if (!clickedOnKey && !IsPointOverGradientArea(gradientDrawRect, mousePos))
            {
                //ResetGradientEditState();
            }

            return clickedOnKey;
        }
        #endregion

        #region 辅助方法：检测点击是否在关键帧上
        private bool CheckClickOnGradientKeys(TrackInfo track, Rect gradientDrawRect, Vector2 mousePos)
        {
            bool clickedOnKey = false;
            var colorKeys = track.ColorGradient.colorKeys;

            for (int i = 0; i < colorKeys.Length; i++)
            {
                if (IsPointOverGradientKey(mousePos, colorKeys[i].time, gradientDrawRect, isAlpha: false, out Vector2 keyScreenPos))
                {
                    //Debug.Log($"[Gradient] Clicked on COLOR key {i} at time {colorKeys[i].time:F3}");
                    state.CurrentGradientEditMode = GradientEditMode.ClickedColorKey;
                    state.SelectedGradientKey = (track.TrackIndex, i, GradientKeyType.Color);
                    // 缓存关键帧的屏幕坐标（用于后续拖拽距离判断）
                    draggingKeyData = (track.TrackIndex, i, new GradientKeySnapshot(colorKeys[i].time, GradientKeyType.Color), mousePos, keyScreenPos);
                    clickedOnKey = true;
                    break;
                }
            }

            if (!clickedOnKey)
            {
                var alphaKeys = track.ColorGradient.alphaKeys;
                for (int i = 0; i < alphaKeys.Length; i++)
                {
                    if (IsPointOverGradientKey(mousePos, alphaKeys[i].time, gradientDrawRect, isAlpha: true, out Vector2 keyScreenPos))
                    {
                        //Debug.Log($"[Gradient] Clicked on ALPHA key {i} at time {alphaKeys[i].time:F3}");
                        state.CurrentGradientEditMode = GradientEditMode.ClickedAlphaKey;
                        state.SelectedGradientKey = (track.TrackIndex, i, GradientKeyType.Alpha);
                        // 缓存关键帧的屏幕坐标（用于后续拖拽距离判断）
                        draggingKeyData = (track.TrackIndex, i, new GradientKeySnapshot(alphaKeys[i].time, GradientKeyType.Alpha), mousePos, keyScreenPos);
                        clickedOnKey = true;
                        break;
                    }
                }
            }

            return clickedOnKey;
        }
        #endregion

        #region 核心方法：在指定位置添加新的颜色关键帧
        private void AddNewColorKeyframe(TrackInfo track, float normalizedTime)
        {
            if (track.ColorGradient == null) return;
            EditorUtility.SetDirty(_activePreset);
            
            Color colorAtTime = track.ColorGradient.Evaluate(normalizedTime);
            GradientColorKey newColorKey = new GradientColorKey(colorAtTime, normalizedTime);

            List<GradientColorKey> colorKeys = track.ColorGradient.colorKeys.ToList();
            colorKeys.Add(newColorKey);
            colorKeys.Sort((a, b) => a.time.CompareTo(b.time));
            track.ColorGradient.colorKeys = colorKeys.ToArray();

            int newKeyIndex = colorKeys.FindIndex(k => Mathf.Approximately(k.time, normalizedTime));
            if (newKeyIndex != -1)
            {
                state.SelectedGradientKey = (track.TrackIndex, newKeyIndex, GradientKeyType.Color);
                state.CurrentGradientEditMode = GradientEditMode.ClickedColorKey;
            }
        }
        #endregion

        #region 辅助方法：检测点击是否在渐变区域内
        private bool IsPointOverGradientArea(Rect gradientDrawRect, Vector2 mousePos)
        {
            Rect hitRect = new Rect(
                gradientDrawRect.x - 2,
                gradientDrawRect.y - 2,
                gradientDrawRect.width + 4,
                gradientDrawRect.height + 4
            );
            return hitRect.Contains(mousePos);
        }
        #endregion

        #region 辅助方法：检测点击是否在关键帧上
        private bool IsPointOverGradientKey(Vector2 point, float keyTime, Rect gradientDrawRect, bool isAlpha, out Vector2 keyScreenPos)
        {
            const float MARKER_SIZE = 8f;
            const float MARGIN = 4f;

            float keyX = gradientDrawRect.x + keyTime * gradientDrawRect.width;
            float pickY = isAlpha
                ? gradientDrawRect.y - MARGIN - MARKER_SIZE
                : gradientDrawRect.yMax + MARGIN;

            // 赋值关键帧的屏幕坐标（用于后续距离判断）
            keyScreenPos = new Vector2(keyX, pickY + MARKER_SIZE / 2f);

            Rect pickRect = new Rect(
                keyX - KEYFRAME_CLICK_RADIUS,
                pickY,
                KEYFRAME_CLICK_RADIUS * 2,
                MARKER_SIZE + 4
            );

            return pickRect.Contains(point);
        }
        #endregion

        #region 核心实现：关键帧横向拖拽处理
        private bool HandleMouseDrag(Rect fullCurveRect, Vector2 delta)
        {
            if (!draggingKeyData.HasValue) return false;

            var data = draggingKeyData.Value;
            var track = _trackManager.GetTracks().Find(t => t.TrackIndex == data.trackIndex);
            if (track == null || track.ColorGradient == null) return false;

            // 计算拖拽距离，超过阈值才准备启动拖拽
            float dragDistance = Vector2.Distance(Event.current.mousePosition, data.startPos);

            if (dragDistance > DRAG_ACTIVATION_THRESHOLD && !isDragInitiated)
            {
                // ========== 新增核心判断：仅在拖拽启动前，检查鼠标与关键帧的初始距离 ==========
                float distanceToKey = Vector2.Distance(Event.current.mousePosition, data.keyScreenPos);
                if (distanceToKey > KEYFRAME_DRAG_MAX_DISTANCE)
                {
                    // 距离过远，直接重置拖拽数据，不启动拖拽
                    draggingKeyData = null;
                    return false;
                }
                
                EditorUtility.SetDirty(_activePreset);

                // 距离合规，标记拖拽启动，切换状态
                isDragInitiated = true;
                if (state.CurrentGradientEditMode == GradientEditMode.ClickedColorKey)
                {
                    state.CurrentGradientEditMode = GradientEditMode.MovingColorKey;
                }
                else if (state.CurrentGradientEditMode == GradientEditMode.ClickedAlphaKey)
                {
                    state.CurrentGradientEditMode = GradientEditMode.MovingAlphaKey;
                }
            }

            // 拖拽未启动，直接返回
            if (!isDragInitiated) return false;

            // ========== 拖拽过程中：不再进行任何距离判断，直接执行拖拽逻辑 ==========
            float gradientHeight = fullCurveRect.height * 0.5f;
            Rect gradientDrawRect = new Rect(
                fullCurveRect.x,
                fullCurveRect.y + (fullCurveRect.height - gradientHeight) * 0.5f,
                fullCurveRect.width,
                gradientHeight
            );

            float mouseX = Event.current.mousePosition.x;
            float startX = data.startPos.x;
            float xDelta = mouseX - startX;
            float timeDelta = xDelta / gradientDrawRect.width;
            float newTime = Mathf.Clamp01(data.originalKey.time + timeDelta);

            if (data.originalKey.type == GradientKeyType.Color)
            {
                UpdateColorKeyTime(track, data.keyIndex, newTime);
            }
            else
            {
                UpdateAlphaKeyTime(track, data.keyIndex, newTime);
            }

            return true;
        }

        /// <summary>
        /// 更新颜色关键帧的时间（仅修改time，保留color不变）
        /// </summary>
        private void UpdateColorKeyTime(TrackInfo track, int keyIndex, float newTime)
        {
            // 先判断拖拽数据是否存在，避免空引用
            if (!draggingKeyData.HasValue) return;

            List<GradientColorKey> colorKeys = track.ColorGradient.colorKeys.ToList();

            if (keyIndex < 0 || keyIndex >= colorKeys.Count) return;

            Color originalColor = colorKeys[keyIndex].color;
            colorKeys[keyIndex] = new GradientColorKey(originalColor, newTime);
            colorKeys.Sort((a, b) => a.time.CompareTo(b.time));
            track.ColorGradient.colorKeys = colorKeys.ToArray();

            int newIndex = colorKeys.FindIndex(k =>
                Mathf.Approximately(k.time, newTime) && k.color == originalColor);
            if (newIndex != -1)
            {
                var currentDragData = draggingKeyData.Value;
                state.SelectedGradientKey = (track.TrackIndex, newIndex, GradientKeyType.Color);
                draggingKeyData = (
                    currentDragData.trackIndex,
                    newIndex,
                    currentDragData.originalKey,
                    currentDragData.startPos,
                    currentDragData.keyScreenPos
                );
            }

            //Debug.Log($"[Gradient] Moved Color Key to time: {newTime:F3}");
        }

        /// <summary>
        /// 更新透明度关键帧的时间（仅修改time，保留alpha不变）
        /// </summary>
        private void UpdateAlphaKeyTime(TrackInfo track, int keyIndex, float newTime)
        {
            // 先判断拖拽数据是否存在，避免空引用
            if (!draggingKeyData.HasValue) return;

            List<GradientAlphaKey> alphaKeys = track.ColorGradient.alphaKeys.ToList();

            if (keyIndex < 0 || keyIndex >= alphaKeys.Count) return;

            float originalAlpha = alphaKeys[keyIndex].alpha;
            alphaKeys[keyIndex] = new GradientAlphaKey(originalAlpha, newTime);
            alphaKeys.Sort((a, b) => a.time.CompareTo(b.time));
            track.ColorGradient.alphaKeys = alphaKeys.ToArray();

            int newIndex = alphaKeys.FindIndex(k =>
                Mathf.Approximately(k.time, newTime) && Mathf.Approximately(k.alpha, originalAlpha));
            if (newIndex != -1)
            {
                var currentDragData = draggingKeyData.Value;
                state.SelectedGradientKey = (track.TrackIndex, newIndex, GradientKeyType.Alpha);
                draggingKeyData = (
                    currentDragData.trackIndex,
                    newIndex,
                    currentDragData.originalKey,
                    currentDragData.startPos,
                    currentDragData.keyScreenPos
                );
            }

            //Debug.Log($"[Gradient] Moved Alpha Key to time: {newTime:F3}");
        }
        #endregion

        private void HandleMouseUp()
        {
            isDragInitiated = false;
            if (state.CurrentGradientEditMode == GradientEditMode.MovingColorKey)
            {
                state.CurrentGradientEditMode = GradientEditMode.ClickedColorKey;
            }
            else if (state.CurrentGradientEditMode == GradientEditMode.MovingAlphaKey)
            {
                state.CurrentGradientEditMode = GradientEditMode.ClickedAlphaKey;
            }
            draggingKeyData = null;
        }

        #region 核心修改：删除关键帧（仅保留至少1个关键帧的限制）
        private bool HandleKeyDelete()
        {
            if (state.SelectedGradientKey == null)
            {
                //Debug.Log("[Gradient] No gradient key selected for deletion");
                return false;
            }

            var (trackIndex, keyIndex, keyType) = state.SelectedGradientKey.Value;
            var track = _trackManager.GetTracks().Find(t => t.TrackIndex == trackIndex);

            if (track == null || track.ColorGradient == null)
            {
                //Debug.Log("[Gradient] Track or gradient is null, cannot delete key");
                return false;
            }
            
            EditorUtility.SetDirty(_activePreset);

            bool deleted = false;
            if (keyType == GradientKeyType.Color)
            {
                var colorKeys = track.ColorGradient.colorKeys.ToList();
                if (colorKeys.Count <= 1)
                {
                    //Debug.Log("[Gradient] Cannot delete key: At least 1 color key is required");
                    return false;
                }

                colorKeys.RemoveAt(keyIndex);
                track.ColorGradient.colorKeys = colorKeys.ToArray();
                deleted = true;
                //Debug.Log($"[Gradient] Deleted color key at index {keyIndex}, remaining keys: {colorKeys.Count}");
            }
            else
            {
                var alphaKeys = track.ColorGradient.alphaKeys.ToList();
                if (alphaKeys.Count <= 1)
                {
                    //Debug.Log("[Gradient] Cannot delete key: At least 1 alpha key is required");
                    return false;
                }

                alphaKeys.RemoveAt(keyIndex);
                track.ColorGradient.alphaKeys = alphaKeys.ToArray();
                deleted = true;
                //Debug.Log($"[Gradient] Deleted alpha key at index {keyIndex}, remaining keys: {alphaKeys.Count}");
            }

            if (deleted)
            {
                ResetGradientEditState();
            }

            return deleted;
        }
        #endregion

        #region 工具方法
        private void ResetGradientEditState()
        {
            state.CurrentGradientEditMode = GradientEditMode.None;
            state.SelectedGradientKey = null;
            draggingKeyData = null;
            isDragInitiated = false;
        }
        #endregion
    }
}