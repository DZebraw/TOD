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
            Undo.SetCurrentGroupName("Create TOD System with Weather Controller");

            GameObject todRoot = new GameObject("Dawn TOD System");
            todRoot.AddComponent<DawnTODSystem>();

            if (Selection.activeGameObject != null)
            {
                todRoot.transform.SetParent(Selection.activeGameObject.transform, false);
            }

            GameObject weatherControllerObj = new GameObject("Sunny");
            weatherControllerObj.transform.SetParent(todRoot.transform, false); 
            weatherControllerObj.AddComponent<DawnWeatherController>();

            Undo.RegisterCreatedObjectUndo(todRoot, "Create TOD System with Weather Controller");

            Selection.activeGameObject = todRoot;
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