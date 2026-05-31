using System;
using System.Collections.Generic;
using UnityEngine;

namespace DawnTODEditor
{
    /// <summary>
    /// 视图级别枚举
    /// </summary>
    public enum ViewLevel
    {
        Level1_Playback = 0,    // 仅播放控制
        Level2_LightControl,    // + 灯光开关
        Level3_CurveEditor      // + 曲线编辑器
    }

    /// <summary>
    /// 编辑器模式枚举
    /// </summary>
    public enum EditorMode
    {
        Keyframes = 0,  // 关键帧视图
        Curves          // 曲线视图
    }
    
    /// <summary>
    /// 曲线编辑
    /// </summary>
    public enum CurveEditMode
    {
        None,
        ClickedKeyframe,
        MovingKeyframe,
        ClickedTangent,
        ClickedCurve,
    };

    public enum GradientEditMode
    {
        None,
        ClickedColorKey,
        MovingColorKey,
        ClickedAlphaKey,
        MovingAlphaKey,
        ClickedGradient
    }

    /// <summary>
    /// 时间显示模式枚举
    /// </summary>
    public enum TimeDisplayMode
    {
        Format24H = 0,  // 24小时制
        Format12H       // 12小时制
    }

    /// <summary>
    /// 轨道类型枚举
    /// </summary>
    public enum TrackType
    {
        Group,          // 分组轨道
        FloatCurve,     // 浮点曲线
        ColorGradient   // 颜色渐变
    }

    /// <summary>
    /// 内置组件类型枚举
    /// </summary>
    public enum BuiltinType
    {
        None = 0,
        Sun,
        Moon,
        SkyLight,
        Fog,
        Exposure
    }

    /// <summary>
    /// 关键帧句柄
    /// </summary>
    [Serializable]
    public struct KeyframeHandle : IEquatable<KeyframeHandle>
    {
        public int TrackIndex;
        public int KeyIndex;

        public KeyframeHandle(int trackIndex, int keyIndex)
        {
            TrackIndex = trackIndex;
            KeyIndex = keyIndex;
        }

        public bool Equals(KeyframeHandle other)
        {
            return TrackIndex == other.TrackIndex && KeyIndex == other.KeyIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is KeyframeHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(TrackIndex, KeyIndex);
        }

        public static bool operator ==(KeyframeHandle left, KeyframeHandle right) => left.Equals(right);
        public static bool operator !=(KeyframeHandle left, KeyframeHandle right) => !left.Equals(right);
    }
    
    /// <summary>
    /// 轨道信息
    /// </summary>
    [Serializable]
    public class TrackInfo
    {
        public int TrackIndex;
        public string DisplayName;
        public string FullName;
        public TrackType Type;
        public BuiltinType BuiltinType;
        public int Depth;
        public bool IsExpanded;

        // 曲线引用
        public AnimationCurve FloatCurve;
        public Gradient ColorGradient;

        // 父子关系
        public int ParentIndex;
        public List<int> ChildIndices;

        public TrackInfo()
        {
            ChildIndices = new List<int>();
            IsExpanded = true;
            ParentIndex = -1;
            Depth = 0;
        }

        public bool IsGroup => Type == TrackType.Group;
        public bool IsColorTrack => Type == TrackType.ColorGradient;
    }

    /// <summary>
    /// 播放状态
    /// </summary>
    public enum PlaybackState
    {
        Stopped,
        PlayingForward,
        PlayingBackward
    }

    /// <summary>
    /// 播放控制器数据
    /// </summary>
    [Serializable]
    public class PlaybackData
    {
        public PlaybackState State;
        public float PlaybackSpeed;
        public bool IsLooping;

        public PlaybackData()
        {
            State = PlaybackState.Stopped;
            PlaybackSpeed = 0.1f;
            IsLooping = true;
        }

        public bool IsPlaying => State != PlaybackState.Stopped;
        public int PlayDirection => State == PlaybackState.PlayingBackward ? -1 : 1;
    }

    /// <summary>
    /// 编辑器状态数据
    /// </summary>
    [Serializable]
    public class LightingEditorState
    {
        // 当前时间 (0-1 归一化)
        public float CurrentTime;

        // 视图范围
        public float ViewRangeMin;
        public float ViewRangeMax;

        // 视图级别
        public ViewLevel CurrentViewLevel;

        // 编辑器模式
        public EditorMode CurrentEditorMode;

        // 时间显示模式
        public TimeDisplayMode TimeDisplayMode;

        // 播放数据
        public PlaybackData Playback;

        // 选中的轨道索引
        public HashSet<int> SelectedTrackIndices;

        //是否正在拖拽关键帧
        public bool IsDraggingKeyframe;

        public bool IsDraggingColorKey;

        //EditorMode = Curves下的编辑状态
        public CurveEditMode CurrentCurveEditMode = CurveEditMode.None;

        public GradientEditMode CurrentGradientEditMode = GradientEditMode.None;

        // 当前选中的关键帧（track index + key index）
        public (int trackIndex, int keyIndex)? SelectedKeyframe = null;
        public (int trackIndex,int keyIndex,GradientKeyType gradientKeyType)? SelectedGradientKey { get; set; }

        public LightingEditorState()
        {
            CurrentTime = 0.5f;
            ViewRangeMin = 0f;
            ViewRangeMax = 1f;
            CurrentViewLevel = ViewLevel.Level3_CurveEditor;
            CurrentEditorMode = EditorMode.Keyframes;
            TimeDisplayMode = TimeDisplayMode.Format24H;
            Playback = new PlaybackData();
            SelectedTrackIndices = new HashSet<int>();
        }

        /// <summary>
        /// 获取格式化的时间字符串
        /// </summary>
        public string GetFormattedTime()
        {
            float hours = CurrentTime * 24f;
            int h = Mathf.FloorToInt(hours);
            int m = Mathf.FloorToInt((hours - h) * 60f);

            if (TimeDisplayMode == TimeDisplayMode.Format12H)
            {
                string ampm = h >= 12 ? "PM" : "AM";
                int h12 = h % 12;
                if (h12 == 0) h12 = 12;
                return $"{h12:D2}:{m:D2} {ampm}";
            }

            return $"{h:D2}:{m:D2}";
        }
    }
}
