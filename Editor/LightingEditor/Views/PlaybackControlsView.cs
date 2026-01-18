using System;
using UnityEngine;
using UnityEditor;

namespace NeuroTODEditor
{
    /// <summary>
    /// 播放控制视图
    /// 包含播放/暂停、跳转、步进等控制按钮
    /// </summary>
    public class PlaybackControlsView
    {
        // ========== 常量 ==========
        private const float BUTTON_SIZE = 24f;
        private const float BUTTON_SPACING = 2f;
        private const float SLIDER_MIN_WIDTH = 100f;

        // ========== 状态 ==========
        private PlaybackData playbackData;
        private float currentTime;
        private ViewLevel viewLevel;

        // ========== 事件 ==========
        public event Action OnPlayForward;
        public event Action OnPlayBackward;
        public event Action OnStop;
        public event Action OnJumpToStart;
        public event Action OnJumpToEnd;
        public event Action OnStepForward;
        public event Action OnStepBackward;
        public event Action OnJumpToPreviousKey;
        public event Action OnJumpToNextKey;
        public event Action<float> OnTimeChanged;
        public event Action<bool> OnLoopChanged;

        public PlaybackControlsView()
        {
            playbackData = new PlaybackData();
        }

        /// <summary>
        /// 设置播放数据
        /// </summary>
        public void SetPlaybackData(PlaybackData data)
        {
            playbackData = data ?? new PlaybackData();
        }

        /// <summary>
        /// 设置当前时间
        /// </summary>
        public void SetCurrentTime(float time)
        {
            currentTime = Mathf.Clamp01(time);
        }

        /// <summary>
        /// 设置视图级别
        /// </summary>
        public void SetViewLevel(ViewLevel level)
        {
            viewLevel = level;
        }

        /// <summary>
        /// 绘制播放控制条
        /// </summary>
        public void Draw(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));

            float currentX = rect.x + 4;
            float buttonY = rect.y + (rect.height - BUTTON_SIZE) / 2;

            // Level 3: 显示完整控制
            if (viewLevel == ViewLevel.Level3_CurveEditor)
            {
                // 跳转到开头
                if (DrawButton(new Rect(currentX, buttonY, BUTTON_SIZE, BUTTON_SIZE), "Animation.FirstKey", "Jump to Start"))
                {
                    OnJumpToStart?.Invoke();
                }
                currentX += BUTTON_SIZE + BUTTON_SPACING;

                // 上一关键帧
                if (DrawButton(new Rect(currentX, buttonY, BUTTON_SIZE, BUTTON_SIZE), "Animation.PrevKey", "Previous Keyframe"))
                {
                    OnJumpToPreviousKey?.Invoke();
                }
                currentX += BUTTON_SIZE + BUTTON_SPACING;

                // 后退一步
                if (DrawButton(new Rect(currentX, buttonY, BUTTON_SIZE, BUTTON_SIZE), "Animation.StepBack", "Step Backward"))
                {
                    OnStepBackward?.Invoke();
                }
                currentX += BUTTON_SIZE + BUTTON_SPACING;
            }

            // 后退播放
            string backIcon = playbackData.State == PlaybackState.PlayingBackward ? "PauseButton" : "Animation.PlayBack";
            if (DrawButton(new Rect(currentX, buttonY, BUTTON_SIZE, BUTTON_SIZE), backIcon, "Play Backward"))
            {
                if (playbackData.State == PlaybackState.PlayingBackward)
                    OnStop?.Invoke();
                else
                    OnPlayBackward?.Invoke();
            }
            currentX += BUTTON_SIZE + BUTTON_SPACING;

            // 前进播放
            string playIcon = playbackData.State == PlaybackState.PlayingForward ? "PauseButton" : "Animation.Play";
            if (DrawButton(new Rect(currentX, buttonY, BUTTON_SIZE, BUTTON_SIZE), playIcon, "Play Forward"))
            {
                if (playbackData.State == PlaybackState.PlayingForward)
                    OnStop?.Invoke();
                else
                    OnPlayForward?.Invoke();
            }
            currentX += BUTTON_SIZE + BUTTON_SPACING;

            if (viewLevel == ViewLevel.Level3_CurveEditor)
            {
                // 前进一步
                if (DrawButton(new Rect(currentX, buttonY, BUTTON_SIZE, BUTTON_SIZE), "Animation.StepFwd", "Step Forward"))
                {
                    OnStepForward?.Invoke();
                }
                currentX += BUTTON_SIZE + BUTTON_SPACING;

                // 下一关键帧
                if (DrawButton(new Rect(currentX, buttonY, BUTTON_SIZE, BUTTON_SIZE), "Animation.NextKey", "Next Keyframe"))
                {
                    OnJumpToNextKey?.Invoke();
                }
                currentX += BUTTON_SIZE + BUTTON_SPACING;

                // 跳转到结尾
                if (DrawButton(new Rect(currentX, buttonY, BUTTON_SIZE, BUTTON_SIZE), "Animation.LastKey", "Jump to End"))
                {
                    OnJumpToEnd?.Invoke();
                }
                currentX += BUTTON_SIZE + BUTTON_SPACING;

                // 循环按钮
                currentX += 8;
                Color loopColor = playbackData.IsLooping ? new Color(0.3f, 0.6f, 1f) : Color.white;
                GUI.color = loopColor;
                if (DrawButton(new Rect(currentX, buttonY, BUTTON_SIZE, BUTTON_SIZE), "Animation.LoopOn", "Toggle Loop"))
                {
                    playbackData.IsLooping = !playbackData.IsLooping;
                    OnLoopChanged?.Invoke(playbackData.IsLooping);
                }
                GUI.color = Color.white;
                currentX += BUTTON_SIZE + BUTTON_SPACING;
            }

            // 时间滑块
            currentX += 8;
            float sliderWidth = rect.xMax - currentX - 80;
            if (sliderWidth > SLIDER_MIN_WIDTH)
            {
                Rect sliderRect = new Rect(currentX, buttonY + 4, sliderWidth, BUTTON_SIZE - 8);
                EditorGUI.BeginChangeCheck();
                float newTime = GUI.HorizontalSlider(sliderRect, currentTime, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    OnTimeChanged?.Invoke(newTime);
                }
                currentX += sliderWidth + 8;
            }

            // 时间显示
            Rect timeRect = new Rect(rect.xMax - 70, buttonY, 66, BUTTON_SIZE);
            string timeText = FormatTime(currentTime);
            GUI.Label(timeRect, timeText, EditorStyles.label);
        }

        private bool DrawButton(Rect rect, string iconName, string tooltip)
        {
            GUIContent content = EditorGUIUtility.IconContent(iconName);
            content.tooltip = tooltip;
            return GUI.Button(rect, content, EditorStyles.toolbarButton);
        }

        private string FormatTime(float normalizedTime)
        {
            float hours = normalizedTime * 24f;
            int h = Mathf.FloorToInt(hours);
            int m = Mathf.FloorToInt((hours - h) * 60f);
            return $"{h:D2}:{m:D2}";
        }

        /// <summary>
        /// 绘制紧凑版播放控制（用于 Level 1/2）
        /// </summary>
        public void DrawCompact(Rect rect)
        {
            float currentX = rect.x + 4;
            float buttonY = rect.y + (rect.height - BUTTON_SIZE) / 2;

            // 后退播放
            string backIcon = playbackData.State == PlaybackState.PlayingBackward ? "PauseButton" : "Animation.PlayBack";
            if (DrawButton(new Rect(currentX, buttonY, BUTTON_SIZE, BUTTON_SIZE), backIcon, "Play Backward"))
            {
                if (playbackData.State == PlaybackState.PlayingBackward)
                    OnStop?.Invoke();
                else
                    OnPlayBackward?.Invoke();
            }
            currentX += BUTTON_SIZE + BUTTON_SPACING;

            // 前进播放
            string playIcon = playbackData.State == PlaybackState.PlayingForward ? "PauseButton" : "Animation.Play";
            if (DrawButton(new Rect(currentX, buttonY, BUTTON_SIZE, BUTTON_SIZE), playIcon, "Play Forward"))
            {
                if (playbackData.State == PlaybackState.PlayingForward)
                    OnStop?.Invoke();
                else
                    OnPlayForward?.Invoke();
            }
            currentX += BUTTON_SIZE + 16;

            // 时间滑块
            float sliderWidth = rect.xMax - currentX - 70;
            if (sliderWidth > SLIDER_MIN_WIDTH)
            {
                Rect sliderRect = new Rect(currentX, buttonY + 4, sliderWidth, BUTTON_SIZE - 8);
                EditorGUI.BeginChangeCheck();
                float newTime = GUI.HorizontalSlider(sliderRect, currentTime, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    OnTimeChanged?.Invoke(newTime);
                }
            }

            // 时间显示
            Rect timeRect = new Rect(rect.xMax - 66, buttonY, 62, BUTTON_SIZE);
            string timeText = FormatTime(currentTime);
            GUI.Label(timeRect, timeText, EditorStyles.label);
        }
    }
}
