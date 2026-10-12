using UnityEditor;
using UnityEngine;

namespace NubisStorm.Editor
{
    public sealed class NubisCirrusEditorWindow : EditorWindow
    {
        [SerializeField] NubisCirrusData data;
        int target,channel,view,undoGroup,control;
        float radius=.055f,value=1,opacity=.5f,hardness=.35f;
        bool reveal=true,painting,dirty=true;
        Vector2 previous,scroll;
        Texture2D preview;
        static readonly string[] Channels={"Coverage","Type","Influence Mask"};
        [MenuItem("Tools/Nubis Clouds/Cirrus NDF Editor")]
        public static void Open()
        {
            var window=GetWindow<NubisCirrusEditorWindow>("Cirrus NDF Editor");
            window.minSize=new Vector2(900,620);
            var volume=NubisStormVolume.Active;
            if(volume && volume.cloudData) window.data=volume.cloudData.cirrus;
            NubisCirrusAuthoring.TrackUndo(window.data);
            window.dirty=true; window.Show();
        }
        void OnEnable() {NubisCirrusAuthoring.TrackUndo(data); Undo.undoRedoPerformed+=UndoChanged; wantsMouseMove=true;}
        void OnDisable() {EndStroke(); Undo.undoRedoPerformed-=UndoChanged; if(preview) DestroyImmediate(preview);}
        void OnLostFocus() => EndStroke();
        void OnProjectChange() {dirty=true; Repaint();}
        void UndoChanged() {NubisCirrusAuthoring.RestoreAfterUndo(data); dirty=true; Repaint();}
        void OnGUI()
        {
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUILayout.VerticalScope(GUILayout.Width(300)))
                {
                    scroll=EditorGUILayout.BeginScrollView(scroll);
                    EditorGUILayout.LabelField("CIRRUS / 2.5D NDF",EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    var selected=(NubisCirrusData)EditorGUILayout.ObjectField("卷云数据",data,typeof(NubisCirrusData),false);
                    if(EditorGUI.EndChangeCheck()) {EndStroke(); data=selected; NubisCirrusAuthoring.TrackUndo(data); dirty=true;}
                    if(!data)
                    {
                        if(GUILayout.Button("补齐共享卷云资产")) {data=NubisCloudAssetBuilder.Build().cirrus; dirty=true;}
                    }
                    else Controls();
                    EditorGUILayout.EndScrollView();
                }
                using(new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(target==0 ? "Procedural Coverage / Type" : target==1 ? "Anchored Cirrus Influence" : "Streaky / Wispy / Round",EditorStyles.boldLabel);
                    float side=Mathf.Max(100,Mathf.Min(position.width-330,position.height-75));
                    Rect rect=GUILayoutUtility.GetRect(side,side,GUILayout.ExpandWidth(false));
                    Refresh(); EditorGUI.DrawRect(rect,Color.black);
                    if(preview) GUI.DrawTexture(rect,preview,ScaleMode.StretchToFill,false);
                    if(data && target==1) Canvas(rect);
                    EditorGUILayout.LabelField("X →   ↑ Z     左键绘制 · Shift 擦除 Mask · Ctrl+Z / Ctrl+Y",EditorStyles.miniLabel);
                }
            }
            if(Event.current.rawType==EventType.MouseUp) EndStroke();
        }
        void Controls()
        {
            EditorGUI.BeginChangeCheck();
            int next=GUILayout.Toolbar(target,new[]{"程序 NDF","手工影响","形态纹理"});
            if(EditorGUI.EndChangeCheck()) {EndStroke(); target=next; view=0; dirty=true;}
            var serial=new SerializedObject(data); serial.Update();
            foreach(string name in new[]{"fieldSize","shapeSize","seed","frequency"}) EditorGUILayout.PropertyField(serial.FindProperty(name));
            if(serial.ApplyModifiedProperties()) {dirty=true; NubisNdfAuthoring.RefreshViews();}
            if(GUILayout.Button("重新生成底图与三种纹理")) {EndStroke(); NubisCirrusAuthoring.Generate(data); dirty=true;}
            EditorGUILayout.HelpBox("生成更新程序 NDF 并恢复 P65 原图形态，保留手工 Mask。Type：0 条带 / 0.5 丝缕 / 1 团状。RGB 分别对应 cr_streaky / cr_wispy / cr_round；密度混合使用原稿 P65 公式。",MessageType.None);
            if(target==1)
            {
                EditorGUI.BeginChangeCheck();
                Vector2 center=EditorGUILayout.Vector2Field("固定中心 XZ（米）",data.authoredCenter);
                Vector2 size=EditorGUILayout.Vector2Field("覆盖范围 XZ（米）",data.authoredSize);
                if(EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(data,"Move cirrus influence"); data.authoredCenter=center;
                    data.authoredSize=Vector2.Max(size,Vector2.one*100); EditorUtility.SetDirty(data); dirty=true; NubisNdfAuthoring.RefreshViews();
                }
                channel=EditorGUILayout.Popup("绘制字段",channel,Channels);
                radius=EditorGUILayout.Slider("半径（图幅）",radius,.005f,.3f);
                value=EditorGUILayout.Slider("目标值",value,0,1); opacity=EditorGUILayout.Slider("强度",opacity,.01f,1);
                hardness=EditorGUILayout.Slider("硬度",hardness,0,1); reveal=EditorGUILayout.Toggle("同时显露 Mask",reveal);
                if(GUILayout.Button("清空影响 Mask（可撤销）"))
                {
                    EndStroke(); Undo.RegisterCompleteObjectUndo(data,"Clear cirrus mask");
                    for(int i=0;i<data.resolution*data.resolution;i++) data.Set(i,2,0);
                    NubisCirrusAuthoring.Save(data); dirty=true;
                }
                if(GUILayout.Button("保存手工图")) NubisCirrusAuthoring.Save(data);
            }
            EditorGUI.BeginChangeCheck();
            view=EditorGUILayout.Popup("预览",view,target==2 ? new[]{"条带 Streaky","丝缕 Wispy","团状 Round"} : target==0 ? new[]{"合成 RGB","Coverage","Type"} : new[]{"合成 RGB × Mask","Coverage","Type","Mask"});
            if(EditorGUI.EndChangeCheck()) dirty=true;
            EditorGUILayout.HelpBox("程序分布与形态噪声分别使用 Cirrus Field Velocity / Cirrus Wind Velocity。手工区域固定，内部细节继续流动。与下层体积云的字段和风速完全独立。",MessageType.Info);
            if(GUILayout.Button("打开世界总览")) NubisWorldMapWindow.Open();
        }
        void Refresh()
        {
            if(!dirty) return; dirty=false;
            if(preview) DestroyImmediate(preview); preview=null;
            if(!data || !data.IsReady) return;
            int n=target==2 ? data.shapes.width : target==0 ? data.weather.width : data.resolution;
            var pixels=target==2 ? data.shapes.GetPixels() : target==0 ? data.weather.GetPixels() : data.authored.GetPixels();
            for(int i=0;i<pixels.Length;i++)
            {
                Color p=pixels[i];
                if(target==2) {float v=p[view]; pixels[i]=new Color(v,v,v,1);}
                else if(view==0) {float c=p.r*(target==1 ? p.b : 1); pixels[i]=new Color(c,c*p.g,c*(1-p.g),1);}
                else {float v=p[view-1]; pixels[i]=new Color(v,v,v,1);}
            }
            preview=new Texture2D(n,n,TextureFormat.RGB24,false) {hideFlags=HideFlags.HideAndDontSave};
            preview.SetPixels(pixels); preview.Apply();
        }
        void Canvas(Rect rect)
        {
            var e=Event.current; control=GUIUtility.GetControlID("CirrusBrush".GetHashCode(),FocusType.Passive);
            if(rect.Contains(e.mousePosition))
            {
                Handles.BeginGUI(); Handles.color=Color.white; Handles.DrawWireDisc(e.mousePosition,Vector3.forward,radius*rect.width); Handles.EndGUI();
                if(e.type==EventType.MouseMove) Repaint();
            }
            Vector2 uv=new Vector2((e.mousePosition.x-rect.x)/rect.width,1-(e.mousePosition.y-rect.y)/rect.height);
            if(e.type==EventType.MouseDown && e.button==0 && rect.Contains(e.mousePosition) && data.IsReady && AssetDatabase.Contains(data))
            {
                Undo.IncrementCurrentGroup(); undoGroup=Undo.GetCurrentGroup(); Undo.RegisterCompleteObjectUndo(data,"Paint cirrus NDF");
                painting=true; GUIUtility.hotControl=control; previous=uv; Stroke(uv,e.shift); e.Use();
            }
            else if(e.type==EventType.MouseDrag && painting) {Stroke(uv,e.shift); e.Use();}
            else if(e.type==EventType.MouseUp && painting) {EndStroke(); e.Use();}
        }
        void Stroke(Vector2 uv,bool erase)
        {
            int count=Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(previous,uv)/(radius*.2f)),1,1024);
            for(int i=1;i<=count;i++) NubisCirrusAuthoring.Paint(data,Vector2.Lerp(previous,uv,i/(float)count),Vector2.one*radius,
                erase ? 2 : channel,erase ? 0 : value,opacity,hardness,!erase && reveal);
            previous=uv; data.Upload(); EditorUtility.SetDirty(data); dirty=true; NubisNdfAuthoring.RefreshViews(); Repaint();
        }
        void EndStroke()
        {
            if(!painting) return; painting=false; NubisCirrusAuthoring.Save(data); Undo.CollapseUndoOperations(undoGroup);
            if(GUIUtility.hotControl==control) GUIUtility.hotControl=0;
        }
    }
}
