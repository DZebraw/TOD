using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Curve",menuName = "Custom/Float Curve Asset",order = 100)]
public class FloatCurveAsset : ScriptableObject
{
    //[Tooltip("可编辑的动画曲线")]
    public AnimationCurve curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(24f, 1f))
    {
        preWrapMode = WrapMode.Loop,
        postWrapMode = WrapMode.Loop
    };

    //输入x:time用于读取曲线的y值
    public float Evaluate(float time)
    {
        return curve.Evaluate(time);
    }
}
