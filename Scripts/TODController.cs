using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UIElements;

public class TODController : MonoBehaviour
{
    public static TODController instance;
    [Range(0,24)]
    public float timeOfDay;
    private float alpha;

    public float oritSpeed = 1.0f;
    public Light sun;
    public Light moon;
    
    public Volume skyVolume;
    private PhysicallyBasedSky sky;
    
    public TODStateAsset todState;
    
    private bool isNight;

    private void Start()
    {
        skyVolume.profile.TryGet<PhysicallyBasedSky>(out sky);
    }

    private void Awake()
    {
        if(instance == null)
        {
            instance = this;	
        }
        else
        {
            if(instance != this)
            {
                Destroy(gameObject);
            }
        }
    }
    
    void Update()
    {
        timeOfDay += Time.deltaTime * oritSpeed;
        if(timeOfDay>24)
            timeOfDay = 0;

        UpdateTime();
        ApplyCurve();
    }

    private void OnValidate()
    {
        UpdateTime();
        
        // 重新尝试获取 sky（但要确保 volume 和 profile 存在）
        if (skyVolume != null && skyVolume.profile != null)
        {
            skyVolume.profile.TryGet<PhysicallyBasedSky>(out sky);
        }
        else
        {
            sky = null; // 显式置空，避免残留旧引用
        }
        
        ApplyCurve();
    }

    private void ApplyCurve()
    {
        if (todState == null) return;

        // 光源强度/颜色
        if (sun != null)
        {
            sun.intensity = todState.sunIntensity.Evaluate(timeOfDay);
            sun.color = todState.sunColor.Evaluate(alpha);
        }

        if (moon != null)
        {
            moon.intensity = todState.moonIntensity.Evaluate(timeOfDay);
            moon.color = todState.moonColor.Evaluate(alpha);
        }

        // 天空盒（Physically Based Sky）
        if (sky != null)
        {
            sky.spaceEmissionMultiplier.value = todState.starEmission.Evaluate(timeOfDay);
        }
    }
    
    private void UpdateTime() 
    {
        alpha = timeOfDay / 24.0f;
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