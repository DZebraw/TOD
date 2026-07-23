#if USING_URP
using DawnTOD;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace DawnTODEditor
{
    [CustomEditor(typeof(DawnVolumetricCloudVolume))]
    internal sealed class DawnVolumetricCloudVolumeEditor : VolumeComponentEditor
    {
        private const float MinTiling = 0.000001f;
        private const float MinShapeSize = 10f;
        private const float MaxShapeSize = 5000f;
        private const float MinDetailFrequency = 0.25f;
        private const float MaxDetailFrequency = 32f;
        private const float MinDensityBias = -1f;
        private const float MaxDensityBias = 1f;

        private SerializedDataParameter m_Enabled;
        private SerializedDataParameter m_BoundsCenter;
        private SerializedDataParameter m_BoundsSize;
        private SerializedDataParameter m_ShapeNoise;
        private SerializedDataParameter m_DetailNoise;
        private SerializedDataParameter m_WeatherMap;
        private SerializedDataParameter m_MaskNoise;
        private SerializedDataParameter m_BlueNoise;
        private SerializedDataParameter m_Downsample;
        private SerializedDataParameter m_MaxRayMarchSteps;
        private SerializedDataParameter m_RayStepExponent;
        private SerializedDataParameter m_RayStepLength;
        private SerializedDataParameter m_RayOffsetStrength;
        private SerializedDataParameter m_Coverage;
        private SerializedDataParameter m_WeatherMapTiling;
        private SerializedDataParameter m_ShapeTiling;
        private SerializedDataParameter m_DetailTiling;
        private SerializedDataParameter m_DensityOffset;
        private SerializedDataParameter m_DensityMultiplier;
        private SerializedDataParameter m_ShapeNoiseWeights;
        private SerializedDataParameter m_DetailWeights;
        private SerializedDataParameter m_DetailNoiseWeight;
        private SerializedDataParameter m_HeightProfileBlend;
        private SerializedDataParameter m_CloudBaseSoftness;
        private SerializedDataParameter m_CloudBodyHeight;
        private SerializedDataParameter m_VerticalGrowth;
        private SerializedDataParameter m_CloudTopSoftness;
        private SerializedDataParameter m_ColorA;
        private SerializedDataParameter m_ColorB;
        private SerializedDataParameter m_ColorOffset1;
        private SerializedDataParameter m_ColorOffset2;
        private SerializedDataParameter m_LightAbsorptionTowardSun;
        private SerializedDataParameter m_LightAbsorptionThroughCloud;
        private SerializedDataParameter m_PhaseParameters;
        private SerializedDataParameter m_PhaseMinimum;
        private SerializedDataParameter m_MultiScatterExtinction;
        private SerializedDataParameter m_MultiScatterContribution;
        private SerializedDataParameter m_MultiScatterDirectionality;
        private SerializedDataParameter m_SkyLightTint;
        private SerializedDataParameter m_SkyLightIntensity;
        private SerializedDataParameter m_GroundLightTint;
        private SerializedDataParameter m_GroundLightIntensity;
        private SerializedDataParameter m_SpeedWarp;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<DawnVolumetricCloudVolume>(serializedObject);

            m_Enabled = Unpack(o.Find(x => x.enabled));
            m_BoundsCenter = Unpack(o.Find(x => x.boundsCenter));
            m_BoundsSize = Unpack(o.Find(x => x.boundsSize));
            m_ShapeNoise = Unpack(o.Find(x => x.shapeNoise));
            m_DetailNoise = Unpack(o.Find(x => x.detailNoise));
            m_WeatherMap = Unpack(o.Find(x => x.weatherMap));
            m_MaskNoise = Unpack(o.Find(x => x.maskNoise));
            m_BlueNoise = Unpack(o.Find(x => x.blueNoise));
            m_Downsample = Unpack(o.Find(x => x.downsample));
            m_MaxRayMarchSteps = Unpack(o.Find(x => x.maxRayMarchSteps));
            m_RayStepExponent = Unpack(o.Find(x => x.rayStepExponent));
            m_RayStepLength = Unpack(o.Find(x => x.rayStepLength));
            m_RayOffsetStrength = Unpack(o.Find(x => x.rayOffsetStrength));
            m_Coverage = Unpack(o.Find(x => x.coverage));
            m_WeatherMapTiling = Unpack(o.Find(x => x.weatherMapTiling));
            m_ShapeTiling = Unpack(o.Find(x => x.shapeTiling));
            m_DetailTiling = Unpack(o.Find(x => x.detailTiling));
            m_DensityOffset = Unpack(o.Find(x => x.densityOffset));
            m_DensityMultiplier = Unpack(o.Find(x => x.densityMultiplier));
            m_ShapeNoiseWeights = Unpack(o.Find(x => x.shapeNoiseWeights));
            m_DetailWeights = Unpack(o.Find(x => x.detailWeights));
            m_DetailNoiseWeight = Unpack(o.Find(x => x.detailNoiseWeight));
            m_HeightProfileBlend = Unpack(o.Find(x => x.heightProfileBlend));
            m_CloudBaseSoftness = Unpack(o.Find(x => x.cloudBaseSoftness));
            m_CloudBodyHeight = Unpack(o.Find(x => x.cloudBodyHeight));
            m_VerticalGrowth = Unpack(o.Find(x => x.verticalGrowth));
            m_CloudTopSoftness = Unpack(o.Find(x => x.cloudTopSoftness));
            m_ColorA = Unpack(o.Find(x => x.colorA));
            m_ColorB = Unpack(o.Find(x => x.colorB));
            m_ColorOffset1 = Unpack(o.Find(x => x.colorOffset1));
            m_ColorOffset2 = Unpack(o.Find(x => x.colorOffset2));
            m_LightAbsorptionTowardSun =
                Unpack(o.Find(x => x.lightAbsorptionTowardSun));
            m_LightAbsorptionThroughCloud =
                Unpack(o.Find(x => x.lightAbsorptionThroughCloud));
            m_PhaseParameters = Unpack(o.Find(x => x.phaseParameters));
            m_PhaseMinimum = Unpack(o.Find(x => x.phaseMinimum));
            m_MultiScatterExtinction =
                Unpack(o.Find(x => x.multiScatterExtinction));
            m_MultiScatterContribution =
                Unpack(o.Find(x => x.multiScatterContribution));
            m_MultiScatterDirectionality =
                Unpack(o.Find(x => x.multiScatterDirectionality));
            m_SkyLightTint = Unpack(o.Find(x => x.skyLightTint));
            m_SkyLightIntensity = Unpack(o.Find(x => x.skyLightIntensity));
            m_GroundLightTint = Unpack(o.Find(x => x.groundLightTint));
            m_GroundLightIntensity =
                Unpack(o.Find(x => x.groundLightIntensity));
            m_SpeedWarp = Unpack(o.Find(x => x.speedWarp));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            PropertyField(m_Enabled);

            PropertyField(m_BoundsCenter);
            PropertyField(m_BoundsSize);

            PropertyField(m_ShapeNoise);
            PropertyField(m_DetailNoise);
            PropertyField(m_WeatherMap);
            PropertyField(m_MaskNoise);
            PropertyField(m_BlueNoise);

            PropertyField(m_Downsample);
            PropertyField(m_MaxRayMarchSteps);
            PropertyField(m_RayStepExponent);
            PropertyField(m_RayStepLength);
            PropertyField(m_RayOffsetStrength);

            PropertyField(
                m_Coverage,
                new GUIContent(
                    "Cloud Coverage",
                    "Controls horizontal cloud amount without changing the density of fully covered regions."));
            DrawWeatherPatternSize();
            DrawShapeSize();
            DrawDetailFrequency();
            DrawDensityBias();
            PropertyField(m_DensityMultiplier);
            PropertyField(m_ShapeNoiseWeights);
            PropertyField(
                m_DetailWeights,
                new GUIContent(
                    "Detail Contrast",
                    "Contrast exponent applied to the detail texture before it erodes cloud surfaces."));
            PropertyField(
                m_DetailNoiseWeight,
                new GUIContent(
                    "Detail Erosion Strength",
                    "Controls how strongly the detail texture erodes cloud surfaces. Zero disables the detail texture contribution."));
            DrawDetailNoiseWarnings();
            PropertyField(
                m_HeightProfileBlend,
                new GUIContent(
                    "Height Profile Blend",
                    "Zero restores the legacy height gradient; one uses the art-directed cloud profile."));
            PropertyField(
                m_CloudBaseSoftness,
                new GUIContent(
                    "Cloud Base Softness",
                    "Softens the edge while retaining a level cloud base."));
            PropertyField(
                m_CloudBodyHeight,
                new GUIContent(
                    "Cloud Body Height",
                    "Sets the minimum height of the full cloud body."));
            PropertyField(
                m_VerticalGrowth,
                new GUIContent(
                    "Vertical Growth",
                    "Grows selected weather-map regions above the main body."));
            PropertyField(
                m_CloudTopSoftness,
                new GUIContent(
                    "Cloud Top Softness",
                    "Controls the vertical fade distance of cloud tops."));

            PropertyField(m_ColorA);
            PropertyField(m_ColorB);
            PropertyField(m_ColorOffset1);
            PropertyField(m_ColorOffset2);
            PropertyField(m_LightAbsorptionTowardSun);
            PropertyField(m_LightAbsorptionThroughCloud);
            DrawPhaseParameters();
            PropertyField(
                m_PhaseMinimum,
                new GUIContent(
                    "Non-Sun-Facing Fill",
                    "Minimum direct-light response used to keep side and back faces readable."));
            PropertyField(
                m_MultiScatterExtinction,
                new GUIContent(
                    "Internal Light Extinction",
                    "Lower values let sunlight travel farther through thick clouds."));
            PropertyField(
                m_MultiScatterContribution,
                new GUIContent(
                    "Internal Light Contribution",
                    "Adds the second scattering order; zero restores single scattering."));
            PropertyField(
                m_MultiScatterDirectionality,
                new GUIContent(
                    "Internal Light Directionality",
                    "Zero is diffuse in all directions; one follows the direct-light phase."));
            PropertyField(
                m_SkyLightTint,
                new GUIContent(
                    "Sky Fill Tint",
                    "Tints the current scene ambient color entering from above."));
            PropertyField(
                m_SkyLightIntensity,
                new GUIContent(
                    "Sky Fill Intensity",
                    "Controls cool, diffuse illumination from the sky."));
            PropertyField(
                m_GroundLightTint,
                new GUIContent(
                    "Ground Bounce Tint",
                    "Represents the average color reflected by the terrain."));
            PropertyField(
                m_GroundLightIntensity,
                new GUIContent(
                    "Ground Bounce Intensity",
                    "Controls reflected light entering the underside of the cloud."));

            PropertyField(m_SpeedWarp);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPhaseParameters()
        {
            using (var scope = new OverridablePropertyScope(
                       m_PhaseParameters,
                       new GUIContent(
                           "Forward Scattering",
                           "Concentrates direct light around the sun direction."),
                       this))
            {
                if (!scope.displayed)
                {
                    return;
                }

                Vector4 phase = m_PhaseParameters.value.vector4Value;
                EditorGUI.BeginChangeCheck();
                phase.x = EditorGUILayout.Slider(scope.label, phase.x, 0f, 0.9f);
                phase.y = EditorGUILayout.Slider(
                    new GUIContent(
                        "Backward Scattering",
                        "Adds a restrained response opposite the sun direction."),
                    phase.y,
                    -0.75f,
                    0f);
                phase.z = EditorGUILayout.Slider(
                    new GUIContent(
                        "Forward / Backward Balance",
                        "Zero uses backward scattering; one uses forward scattering."),
                    phase.z,
                    0f,
                    1f);
                phase.w = EditorGUILayout.Slider(
                    new GUIContent(
                        "Directional Light Response",
                        "Overall strength of the directional phase response."),
                    phase.w,
                    0f,
                    4f);
                if (EditorGUI.EndChangeCheck())
                {
                    m_PhaseParameters.value.vector4Value = phase;
                }
            }
        }

        private void DrawWeatherPatternSize()
        {
            using (var scope = new OverridablePropertyScope(
                       m_WeatherMapTiling,
                       new GUIContent(
                           "Weather Pattern Size (World Units)",
                           "World-space length of one weather-map repeat. Bounds Size only reveals more of this fixed-scale pattern."),
                       this))
            {
                if (!scope.displayed)
                {
                    return;
                }

                float tiling = Mathf.Max(
                    m_WeatherMapTiling.value.floatValue,
                    MinTiling);
                float patternSize = 1f / tiling;
                EditorGUI.BeginChangeCheck();
                float newPatternSize = EditorGUILayout.Slider(
                    scope.label,
                    patternSize,
                    MinShapeSize,
                    MaxShapeSize);
                if (EditorGUI.EndChangeCheck())
                {
                    m_WeatherMapTiling.value.floatValue =
                        1f / Mathf.Max(newPatternSize, MinShapeSize);
                }
            }
        }

        private void DrawShapeSize()
        {
            using (var scope = new OverridablePropertyScope(
                       m_ShapeTiling,
                       new GUIContent(
                           "Shape Size (World Units)",
                           "World-space length of one shape-noise repeat. Larger values make larger cloud formations."),
                       this))
            {
                if (!scope.displayed)
                {
                    return;
                }

                float tiling = Mathf.Max(m_ShapeTiling.value.floatValue, MinTiling);
                float detailTiling = Mathf.Max(m_DetailTiling.value.floatValue, MinTiling);
                float detailFrequency = detailTiling / tiling;
                float shapeSize = 1f / tiling;
                EditorGUI.BeginChangeCheck();
                float newShapeSize = EditorGUILayout.Slider(
                    scope.label,
                    shapeSize,
                    MinShapeSize,
                    MaxShapeSize);
                if (EditorGUI.EndChangeCheck())
                {
                    float newTiling = 1f / Mathf.Max(newShapeSize, MinShapeSize);
                    m_ShapeTiling.value.floatValue = newTiling;

                    // Keep the relative detail scale stable when both controls are
                    // overridden by this profile. This makes Shape Size behave like
                    // an artist-facing scale instead of silently changing Detail Frequency.
                    if (m_DetailTiling.overrideState.boolValue)
                    {
                        m_DetailTiling.value.floatValue =
                            Mathf.Max(newTiling * detailFrequency, MinTiling);
                    }
                }
            }
        }

        private void DrawDetailFrequency()
        {
            using (var scope = new OverridablePropertyScope(
                       m_DetailTiling,
                       new GUIContent(
                           "Detail Frequency (x)",
                           "Detail-noise frequency relative to the shape noise. 5x means five detail repeats across one shape repeat."),
                       this))
            {
                if (!scope.displayed)
                {
                    return;
                }

                float shapeTiling = Mathf.Max(m_ShapeTiling.value.floatValue, MinTiling);
                float detailTiling = Mathf.Max(m_DetailTiling.value.floatValue, MinTiling);
                float detailFrequency = detailTiling / shapeTiling;
                EditorGUI.BeginChangeCheck();
                float newDetailFrequency = EditorGUILayout.Slider(
                    scope.label,
                    detailFrequency,
                    MinDetailFrequency,
                    MaxDetailFrequency);
                if (EditorGUI.EndChangeCheck())
                {
                    m_DetailTiling.value.floatValue =
                        Mathf.Max(shapeTiling * newDetailFrequency, MinTiling);
                }
            }
        }

        private void DrawDensityBias()
        {
            using (var scope = new OverridablePropertyScope(
                       m_DensityOffset,
                       new GUIContent(
                           "Density Bias",
                           "Direct additive bias applied to the base cloud density. Negative values reduce coverage."),
                       this))
            {
                if (!scope.displayed)
                {
                    return;
                }

                float densityBias = m_DensityOffset.value.floatValue * 0.01f;
                EditorGUI.BeginChangeCheck();
                float newDensityBias = EditorGUILayout.Slider(
                    scope.label,
                    densityBias,
                    MinDensityBias,
                    MaxDensityBias);
                if (EditorGUI.EndChangeCheck())
                {
                    m_DensityOffset.value.floatValue = newDensityBias * 100f;
                }
            }
        }

        private void DrawDetailNoiseWarnings()
        {
            if (m_DetailNoiseWeight.overrideState.boolValue &&
                m_DetailNoiseWeight.value.floatValue <= 0f)
            {
                EditorGUILayout.HelpBox(
                    "Detail Noise has no effect because Detail Erosion Strength is zero.",
                    MessageType.Warning);
            }

            if (!m_ShapeTiling.overrideState.boolValue ||
                !m_DetailTiling.overrideState.boolValue)
            {
                return;
            }

            float shapeTiling = Mathf.Max(
                m_ShapeTiling.value.floatValue,
                MinTiling);
            float detailFrequency =
                m_DetailTiling.value.floatValue / shapeTiling;
            if (detailFrequency <= 1f)
            {
                EditorGUILayout.HelpBox(
                    "Detail Frequency is not higher than the base Shape Frequency, so the detail texture cannot produce fine cloud structure.",
                    MessageType.Warning);
            }
        }
    }
}
#endif
