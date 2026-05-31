using System;
using DawnTODEditor;
using UnityEditor;
using UnityEngine;

internal static class LightingEditorPlaybackBarDrawer
{
    public static void DrawPlaybackBar(
        LightingEditorState state,
        // 各种播放控制回调
        Action jumpToStart,
        Action stepBackward,
        Action togglePlayBackward,
        Action togglePlayForward,
        Action stepForward,
        Action jumpToEnd,
        Action<float> setCurrentTime)
    {
        Event evt = Event.current;
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(LightingEditorConstants.PLAYBACK_BAR_HEIGHT));
        {
            // Level 3 显示完整控制
            if (state.CurrentViewLevel == ViewLevel.Level3_CurveEditor)
            {
                // 跳转到开头
                if (GUILayout.Button(EditorGUIUtility.IconContent("Animation.FirstKey"), GUILayout.Width(28)))
                {
                    jumpToStart?.Invoke();
                    // 标记事件已使用，避免重复处理
                    evt.Use();
                }

                // 上一帧
                if (GUILayout.Button(EditorGUIUtility.IconContent("Animation.PrevKey"), GUILayout.Width(28)))
                {
                    stepBackward?.Invoke();
                    evt.Use();
                }
            }

            // 后退播放
            var backIcon = state.Playback.State == PlaybackState.PlayingBackward
                ? EditorGUIUtility.IconContent("PauseButton")
                : CustomIconUtility.Icon("LightingEditorPlayBack");
            if (GUILayout.Button(backIcon, GUILayout.Width(28)))
            {
                togglePlayBackward?.Invoke();
                evt.Use();
            }

            // 前进播放
            var playIcon = state.Playback.State == PlaybackState.PlayingForward
                ? EditorGUIUtility.IconContent("PauseButton")
                : CustomIconUtility.Icon("LightingEditorPlay");
            if (GUILayout.Button(playIcon, GUILayout.Width(28)))
            {
                togglePlayForward?.Invoke();
                evt.Use();
            }

            if (state.CurrentViewLevel == ViewLevel.Level3_CurveEditor)
            {
                // 下一帧
                if (GUILayout.Button(EditorGUIUtility.IconContent("Animation.NextKey"), GUILayout.Width(28)))
                {
                    stepForward?.Invoke();
                    evt.Use();
                }

                // 跳转到结尾
                if (GUILayout.Button(EditorGUIUtility.IconContent("Animation.LastKey"), GUILayout.Width(28)))
                {
                    jumpToEnd?.Invoke();
                    evt.Use();
                }
            }

            GUILayout.Space(8);

            // 时间滑块
            EditorGUI.BeginChangeCheck();
            float newTime = GUILayout.HorizontalSlider(state.CurrentTime, 0f, 1f, GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
            {
                setCurrentTime?.Invoke(newTime);
                evt.Use();
            }

            GUILayout.Space(8);

            // 时间显示
            GUILayout.Label(state.GetFormattedTime(), GUILayout.Width(60));
        }
        EditorGUILayout.EndHorizontal();
    }
}