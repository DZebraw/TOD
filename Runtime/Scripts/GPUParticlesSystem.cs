using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace DawnTOD
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [ExecuteAlways]
    public class DawnGPUParticleSystem : MonoBehaviour
    {
        private static DawnGPUParticleSystem instance;
        public static DawnGPUParticleSystem Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<DawnGPUParticleSystem>();
                }
                return instance;
            }
        }

        [HideInInspector] public bool ParticleShow = false; //由WeatherController控制
        [HideInInspector] public float baseFallSpeed = 40f;
        [HideInInspector] public float rainDensity = 1.0f;
        [HideInInspector] public float rainWindZRotation = 0f;
        [HideInInspector] public Vector2Int maxParticlesCount = new Vector2Int(512, 512);
        [HideInInspector] public Vector2 emitterSize = new Vector2(35, 35);

        private Mesh particlesMesh;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;

        private ComputeShader rainyParticleUpdateCS;
        private RenderTexture rainyParticleStateRT0;
        private RenderTexture rainyParticleStateRT1;
        private int updateKernel; // Compute Shader的Kernel ID

        private float yOffset = 50f; //后续基于深度图抓到的高度进行向上偏移
        private Camera mainCamera;

        private Material originalMaterial;
        private bool isMaterialHidden = false;

        private bool IsEditorMode => !Application.isPlaying;
        private const float EditorDeltaTime = 0.02f;

        private bool needRebuild = false;

        private void Awake()
        {
            // 单例逻辑仅在运行模式执行
            if (Application.isPlaying)
            {
                if (instance == null)
                {
                    instance = this;
                }
                else if (instance != this)
                {
                    DestroyImmediate(gameObject);
                    return;
                }
            }
        }

        private void OnEnable()
        {
            needRebuild = true;
            CacheMainCamera();

            // 缓存原始材质（确保只缓存一次）
            if (meshRenderer != null && originalMaterial == null)
            {
                originalMaterial = meshRenderer.sharedMaterial;
            }
        }

        private void Update()
        {
            if (needRebuild)
            {
                RebuildParticleSystem();
                needRebuild = false;
            }

            // 区分编辑器模式和运行模式的更新逻辑
            if (IsEditorMode)
            {
                EditorModeUpdate();
            }
            else
            {
                PlayModeUpdate();
            }
        }

        private void RebuildParticleSystem()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();

            if (meshRenderer.sharedMaterial == null)
            {
                meshRenderer.sharedMaterial = new Material(Shader.Find("TOD/RaindropParticle"));
                originalMaterial = meshRenderer.sharedMaterial;
            }

            ValidateAndGenerateMesh();
            InitGPUSimulation();
        }

        private void EditorModeUpdate()
        {
            if (ParticleShow)
            {
                if (isMaterialHidden && meshRenderer != null)
                {
                    meshRenderer.sharedMaterial = originalMaterial;
                    isMaterialHidden = false;
                }

                SyncWithMainCamera();
                UpdateGPUSimulationInEditor();
            }
            else
            {
                if (meshRenderer != null && !isMaterialHidden)
                {
                    Material hiddenMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
                    hiddenMaterial.color = new Color(0, 0, 0, 0); //完全透明
                    hiddenMaterial.renderQueue = int.MaxValue;
                    meshRenderer.sharedMaterial = hiddenMaterial;
                    isMaterialHidden = true;
                }
            }
        }

        private void PlayModeUpdate()
        {
            if (ParticleShow)
            {
                // 恢复粒子渲染
                if (isMaterialHidden && meshRenderer != null)
                {
                    meshRenderer.sharedMaterial = originalMaterial;
                    isMaterialHidden = false;
                }

                SyncWithMainCamera();
                UpdateGPUSimulation();
            }
            else
            {
                if (meshRenderer != null && !isMaterialHidden)
                {
                    Material hiddenMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
                    hiddenMaterial.color = new Color(0, 0, 0, 0); //完全透明
                    hiddenMaterial.renderQueue = int.MaxValue;
                    meshRenderer.sharedMaterial = hiddenMaterial;
                    isMaterialHidden = true;
                }
            }
        }

        private void OnDestroy()
        {
            // 释放资源
            if (rainyParticleStateRT0 != null) rainyParticleStateRT0.Release();
            if (rainyParticleStateRT1 != null) rainyParticleStateRT1.Release();

            // 重置单例
            if (Application.isPlaying && instance == this)
            {
                instance = null;
            }

            // 清理临时材质
            if (isMaterialHidden && meshRenderer != null)
            {
                if (Application.isPlaying)
                    Destroy(meshRenderer.sharedMaterial);
                else
                    DestroyImmediate(meshRenderer.sharedMaterial);
            }

            needRebuild = false;
        }

        #region 摄像机同步逻辑
        private void CacheMainCamera()
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                mainCamera = GameObject.FindGameObjectWithTag("MainCamera")?.GetComponent<Camera>();
            }
        }

        private void SyncWithMainCamera()
        {
            if (mainCamera == null)
            {
                CacheMainCamera();
                if (mainCamera == null)
                {
                    Debug.LogWarning("未找到主摄像机，粒子系统无法同步变换！", this);
                    return;
                }
            }

            Vector3 newPos = transform.position;
            newPos.x = mainCamera.transform.position.x;
            newPos.z = mainCamera.transform.position.z;
            newPos.y = mainCamera.transform.position.y + yOffset;
            transform.position = newPos;
        }
        #endregion

        #region GPU粒子
        // 初始化RenderTexture和Compute Shader
        private void InitGPUSimulation()
        {
            if (rainyParticleUpdateCS == null)
            {
#if UNITY_EDITOR
                string[] csGuids = UnityEditor.AssetDatabase.FindAssets("t:ComputeShader RainyParticleUpdate");
                if (csGuids.Length > 0)
                {
                    string csPath = UnityEditor.AssetDatabase.GUIDToAssetPath(csGuids[0]);
                    rainyParticleUpdateCS = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(csPath);
                }
#endif
                if (rainyParticleUpdateCS == null)
                {
                    Debug.LogWarning($"未在Project中找到名为\"RainyParticleUpdate\"的ComputeShader！请检查文件名称或路径", this);
                    return;
                }
            }

            int texWidth = maxParticlesCount.x;
            int texHeight = maxParticlesCount.y;

            // 创建粒子状态纹理
            rainyParticleStateRT0 = new RenderTexture(texWidth, texHeight, 0, RenderTextureFormat.ARGBFloat);
            rainyParticleStateRT0.enableRandomWrite = true;
            rainyParticleStateRT0.filterMode = FilterMode.Point;
            rainyParticleStateRT0.Create();

            rainyParticleStateRT1 = new RenderTexture(texWidth, texHeight, 0, RenderTextureFormat.ARGBFloat);
            rainyParticleStateRT1.enableRandomWrite = true;
            rainyParticleStateRT1.filterMode = FilterMode.Point;
            rainyParticleStateRT1.Create();

            // 初始化Compute Shader
            updateKernel = rainyParticleUpdateCS.FindKernel("CSMain");
            rainyParticleUpdateCS.SetInts("ParticleCount", texWidth, texHeight);
            rainyParticleUpdateCS.SetVector("EmitterSize", emitterSize);
            rainyParticleUpdateCS.SetFloat("BaseFallSpeed", baseFallSpeed);
            rainyParticleUpdateCS.SetTexture(updateKernel, "ParticleState", rainyParticleStateRT0);
            rainyParticleUpdateCS.SetTexture(updateKernel, "Result", rainyParticleStateRT0);
            rainyParticleUpdateCS.SetFloat("DeltaTime", 0);
            rainyParticleUpdateCS.Dispatch(updateKernel, texWidth / 8, texHeight / 8, 1);

            // 给材质传递RenderTexture
            meshRenderer.sharedMaterial.SetTexture("_ParticleStateRT", rainyParticleStateRT0);
        }

        // 运行模式 - 每帧执行Ping-Pong更新
        private void UpdateGPUSimulation()
        {
            if (rainyParticleUpdateCS == null || rainyParticleStateRT0 == null || rainyParticleStateRT1 == null || meshRenderer == null) return;

            rainyParticleUpdateCS.SetFloat("BaseFallSpeed", baseFallSpeed);
            rainyParticleUpdateCS.SetFloat("RainDensity", rainDensity);

            // 更新粒子状态
            rainyParticleUpdateCS.SetFloat("DeltaTime", Time.deltaTime);
            rainyParticleUpdateCS.SetTexture(updateKernel, "ParticleState", rainyParticleStateRT0);
            rainyParticleUpdateCS.SetTexture(updateKernel, "Result", rainyParticleStateRT1);
            rainyParticleUpdateCS.Dispatch(updateKernel, maxParticlesCount.x / 8, maxParticlesCount.y / 8, 1);

            // 交换纹理
            (rainyParticleStateRT0, rainyParticleStateRT1) = (rainyParticleStateRT1, rainyParticleStateRT0);

            // 更新材质参数
            meshRenderer.sharedMaterial.SetTexture("_ParticleStateRT", rainyParticleStateRT0);
            meshRenderer.sharedMaterial.SetFloat("_RainDensity", rainDensity);
            meshRenderer.sharedMaterial.SetFloat("_WindZRotation", rainWindZRotation);
        }

        // 编辑器模式更新
        private void UpdateGPUSimulationInEditor()
        {
            if (rainyParticleUpdateCS == null || rainyParticleStateRT0 == null || rainyParticleStateRT1 == null || meshRenderer == null) return;

            rainyParticleUpdateCS.SetFloat("BaseFallSpeed", baseFallSpeed);
            rainyParticleUpdateCS.SetFloat("RainDensity", rainDensity);
            rainyParticleUpdateCS.SetFloat("DeltaTime", EditorDeltaTime);

            rainyParticleUpdateCS.SetTexture(updateKernel, "ParticleState", rainyParticleStateRT0);
            rainyParticleUpdateCS.SetTexture(updateKernel, "Result", rainyParticleStateRT1);
            rainyParticleUpdateCS.Dispatch(updateKernel, maxParticlesCount.x / 8, maxParticlesCount.y / 8, 1);

            // 交换纹理
            (rainyParticleStateRT0, rainyParticleStateRT1) = (rainyParticleStateRT1, rainyParticleStateRT0);

            // 更新材质参数
            meshRenderer.sharedMaterial.SetTexture("_ParticleStateRT", rainyParticleStateRT0);
            meshRenderer.sharedMaterial.SetFloat("_RainDensity", rainDensity);
            meshRenderer.sharedMaterial.SetFloat("_WindZRotation", rainWindZRotation);
        }
        #endregion

        #region 网格相关
        /// <summary>
        /// 验证参数并生成粒子网格
        /// </summary>
        public void ValidateAndGenerateMesh()
        {
            // 限制粒子数量为2的幂次（GPU纹理采样优化）
            maxParticlesCount.x = Mathf.Clamp(Mathf.ClosestPowerOfTwo(maxParticlesCount.x), 2, 2048);
            maxParticlesCount.y = Mathf.Clamp(Mathf.ClosestPowerOfTwo(maxParticlesCount.y), 2, 2048);

            // 生成网格
            particlesMesh = GenerateParticlesMeshInternal(maxParticlesCount.x, maxParticlesCount.y, emitterSize);

            // 赋值给MeshFilter
            meshFilter.sharedMesh = particlesMesh;
        }

        /// <summary>
        /// 生成粒子网格的底层逻辑
        /// </summary>
        private Mesh GenerateParticlesMeshInternal(int countX, int countZ, Vector2 meshSize)
        {
            Mesh mesh = new Mesh();
            mesh.name = "SimplifiedGPUParticleMesh";

            // 根据粒子总数选择索引格式
            mesh.indexFormat = countX * countZ > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            // 初始化数据容器
            Vector3[] allVertices = new Vector3[countX * countZ * 4];
            int[] allTris = new int[countX * countZ * 6];
            Vector2[] allUVs = new Vector2[countX * countZ * 4];
            Color[] allColors = new Color[countX * countZ * 4];

            float xStep = meshSize.x / countX;
            float zStep = meshSize.y / countZ;
            int vertexIndex = 0;
            int triIndex = 0;

            // 遍历所有粒子，生成每个粒子的Quad
            for (int z = 0; z < countZ; z++)
            {
                for (int x = 0; x < countX; x++)
                {
                    // 计算粒子中心位置
                    Vector3 particleCenter = new Vector3(
                        x * xStep - meshSize.x / 2,
                        0,
                        z * zStep - meshSize.y / 2
                    );

                    // 赋值顶点
                    allVertices[vertexIndex + 0] = particleCenter; // 左下
                    allVertices[vertexIndex + 1] = particleCenter; // 右下
                    allVertices[vertexIndex + 2] = particleCenter; // 左上
                    allVertices[vertexIndex + 3] = particleCenter; // 右上

                    // 赋值UV
                    allUVs[vertexIndex + 0] = new Vector2(0, 0);
                    allUVs[vertexIndex + 1] = new Vector2(1, 0);
                    allUVs[vertexIndex + 2] = new Vector2(0, 1);
                    allUVs[vertexIndex + 3] = new Vector2(1, 1);

                    // 存储粒子在纹理中的UV坐标（用Color通道传递给Shader）
                    Color particleUV = new Color((float)x / countX, (float)z / countZ, 0, Random.value);
                    allColors[vertexIndex + 0] = particleUV;
                    allColors[vertexIndex + 1] = particleUV;
                    allColors[vertexIndex + 2] = particleUV;
                    allColors[vertexIndex + 3] = particleUV;

                    // 赋值三角面索引
                    int baseTriIndex = vertexIndex;
                    allTris[triIndex + 0] = baseTriIndex + 0;
                    allTris[triIndex + 1] = baseTriIndex + 2;
                    allTris[triIndex + 2] = baseTriIndex + 1;
                    allTris[triIndex + 3] = baseTriIndex + 1;
                    allTris[triIndex + 4] = baseTriIndex + 2;
                    allTris[triIndex + 5] = baseTriIndex + 3;

                    vertexIndex += 4;
                    triIndex += 6;
                }
            }

            // 赋值网格数据
            mesh.vertices = allVertices;
            mesh.triangles = allTris;
            mesh.uv = allUVs;
            mesh.colors = allColors;

            // 重新计算包围盒
            mesh.RecalculateBounds();
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(meshSize.x, 100, meshSize.y));

            return mesh;
        }
        #endregion

        #region 编辑器辅助功能
#if UNITY_EDITOR
        // 绘制发射器范围Gizmos
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0, 0.8f, 1, 0.5f);
            Gizmos.DrawWireCube(transform.position, new Vector3(emitterSize.x, 0, emitterSize.y));
        }

        // 参数校验（仅更新参数，不执行任何组件操作）
        private void OnValidate()
        {
            if (IsEditorMode && rainyParticleUpdateCS != null)
            {
                rainyParticleUpdateCS.SetFloat("BaseFallSpeed", baseFallSpeed);
                rainyParticleUpdateCS.SetVector("EmitterSize", emitterSize);
                rainyParticleUpdateCS.SetInts("ParticleCount", maxParticlesCount.x, maxParticlesCount.y);
                rainyParticleUpdateCS.SetFloat("RainDensity", rainDensity);
            }
        }
#endif
        #endregion
    }
}