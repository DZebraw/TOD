using UnityEditor;
using UnityEngine;

namespace DawnTODEditor.AI
{
    internal sealed class DawnTodAiSettingsWindow : EditorWindow
    {
        private DawnTodAiApiKeyStore _apiKeyStore;
        private DawnTodAiApiKeyStatus _status;
        private string _apiKeyInput = string.Empty;
        private string _operationMessage;
        private MessageType _operationMessageType = MessageType.Info;

        public static void ShowWindow()
        {
            DawnTodAiSettingsWindow window = GetWindow<DawnTodAiSettingsWindow>(true);
            window.titleContent = new GUIContent("DawnTOD AI Settings");
            window.minSize = new Vector2(440f, 270f);
            window.maxSize = new Vector2(620f, 340f);
            window.ShowUtility();
        }

        private void OnEnable()
        {
            _apiKeyStore = new DawnTodAiApiKeyStore();
            RefreshStatus();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("DeepSeek Provider", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Mode", DawnTodAiProtocol.Mode);
            EditorGUILayout.TextField("Endpoint", DawnTodAiProtocol.DeepSeekBaseUrl);
            EditorGUILayout.TextField("Model", DawnTodAiProtocol.DeepSeekModel);
            EditorGUILayout.TextField("Schema", DawnTodAiProtocol.SchemaVersion);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Current Windows User API Key", EditorStyles.boldLabel);
            _apiKeyInput = EditorGUILayout.PasswordField("API Key", _apiKeyInput);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_apiKeyInput));
            if (GUILayout.Button("Save", GUILayout.Width(76f), GUILayout.Height(24f)))
            {
                SaveApiKey();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(_status?.IsConfigured != true);
            if (GUILayout.Button("Clear", GUILayout.Width(76f), GUILayout.Height(24f)))
            {
                ClearApiKey();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            string statusMessage = _status?.IsConfigured == true
                ? "An API key is configured and encrypted with Windows DPAPI. Restart the service after changing it."
                : _status?.ErrorMessage ?? "No API key is configured.";
            EditorGUILayout.HelpBox(
                _operationMessage ?? statusMessage,
                _operationMessage != null
                    ? _operationMessageType
                    : (_status?.IsConfigured == true ? MessageType.Info : MessageType.Warning));

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(80f), GUILayout.Height(24f)))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8f);
        }

        private void SaveApiKey()
        {
            DawnTodAiApiKeyOperationResult result = _apiKeyStore.Save(_apiKeyInput);
            _apiKeyInput = string.Empty;
            _operationMessage = result.IsSuccess
                ? "API key saved securely. Restart the local service to use it."
                : result.ErrorMessage;
            _operationMessageType = result.IsSuccess ? MessageType.Info : MessageType.Error;
            RefreshStatus();
            GUI.FocusControl(null);
        }

        private void ClearApiKey()
        {
            DawnTodAiApiKeyOperationResult result = _apiKeyStore.Clear();
            _apiKeyInput = string.Empty;
            _operationMessage = result.IsSuccess
                ? "The encrypted API key configuration was removed."
                : result.ErrorMessage;
            _operationMessageType = result.IsSuccess ? MessageType.Info : MessageType.Error;
            RefreshStatus();
            GUI.FocusControl(null);
        }

        private void RefreshStatus()
        {
            _status = _apiKeyStore.GetStatus();
            Repaint();
        }
    }
}
