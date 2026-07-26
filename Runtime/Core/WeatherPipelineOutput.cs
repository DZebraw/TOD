using System;
using System.Collections.Generic;
using UnityEngine;

namespace DawnTOD
{
    /// <summary>
    /// Pipeline-neutral environment values produced by the weather blender.
    /// Directional lights and precipitation remain common scene outputs owned by
    /// <see cref="DawnTODSystem"/>.
    /// </summary>
    public readonly struct WeatherPipelineOutputState
    {
        public float StarEmission { get; }
        public float FogDistance { get; }
        public float FogHeight { get; }
        public Color FogColor { get; }
        public float ExposureCompensation { get; }
        public bool FogEnabled { get; }
        public bool FogAffectSky { get; }

        public WeatherPipelineOutputState(
            float starEmission,
            float fogDistance,
            float fogHeight,
            Color fogColor,
            float exposureCompensation,
            bool fogEnabled,
            bool fogAffectSky)
        {
            StarEmission = starEmission;
            FogDistance = fogDistance;
            FogHeight = fogHeight;
            FogColor = fogColor;
            ExposureCompensation = exposureCompensation;
            FogEnabled = fogEnabled;
            FogAffectSky = fogAffectSky;
        }
    }

    /// <summary>
    /// Runtime boundary between Dawn TOD's pipeline-neutral weather
    /// evaluation and a render-pipeline-specific environment implementation.
    /// </summary>
    public interface IWeatherPipelineOutput
    {
        WeatherPipelineCapabilities Capabilities { get; }

        /// <summary>
        /// Ensures transient resources and cached component references are ready.
        /// Must be idempotent because DawnTODSystem may call it every frame.
        /// </summary>
        void Prepare();

        void Apply(in WeatherPipelineOutputState state);

        /// <summary>
        /// Releases transient resources created by this output.
        /// Must be safe to call more than once.
        /// </summary>
        void Release();

        bool IsConfigured(out string errorMessage);
    }

    /// <summary>
    /// Registration point used by optional pipeline assemblies. The core assembly
    /// never references URP or HDRP implementation types directly.
    /// </summary>
    public static class WeatherPipelineOutputRegistry
    {
        private sealed class Registration
        {
            public Type ImplementationType { get; }
            public Func<DawnTODSystem, IWeatherPipelineOutput> Factory { get; }
            public Type AuthoringComponentType { get; }

            public Registration(
                Type implementationType,
                Func<DawnTODSystem, IWeatherPipelineOutput> factory,
                Type authoringComponentType)
            {
                ImplementationType = implementationType;
                Factory = factory;
                AuthoringComponentType = authoringComponentType;
            }
        }

        private static readonly Dictionary<
            WeatherRenderPipelineKind,
            Registration> Registrations =
                new Dictionary<WeatherRenderPipelineKind, Registration>();

        public static void Register(
            WeatherRenderPipelineKind pipelineKind,
            Type implementationType,
            Func<DawnTODSystem, IWeatherPipelineOutput> factory,
            Type authoringComponentType = null)
        {
            if (pipelineKind == WeatherRenderPipelineKind.Unknown)
            {
                throw new ArgumentException(
                    "A pipeline output cannot be registered for Unknown.",
                    nameof(pipelineKind));
            }

            if (implementationType == null ||
                implementationType.IsAbstract ||
                !typeof(IWeatherPipelineOutput).IsAssignableFrom(
                    implementationType))
            {
                throw new ArgumentException(
                    $"{implementationType?.FullName ?? "null"} is not a " +
                    $"concrete {nameof(IWeatherPipelineOutput)}.",
                    nameof(implementationType));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (authoringComponentType != null &&
                (authoringComponentType.IsAbstract ||
                 !typeof(Component).IsAssignableFrom(
                     authoringComponentType)))
            {
                throw new ArgumentException(
                    $"{authoringComponentType.FullName} is not a concrete " +
                    $"{nameof(Component)}.",
                    nameof(authoringComponentType));
            }

            if (Registrations.TryGetValue(
                    pipelineKind,
                    out Registration existing) &&
                existing.ImplementationType != implementationType)
            {
                throw new InvalidOperationException(
                    $"{pipelineKind} already has pipeline output " +
                    $"{existing.ImplementationType.FullName} registered.");
            }

            Registrations[pipelineKind] =
                new Registration(
                    implementationType,
                    factory,
                    authoringComponentType);
        }

        public static bool TryCreate(
            WeatherRenderPipelineKind pipelineKind,
            DawnTODSystem owner,
            out IWeatherPipelineOutput output)
        {
            output = null;
            if (owner == null ||
                !Registrations.TryGetValue(
                    pipelineKind,
                    out Registration registration))
            {
                return false;
            }

            output = registration.Factory(owner);
            return output != null;
        }

        public static bool IsRegistered(
            WeatherRenderPipelineKind pipelineKind)
        {
            return Registrations.ContainsKey(pipelineKind);
        }

        public static bool TryGetAuthoringComponentType(
            WeatherRenderPipelineKind pipelineKind,
            out Type componentType)
        {
            componentType = null;
            if (!Registrations.TryGetValue(
                    pipelineKind,
                    out Registration registration) ||
                registration.AuthoringComponentType == null)
            {
                return false;
            }

            componentType = registration.AuthoringComponentType;
            return true;
        }
    }
}
