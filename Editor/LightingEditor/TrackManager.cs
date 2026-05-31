using System.Collections.Generic;
using UnityEngine;
using DawnTOD;
using System.Linq;
using System;

namespace DawnTODEditor
{
    public class TrackManager
    {
        private List<TrackInfo> _tracks = new List<TrackInfo>();

        /// <summary>
        /// 获取当前所有轨道
        /// </summary>
        public List<TrackInfo> GetTracks() => new List<TrackInfo>(_tracks);

        public event Action OnTracksRefreshed;

        /// <summary>
        /// 根据轨道索引获取单个轨道
        /// </summary>
        public TrackInfo GetTrackByIndex(int trackIndex)
        {
            return _tracks.FirstOrDefault(t => t.TrackIndex == trackIndex);
        }

        /// <summary>
        /// 获取指定类型的所有轨道
        /// </summary>
        public List<TrackInfo> GetTracksByType(TrackType trackType)
        {
            return _tracks.Where(t => t.Type == trackType).ToList();
        }

        /// <summary>
        /// 获取指定组的所有子轨道
        /// </summary>
        public List<TrackInfo> GetChildTracksByGroupIndex(int groupIndex)
        {
            return _tracks.Where(t => t.ParentIndex == groupIndex).ToList();
        }

        /// <summary>
        /// 根据激活的预设刷新轨道
        /// </summary>
        public void RefreshTracks(DawnWeatherPreset activePreset)
        {
            _tracks.Clear();
            if (activePreset == null)
            {
                // 通知数据变更
                OnTracksRefreshed?.Invoke();
                return;
            }

            int trackIndex = 0;

            CreateTrackGroup("Sun", BuiltinType.Sun, ref trackIndex,
                new[] {
                    ("Intensity", activePreset.sunIntensityCurve),
                    ("Azimuth", activePreset.sunAzimuthCurve),
                    ("Elevation", activePreset.sunElevationCurve)
                },
                new[] {
                    ("SunColor", activePreset.sunColorGradient)
                });

            CreateTrackGroup("Moon", BuiltinType.Moon, ref trackIndex,
                new[] {
                    ("Intensity", activePreset.moonIntensityCurve),
                    ("Azimuth", activePreset.moonAzimuthCurve),
                    ("Elevation", activePreset.moonElevationCurve)
                },
                new[] {
                    ("MoonColor", activePreset.moonColorGradient)
                });

            CreateTrackGroup("Sky", BuiltinType.SkyLight, ref trackIndex,
                new[] {
                    ("Star Emission", activePreset.starEmissionCurve)
                },
                Array.Empty<(string name, Gradient gradient)>()
                );

            CreateTrackGroup("Fog", BuiltinType.Fog, ref trackIndex,
                new[] {
                    ("Height", activePreset.fogHeightCurve),
                    ("Distance", activePreset.fogDistanceCurve)
                },
                new[] {
                    ("FogColor", activePreset.fogColorGradient)
                });

            CreateTrackGroup("Exposure",BuiltinType.Exposure,ref trackIndex,
                new[] {
                    ("Compensation",activePreset.exposureCompensationCurve),
                },
                Array.Empty<(string name, Gradient gradient)>()
                );

            // 通知数据变更
            OnTracksRefreshed?.Invoke();
        }

        /// <summary>
        /// 创建轨道组（复用原有逻辑）
        /// </summary>
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
            _tracks.Add(group);

            // 添加浮点曲线子轨道
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
                _tracks.Add(child);
            }

            // 添加渐变子轨道
            foreach (var (name, gradient) in gradientChildren)
            {
                if (gradient == null) continue;
                var child = new TrackInfo
                {
                    TrackIndex = trackIndex++,
                    DisplayName = name,
                    FullName = $"{groupName}.{name}",
                    Type = TrackType.ColorGradient,
                    BuiltinType = builtinType,
                    Depth = 1,
                    ParentIndex = groupIndex,
                    ColorGradient = gradient
                };
                group.ChildIndices.Add(child.TrackIndex);
                _tracks.Add(child);
            }
        }

        /// <summary>
        /// 检查轨道是否应该被绘制（根据父节点折叠状态，使用内部自有数据）
        /// </summary>
        public bool ShouldDrawTrack(TrackInfo track)
        {
            if (track.ParentIndex == -1) return true;

            int parentIdx = track.ParentIndex;
            while (parentIdx != -1)
            {
                TrackInfo parentTrack = _tracks.FirstOrDefault(t => t.TrackIndex == parentIdx);
                if (parentTrack == null || !parentTrack.IsExpanded)
                {
                    return false;
                }
                parentIdx = parentTrack.ParentIndex;
            }
            return true;
        }
    }
}