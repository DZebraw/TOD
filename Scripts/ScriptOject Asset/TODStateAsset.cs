using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New State",menuName = "TODState",order = 100)]
public class TODStateAsset : ScriptableObject
{
    public AnimationCurve sunIntensity = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(24f, 1f))
    {
        preWrapMode = WrapMode.Loop,
        postWrapMode = WrapMode.Loop
    };
    
}
