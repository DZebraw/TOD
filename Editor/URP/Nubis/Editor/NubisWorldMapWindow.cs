using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NubisStorm.Editor
{
    // Editor-only map. It evaluates the renderer's actual NDF, rather than maintaining a second CPU model.
    public sealed class NubisWorldMapWindow : EditorWindow
    {
        [SerializeField] NubisStormVolume volume;
        [SerializeField] Vector2 center;
        [SerializeField] float extent=64000, brushRadius=700;
        [SerializeField] int mode, tool, paintLayer;
        [SerializeField] int cloudLayer;
        [SerializeField] bool tint=true, guides=true;
        [SerializeField] NubisNdfChannel channel=NubisNdfChannel.InfluenceMask;
        [SerializeField] float value=1, opacity=.5f, hardness=.35f;
        [SerializeField] bool reveal=true;
        Material material;
        RenderTexture preview;
        Vector2 scroll, previousPoint;
        NubisInfluenceData strokeAsset;
        Vector2 strokeCenter, strokeSize;
        int control, undoGroup, signature;
        bool painting, placing, panning, dirty=true;
        double nextUpdate;
        string status="滚轮缩放 · 中键/右键拖动平移";
        static readonly string[] Modes={"合成字段 RGB","覆盖率","顶部类型","底部类型","最低高度","最高高度","手工影响 Mask","风暴 Mask"};
        static readonly string[] Channels={"Min Height","Max Height","Coverage","Top Type","Bottom Type","Influence Mask"};

        [MenuItem("Tools/Nubis Clouds/World Cloud Map")]
        public static void Open()
        {
            var window=GetWindow<NubisWorldMapWindow>("World Cloud Map");
            window.minSize=new Vector2(940,680);
            if(!window.volume) window.volume=NubisStormVolume.Active;
            window.dirty=true; window.Show();
        }
        void OnEnable()
        {
            wantsMouseMove=true;
            TrackUndo();
            Undo.undoRedoPerformed+=UndoChanged;
            EditorApplication.update+=Tick;
        }
        void OnDisable()
        {
            FinishGesture();
            Undo.undoRedoPerformed-=UndoChanged;
            EditorApplication.update-=Tick;
            if(material) DestroyImmediate(material);
            if(preview) {preview.Release(); DestroyImmediate(preview);}
        }
        void OnLostFocus() => FinishGesture();
        void OnProjectChange() {dirty=true; Repaint();}
        void UndoChanged()
        {
            if(volume && volume.cloudData)
            {
                var authored=volume.cloudData.authoredNdf;
                NubisNdfAuthoring.RestoreAfterUndo(authored);
                if(volume.StormNdf && volume.StormNdf!=authored && AssetDatabase.Contains(volume.StormNdf))
                    NubisNdfAuthoring.RestoreAfterUndo(volume.StormNdf);
            }
            dirty=true; NubisNdfAuthoring.RefreshViews(); Repaint();
        }
        void TrackUndo()
        {
            if(!volume || !volume.cloudData) return;
            NubisNdfAuthoring.TrackUndo(volume.cloudData.authoredNdf);
            NubisNdfAuthoring.TrackUndo(volume.StormNdf);
        }
        void Tick()
        {
            if(EditorApplication.timeSinceStartup<nextUpdate) return;
            nextUpdate=EditorApplication.timeSinceStartup+.15;
            if(!volume) volume=NubisStormVolume.Active;
            if(!volume) return;
            TrackUndo();
            int next=volume.HistorySignature()^volume.CirrusSignature()^volume.AnimationTime.GetHashCode();
            if(next!=signature) {signature=next; dirty=true; Repaint();}
        }
        void OnGUI()
        {
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUILayout.VerticalScope(GUILayout.Width(290)))
                {
                    scroll=EditorGUILayout.BeginScrollView(scroll);
                    Controls(); EditorGUILayout.EndScrollView();
                }
                using(new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField("WORLD CLOUD DISTRIBUTION / 世界云分布",EditorStyles.boldLabel);
                    float side=Mathf.Max(100,Mathf.Min(position.width-320,position.height-100));
                    Rect rect=GUILayoutUtility.GetRect(side,side,GUILayout.ExpandWidth(false));
                    if(Event.current.type==EventType.Repaint) RenderPreview();
                    EditorGUI.DrawRect(rect,new Color(.025f,.025f,.03f));
                    if(preview && volume && volume.IsReady) GUI.DrawTexture(rect,preview,ScaleMode.StretchToFill,false);
                    DrawGuides(rect);
                    HandleMap(rect);
                    EditorGUILayout.LabelField($"X →   ↑ Z    图幅 {extent/1000:F1} × {extent/1000:F1} km",EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(status,EditorStyles.wordWrappedMiniLabel);
                }
            }
            if(Event.current.rawType==EventType.MouseUp) FinishGesture();
        }
        void Controls()
        {
            EditorGUILayout.LabelField("NUBIS / WORLD MAP",EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var selected=(NubisStormVolume)EditorGUILayout.ObjectField("场景云模型",volume,typeof(NubisStormVolume),true);
            if(EditorGUI.EndChangeCheck()) {FinishGesture(); volume=selected; dirty=true;}
            if(!volume || !volume.IsReady)
            {
                EditorGUILayout.HelpBox("请打开 SkyClouds / Superstorm 场景，或选择已绑定云数据的 NubisStormVolume。",MessageType.Info); return;
            }
            EditorGUI.BeginChangeCheck();
            int nextLayer=EditorGUILayout.Popup("云子层",cloudLayer,new[]{"层积云 / Stratus","卷云 / Cirrus"});
            if(nextLayer!=cloudLayer) {FinishGesture(); cloudLayer=nextLayer; mode=tool=0; dirty=true;}
            mode=EditorGUILayout.Popup("显示字段",mode,cloudLayer==0 ? Modes : new[]{"合成字段 RGB","Coverage","Type","形态密度","Influence Mask"});
            tint=EditorGUILayout.Toggle("合成图叠加影响色",tint);
            guides=EditorGUILayout.Toggle("区域与相机标识",guides);
            center=EditorGUILayout.Vector2Field("地图中心 XZ（米）",center);
            extent=Mathf.Clamp(EditorGUILayout.FloatField("图幅边长（米）",extent),1000,512000);
            if(EditorGUI.EndChangeCheck()) dirty=true;
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("世界总览"))
                {
                    center=Vector2.zero;
                    float period=cloudLayer==1 && volume.cloudData.cirrus ? volume.cloudData.cirrus.fieldSize : volume.cloudData.fieldSize;
                    extent=Mathf.Clamp(period*4,1000,512000); dirty=true;
                }
                if(GUILayout.Button(cloudLayer==0 ? "定位风暴" : "定位手工区"))
                {
                    var cirrus=volume.cloudData.cirrus;
                    center=cloudLayer==1 && cirrus ? cirrus.authoredCenter : volume.stormOffset;
                    extent=Mathf.Clamp(cloudLayer==1 && cirrus ? Mathf.Max(cirrus.authoredSize.x,cirrus.authoredSize.y)*1.2f : volume.radius*4,1000,512000); dirty=true;
                }
            }
            if(cloudLayer==1)
            {
                var cirrus=volume.cloudData.cirrus;
                EditorGUILayout.HelpBox(cirrus ? $"独立卷云 NDF，周期 {cirrus.fieldSize/1000:F1} km，高度 {volume.EffectiveCirrusAltitude:F0} m。RGB = Coverage / Type×Coverage / (1−Type)×Coverage。青框是卷云手工范围。" : "请在卷云编辑器中补齐数据资产。",MessageType.Info);
                if(GUILayout.Button("编辑卷云 NDF / Influence")) NubisCirrusEditorWindow.Open();
                if(GUILayout.Button("导出卷云预览 PNG")) Export();
                return;
            }
            EditorGUILayout.HelpBox($"程序底图周期 {volume.cloudData.fieldSize/1000:F1} km。地图为球面云模型的 XZ 展开区域，并非整颗行星的唯一气象图。",MessageType.None);
            EditorGUILayout.Space();
            int next=GUILayout.Toolbar(tool,new[]{"浏览","放置风暴","绘制"});
            if(next!=tool) {FinishGesture(); tool=next;}
            if(tool==1)
            {
                EditorGUILayout.HelpBox("左键点击或拖动设置风暴中心，同时启用 Storm Strength=1。修改可撤销；保存场景以保留位置。",MessageType.Info);
            }
            EditorGUI.BeginChangeCheck();
            Vector2 offset=EditorGUILayout.Vector2Field("风暴中心 XZ（米）",volume.stormOffset);
            float radius=Mathf.Max(100,EditorGUILayout.FloatField("风暴半径（米）",volume.radius));
            float strength=EditorGUILayout.Slider("风暴强度",volume.stormStrength,0,1);
            if(EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(volume,"Edit world storm"); volume.stormOffset=offset; volume.radius=radius; volume.stormStrength=strength;
                SceneChanged();
            }
            if(tool==2)
            {
                EditorGUI.BeginChangeCheck();
                paintLayer=GUILayout.Toolbar(paintLayer,new[]{"手工云","风暴"});
                if(EditorGUI.EndChangeCheck()) FinishGesture();
                channel=(NubisNdfChannel)EditorGUILayout.Popup("绘制字段",(int)channel,Channels);
                brushRadius=Mathf.Clamp(EditorGUILayout.FloatField("笔刷半径（米）",brushRadius),10,50000);
                value=EditorGUILayout.Slider("目标值",value,0,1);
                opacity=EditorGUILayout.Slider("强度",opacity,.01f,1);
                hardness=EditorGUILayout.Slider("硬度",hardness,0,1);
                reveal=EditorGUILayout.Toggle("同时显露 Mask",reveal);
                EditorGUILayout.HelpBox("白圈是当前笔刷。左键画，Shift 擦除影响 Mask。仅在所选资产的边框内生效；风暴强度为 0 时绘制不会自动启用风暴。",MessageType.None);
            }
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("青色边框：手工图范围\n橙色边框：风暴 NDF 范围（实际形状见风暴 Mask）\n蓝色标记：主相机\nRGB：Coverage / TopType×Coverage / BottomType×Coverage",MessageType.None);
            EditorGUILayout.HelpBox("合成图读取与三维渲染相同的 NDF 和影响权重。它显示分布字段，不包含高度剖面、噪声侵蚀和光照，不能直接当作俯视云照片。",MessageType.None);
            if(GUILayout.Button("打开局部 NDF 编辑器")) NubisNdfEditorWindow.Open();
            if(GUILayout.Button("导出当前分布 PNG")) Export();
        }
        void RenderPreview()
        {
            if(!dirty || !volume || !volume.IsReady) return;
            if(!material)
            {
                var shader=Shader.Find("Hidden/NubisStorm/WorldMap");
                if(!shader) return;
                material=new Material(shader) {hideFlags=HideFlags.HideAndDontSave};
            }
            if(!preview)
            {
                preview=new RenderTexture(1024,1024,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear)
                    {hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear};
                preview.Create();
            }
            RenderMap(volume,material,preview,center,extent,mode,tint,cloudLayer);
            dirty=false;
        }
        public static void RenderMap(NubisStormVolume source,Material map,RenderTexture destination,Vector2 localCenter,float width,int view,bool influenceTint,int layer=0)
        {
            source.Apply(map);
            map.SetVector("_MapRegion",new Vector4(source.transform.position.x+localCenter.x,source.transform.position.z+localCenter.y,width,width));
            map.SetFloat("_MapMode",view); map.SetFloat("_MapTint",influenceTint ? 1 : 0);
            map.SetFloat("_MapLayer",layer);
            var previous=RenderTexture.active;
            try {Graphics.Blit(Texture2D.blackTexture,destination,map,0);}
            finally {RenderTexture.active=previous;}
        }
        Vector2 MapPoint(Rect rect,Vector2 mouse) => center+new Vector2((mouse.x-rect.x)/rect.width-.5f,.5f-(mouse.y-rect.y)/rect.height)*extent;
        Vector2 ScreenPoint(Rect rect,Vector2 point) => rect.center+Vector2.Scale((point-center)/extent,new Vector2(rect.width,-rect.height));
        void DrawGuides(Rect rect)
        {
            if(!guides || !volume || !volume.cloudData) return;
            GUI.BeginGroup(rect);
            Rect local=new Rect(0,0,rect.width,rect.height);
            Handles.BeginGUI();
            if(cloudLayer==1)
            {
                var cirrus=volume.cloudData.cirrus;
                if(cirrus) Outline(local,cirrus.authoredCenter,Vector2.Max(cirrus.authoredSize,Vector2.one*100),Color.cyan,"CIRRUS AUTHORED");
                Handles.EndGUI(); GUI.EndGroup(); return;
            }
            var a=volume.cloudData.authoredNdf;
            if(a) Outline(local,a.center,Vector2.Max(a.worldSize,Vector2.one*100),Color.cyan,"AUTHORED");
            Outline(local,volume.stormOffset,Vector2.one*volume.radius*2.32f,new Color(1,.45f,.1f),
                volume.stormStrength>0 ? "STORM" : "STORM (OFF)");
            var camera=Camera.main;
            if(camera)
            {
                Vector2 p=ScreenPoint(local,CameraModelXZ(volume,camera.transform.position));
                Handles.color=new Color(.2f,.65f,1);
                Handles.DrawSolidDisc(p,Vector3.forward,4);
                Vector2 q=ScreenPoint(local,CameraModelXZ(volume,camera.transform.position+camera.transform.forward*extent*.03f));
                Handles.DrawLine(p,q);
                GUI.Label(new Rect(p.x+7,p.y-18,130,20),"MAIN CAMERA",EditorStyles.whiteMiniLabel);
            }
            Handles.EndGUI(); GUI.EndGroup();
        }
        void Outline(Rect rect,Vector2 midpoint,Vector2 size,Color color,string label)
        {
            Vector2 a=ScreenPoint(rect,midpoint-size*.5f),b=ScreenPoint(rect,midpoint+size*.5f);
            Handles.color=color;
            Handles.DrawAAPolyLine(2,new Vector3(a.x,a.y),new Vector3(b.x,a.y),new Vector3(b.x,b.y),new Vector3(a.x,b.y),new Vector3(a.x,a.y));
            Vector2 p=ScreenPoint(rect,midpoint);
            Handles.DrawLine(p-Vector2.right*5,p+Vector2.right*5); Handles.DrawLine(p-Vector2.up*5,p+Vector2.up*5);
            GUI.Label(new Rect(b.x+5,b.y,150,20),label,EditorStyles.whiteMiniLabel);
        }
        public static Vector2 CameraModelXZ(NubisStormVolume source,Vector3 world)
        {
            Vector2 offset=new Vector2(world.x-source.transform.position.x,world.z-source.transform.position.z);
            if(!source.sphericalSky) return offset;
            double r=Math.Max(10000,source.planetRadius),horizontal=offset.magnitude;
            return offset*(float)(Math.Atan2(horizontal,world.y+r)*r/Math.Max(horizontal,1e-5));
        }
        void HandleMap(Rect rect)
        {
            control=GUIUtility.GetControlID("NubisWorldMap".GetHashCode(),FocusType.Passive);
            var e=Event.current;
            bool inside=rect.Contains(e.mousePosition);
            if(inside)
            {
                var point=MapPoint(rect,e.mousePosition);
                if(e.type==EventType.MouseMove) {status=$"云模型 XZ：{point.x:F0}, {point.y:F0} m · 滚轮缩放 / 中键平移"; Repaint();}
                if(tool==2)
                {
                    GUI.BeginGroup(rect); Handles.BeginGUI(); Handles.color=Color.white;
                    Handles.DrawWireDisc(e.mousePosition-rect.position,Vector3.forward,brushRadius/extent*rect.width);
                    Handles.EndGUI(); GUI.EndGroup();
                }
                if(e.type==EventType.ScrollWheel && !painting && !placing)
                {
                    extent=Mathf.Clamp(extent*Mathf.Exp(e.delta.y*.08f),1000,512000);
                    center+=point-MapPoint(rect,e.mousePosition); dirty=true; Repaint(); e.Use();
                }
            }
            if(e.type==EventType.MouseDown && inside && volume && volume.IsReady)
            {
                if(e.button==1 || e.button==2 || (e.button==0 && tool==0)) panning=true;
                else if(e.button==0 && tool==1 && volume.StormNdf && volume.StormNdf.IsReady)
                {
                    BeginUndo("Place world storm"); Undo.RegisterCompleteObjectUndo(volume,"Place world storm"); placing=true;
                    Place(MapPoint(rect,e.mousePosition));
                }
                else if(e.button==0 && tool==2)
                {
                    strokeAsset=paintLayer==0 ? volume.cloudData.authoredNdf : volume.StormNdf;
                    if(!strokeAsset || !strokeAsset.IsReady || !AssetDatabase.Contains(strokeAsset))
                    {status="先在局部 NDF 编辑器中创建并绑定已保存的 Influence 资产。"; strokeAsset=null; return;}
                    strokeCenter=paintLayer==0 ? strokeAsset.center : volume.stormOffset;
                    strokeSize=paintLayer==0 ? Vector2.Max(strokeAsset.worldSize,Vector2.one*100) : Vector2.one*volume.radius*2.32f;
                    BeginUndo("Paint world NDF"); Undo.RegisterCompleteObjectUndo(strokeAsset,"Paint world NDF"); painting=true;
                    previousPoint=MapPoint(rect,e.mousePosition); Stroke(previousPoint,previousPoint,e.shift);
                }
                if(panning || placing || painting) {GUIUtility.hotControl=control; e.Use();}
            }
            else if(e.type==EventType.MouseDrag && GUIUtility.hotControl==control)
            {
                if(panning) {center+=new Vector2(-e.delta.x,e.delta.y)*extent/rect.width; dirty=true;}
                else if(placing) Place(MapPoint(rect,e.mousePosition));
                else if(painting)
                {
                    Vector2 p=MapPoint(rect,e.mousePosition); Stroke(previousPoint,p,e.shift); previousPoint=p;
                }
                Repaint(); e.Use();
            }
            else if(e.type==EventType.MouseUp && GUIUtility.hotControl==control)
            {FinishGesture(); e.Use();}
        }
        void BeginUndo(string name) {Undo.IncrementCurrentGroup(); undoGroup=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName(name);}
        void Place(Vector2 point) {volume.stormOffset=point; volume.stormStrength=1; SceneChanged();}
        void SceneChanged()
        {
            EditorUtility.SetDirty(volume); PrefabUtility.RecordPrefabInstancePropertyModifications(volume);
            if(!Application.isPlaying) EditorSceneManager.MarkSceneDirty(volume.gameObject.scene);
            dirty=true; NubisNdfAuthoring.RefreshViews();
        }
        void Stroke(Vector2 from,Vector2 to,bool erase)
        {
            int steps=Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(from,to)/Mathf.Max(10,brushRadius*.2f)),1,2048);
            for(int i=1;i<=steps;i++)
            {
                Vector2 p=Vector2.Lerp(from,to,i/(float)steps)-strokeCenter;
                NubisNdfAuthoring.Paint(strokeAsset,new Vector2(p.x/strokeSize.x,p.y/strokeSize.y)+Vector2.one*.5f,
                    new Vector2(brushRadius/strokeSize.x,brushRadius/strokeSize.y),erase ? NubisNdfChannel.InfluenceMask : channel,
                    erase ? 0 : value,opacity,hardness,!erase && reveal);
            }
            strokeAsset.Upload(); EditorUtility.SetDirty(strokeAsset); dirty=true; NubisNdfAuthoring.RefreshViews();
        }
        void FinishGesture()
        {
            bool changed=painting || placing;
            if(painting && strokeAsset) NubisNdfAuthoring.Save(strokeAsset);
            if(changed) Undo.CollapseUndoOperations(undoGroup);
            if(GUIUtility.hotControl==control) GUIUtility.hotControl=0;
            painting=placing=panning=false; strokeAsset=null;
            if(changed) {status="修改完成，可 Ctrl+Z 撤销。风暴位置请随场景保存。"; dirty=true;}
        }
        void Export()
        {
            RenderPreview(); if(!preview) return;
            string path=EditorUtility.SaveFilePanel("Export world cloud distribution","","World-Cloud-Map.png","png");
            if(string.IsNullOrEmpty(path)) return;
            var previous=RenderTexture.active;
            var image=new Texture2D(preview.width,preview.height,TextureFormat.RGB24,false,true);
            try {RenderTexture.active=preview; image.ReadPixels(new Rect(0,0,preview.width,preview.height),0,0); image.Apply(); File.WriteAllBytes(path,image.EncodeToPNG());}
            finally {RenderTexture.active=previous; DestroyImmediate(image);}
            status="已导出分布数据预览（不含编辑器边框与标识）。";
        }
    }
}
