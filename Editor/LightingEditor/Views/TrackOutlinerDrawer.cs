using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;

namespace DawnTODEditor
{
    public class TrackOutlinerDrawer : IStateAwareDrawer, IDisposable
    {
        public const float TIMELINE_RULER_HEIGHT = 24f;
        private const float TRACK_ITEM_HEIGHT = 24f;

        private LightingEditorState _state;
        private TrackManager _trackManager;
        private Action _onRepaint;

        public Vector2 _scrollPosition;
        private GUIStyle _trackLabelStyle;
        private bool _styleInitialized;

        public void Initialize(LightingEditorState state, TrackManager trackManager)
        {
            _state = state;
            _trackManager = trackManager;

            if (_trackManager != null)
            {
                _trackManager.OnTracksRefreshed += OnTracksRefreshed;
            }
        }

        public void RegisterCallbacks(Action onRepaint)
        {
            _onRepaint = onRepaint;
        }

        private void OnTracksRefreshed()
        {
            _scrollPosition = Vector2.zero;
            _onRepaint?.Invoke();
        }

        public void Dispose()
        {
            if (_trackManager != null)
            {
                _trackManager.OnTracksRefreshed -= OnTracksRefreshed;
            }
        }

        public void Draw(Rect drawRect)
        {
            InitStyleIfNeeded();

            GUI.Box(drawRect, GUIContent.none, EditorStyles.helpBox);

            Rect headerRect = new Rect(drawRect.x, drawRect.y, drawRect.width, TIMELINE_RULER_HEIGHT + 24);
            EditorGUI.DrawRect(headerRect, new Color(0.2f, 0.2f, 0.2f));
            GUI.Label(headerRect, "  TOD Components", EditorStyles.boldLabel);

            if (_trackManager == null) return;

            List<TrackInfo> visibleTracks = GetVisibleTracks();
            float contentHeight = visibleTracks.Count * TRACK_ITEM_HEIGHT;

            Rect listRect = new Rect(drawRect.x, drawRect.y + TIMELINE_RULER_HEIGHT + 24,
                drawRect.width, drawRect.height - TIMELINE_RULER_HEIGHT - 24);

            _scrollPosition = GUI.BeginScrollView(listRect, _scrollPosition,
                new Rect(0, 0, drawRect.width - 16, contentHeight));

            for (int i = 0; i < visibleTracks.Count; i++)
            {
                DrawTrackItem(new Rect(0, i * TRACK_ITEM_HEIGHT, drawRect.width - 16, TRACK_ITEM_HEIGHT),
                    visibleTracks[i]);
            }

            GUI.EndScrollView();
        }

        public bool HandleEvent(Rect drawRect, Event evt)
        {
            return false;
        }

        #region 内部辅助方法
        private void InitStyleIfNeeded()
        {
            if (_styleInitialized || _trackLabelStyle != null) return;

            if (EditorStyles.label == null)
            {
                _trackLabelStyle = GUI.skin.label;
            }
            else
            {
                _trackLabelStyle = new GUIStyle(EditorStyles.label);
            }

            _trackLabelStyle.alignment = TextAnchor.MiddleLeft;
            _trackLabelStyle.padding = new RectOffset(1, 1, 0, 0);

            _styleInitialized = true;
        }

        public List<TrackInfo> GetVisibleTracks()
        {
            List<TrackInfo> visibleTracks = new List<TrackInfo>();
            if (_trackManager == null) return visibleTracks;

            List<TrackInfo> allTracks = _trackManager.GetTracks();
            GetVisibleTracksRecursive(allTracks, visibleTracks);

            return visibleTracks;
        }


        private void GetVisibleTracksRecursive(List<TrackInfo> allTracks, List<TrackInfo> visibleTracks,
            int parentIndex = -1, bool parentIsExpanded = true)
        {
            foreach (var track in allTracks)
            {
                if (track.ParentIndex == parentIndex)
                {
                    if (parentIsExpanded)
                    {
                        visibleTracks.Add(track);
                        if (track.IsGroup && track.IsExpanded)
                        {
                            GetVisibleTracksRecursive(allTracks, visibleTracks, track.TrackIndex, true);
                        }
                        else if (track.IsGroup)
                        {
                            GetVisibleTracksRecursive(allTracks, visibleTracks, track.TrackIndex, false);
                        }
                    }
                    else if (track.IsGroup)
                    {
                        GetVisibleTracksRecursive(allTracks, visibleTracks, track.TrackIndex, false);
                    }
                }
            }
        }

        private void DrawTrackItem(Rect rect, TrackInfo track)
        {
            if (_state == null || _trackLabelStyle == null) return;

            bool isSelected = _state.SelectedTrackIndices.Contains(track.TrackIndex);

            if (isSelected)
            {
                EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.9f, 0.5f));
            }
            else if (track.IsGroup)
            {
                EditorGUI.DrawRect(rect, new Color(0.25f, 0.25f, 0.25f));
            }

            float indent = track.Depth * 16f;
            Rect labelRect = new Rect(rect.x + indent + 20, rect.y, rect.width - indent - 60, rect.height);

            if (track.IsGroup && track.ChildIndices.Count > 0)
            {
                Rect foldoutRect = new Rect(rect.x + indent, rect.y, 20, rect.height);
                track.IsExpanded = EditorGUI.Foldout(foldoutRect, track.IsExpanded, GUIContent.none);
            }

            GUI.Label(labelRect, track.DisplayName, _trackLabelStyle);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                HandleTrackSelection(track);
                Event.current.Use();
                _onRepaint?.Invoke();
            }
        }

        private void HandleTrackSelection(TrackInfo track)
        {
            if (_state == null || _trackManager == null) return;

            TrackInfo validTrack = _trackManager.GetTrackByIndex(track.TrackIndex);
            if (validTrack == null) return;

            if (Event.current.control || Event.current.command)
            {
                if (_state.SelectedTrackIndices.Contains(validTrack.TrackIndex))
                {
                    _state.SelectedTrackIndices.Remove(validTrack.TrackIndex);
                    ClearSelectedKeyframe();
                }
                else
                {
                    _state.SelectedTrackIndices.Add(validTrack.TrackIndex);
                    ClearSelectedKeyframe();
                }
            }
            else
            {
                if (!_state.SelectedTrackIndices.Contains(validTrack.TrackIndex))
                {
                    ClearSelectedKeyframe();
                }
                _state.SelectedTrackIndices.Clear();
                _state.SelectedTrackIndices.Add(validTrack.TrackIndex);
            }
        }

        private void ClearSelectedKeyframe()
        {
            if (_state == null) return;

            _state.SelectedKeyframe = null;
            _state.CurrentCurveEditMode = CurveEditMode.None;
            _state.SelectedGradientKey = null;
            _state.CurrentGradientEditMode = GradientEditMode.None;
        }
        #endregion
    }
}