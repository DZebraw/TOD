using System;
using System.Linq;
using DawnTOD;
using UnityEditor;
using UnityEngine;

namespace DawnTODEditor.AI
{
    internal readonly struct DawnTodAiPanelLayout
    {
        public readonly float Width;
        public readonly float Height;
        public readonly float ControllerFieldWidth;

        public DawnTodAiPanelLayout(
            float width,
            float height,
            float controllerFieldWidth)
        {
            Width = width;
            Height = height;
            ControllerFieldWidth = controllerFieldWidth;
        }
    }

    internal sealed class DawnTodAiPanel : IDisposable
    {
        private const string InputControlName = "DawnTOD.AI.Instruction";

        private readonly DawnTodAiPanelController _controller;
        private Vector2 _historyScroll;
        private string _input = string.Empty;
        private int _lastHistoryCount;
        private GUIStyle _wrappedLabel;
        private GUIStyle _inputStyle;
        private GUIStyle _rawJsonStyle;

        public bool HasActiveRequest => _controller.HasActiveRequest;

        public DawnTodAiPanel(
            IDawnTodAiServiceControl service,
            IDawnTodAiRequestCoordinator requestCoordinator,
            Action repaint)
        {
            _controller = new DawnTodAiPanelController(
                service,
                requestCoordinator,
                repaint);
        }

        public void Draw(
            Rect rect,
            DawnWeatherController selectedController,
            float capturedHour,
            Action<DawnWeatherController> onControllerSelected,
            Action pausePlayback,
            Action<DawnTodAiAnalyzeResult> onApplied)
        {
            EnsureStyles();
            DawnTodAiPanelLayout layout = CalculateLayout(rect);
            GUILayout.BeginArea(rect);
            EditorGUILayout.BeginVertical(
                GUILayout.Width(layout.Width),
                GUILayout.Height(layout.Height));
            DrawTargetAndService(
                selectedController,
                onControllerSelected,
                layout.ControllerFieldWidth);
            EditorGUILayout.Space(4f);
            DrawHistory();
            EditorGUILayout.Space(4f);
            DrawComposer(
                selectedController,
                capturedHour,
                pausePlayback,
                onApplied);
            EditorGUILayout.EndVertical();
            GUILayout.EndArea();
        }

        internal static DawnTodAiPanelLayout CalculateLayout(Rect rect)
        {
            float width = Mathf.Max(0f, rect.width);
            float height = Mathf.Max(0f, rect.height);
            float controllerFieldWidth = Mathf.Clamp(width * 0.42f, 150f, 280f);
            return new DawnTodAiPanelLayout(
                width,
                height,
                controllerFieldWidth);
        }

        public void Dispose()
        {
            _controller.Dispose();
        }

        internal static bool IsSendShortcut(Event currentEvent)
        {
            return currentEvent != null &&
                   currentEvent.type == EventType.KeyDown &&
                   (currentEvent.keyCode == KeyCode.Return ||
                    currentEvent.keyCode == KeyCode.KeypadEnter) &&
                   (currentEvent.control || currentEvent.command);
        }

        private void DrawTargetAndService(
            DawnWeatherController selectedController,
            Action<DawnWeatherController> onControllerSelected,
            float fieldWidth)
        {
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.ExpandWidth(true));
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Controller", GUILayout.Width(72f));
            EditorGUI.BeginDisabledGroup(_controller.HasActiveRequest);
            DrawControllerSelector(selectedController, onControllerSelected, fieldWidth);
            EditorGUI.EndDisabledGroup();
            GUILayout.Space(8f);
            GUILayout.Label("Preset", GUILayout.Width(42f));
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(
                selectedController != null ? selectedController.ActivePreset : null,
                typeof(DawnWeatherPreset),
                false,
                GUILayout.MinWidth(120f));
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Service", GUILayout.Width(72f));
            DrawServiceStatus();
            GUILayout.Label("Mode: " + DawnTodAiProtocol.Mode, EditorStyles.miniLabel, GUILayout.Width(104f));
            GUILayout.FlexibleSpace();
            DrawServiceButtons();
            EditorGUILayout.EndHorizontal();

            string serviceError = _controller.ServiceActionError ??
                                  _controller.Service.LastErrorMessage;
            if (!string.IsNullOrEmpty(serviceError))
            {
                EditorGUILayout.HelpBox(serviceError, MessageType.Error);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawControllerSelector(
            DawnWeatherController selectedController,
            Action<DawnWeatherController> onControllerSelected,
            float width)
        {
            string currentName = selectedController != null
                ? GetHierarchyPath(selectedController.transform)
                : "None";
            if (!GUILayout.Button(
                    new GUIContent(currentName, "Select the request target in the current scene."),
                    EditorStyles.popup,
                    GUILayout.Width(width)))
            {
                return;
            }

            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent("None"),
                selectedController == null,
                () => onControllerSelected?.Invoke(null));
            menu.AddSeparator(string.Empty);

            DawnWeatherController[] controllers = UnityEngine.Object
                .FindObjectsOfType<DawnWeatherController>(true)
                .Where(controller => controller != null &&
                                     controller.gameObject.scene.IsValid())
                .OrderBy(controller => GetHierarchyPath(controller.transform),
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (controllers.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("No Weather Controllers in the scene"));
            }
            else
            {
                foreach (DawnWeatherController controller in controllers)
                {
                    DawnWeatherController captured = controller;
                    menu.AddItem(
                        new GUIContent(GetHierarchyPath(captured.transform)),
                        captured == selectedController,
                        () => onControllerSelected?.Invoke(captured));
                }
            }

            menu.ShowAsContext();
        }

        private void DrawServiceStatus()
        {
            DawnTodAiServiceState state = _controller.Service.State;
            Rect markerRect = GUILayoutUtility.GetRect(
                12f,
                EditorGUIUtility.singleLineHeight,
                GUILayout.Width(12f));
            EditorGUI.DrawRect(
                new Rect(markerRect.x + 2f, markerRect.y + 5f, 8f, 8f),
                GetServiceStateColor(state));
            GUILayout.Label(state.ToString(), EditorStyles.boldLabel, GUILayout.Width(62f));
        }

        private void DrawServiceButtons()
        {
            DawnTodAiServiceState state = _controller.Service.State;
            bool busy = _controller.IsServiceOperationInFlight ||
                        state == DawnTodAiServiceState.Starting ||
                        state == DawnTodAiServiceState.Stopping;

            EditorGUI.BeginDisabledGroup(
                busy || (state != DawnTodAiServiceState.Stopped &&
                         state != DawnTodAiServiceState.Error));
            if (GUILayout.Button("Start", GUILayout.Width(52f), GUILayout.Height(22f)))
            {
                StartService();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(
                busy || (state != DawnTodAiServiceState.Ready &&
                         state != DawnTodAiServiceState.Error));
            if (GUILayout.Button("Stop", GUILayout.Width(52f), GUILayout.Height(22f)))
            {
                StopService();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(
                busy || (state != DawnTodAiServiceState.Ready &&
                         state != DawnTodAiServiceState.Error));
            if (GUILayout.Button("Restart", GUILayout.Width(58f), GUILayout.Height(22f)))
            {
                RestartService();
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Settings", GUILayout.Width(62f), GUILayout.Height(22f)))
            {
                DawnTodAiSettingsWindow.ShowWindow();
            }
        }

        private void DrawHistory()
        {
            EditorGUILayout.LabelField("Session History", EditorStyles.boldLabel);
            if (_controller.History.Count != _lastHistoryCount)
            {
                _lastHistoryCount = _controller.History.Count;
                _historyScroll.y = float.MaxValue;
            }

            _historyScroll = EditorGUILayout.BeginScrollView(
                _historyScroll,
                GUI.skin.box,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            if (_controller.History.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No requests yet. Start the service, select a controller, and send an instruction.",
                    MessageType.Info);
            }

            foreach (DawnTodAiHistoryEntry entry in _controller.History)
            {
                DrawHistoryEntry(entry);
                EditorGUILayout.Space(3f);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawHistoryEntry(DawnTodAiHistoryEntry entry)
        {
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.ExpandWidth(true));
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(entry.StartedAt.ToString("HH:mm:ss"), EditorStyles.miniLabel, GUILayout.Width(58f));
            GUILayout.Label("User", EditorStyles.boldLabel, GUILayout.Width(34f));
            GUILayout.Label(entry.UserInput, _wrappedLabel);
            EditorGUILayout.EndHorizontal();

            GUILayout.Label(
                $"Target: {entry.ControllerName} / {entry.PresetName} at {FormatHour(entry.CapturedHour)}",
                EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            DrawHistoryStatus(entry);
            GUILayout.FlexibleSpace();
            if (ReferenceEquals(entry, _controller.ActiveEntry) &&
                _controller.HasActiveRequest &&
                entry.Status != DawnTodAiHistoryStatus.Cancelled)
            {
                if (GUILayout.Button("Cancel", GUILayout.Width(62f), GUILayout.Height(22f)))
                {
                    CancelRequest();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (entry.AppliedFields.Count > 0)
            {
                GUILayout.Label(
                    "Applied fields: " + string.Join(", ", entry.AppliedFields),
                    _wrappedLabel);
                GUILayout.Label("Undo: Ctrl+Z", EditorStyles.miniLabel);
            }

            if (!string.IsNullOrEmpty(entry.ErrorMessage))
            {
                string error = string.IsNullOrEmpty(entry.ErrorCode)
                    ? entry.ErrorMessage
                    : entry.ErrorCode + ": " + entry.ErrorMessage;
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            if (!string.IsNullOrEmpty(entry.RawJson))
            {
                entry.IsRawJsonExpanded = EditorGUILayout.Foldout(
                    entry.IsRawJsonExpanded,
                    "Raw JSON",
                    true);
                if (entry.IsRawJsonExpanded)
                {
                    float rawHeight = Mathf.Clamp(
                        36f + entry.RawJson.Length / 7f,
                        72f,
                        160f);
                    EditorGUILayout.SelectableLabel(
                        entry.RawJson,
                        _rawJsonStyle,
                        GUILayout.Height(rawHeight));
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawHistoryStatus(DawnTodAiHistoryEntry entry)
        {
            Rect markerRect = GUILayoutUtility.GetRect(
                12f,
                EditorGUIUtility.singleLineHeight,
                GUILayout.Width(12f));
            EditorGUI.DrawRect(
                new Rect(markerRect.x + 2f, markerRect.y + 5f, 8f, 8f),
                GetHistoryStateColor(entry.Status));

            string statusText = GetHistoryStatusText(entry);
            GUILayout.Label(statusText, EditorStyles.boldLabel);
            if (entry.Duration > TimeSpan.Zero &&
                (entry.Status == DawnTodAiHistoryStatus.Success ||
                 entry.Status == DawnTodAiHistoryStatus.Error ||
                 entry.Status == DawnTodAiHistoryStatus.Cancelled))
            {
                GUILayout.Label(
                    FormatDuration(entry.Duration),
                    EditorStyles.miniLabel,
                    GUILayout.Width(64f));
            }
        }

        private void DrawComposer(
            DawnWeatherController selectedController,
            float capturedHour,
            Action pausePlayback,
            Action<DawnTodAiAnalyzeResult> onApplied)
        {
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("Instruction", EditorStyles.boldLabel);
            GUI.SetNextControlName(InputControlName);
            _input = EditorGUILayout.TextArea(
                _input,
                _inputStyle,
                GUILayout.MinHeight(58f),
                GUILayout.MaxHeight(90f),
                GUILayout.ExpandWidth(true));

            bool canSend = _controller.CanSend(
                _input,
                selectedController,
                capturedHour,
                Application.platform,
                out string reason);
            Event currentEvent = Event.current;
            if (IsSendShortcut(currentEvent) &&
                GUI.GetNameOfFocusedControl() == InputControlName)
            {
                if (canSend)
                {
                    currentEvent.Use();
                    BeginSend(
                        selectedController,
                        capturedHour,
                        pausePlayback,
                        onApplied);
                }
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(
                canSend ? "Ctrl+Enter to send" : reason,
                EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(!canSend);
            if (GUILayout.Button("Send", GUILayout.Width(76f), GUILayout.Height(26f)))
            {
                BeginSend(
                    selectedController,
                    capturedHour,
                    pausePlayback,
                    onApplied);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private async void BeginSend(
            DawnWeatherController selectedController,
            float capturedHour,
            Action pausePlayback,
            Action<DawnTodAiAnalyzeResult> onApplied)
        {
            string submitted = _input;
            _input = string.Empty;
            GUI.FocusControl(null);
            await _controller.SendAsync(
                submitted,
                selectedController,
                capturedHour,
                Application.platform,
                pausePlayback,
                onApplied);
        }

        private async void CancelRequest()
        {
            await _controller.CancelAsync();
        }

        private async void StartService()
        {
            await _controller.StartServiceAsync();
        }

        private async void StopService()
        {
            await _controller.StopServiceAsync();
        }

        private async void RestartService()
        {
            await _controller.RestartServiceAsync();
        }

        private void EnsureStyles()
        {
            if (_wrappedLabel != null)
            {
                return;
            }

            _wrappedLabel = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true
            };
            _inputStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                padding = new RectOffset(6, 6, 6, 6)
            };
            _rawJsonStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                fontSize = 10
            };
        }

        private static string GetHistoryStatusText(DawnTodAiHistoryEntry entry)
        {
            switch (entry.Status)
            {
                case DawnTodAiHistoryStatus.Analyzing:
                    return "Analyzing...";
                case DawnTodAiHistoryStatus.Validating:
                    return "Validating...";
                case DawnTodAiHistoryStatus.Applying:
                    return "Applying...";
                case DawnTodAiHistoryStatus.Success:
                    return entry.AppliedFields.Count > 0
                        ? $"Applied {entry.AppliedFields.Count} field(s)"
                        : "No changes";
                case DawnTodAiHistoryStatus.Cancelled:
                    return "Cancelled";
                default:
                    return "Error";
            }
        }

        private static Color GetServiceStateColor(DawnTodAiServiceState state)
        {
            switch (state)
            {
                case DawnTodAiServiceState.Ready:
                    return new Color(0.25f, 0.72f, 0.38f);
                case DawnTodAiServiceState.Starting:
                case DawnTodAiServiceState.Stopping:
                    return new Color(0.95f, 0.68f, 0.2f);
                case DawnTodAiServiceState.Error:
                    return new Color(0.9f, 0.3f, 0.3f);
                default:
                    return EditorGUIUtility.isProSkin
                        ? new Color(0.55f, 0.55f, 0.55f)
                        : new Color(0.4f, 0.4f, 0.4f);
            }
        }

        private static Color GetHistoryStateColor(DawnTodAiHistoryStatus status)
        {
            switch (status)
            {
                case DawnTodAiHistoryStatus.Success:
                    return new Color(0.25f, 0.72f, 0.38f);
                case DawnTodAiHistoryStatus.Error:
                    return new Color(0.9f, 0.3f, 0.3f);
                case DawnTodAiHistoryStatus.Cancelled:
                    return new Color(0.55f, 0.55f, 0.55f);
                default:
                    return new Color(0.25f, 0.58f, 0.95f);
            }
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }

        private static string FormatHour(float hour)
        {
            float wrapped = Mathf.Repeat(hour, 24f);
            int totalMinutes = Mathf.FloorToInt(wrapped * 60f + 0.0001f);
            return $"{totalMinutes / 60:D2}:{totalMinutes % 60:D2}";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            return duration.TotalSeconds >= 1d
                ? duration.TotalSeconds.ToString("0.0") + " s"
                : duration.TotalMilliseconds.ToString("0") + " ms";
        }
    }
}
