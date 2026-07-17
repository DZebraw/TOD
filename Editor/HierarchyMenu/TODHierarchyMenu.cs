using UnityEngine;
using UnityEditor;
using DawnTOD;

namespace DawnTODEditor
{
    /// <summary>
    /// Hierarchy �Ҽ��˵����ߣ����ٴ��� TODSystem ���Ӽ� WeatherController
    /// </summary>
    public static class TODHierarchyMenu
    {
        private const int MenuPriority = 50;

        [MenuItem("GameObject/MagicDawn/TOD System with Weather Controller", false, MenuPriority)]
        private static void CreateTODSystemWithWeatherController()
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create TOD System with Weather Controller");

            GameObject todRoot = new GameObject("Dawn TOD System");
            DawnTODSystem todSystem = todRoot.AddComponent<DawnTODSystem>();

            if (Selection.activeGameObject != null)
            {
                todRoot.transform.SetParent(Selection.activeGameObject.transform, false);
            }

            GameObject weatherControllerObj = new GameObject("Sunny");
            weatherControllerObj.transform.SetParent(todRoot.transform, false); 
            weatherControllerObj.AddComponent<DawnWeatherController>();

            DawnGPUParticleSystem rainOutput =
                DawnRainOutputEditorUtility.CreateRainOutput(
                    todRoot.transform,
                    false);
            todSystem.RainParticleSystem = rainOutput;

            Undo.RegisterCreatedObjectUndo(todRoot, "Create TOD System with Weather Controller");
            Undo.CollapseUndoOperations(undoGroup);

            Selection.activeGameObject = todRoot;
        }

        [MenuItem("GameObject/MagicDawn/Rain Output", false, MenuPriority + 1)]
        private static void CreateRainOutput()
        {
            GameObject selection = Selection.activeGameObject;
            DawnTODSystem todSystem = selection != null
                ? selection.GetComponentInParent<DawnTODSystem>()
                : null;
            Transform parent = todSystem != null
                ? todSystem.transform
                : selection != null
                    ? selection.transform
                    : null;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create TOD Rain Output");
            DawnGPUParticleSystem rainOutput =
                DawnRainOutputEditorUtility.EnsureRainOutput(
                    todSystem,
                    parent,
                    true);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = rainOutput.gameObject;
        }

        [MenuItem("GameObject/MagicDawn/Weather Controller", false, MenuPriority)]
        private static void CreateWeatherController()
        {
            Undo.SetCurrentGroupName("Create Weather Controller");

            GameObject todRoot = new GameObject("Weather");
            todRoot.AddComponent<DawnWeatherController>();

            if (Selection.activeGameObject != null)
            {
                todRoot.transform.SetParent(Selection.activeGameObject.transform, false);
            }

            Undo.RegisterCreatedObjectUndo(todRoot, "Create Weather Controller");

            Selection.activeGameObject = todRoot;
        }
    }
}
