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
    
    public TODStateAsset todState;
    
    private bool isNight;

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
        ApplyCurve();
    }

    private void ApplyCurve()
    {
        if (todState != null)
        {
            sun.intensity = todState.sunIntensity.Evaluate(timeOfDay);
            sun.color = todState.sunColor.Evaluate(alpha);
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