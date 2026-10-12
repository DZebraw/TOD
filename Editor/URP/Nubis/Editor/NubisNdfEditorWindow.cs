using System.IO;
using UnityEditor;
using UnityEngine;

namespace NubisStorm.Editor
{
    public sealed class NubisNdfEditorWindow : EditorWindow
    {
        NubisCloudData data;
        NubisInfluenceData asset;
        int target=0, previewMode, undoGroup;
        NubisNdfChannel channel=NubisNdfChannel.InfluenceMask;
        float radius=0.055f,opacity=0.5f,hardness=0.35f,value=1,eye=0.19f;
        bool reveal=true,painting,showBase=true;
        Vector2 previousUV,scroll;
        Texture2D preview;
        bool previewDirty=true;
        string status="左键绘制；Shift 擦除 Mask；Ctrl+Z / Ctrl+Y 撤销与重做。";
        static readonly string[] Channels={"Min Height","Max Height","Coverage","Top Type","Bottom Type","Influence Mask"};
        static readonly string[] Views={"Composite · RGB","Min Height","Max Height","Coverage","Top Type","Bottom Type","Influence Mask"};

        [MenuItem("Tools/Nubis Clouds/NDF Editor")]
        public static void Open()
        {
            var window=GetWindow<NubisNdfEditorWindow>("Nubis NDF Editor");
            window.minSize=new Vector2(900,650);
            if(NubisStormVolume.Active) window.data=NubisStormVolume.Active.cloudData;
            window.SelectTarget(); window.Show();
        }
        public static void OpenAsset(NubisInfluenceData selected)
        {
            var window=GetWindow<NubisNdfEditorWindow>("Nubis NDF Editor");
            window.minSize=new Vector2(900,650); window.asset=selected; window.target=1; window.previewDirty=true;
            NubisNdfAuthoring.TrackUndo(selected); window.Show();
        }
        void OnEnable()
        {
            foreach(var item in Resources.FindObjectsOfTypeAll<NubisInfluenceData>()) NubisNdfAuthoring.TrackUndo(item);
            Undo.undoRedoPerformed+=UndoChanged; wantsMouseMove=true;
        }
        void OnDisable() { EndStroke(); Undo.undoRedoPerformed-=UndoChanged; if(preview) DestroyImmediate(preview); }
        void OnLostFocus() => EndStroke();
        void OnProjectChange()
        {
            if(!data && NubisStormVolume.Active) data=NubisStormVolume.Active.cloudData;
            if(!asset) SelectTarget();
            previewDirty=true; Repaint();
        }
        void UndoChanged()
        {
            foreach(var item in Resources.FindObjectsOfTypeAll<NubisInfluenceData>())
                NubisNdfAuthoring.RestoreAfterUndo(item);
            previewDirty=true; NubisNdfAuthoring.RefreshViews(); Repaint();
        }
        void SelectTarget()
        {
            EndStroke();
            asset=target==2 ? (NubisStormVolume.Active ? NubisStormVolume.Active.StormNdf : (data ? data.superstormNdf : null)) : (data ? data.authoredNdf : null);
            NubisNdfAuthoring.TrackUndo(asset);
            previewDirty=true;
        }
        void OnGUI()
        {
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUILayout.VerticalScope(GUILayout.Width(285)))
                {
                    scroll=EditorGUILayout.BeginScrollView(scroll);
                    EditorGUILayout.LabelField("NUBIS / NDF AUTHORING",EditorStyles.boldLabel);
                    if(GUILayout.Button("打开世界云分布总览")) NubisWorldMapWindow.Open();
                    if(GUILayout.Button("卷云 / Cirrus NDF 编辑器")) NubisCirrusEditorWindow.Open();
                    EditorGUI.BeginChangeCheck();
                    data=(NubisCloudData)EditorGUILayout.ObjectField("Shared Cloud Data",data,typeof(NubisCloudData),false);
                    int next=GUILayout.Toolbar(target,new[]{"生成底图","手工影响","风暴影响"});
                    if(EditorGUI.EndChangeCheck() || next!=target) {target=next; SelectTarget();}
                    EditorGUILayout.Space();
                    if(target==0) GeneratorControls(); else PaintControls();
                    EditorGUILayout.Space();
                    EditorGUI.BeginChangeCheck();
                    previewMode=EditorGUILayout.Popup("预览通道",previewMode,Views);
                    if(EditorGUI.EndChangeCheck()) previewDirty=true;
                    EditorGUILayout.HelpBox("Composite 是字段伪彩色：R=Coverage，G=TopType×Coverage，B=BottomType×Coverage。影响图再乘 Mask；黑色代表不覆盖底图。不是 RGB 云照片。",MessageType.Info);
                    if(GUILayout.Button("导出当前预览 PNG")) ExportPreview();
                    EditorGUILayout.HelpBox("程序底图可随 Field Velocity 流动；手工影响图固定在中心/范围内。Wind Velocity 只驱动三维噪声。风暴区域的噪声仍可旋转。",MessageType.None);
                    EditorGUILayout.EndScrollView();
                }
                using(new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(target==0 ? "Procedural NDF" : target==1 ? "World-anchored Influence NDF" : "Superstorm Influence NDF",EditorStyles.boldLabel);
                    float size=Mathf.Max(100,Mathf.Min(position.width-315,position.height-90));
                    Rect rect=GUILayoutUtility.GetRect(size,size,GUILayout.ExpandWidth(false));
                    RefreshPreview();
                    EditorGUI.DrawRect(rect,new Color(0.025f,0.025f,0.025f));
                    if(preview) GUI.DrawTexture(rect,preview,ScaleMode.StretchToFill,false);
                    if(target!=0 && asset) HandleCanvas(rect);
                    EditorGUILayout.LabelField("X →     ↑ Z     "+status,EditorStyles.wordWrappedMiniLabel);
                }
            }
            if(painting && Event.current.type==EventType.MouseUp) EndStroke();
        }
        void GeneratorControls()
        {
            if(!data) {EditorGUILayout.HelpBox("先选择场景的 SharedCloudData。",MessageType.Info); return;}
            var serialized=new SerializedObject(data);
            serialized.Update();
            EditorGUILayout.PropertyField(serialized.FindProperty("weatherSeed"),new GUIContent("Seed"));
            EditorGUILayout.PropertyField(serialized.FindProperty("weatherFrequency"),new GUIContent("Frequency"));
            EditorGUILayout.PropertyField(serialized.FindProperty("weatherCoverageBias"),new GUIContent("Coverage Bias"));
            serialized.ApplyModifiedProperties();
            if(GUILayout.Button("重新生成程序底图"))
            {
                NubisCloudAssetBuilder.RegenerateWeather(data); previewDirty=true;
                status="已更新程序底图；手工和风暴资产保留。";
            }
            EditorGUILayout.HelpBox("生成器控制大范围云分布。要定制局部云景，切换到手工影响后绘制，重新生成底图不会覆盖手工图。",MessageType.None);
        }
        void PaintControls()
        {
            EditorGUI.BeginChangeCheck();
            var selected=(NubisInfluenceData)EditorGUILayout.ObjectField("Influence Asset",asset,typeof(NubisInfluenceData),false);
            if(EditorGUI.EndChangeCheck()) {EndStroke(); asset=selected; NubisNdfAuthoring.TrackUndo(asset); previewDirty=true;}
            if(GUILayout.Button("新建空白 Influence 资产"))
            {
                string path=EditorUtility.SaveFilePanelInProject("New Influence NDF","InfluenceNDF","asset","保存可编辑的数据资产");
                if(!string.IsNullOrEmpty(path)) {asset=NubisNdfAuthoring.Create(path); AssignAsset(); previewDirty=true;}
            }
            if(!asset) {EditorGUILayout.HelpBox("选择或新建一份 Influence NDF。",MessageType.Info); return;}
            if(!asset.IsReady) NubisNdfAuthoring.Save(asset);
            EditorGUI.BeginChangeCheck();
            showBase=EditorGUILayout.Toggle("Composite 叠加程序底图",showBase);
            if(EditorGUI.EndChangeCheck()) previewDirty=true;
            if(GUILayout.Button(target==2 ? "应用到当前风暴" : "应用到 SharedCloudData")) AssignAsset();
            if(target==1)
            {
                EditorGUI.BeginChangeCheck();
                Vector2 center=EditorGUILayout.Vector2Field("固定中心 XZ（米）",asset.center);
                Vector2 size=EditorGUILayout.Vector2Field("覆盖范围 XZ（米）",asset.worldSize);
                if(EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(asset,"Move authored NDF"); asset.center=center;
                    asset.worldSize=new Vector2(Mathf.Max(100,size.x),Mathf.Max(100,size.y)); EditorUtility.SetDirty(asset);
                    NubisNdfAuthoring.RefreshViews();
                }
            }
            else EditorGUILayout.HelpBox("风暴使用场景 Storm Offset / Radius 定位与缩放；Mask 同时控制字段覆盖、旋转、砧状结构和内光。",MessageType.None);
            channel=(NubisNdfChannel)EditorGUILayout.Popup("绘制字段",(int)channel,Channels);
            radius=EditorGUILayout.Slider("笔刷半径（图幅）",radius,0.005f,0.3f);
            opacity=EditorGUILayout.Slider("强度",opacity,0.01f,1);
            hardness=EditorGUILayout.Slider("硬度",hardness,0,1);
            value=EditorGUILayout.Slider("目标数值",value,0,1);
            reveal=EditorGUILayout.Toggle("绘制字段时显露 Mask",reveal);
            EditorGUILayout.HelpBox("先画 Mask 定位，再调整 Coverage / Type / Height。Coverage=0 且 Mask=1 可清空局部天空；Shift 只擦除影响，恢复底图。",MessageType.None);
            if(GUILayout.Button("云团模板（替换本图，可撤销）")) ChangeAsset(()=>NubisNdfAuthoring.CloudPreset(asset));
            eye=EditorGUILayout.Slider("风暴模板眼半径",eye,0.04f,0.45f);
            if(GUILayout.Button("风暴模板（替换本图，可撤销）")) ChangeAsset(()=>NubisNdfAuthoring.StormPreset(asset,eye));
            if(GUILayout.Button("清空影响 Mask（保留五个字段）")) ChangeAsset(()=> {for(int i=0;i<asset.resolution*asset.resolution;i++) asset.Set(i,5,0);});
            if(GUILayout.Button("另存副本"))
            {
                string path=EditorUtility.SaveFilePanelInProject("Copy Influence NDF",asset.name+" Copy","asset","保存独立的可编辑副本");
                if(!string.IsNullOrEmpty(path))
                {
                    var copy=NubisNdfAuthoring.Create(path); copy.resolution=asset.resolution; copy.source=(byte[])asset.source.Clone(); copy.center=asset.center; copy.worldSize=asset.worldSize;
                    NubisNdfAuthoring.Save(copy); asset=copy; AssignAsset(); previewDirty=true;
                }
            }
            if(GUILayout.Button("保存")) {NubisNdfAuthoring.Save(asset); status="已保存："+AssetDatabase.GetAssetPath(asset);}
        }
        void ChangeAsset(System.Action action)
        {
            EndStroke(); Undo.RegisterCompleteObjectUndo(asset,"Edit Influence NDF"); action();
            NubisNdfAuthoring.Save(asset); previewDirty=true; NubisNdfAuthoring.RefreshViews();
        }
        void AssignAsset()
        {
            if(target==2 && NubisStormVolume.Active)
            {
                Undo.RecordObject(NubisStormVolume.Active,"Assign storm NDF"); NubisStormVolume.Active.stormNdf=asset;
                EditorUtility.SetDirty(NubisStormVolume.Active);
            }
            else if(data) {Undo.RecordObject(data,"Assign authored NDF"); data.authoredNdf=asset; EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();}
            NubisNdfAuthoring.RefreshViews();
        }
        void HandleCanvas(Rect rect)
        {
            var e=Event.current;
            if(rect.Contains(e.mousePosition))
            {
                Handles.BeginGUI(); Handles.color=Color.white;
                Handles.DrawWireDisc(e.mousePosition,Vector3.forward,radius*rect.width); Handles.EndGUI();
                if(e.type==EventType.MouseMove) Repaint();
            }
            if(e.type==EventType.MouseDown && e.button==0 && rect.Contains(e.mousePosition))
            {
                Undo.IncrementCurrentGroup(); undoGroup=Undo.GetCurrentGroup();
                Undo.RegisterCompleteObjectUndo(asset,"Paint Influence NDF"); painting=true;
                previousUV=new Vector2((e.mousePosition.x-rect.x)/rect.width,1-(e.mousePosition.y-rect.y)/rect.height);
                Stroke(previousUV,previousUV,e.shift); e.Use();
            }
            else if(e.type==EventType.MouseDrag && e.button==0 && painting)
            {
                var uv=new Vector2(Mathf.Clamp01((e.mousePosition.x-rect.x)/rect.width),Mathf.Clamp01(1-(e.mousePosition.y-rect.y)/rect.height));
                Stroke(previousUV,uv,e.shift); previousUV=uv; e.Use();
            }
            else if(e.type==EventType.MouseUp && painting) {EndStroke(); e.Use();}
        }
        void Stroke(Vector2 from,Vector2 to,bool eraseMask)
        {
            int steps=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(from,to)/Mathf.Max(0.001f,radius*0.2f)));
            for(int i=1;i<=steps;i++) NubisNdfAuthoring.Paint(asset,Vector2.Lerp(from,to,i/(float)steps),radius,
                eraseMask ? NubisNdfChannel.InfluenceMask : channel,eraseMask ? 0 : value,opacity,hardness,!eraseMask && reveal);
            asset.Upload(); EditorUtility.SetDirty(asset); previewDirty=true; NubisNdfAuthoring.RefreshViews(); Repaint();
            status="绘制中 · "+Channels[(int)channel];
        }
        void EndStroke()
        {
            if(!painting) return;
            painting=false; NubisNdfAuthoring.Save(asset); Undo.CollapseUndoOperations(undoGroup);
            status="笔画已保存，可撤销。";
        }
        void RefreshPreview()
        {
            if(!previewDirty) return; previewDirty=false;
            Color[] pixels=null; int n=256;
            if(target==0 && data && data.weather)
            {
                n=data.weather.width; var a=data.weather.GetPixels(); var b=data.bottomType.GetPixels(); pixels=new Color[a.Length];
                for(int i=0;i<a.Length;i++)
                {
                    if(previewMode==0) pixels[i]=new Color(a[i].b,a[i].a*a[i].b,b[i].r*a[i].b,1);
                    else {float v=previewMode<=4 ? a[i][previewMode-1] : previewMode==5 ? b[i].r : 0; pixels[i]=new Color(v,v,v,1);}
                }
            }
            else if(asset)
            {
                n=asset.resolution; pixels=NubisNdfAuthoring.Preview(asset,previewMode-1);
                if(showBase && previewMode==0 && data && data.weather && data.bottomType)
                {
                    var baseFields=data.weather.GetPixels(); var baseBottom=data.bottomType.GetPixels();
                    var volume=NubisStormVolume.Active;
                    Vector2 extent=target==2 && volume ? Vector2.one*volume.radius*2.32f : asset.worldSize;
                    Vector2 center=target==2 && volume ? volume.stormOffset : asset.center;
                    Vector2 drift=volume ? volume.fieldVelocity*volume.AnimationTime : Vector2.zero;
                    for(int y=0;y<n;y++) for(int x=0;x<n;x++)
                    {
                        int i=x+y*n;
                        Vector2 p=center+Vector2.Scale(new Vector2((x+0.5f)/n-.5f,(y+0.5f)/n-.5f),extent);
                        Vector2 uv=(p-drift)/data.fieldSize+Vector2.one*.5f;
                        Color fields=Bilinear(baseFields,data.weather.width,uv);
                        float bottom=Bilinear(baseBottom,data.bottomType.width,uv).r;
                        float mask=asset.Get(i,5);
                        fields=Color.Lerp(fields,new Color(asset.Get(i,0),asset.Get(i,1),asset.Get(i,2),asset.Get(i,3)),mask);
                        bottom=Mathf.Lerp(bottom,asset.Get(i,4),mask);
                        pixels[i]=new Color(fields.b,fields.a*fields.b,bottom*fields.b,1);
                    }
                }
            }
            if(preview) DestroyImmediate(preview);
            preview=null; if(pixels==null) return;
            preview=new Texture2D(n,n,TextureFormat.RGB24,false) {hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point};
            preview.SetPixels(pixels); preview.Apply();
        }
        static Color Bilinear(Color[] pixels,int n,Vector2 uv)
        {
            float x=Mathf.Repeat(uv.x,1)*n-.5f,y=Mathf.Repeat(uv.y,1)*n-.5f;
            int ix=Mathf.FloorToInt(x),iy=Mathf.FloorToInt(y); float fx=x-ix,fy=y-iy;
            int x0=(ix+n)%n,y0=(iy+n)%n,x1=(x0+1)%n,y1=(y0+1)%n;
            return Color.Lerp(Color.Lerp(pixels[x0+y0*n],pixels[x1+y0*n],fx),Color.Lerp(pixels[x0+y1*n],pixels[x1+y1*n],fx),fy);
        }
        void ExportPreview()
        {
            RefreshPreview(); if(!preview) return;
            string path=EditorUtility.SaveFilePanel("Export NDF preview","", "NDF-preview.png","png");
            if(!string.IsNullOrEmpty(path)) {File.WriteAllBytes(path,preview.EncodeToPNG()); status="预览已导出（数据仍保存于 .asset）。";}
        }
    }
    [CustomEditor(typeof(NubisInfluenceData))]
    public sealed class NubisInfluenceInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if(GUILayout.Button("Open NDF Editor")) NubisNdfEditorWindow.OpenAsset((NubisInfluenceData)target);
        }
    }
}
