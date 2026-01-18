using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NeuroTOD
{
    public class WeatherSystem : MonoBehaviour
    {
        private void SimulateDawn()
        {
            TODEvents.RaiseSunrise();
        }
    }
}
