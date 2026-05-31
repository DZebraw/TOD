using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using DawnTODEditor;
using DawnTOD;
using UnityEditor;

/// <summary>
/// 曲线编辑器交互处理器
/// 负责处理曲线区域的所有鼠标点击、拖拽和逻辑判断
/// </summary>
public class CurveInteractionHandler
{
    private LightingEditorState state;
    private TrackManager _trackManager;
    private DawnWeatherPreset _activePreset;

    private enum DragTargetType
    {
        None,
        Keyframe,
        Tangent // 切线手柄
    }

    private (
        int trackIndex,
        int keyIndex,
        Keyframe[] originalKeys,
        Vector2 startPos,
        Vector2 startKeyScreenPos,
        float startInTangent,
        float startOutTangent,
        DragTargetType dragTarget
    )? draggingData;

    private const float DRAG_ACTIVATION_THRESHOLD = 8f;
    private bool isDragInitiated = false;

    public CurveInteractionHandler(LightingEditorState state, TrackManager trackManager)
    {
        this.state = state;
        this._trackManager = trackManager;
    }
    
    public void UpdateActivePreset(DawnWeatherPreset activePreset)
    {
        this._activePreset = activePreset;
    }

    public bool HandleEvent(Rect rect, Event e)
    {
        if (!rect.Contains(e.mousePosition))
        {
            return false;
        }

        bool used = false;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            isDragInitiated = false;

            if (e.clickCount == 2)
            {
                used = HandleMouseDoubleClick(rect, e.mousePosition);
            }
            else if (e.clickCount == 1)
            {
                used = HandleMouseDown(rect, e.mousePosition);
            }
        }
        else if (e.type == EventType.MouseDrag)
        {
            used = HandleMouseDrag(rect, e.delta);
            if (used) e.Use();
        }
        else if (e.type == EventType.MouseUp)
        {
            HandleMouseUp();
            used = true;
        }
        else if (e.type == EventType.KeyDown)
        {
            if (state.CurrentCurveEditMode == CurveEditMode.ClickedKeyframe && state.SelectedKeyframe.HasValue)
            {
                if (e.keyCode == KeyCode.Delete)
                {
                    var (trackIndex, keyIndex) = state.SelectedKeyframe.Value;
                    var track = _trackManager.GetTracks().Find(t => t.TrackIndex == trackIndex);

                    if (track != null && track.FloatCurve != null)
                    {
                        if (keyIndex != 0 && keyIndex != track.FloatCurve.length - 1)
                        {
                            EditorUtility.SetDirty(_activePreset);
                            
                            track.FloatCurve.RemoveKey(keyIndex);
                            state.CurrentCurveEditMode = CurveEditMode.None;
                            state.SelectedKeyframe = null;
                            draggingData = null;

                            used = true;
                            e.Use();
                        }
                    }
                }
            }
        }

        return used;
    }

    private bool HandleMouseDoubleClick(Rect rect, Vector2 mousePos)
    {
        if (state.SelectedTrackIndices.Count != 1) return false;

        int selectedIdx = state.SelectedTrackIndices.First();
        var selectedTrack = _trackManager.GetTracks().Find(t => t.TrackIndex == selectedIdx);

        if (selectedTrack == null || selectedTrack.FloatCurve == null)
            return false;

        AnimationCurve curve = selectedTrack.FloatCurve;

        var (minVal, maxVal, range) = GetCurveViewRange(curve);

        if (IsPositionOverKeyframes(curve, rect, minVal, maxVal, mousePos))
        {
            return false;
        }

        if (IsPositionOverCurve(curve, rect, minVal, maxVal, mousePos))
        {
            AddKeyframeToCurve(selectedTrack, rect, minVal, maxVal, mousePos);
            return true;
        }

        return false;
    }

    private bool IsPositionOverKeyframes(AnimationCurve curve, Rect curveRect, float minVal, float maxVal, Vector2 mousePos)
    {
        const float KEYFRAME_CLICK_RADIUS = 8f;

        foreach (var key in curve.keys)
        {
            Vector2 keyScreenPos = GetKeyframeScreenPosition(key, curveRect, minVal, maxVal);
            if (Vector2.Distance(mousePos, keyScreenPos) <= KEYFRAME_CLICK_RADIUS)
            {
                return true;
            }
        }
        return false;
    }

    private bool IsPositionOverCurve(AnimationCurve curve, Rect rect, float minVal, float maxVal, Vector2 mousePos)
    {
        const int sampleCount = 256;
        const float CURVE_HIT_TOLERANCE = 10f;

        float range = maxVal - minVal;
        if (range < 0.001f) range = 1f;

        Vector2 prevPoint = Vector2.zero;

        for (int i = 0; i <= sampleCount; i++)
        {
            float t = i / (float)sampleCount;
            float value = curve.Evaluate(t);

            float normalizedValue = (value - minVal) / range;
            float x = rect.x + t * rect.width;
            float y = rect.yMax - normalizedValue * rect.height;

            Vector2 currentPoint = new Vector2(x, y);

            if (i > 0)
            {
                float distance = DistancePointToLineSegment(mousePos, prevPoint, currentPoint);
                if (distance <= CURVE_HIT_TOLERANCE)
                {
                    return true;
                }
            }
            prevPoint = currentPoint;
        }
        return false;
    }

    private float DistancePointToLineSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        Vector2 ap = p - a;

        float t = Vector2.Dot(ap, ab) / Vector2.Dot(ab, ab);
        t = Mathf.Clamp01(t);

        Vector2 closest = a + t * ab;
        return Vector2.Distance(p, closest);
    }

    private void AddKeyframeToCurve(TrackInfo track, Rect rect, float minVal, float maxVal, Vector2 mousePos)
    {
        AnimationCurve curve = track.FloatCurve;
        
        EditorUtility.SetDirty(_activePreset);

        float time = Mathf.InverseLerp(rect.x, rect.x + rect.width, mousePos.x);
        float normalizedY = 1f - Mathf.InverseLerp(rect.y, rect.y + rect.height, mousePos.y);
        float value = Mathf.Lerp(minVal, maxVal, normalizedY);

        Keyframe newKey = new Keyframe(time, value);
        int keyIndex = curve.AddKey(newKey);

        if (keyIndex != -1)
        {
            curve.SmoothTangents(keyIndex, 0);
            state.CurrentCurveEditMode = CurveEditMode.ClickedKeyframe;
            state.SelectedKeyframe = (track.TrackIndex, keyIndex);

            Vector2 keyScreenPos = GetKeyframeScreenPosition(newKey, rect, minVal, maxVal);
            draggingData = (
                track.TrackIndex,
                keyIndex,
                (Keyframe[])curve.keys.Clone(),
                mousePos,
                keyScreenPos,
                newKey.inTangent,
                newKey.outTangent,
                DragTargetType.Keyframe
            );
        }
    }

    private bool HandleMouseDown(Rect rect, Vector2 mousePos)
    {
        if (state.SelectedTrackIndices.Count != 1) return false;

        int selectedIdx = state.SelectedTrackIndices.First();
        var selectedTrack = _trackManager.GetTracks().Find(t => t.TrackIndex == selectedIdx);

        if (selectedTrack == null || selectedTrack.FloatCurve == null || selectedTrack.FloatCurve.length < 2)
            return false;

        var (minVal, maxVal, range) = GetCurveViewRange(selectedTrack.FloatCurve);
        return OnCurveClickedOrDragged(selectedTrack, rect, minVal, maxVal, mousePos);
    }

    private bool OnCurveClickedOrDragged(TrackInfo track, Rect curveRect, float minVal, float maxVal, Vector2 mousePos)
    {
        const float KEYFRAME_CLICK_RADIUS = 8f;
        const float TANGENT_HANDLER_CLICK_RADIUS = 8f;

        for (int i = 0; i < track.FloatCurve.keys.Length; i++)
        {
            var key = track.FloatCurve.keys[i];
            Vector2 keyScreenPos = GetKeyframeScreenPosition(key, curveRect, minVal, maxVal);

            // 点击关键帧
            if (Vector2.Distance(mousePos, keyScreenPos) <= KEYFRAME_CLICK_RADIUS)
            {
                if (Event.current.type == EventType.MouseDown)
                {
                    draggingData = (
                        track.TrackIndex,
                        i,
                        (Keyframe[])track.FloatCurve.keys.Clone(),
                        mousePos,
                        keyScreenPos,
                        key.inTangent,
                        key.outTangent,
                        DragTargetType.Keyframe
                    );

                    state.CurrentCurveEditMode = CurveEditMode.ClickedKeyframe;
                    state.SelectedKeyframe = (track.TrackIndex, i);

                    Event.current.Use();
                    return true;
                }
            }

            // 点击切线手柄
            if (state.SelectedKeyframe.HasValue &&
                state.SelectedKeyframe.Value.trackIndex == track.TrackIndex &&
                state.SelectedKeyframe.Value.keyIndex == i)
            {
                Vector2 outHandlePos = CalculateTangentHandlePosition(
                    keyScreenPos,
                    key.outTangent,
                    TANGENT_HANDLER_LENGTH_PX,
                    curveRect,
                    minVal,
                    maxVal,
                    isOut: true);

                Vector2 inHandlePos = CalculateTangentHandlePosition(
                    keyScreenPos,
                    key.inTangent,
                    TANGENT_HANDLER_LENGTH_PX,
                    curveRect,
                    minVal,
                    maxVal,
                    isOut: false);

                // 右侧切线
                if (Vector2.Distance(mousePos, outHandlePos) <= TANGENT_HANDLER_CLICK_RADIUS)
                {
                    if (Event.current.type == EventType.MouseDown)
                    {
                        draggingData = (
                            track.TrackIndex,
                            i,
                            (Keyframe[])track.FloatCurve.keys.Clone(),
                            mousePos,
                            keyScreenPos,
                            key.inTangent,
                            key.outTangent,
                            DragTargetType.Tangent
                        );

                        state.CurrentCurveEditMode = CurveEditMode.ClickedTangent;
                        Event.current.Use();
                        return true;
                    }
                }

                // 左侧切线
                if (Vector2.Distance(mousePos, inHandlePos) <= TANGENT_HANDLER_CLICK_RADIUS)
                {
                    if (Event.current.type == EventType.MouseDown)
                    {
                        draggingData = (
                            track.TrackIndex,
                            i,
                            (Keyframe[])track.FloatCurve.keys.Clone(),
                            mousePos,
                            keyScreenPos,
                            key.inTangent,
                            key.outTangent,
                            DragTargetType.Tangent
                        );

                        state.CurrentCurveEditMode = CurveEditMode.ClickedTangent;
                        Event.current.Use();
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private bool HandleMouseDrag(Rect rect, Vector2 delta)
    {
        if (!draggingData.HasValue) return false;

        var data = draggingData.Value;
        var track = _trackManager.GetTracks().Find(t => t.TrackIndex == data.trackIndex);
        if (track == null || track.FloatCurve == null) return false;

        AnimationCurve curve = track.FloatCurve;
        int currentKeyIndex = data.keyIndex;

        if (currentKeyIndex >= curve.keys.Length)
        {
            currentKeyIndex = FindKeyIndexByTime(curve, data.originalKeys[data.keyIndex].time);
            if (currentKeyIndex == -1) return false;
        }

        Keyframe currentKey = curve.keys[currentKeyIndex];
        var (minVal, maxVal, range) = GetCurveViewRange(curve);

        // 初始化拖拽状态
        if (!isDragInitiated)
        {
            float dragDistance = Vector2.Distance(Event.current.mousePosition, data.startPos);
            if (dragDistance > DRAG_ACTIVATION_THRESHOLD)
            {
                return false;
            }
            
            EditorUtility.SetDirty(_activePreset);
            
            isDragInitiated = true;

            if (data.dragTarget == DragTargetType.Keyframe)
            {
                state.CurrentCurveEditMode = CurveEditMode.MovingKeyframe;
                state.IsDraggingKeyframe = true;
            }
            else if (data.dragTarget == DragTargetType.Tangent)
            {
                state.IsDraggingKeyframe = false;
            }
        }

        // 关键帧拖拽逻辑
        if (data.dragTarget == DragTargetType.Keyframe && state.CurrentCurveEditMode == CurveEditMode.MovingKeyframe)
        {
            float normalizedMouseY = (Event.current.mousePosition.y - rect.yMin) / rect.height;
            float clampedY = 1f - normalizedMouseY;
            float clampedNewValue = Mathf.Lerp(minVal, maxVal, clampedY);
            float normalizedMouseX = (Event.current.mousePosition.x - rect.xMin) / rect.width;
            float clampedNewTime = Mathf.Clamp01(normalizedMouseX);

            bool isEdgeKey = (currentKeyIndex == 0 || currentKeyIndex == curve.length - 1);

            if (isEdgeKey)
            {
                Keyframe newKey = new Keyframe(
                    currentKey.time,
                    clampedNewValue,
                    currentKey.inTangent,
                    currentKey.outTangent
                );
                curve.MoveKey(currentKeyIndex, newKey);
            }
            else
            {
                Keyframe newKey = new Keyframe(clampedNewTime, clampedNewValue, currentKey.inTangent, currentKey.outTangent);
                curve.MoveKey(currentKeyIndex, newKey);

                int newIndex = FindKeyIndexByTime(curve, clampedNewTime);
                if (newIndex != -1)
                {
                    state.SelectedKeyframe = (track.TrackIndex, newIndex);
                    draggingData = (
                        data.trackIndex,
                        newIndex,
                        (Keyframe[])curve.keys.Clone(),
                        Event.current.mousePosition,
                        data.startKeyScreenPos,
                        currentKey.inTangent,
                        currentKey.outTangent,
                        DragTargetType.Keyframe
                    );
                }
            }

            return true;
        }

        if (data.dragTarget == DragTargetType.Tangent && state.CurrentCurveEditMode == CurveEditMode.ClickedTangent)
        {
            Vector2 currentMousePos = Event.current.mousePosition;
            Vector2 keyScreenPos = data.startKeyScreenPos;

            Vector2 mouseOffset = currentMousePos - keyScreenPos;
            mouseOffset.y = -mouseOffset.y; // 修正Y轴方向

            float dataX = mouseOffset.x / rect.width;
            float dataY = (mouseOffset.y / rect.height) * range;

            float tangentSlope = 0f;
            if (Mathf.Abs(dataX) > 0.0001f)
            {
                tangentSlope = dataY / dataX;
            }

            Keyframe newKey = currentKey;
            newKey.inTangent = tangentSlope;   // 左侧切线斜率与右侧一致
            newKey.outTangent = tangentSlope;  // 右侧切线斜率跟随鼠标

            curve.MoveKey(currentKeyIndex, newKey);

            return true;
        }

        return false;
    }

    private int FindKeyIndexByTime(AnimationCurve curve, float time)
    {
        const float tolerance = 0.001f;
        for (int i = 0; i < curve.keys.Length; i++)
        {
            if (Mathf.Abs(curve.keys[i].time - time) < tolerance)
            {
                return i;
            }
        }
        return -1;
    }

    private void HandleMouseUp()
    {
        isDragInitiated = false;

        if (state.CurrentCurveEditMode == CurveEditMode.MovingKeyframe)
        {
            state.CurrentCurveEditMode = CurveEditMode.ClickedKeyframe;
            state.IsDraggingKeyframe = false;
        }
        else if (state.CurrentCurveEditMode == CurveEditMode.ClickedTangent)
        {
            state.CurrentCurveEditMode = CurveEditMode.ClickedKeyframe;
        }

        draggingData = null;
    }

    private Vector2 GetKeyframeScreenPosition(Keyframe key, Rect rect, float minVal, float maxVal)
    {
        float range = maxVal - minVal;
        if (range < 0.001f) range = 1f;

        float normalizedValue = (key.value - minVal) / range;
        float x = rect.x + key.time * rect.width;
        float y = rect.yMax - normalizedValue * rect.height;

        return new Vector2(x, y);
    }

    private (float min, float max, float range) GetCurveViewRange(AnimationCurve curve)
    {
        float minVal = float.MaxValue;
        float maxVal = float.MinValue;

        const int sampleCount = 256;
        for (int i = 0; i <= sampleCount; i++)
        {
            float t = i / (float)sampleCount;
            float v = curve.Evaluate(t);
            minVal = Mathf.Min(minVal, v);
            maxVal = Mathf.Max(maxVal, v);
        }

        if (Mathf.Abs(maxVal - minVal) < 0.0001f)
            maxVal = minVal + 1f;

        return (minVal, maxVal, maxVal - minVal);
    }

    private Vector2 CalculateTangentHandlePosition(
        Vector2 keyPos,
        float tangentSlope,
        float screenLength,
        Rect viewRect,
        float viewMin,
        float viewMax,
        bool isOut)
    {
        float dataDX = 0.05f;
        if (!isOut) dataDX = -dataDX;

        for (int i = 0; i < 5; i++)
        {
            float dataDY = tangentSlope * dataDX;

            float screenDX = dataDX * viewRect.width;
            float screenDY = -(dataDY * viewRect.height) / (viewMax - viewMin);

            float currentLength = Mathf.Sqrt(screenDX * screenDX + screenDY * screenDY);

            if (Mathf.Approximately(currentLength, screenLength) || currentLength < 0.01f)
            {
                return keyPos + new Vector2(screenDX, screenDY);
            }

            float scale = screenLength / currentLength;
            dataDX *= scale;
        }

        float finalDataDY = tangentSlope * dataDX;
        float finalScreenDX = dataDX * viewRect.width;
        float finalScreenDY = -(finalDataDY * viewRect.height) / (viewMax - viewMin);

        return keyPos + new Vector2(finalScreenDX, finalScreenDY);
    }

    private const float TANGENT_HANDLER_LENGTH_PX = 32f;
}