using UnityEditor;
using UnityEngine;

namespace NubisStorm.Editor
{
    public static class NubisWeatherAuthoring
    {
        const string Root="Assets/NubisCloud/Generated/Weather";
        [MenuItem("Tools/Nubis Clouds/Create clear and overcast weather presets")]
        public static void CreateExamples()
        {
            System.IO.Directory.CreateDirectory(Root);
            Create(Root+"/WeatherClear.asset",193,-.17f,-.22f);
            Create(Root+"/WeatherOvercast.asset",479,.21f,.27f);
            AssetDatabase.SaveAssets();
        }
        static void Create(string path,int seed,float bias,float cirrusBias)
        {
            // Re-running this menu never overwrites an edited preset.
            if(AssetDatabase.LoadAssetAtPath<NubisWeatherPreset>(path)) return;
            var preset=ScriptableObject.CreateInstance<NubisWeatherPreset>();
            preset.seed=seed; preset.coverageBias=bias; preset.cirrusCoverageBias=cirrusBias;
            AssetDatabase.CreateAsset(preset,path); Generate(preset);
        }
        public static void Generate(NubisWeatherPreset preset)
        {
            NubisCloudAssetBuilder.BuildNdf(preset.seed,preset.frequency,preset.coverageBias,out var fields,out var bottom);
            preset.weather=Store(preset,preset.weather,fields);
            preset.bottomType=Store(preset,preset.bottomType,bottom);
            const int n=256;
            var pixels=new Color[n*n]; uint seed=unchecked((uint)preset.seed);
            for(int y=0;y<n;y++) for(int x=0;x<n;x++)
            {
                float u=(x+.5f)/n,v=(y+.5f)/n;
                float noise=NubisCloudAssetBuilder.Perlin(u,v,.317f,3,seed)
                    +.5f*NubisCloudAssetBuilder.Perlin(u,v,.317f,6,seed+127);
                float coverage=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.32f,.38f,noise+preset.cirrusCoverageBias));
                float type=Mathf.Clamp01(.45f+NubisCloudAssetBuilder.Perlin(u+.23f,v+.41f,.317f,3,seed+13)*.8f);
                pixels[x+y*n]=new Color(coverage,type,0,1);
            }
            var cirrus=new Texture2D(n,n,TextureFormat.RGHalf,false,true)
                {name="Weather cirrus coverage-type",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear};
            cirrus.SetPixels(pixels); cirrus.Apply(false,false);
            preset.cirrusWeather=Store(preset,preset.cirrusWeather,cirrus);
            EditorUtility.SetDirty(preset); AssetDatabase.SaveAssets(); NubisNdfAuthoring.RefreshViews();
        }
        static Texture2D Store(NubisWeatherPreset owner,Texture2D existing,Texture2D generated)
        {
            // Do not mutate a texture referenced from another asset.
            if(existing && AssetDatabase.GetAssetPath(existing)==AssetDatabase.GetAssetPath(owner))
            {
                EditorUtility.CopySerialized(generated,existing); Object.DestroyImmediate(generated);
                existing.Apply(false,false); EditorUtility.SetDirty(existing); return existing;
            }
            AssetDatabase.AddObjectToAsset(generated,owner); return generated;
        }
    }
    [CustomEditor(typeof(NubisWeatherPreset))]
    public sealed class NubisWeatherPresetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var preset=(NubisWeatherPreset)target;
            EditorGUILayout.HelpBox("只保存程序天气字段；剖面、三维噪声、手工 Influence 和风暴继续使用场景的共享数据。",MessageType.None);
            if(GUILayout.Button("生成 / 更新此天气的 NDF")) NubisWeatherAuthoring.Generate(preset);
        }
    }
    [CustomEditor(typeof(NubisStormVolume))]
    public sealed class NubisStormVolumeInspector : UnityEditor.Editor
    {
        NubisWeatherPreset transitionTarget;
        void OnEnable() {transitionTarget=((NubisStormVolume)target).weatherTo;}
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var volume=(NubisStormVolume)target;
            if((volume.weatherFrom && !volume.weatherFrom.IsReady) || (volume.weatherTo && !volume.weatherTo.IsReady))
                EditorGUILayout.HelpBox("天气预设尚未生成 NDF，请在预设 Inspector 中生成。",MessageType.Warning);
            EditorGUILayout.HelpBox("编辑模式可设置 From / To 并拖动 Blend。播放时请选择下方过渡目标，避免直接改动正在混合的端点。空目标代表共享底图；过渡保留手工影响，期间完整积分，稳定后恢复稀疏更新。",MessageType.None);
            using(new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                transitionTarget=(NubisWeatherPreset)EditorGUILayout.ObjectField("运行时过渡目标",transitionTarget,typeof(NubisWeatherPreset),false);
                if(GUILayout.Button(volume.IsWeatherTransitioning ? "请求下一段天气过渡" : "开始天气过渡"))
                    if(!volume.TransitionToWeather(transitionTarget,volume.weatherTransitionSeconds))
                        Debug.LogWarning("天气预设尚未生成 NDF。",transitionTarget);
            }
        }
    }
}
