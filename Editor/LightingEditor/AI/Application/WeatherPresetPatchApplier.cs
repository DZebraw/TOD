using System;
using System.Collections.Generic;
using DawnTOD;
using UnityEditor;
using UnityEngine;

namespace DawnTODEditor.AI
{
    public sealed class WeatherIntentTargetSnapshot
    {
        public DawnWeatherController Controller { get; }
        public DawnWeatherPreset Preset { get; }
        public float CapturedHour { get; }

        private WeatherIntentTargetSnapshot(
            DawnWeatherController controller,
            DawnWeatherPreset preset,
            float capturedHour)
        {
            Controller = controller;
            Preset = preset;
            CapturedHour = capturedHour;
        }

        public static WeatherIntentTargetSnapshot Capture(
            DawnWeatherController controller,
            float capturedHour)
        {
            if (controller == null)
            {
                throw new ArgumentNullException(nameof(controller));
            }

            if (controller.ActivePreset == null)
            {
                throw new InvalidOperationException("The target controller has no active preset.");
            }

            if (!IsValidHour(capturedHour))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capturedHour),
                    "Captured time must be finite and in the range [0, 24).");
            }

            return new WeatherIntentTargetSnapshot(
                controller,
                controller.ActivePreset,
                capturedHour);
        }

        private static bool IsValidHour(float hour)
        {
            return !float.IsNaN(hour) &&
                   !float.IsInfinity(hour) &&
                   hour >= 0f &&
                   hour < 24f;
        }
    }

    public enum WeatherIntentApplyStatus
    {
        Applied,
        NoChanges,
        Failed
    }

    public sealed class WeatherIntentApplyResult
    {
        public WeatherIntentApplyStatus Status { get; }
        public bool IsSuccess => Status != WeatherIntentApplyStatus.Failed;
        public bool DidApply => Status == WeatherIntentApplyStatus.Applied;
        public float TargetHour { get; }
        public float TargetNormalizedTime => TargetHour / 24f;
        public IReadOnlyList<string> AppliedFields { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }

        private WeatherIntentApplyResult(
            WeatherIntentApplyStatus status,
            float targetHour,
            IReadOnlyList<string> appliedFields,
            string errorCode,
            string errorMessage)
        {
            Status = status;
            TargetHour = targetHour;
            AppliedFields = appliedFields;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        internal static WeatherIntentApplyResult Applied(float targetHour, List<string> appliedFields)
        {
            return new WeatherIntentApplyResult(
                WeatherIntentApplyStatus.Applied,
                targetHour,
                appliedFields.AsReadOnly(),
                null,
                null);
        }

        internal static WeatherIntentApplyResult NoChanges(float targetHour)
        {
            return new WeatherIntentApplyResult(
                WeatherIntentApplyStatus.NoChanges,
                targetHour,
                Array.Empty<string>(),
                null,
                null);
        }

        internal static WeatherIntentApplyResult Failed(string errorCode, string errorMessage)
        {
            return new WeatherIntentApplyResult(
                WeatherIntentApplyStatus.Failed,
                0f,
                Array.Empty<string>(),
                errorCode,
                errorMessage);
        }
    }

    public static class WeatherPresetPatchApplier
    {
        public const float KeyTimeTolerance = 0.0001f;
        public const string UndoOperationName = "Apply TOD AI Weather Patch";

        private const int MaximumGradientKeys = 8;

        public static WeatherIntentApplyResult Apply(
            WeatherIntentTargetSnapshot target,
            WeatherIntentPatch patch)
        {
            if (target == null)
            {
                return WeatherIntentApplyResult.Failed("TARGET_INVALID", "Target snapshot is missing.");
            }

            if (patch == null)
            {
                return WeatherIntentApplyResult.Failed("PATCH_INVALID", "Weather intent patch is missing.");
            }

            if (!TryValidateTarget(target, out string targetError))
            {
                return WeatherIntentApplyResult.Failed("TARGET_CHANGED", targetError);
            }

            float targetHour = patch.Time.Mode == WeatherIntentTimeMode.Explicit
                ? patch.Time.Hour.Value
                : target.CapturedHour;

            if (!patch.HasChanges)
            {
                return WeatherIntentApplyResult.NoChanges(targetHour);
            }

            PreparedPatch prepared;
            try
            {
                prepared = PreparePatch(target.Preset, patch, targetHour / 24f);
            }
            catch (PatchApplicationException exception)
            {
                return WeatherIntentApplyResult.Failed(exception.Code, exception.Message);
            }

            if (!TryValidateTarget(target, out targetError))
            {
                return WeatherIntentApplyResult.Failed("TARGET_CHANGED", targetError);
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoOperationName);
            Undo.RegisterCompleteObjectUndo(
                new UnityEngine.Object[] { target.Controller, target.Preset },
                UndoOperationName);

            try
            {
                prepared.ApplyTo(target.Preset);

                if (patch.Time.Mode == WeatherIntentTimeMode.Explicit)
                {
                    target.Controller.TimeOfDay = targetHour;
                }
                else
                {
                    target.Controller.Refresh();
                }

                EditorUtility.SetDirty(target.Preset);
                if (patch.Time.Mode == WeatherIntentTimeMode.Explicit)
                {
                    EditorUtility.SetDirty(target.Controller);
                }

                SceneView.RepaintAll();
                Undo.CollapseUndoOperations(undoGroup);
                return WeatherIntentApplyResult.Applied(targetHour, prepared.AppliedFields);
            }
            catch (Exception exception)
            {
                try
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                }
                catch (Exception rollbackException)
                {
                    return WeatherIntentApplyResult.Failed(
                        "ROLLBACK_FAILED",
                        $"Patch application failed and Undo rollback also failed: {rollbackException.Message}");
                }

                SceneView.RepaintAll();
                return WeatherIntentApplyResult.Failed("APPLY_FAILED", exception.Message);
            }
        }

        public static float UnwrapAzimuth(float normalizedAzimuth, float referenceAzimuth)
        {
            float turns = Mathf.Round((referenceAzimuth - normalizedAzimuth) / 360f);
            return normalizedAzimuth + turns * 360f;
        }

        private static bool TryValidateTarget(WeatherIntentTargetSnapshot target, out string error)
        {
            if (target.Controller == null)
            {
                error = "The captured controller no longer exists.";
                return false;
            }

            if (target.Preset == null)
            {
                error = "The captured preset no longer exists.";
                return false;
            }

            if (target.Controller.ActivePreset != target.Preset)
            {
                error = "The controller no longer references the captured preset.";
                return false;
            }

            error = null;
            return true;
        }

        private static PreparedPatch PreparePatch(
            DawnWeatherPreset preset,
            WeatherIntentPatch patch,
            float targetTime)
        {
            var prepared = new PreparedPatch();

            if (patch.Time.Mode == WeatherIntentTimeMode.Explicit)
            {
                prepared.AppliedFields.Add("time.hour");
            }

            PrepareLightPatch(
                prepared,
                patch.Sun,
                "sun",
                targetTime,
                preset.sunAzimuthCurve,
                preset.sunElevationCurve,
                preset.sunIntensityCurve,
                preset.sunColorGradient,
                true);

            PrepareLightPatch(
                prepared,
                patch.Moon,
                "moon",
                targetTime,
                preset.moonAzimuthCurve,
                preset.moonElevationCurve,
                preset.moonIntensityCurve,
                preset.moonColorGradient,
                false);

            return prepared;
        }

        private static void PrepareLightPatch(
            PreparedPatch prepared,
            WeatherIntentLightPatch lightPatch,
            string fieldPrefix,
            float targetTime,
            AnimationCurve azimuthCurve,
            AnimationCurve elevationCurve,
            AnimationCurve intensityCurve,
            Gradient colorGradient,
            bool isSun)
        {
            if (lightPatch.AzimuthDegrees.HasValue)
            {
                RequireCurve(azimuthCurve, fieldPrefix + ".azimuth_deg");
                float reference = azimuthCurve.Evaluate(targetTime);
                if (!IsFinite(reference))
                {
                    throw new PatchApplicationException(
                        "INVALID_TARGET_VALUE",
                        $"The existing value for '{fieldPrefix}.azimuth_deg' is not finite.");
                }

                float unwrapped = UnwrapAzimuth(lightPatch.AzimuthDegrees.Value, reference);
                SetPreparedCurve(
                    prepared,
                    isSun,
                    PreparedCurveType.Azimuth,
                    PrepareCurveKeys(azimuthCurve, targetTime, unwrapped));
                prepared.AppliedFields.Add(fieldPrefix + ".azimuth_deg");
            }

            if (lightPatch.ElevationDegrees.HasValue)
            {
                RequireCurve(elevationCurve, fieldPrefix + ".elevation_deg");
                SetPreparedCurve(
                    prepared,
                    isSun,
                    PreparedCurveType.Elevation,
                    PrepareCurveKeys(elevationCurve, targetTime, lightPatch.ElevationDegrees.Value));
                prepared.AppliedFields.Add(fieldPrefix + ".elevation_deg");
            }

            if (lightPatch.Intensity.HasValue)
            {
                RequireCurve(intensityCurve, fieldPrefix + ".intensity");
                SetPreparedCurve(
                    prepared,
                    isSun,
                    PreparedCurveType.Intensity,
                    PrepareCurveKeys(intensityCurve, targetTime, lightPatch.Intensity.Value));
                prepared.AppliedFields.Add(fieldPrefix + ".intensity");
            }

            if (lightPatch.Color != null)
            {
                if (colorGradient == null)
                {
                    throw PatchApplicationException.MissingTarget(fieldPrefix + ".color");
                }

                PreparedGradient gradient = PrepareGradient(
                    colorGradient,
                    targetTime,
                    lightPatch.Color);
                if (isSun)
                {
                    prepared.SunColor = gradient;
                }
                else
                {
                    prepared.MoonColor = gradient;
                }

                prepared.AppliedFields.Add(fieldPrefix + ".color");
            }
        }

        private static void RequireCurve(AnimationCurve curve, string fieldPath)
        {
            if (curve == null)
            {
                throw PatchApplicationException.MissingTarget(fieldPath);
            }
        }

        private static Keyframe[] PrepareCurveKeys(AnimationCurve source, float targetTime, float value)
        {
            if (!IsFinite(value))
            {
                throw new PatchApplicationException(
                    "INVALID_TARGET_VALUE",
                    "The prepared curve value is not finite.");
            }

            var workingCurve = new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };

            int keyIndex = FindKeyIndex(workingCurve.keys, targetTime);
            if (keyIndex >= 0)
            {
                Keyframe key = workingCurve.keys[keyIndex];
                key.value = value;
                workingCurve.MoveKey(keyIndex, key);
            }
            else
            {
                keyIndex = workingCurve.AddKey(new Keyframe(targetTime, value));
                if (keyIndex < 0)
                {
                    throw new PatchApplicationException(
                        "CURVE_KEY_FAILED",
                        "Unity rejected the new curve key.");
                }

                workingCurve.SmoothTangents(keyIndex, 0f);
            }

            return workingCurve.keys;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static int FindKeyIndex(IReadOnlyList<Keyframe> keys, float targetTime)
        {
            int closestIndex = -1;
            float closestDistance = float.MaxValue;

            for (int i = 0; i < keys.Count; i++)
            {
                float distance = Mathf.Abs(keys[i].time - targetTime);
                if (distance <= KeyTimeTolerance && distance < closestDistance)
                {
                    closestIndex = i;
                    closestDistance = distance;
                }
            }

            return closestIndex;
        }

        private static PreparedGradient PrepareGradient(
            Gradient source,
            float targetTime,
            WeatherIntentColor color)
        {
            var colorKeys = new List<GradientColorKey>(source.colorKeys);
            var alphaKeys = new List<GradientAlphaKey>(source.alphaKeys);

            colorKeys.RemoveAll(key => Mathf.Abs(key.time - targetTime) <= KeyTimeTolerance);
            alphaKeys.RemoveAll(key => Mathf.Abs(key.time - targetTime) <= KeyTimeTolerance);

            if (colorKeys.Count >= MaximumGradientKeys || alphaKeys.Count >= MaximumGradientKeys)
            {
                throw new PatchApplicationException(
                    "GRADIENT_KEY_LIMIT",
                    "The target gradient has no capacity for another key at this time.");
            }

            colorKeys.Add(new GradientColorKey(new Color(color.R, color.G, color.B, 1f), targetTime));
            alphaKeys.Add(new GradientAlphaKey(color.A, targetTime));
            colorKeys.Sort((left, right) => left.time.CompareTo(right.time));
            alphaKeys.Sort((left, right) => left.time.CompareTo(right.time));

            var validationGradient = new Gradient();
            validationGradient.SetKeys(colorKeys.ToArray(), alphaKeys.ToArray());

            return new PreparedGradient(colorKeys.ToArray(), alphaKeys.ToArray());
        }

        private static void SetPreparedCurve(
            PreparedPatch prepared,
            bool isSun,
            PreparedCurveType curveType,
            Keyframe[] keys)
        {
            if (isSun)
            {
                switch (curveType)
                {
                    case PreparedCurveType.Azimuth:
                        prepared.SunAzimuthKeys = keys;
                        break;
                    case PreparedCurveType.Elevation:
                        prepared.SunElevationKeys = keys;
                        break;
                    case PreparedCurveType.Intensity:
                        prepared.SunIntensityKeys = keys;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(curveType), curveType, null);
                }
            }
            else
            {
                switch (curveType)
                {
                    case PreparedCurveType.Azimuth:
                        prepared.MoonAzimuthKeys = keys;
                        break;
                    case PreparedCurveType.Elevation:
                        prepared.MoonElevationKeys = keys;
                        break;
                    case PreparedCurveType.Intensity:
                        prepared.MoonIntensityKeys = keys;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(curveType), curveType, null);
                }
            }
        }

        private enum PreparedCurveType
        {
            Azimuth,
            Elevation,
            Intensity
        }

        private sealed class PreparedPatch
        {
            public readonly List<string> AppliedFields = new List<string>();

            public Keyframe[] SunAzimuthKeys;
            public Keyframe[] SunElevationKeys;
            public Keyframe[] SunIntensityKeys;
            public PreparedGradient SunColor;
            public Keyframe[] MoonAzimuthKeys;
            public Keyframe[] MoonElevationKeys;
            public Keyframe[] MoonIntensityKeys;
            public PreparedGradient MoonColor;

            public void ApplyTo(DawnWeatherPreset preset)
            {
                ApplyCurveKeys(preset.sunAzimuthCurve, SunAzimuthKeys);
                ApplyCurveKeys(preset.sunElevationCurve, SunElevationKeys);
                ApplyCurveKeys(preset.sunIntensityCurve, SunIntensityKeys);
                ApplyGradient(preset.sunColorGradient, SunColor);
                ApplyCurveKeys(preset.moonAzimuthCurve, MoonAzimuthKeys);
                ApplyCurveKeys(preset.moonElevationCurve, MoonElevationKeys);
                ApplyCurveKeys(preset.moonIntensityCurve, MoonIntensityKeys);
                ApplyGradient(preset.moonColorGradient, MoonColor);
            }

            private static void ApplyCurveKeys(AnimationCurve curve, Keyframe[] keys)
            {
                if (keys != null)
                {
                    curve.keys = keys;
                }
            }

            private static void ApplyGradient(Gradient gradient, PreparedGradient preparedGradient)
            {
                if (preparedGradient != null)
                {
                    gradient.SetKeys(preparedGradient.ColorKeys, preparedGradient.AlphaKeys);
                }
            }
        }

        private sealed class PreparedGradient
        {
            public GradientColorKey[] ColorKeys { get; }
            public GradientAlphaKey[] AlphaKeys { get; }

            public PreparedGradient(GradientColorKey[] colorKeys, GradientAlphaKey[] alphaKeys)
            {
                ColorKeys = colorKeys;
                AlphaKeys = alphaKeys;
            }
        }

        private sealed class PatchApplicationException : Exception
        {
            public string Code { get; }

            public PatchApplicationException(string code, string message)
                : base(message)
            {
                Code = code;
            }

            public static PatchApplicationException MissingTarget(string fieldPath)
            {
                return new PatchApplicationException(
                    "MISSING_TARGET",
                    $"The Unity target for '{fieldPath}' is missing.");
            }
        }
    }
}
