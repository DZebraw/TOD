using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public class TODController : MonoBehaviour
{
    [Range(0,24)]
    public float timeOfDay;

    public float oritSpeed = 1.0f;
    public Light sun;
    public Light moon;
    public Volume skyVolume;
    
    public List<TODStateAsset> TODStates = new List<TODStateAsset>();

    public AnimationCurve sunIntensity;
    
    private bool isNight;

    void Start()
    {
    }

    void Update()
    {
        timeOfDay += Time.deltaTime * oritSpeed;
        if(timeOfDay>24)
            timeOfDay = 0;

        UpdateTime();
    }

    private void OnValidate()
    {
        UpdateTime();
        ApplyCurve();

        sunIntensity = CalculateFinalCurve();
    }

    private AnimationCurve CalculateFinalCurve()
    {
        AnimationCurve finalCurve = new AnimationCurve();
        foreach (var todState in TODStates)
        {
            //TODO:在这里通过编辑器设置的权重来计算最终曲线数值
            //目前先假设只有一个state
            finalCurve = todState.sunIntensity;
        }
        return finalCurve;
    }

    private void ApplyCurve()
    {
        sun.intensity = sunIntensity.Evaluate(timeOfDay);
    }

    private void UpdateTime() 
    {
        float alpha = timeOfDay / 24.0f;
        float sunRotation = Mathf.Lerp(-90, 270, alpha);
        float moonRotation = sunRotation - 180;

        sun.transform.rotation = Quaternion.Euler(sunRotation, -150.0f, 0);
        moon.transform.rotation = Quaternion.Euler(moonRotation, -150.0f, 0);

        CheckNightDayTransition(); 
    }

    private void CheckNightDayTransition()
    {
        if(isNight)
        {
            if(moon.transform.rotation.eulerAngles.x > 180)
            {
                StartDay();
            }
        }
        else
        {
            if(sun.transform.rotation.eulerAngles.x > 180)
            {
                StartNight();
            }
        }
    }

    private void StartDay()
    {
        isNight = false;
        sun.shadows = LightShadows.Soft;
        moon.shadows = LightShadows.None;
    }

    private void StartNight()
    {
        isNight = true;
        sun.shadows = LightShadows.None;
        moon.shadows = LightShadows.Soft;
    }
}