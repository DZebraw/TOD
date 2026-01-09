using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public class ModifyHDRPVolume : MonoBehaviour
{
    public Volume volume; 

    void Start()
    {
        if (volume == null)
        {
            Debug.LogError("请指定 Volume 组件！");
            return;
        }

        // 尝试获取 Bloom Override
        if (volume.profile.TryGet(out Bloom bloom))
        {
            // 启用该效果（如果未启用）
            bloom.active = true;

            // 修改参数（例如强度）
            bloom.intensity.value = 0.8f; // 范围通常 0～1 或更高，看具体参数

            Debug.Log("Bloom 强度已设为: " + bloom.intensity.value);
        }
        else
        {
            Debug.LogWarning("Volume Profile 中未找到 Bloom Override，请先在 Inspector 中添加！");
        }
    }
}