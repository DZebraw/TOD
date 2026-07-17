using DawnTOD;
using UnityEditor;
using UnityEngine;

namespace DawnTODEditor
{
    internal static class DawnRainOutputEditorUtility
    {
        internal const string DefaultPrefabPath =
            "Packages/com.tencent.dawn.tod/Runtime/Resources/DawnRainOutput.prefab";

        internal static DawnGPUParticleSystem EnsureRainOutput(
            DawnTODSystem system,
            Transform fallbackParent = null,
            bool registerUndo = true)
        {
            if (system != null && system.RainParticleSystem != null)
            {
                return system.RainParticleSystem;
            }

            if (system != null)
            {
                DawnGPUParticleSystem detected = system.ResolveRainParticleSystem();
                if (detected != null)
                {
                    AssignToSystem(system, detected, registerUndo);
                    return detected;
                }
            }

            Transform parent = system != null ? system.transform : fallbackParent;
            DawnGPUParticleSystem created = CreateRainOutput(parent, registerUndo);
            if (system != null)
            {
                AssignToSystem(system, created, registerUndo);
            }

            return created;
        }

        internal static DawnGPUParticleSystem CreateRainOutput(
            Transform parent,
            bool registerUndo)
        {
            GameObject rainObject = InstantiateDefaultPrefab();
            if (rainObject == null)
            {
                rainObject = new GameObject("Rain Output");
                DawnGPUParticleSystem fallback =
                    rainObject.AddComponent<DawnGPUParticleSystem>();
                fallback.AssignDefaultResourceOverrides();
            }

            rainObject.name = "Rain Output";
            rainObject.transform.SetParent(parent, false);
            DawnGPUParticleSystem rainOutput =
                rainObject.GetComponent<DawnGPUParticleSystem>();
            rainOutput.AssignDefaultResourceOverrides();
            rainOutput.ParticleShow = false;
            rainOutput.GetComponent<MeshRenderer>().enabled = false;

            if (registerUndo)
            {
                Undo.RegisterCreatedObjectUndo(rainObject, "Create TOD Rain Output");
            }

            EditorUtility.SetDirty(rainOutput);
            return rainOutput;
        }

        private static GameObject InstantiateDefaultPrefab()
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath);
            return prefab != null
                ? PrefabUtility.InstantiatePrefab(prefab) as GameObject
                : null;
        }

        private static void AssignToSystem(
            DawnTODSystem system,
            DawnGPUParticleSystem rainOutput,
            bool registerUndo)
        {
            if (registerUndo)
            {
                Undo.RecordObject(system, "Assign TOD Rain Output");
            }

            system.RainParticleSystem = rainOutput;
            system.RefreshWeatherBlendingSystem();
            EditorUtility.SetDirty(system);
        }
    }
}
