using System;
using DawnTOD;
using UnityEditor;
using UnityEngine;

namespace DawnTODEditor
{
    internal static class WeatherPipelineOutputEditorUtility
    {
        internal static bool CanEnsureAuthoringComponent(
            DawnTODSystem system)
        {
            if (system == null)
            {
                return false;
            }

            WeatherRenderPipelineKind pipelineKind =
                WeatherPipelineCapabilities.Current.PipelineKind;
            return WeatherPipelineOutputRegistry
                       .TryGetAuthoringComponentType(
                           pipelineKind,
                           out Type componentType) &&
                   system.GetComponent(componentType) == null;
        }

        internal static Component EnsureAuthoringComponent(
            DawnTODSystem system,
            bool registerUndo)
        {
            if (system == null)
            {
                return null;
            }

            WeatherRenderPipelineKind pipelineKind =
                WeatherPipelineCapabilities.Current.PipelineKind;
            if (!WeatherPipelineOutputRegistry
                .TryGetAuthoringComponentType(
                    pipelineKind,
                    out Type componentType))
            {
                return null;
            }

            Component component = system.GetComponent(componentType);
            if (component == null)
            {
                component = registerUndo
                    ? Undo.AddComponent(
                        system.gameObject,
                        componentType)
                    : system.gameObject.AddComponent(componentType);
            }

            system.RefreshWeatherBlendingSystem();
            EditorUtility.SetDirty(system);
            return component;
        }
    }
}
