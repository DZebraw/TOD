using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[CreateAssetMenu(fileName = "New State",menuName = "TODState",order = 100)]
public class TODStateAsset : ScriptableObject
{
    [Header("Sun Settings")]
    public AnimationCurve sunIntensity = new AnimationCurve(new Keyframe(0f, 130000f), new Keyframe(24f, 130000f));
    public Gradient sunColor = new Gradient();
    
    [Header("Moon Settings")]
    public AnimationCurve moonIntensity = new AnimationCurve(new Keyframe(0f, 5000f), new Keyframe(24f, 5000f));
    public Gradient moonColor = new Gradient();
    public AnimationCurve starEmission = new AnimationCurve(new Keyframe(0f, 1000f), new Keyframe(24f, 1000f));

    [Header("Fog Settings")]
    public AnimationCurve fogDistance = new AnimationCurve();
    public AnimationCurve fogHeight = new AnimationCurve();

    [Header("Cloud Settings")]
    public bool EnableCloud;
}
