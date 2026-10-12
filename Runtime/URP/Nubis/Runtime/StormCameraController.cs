using UnityEngine;

namespace NubisStorm
{
    public sealed class StormCameraController : MonoBehaviour
    {
        public NubisStormVolume storm;
        public float moveSpeed = 600;
        public bool showControls = true;
        Vector2 angles;
        int view;
        void OnEnable() => angles = new Vector2(transform.eulerAngles.y, transform.eulerAngles.x);
        public void SetView(int index)
        {
            if (storm) storm.InvalidateHistory();
            view = index;
            if (index == 2)
            {
                transform.position = new Vector3(0,4500,-8000);
                transform.LookAt(new Vector3(0,1200,70000));
                GetComponent<Camera>().fieldOfView = 75;
            }
            else if (storm && storm.stormStrength < 0.001f)
            {
                transform.position = index == 0 ? new Vector3(0, 450, -5600) : new Vector3(6800, 3600, -9600);
                transform.LookAt(index == 0 ? new Vector3(0, 1900, 1000) : new Vector3(0, 1650, 0));
                GetComponent<Camera>().fieldOfView = index == 0 ? 60 : 50;
            }
            else if (index == 0)
            {
                transform.position = new Vector3(0, 150, -3700);
                transform.LookAt(new Vector3(0, 2900, 0));
                GetComponent<Camera>().fieldOfView = 62;
            }
            else
            {
                transform.position = new Vector3(4500, 2200, -7800);
                transform.LookAt(new Vector3(0, 2500, 0));
                GetComponent<Camera>().fieldOfView = 44;
            }
            angles = new Vector2(transform.eulerAngles.y, transform.eulerAngles.x);
        }
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) && storm) { storm.stormStrength = 0; SetView(view); }
            if (Input.GetKeyDown(KeyCode.Alpha2) && storm) { storm.stormStrength = 1; SetView(view); }
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetView(2);
            if (Input.GetKeyDown(KeyCode.V) && storm) storm.debugView = (NubisDebugView)(((int)storm.debugView+1)%13);
            if (Input.GetKeyDown(KeyCode.C) && storm) storm.enableCirrus=!storm.enableCirrus;
            if (Input.GetKeyDown(KeyCode.T) && storm) { storm.temporalReconstruction = !storm.temporalReconstruction; storm.InvalidateHistory(); }
            if (Input.GetKeyDown(KeyCode.B) && storm) storm.temporalDebug = (NubisTemporalDebug)(((int)storm.temporalDebug+1)%6);
            if (Input.GetKeyDown(KeyCode.L) && storm) storm.lightingDebug = (NubisLightingDebug)(((int)storm.lightingDebug+1)%8);
            if (Input.GetKeyDown(KeyCode.Tab)) SetView(view == 0 ? 1 : 0);
            if (Input.GetKeyDown(KeyCode.H)) showControls = !showControls;
            if (Input.GetKeyDown(KeyCode.Space) && storm)
            {
                if (storm.animate) { storm.frozenTime = storm.AnimationTime; storm.animate = false; }
                else { storm.frozenTime -= Time.time; storm.animate = true; }
            }
            if (!Input.GetMouseButton(1)) return;
            angles.x += Input.GetAxis("Mouse X") * 2;
            angles.y -= Input.GetAxis("Mouse Y") * 2;
            angles.y = Mathf.Clamp(Mathf.DeltaAngle(0, angles.y), -89, 89);
            transform.rotation = Quaternion.Euler(angles.y, angles.x, 0);
            var input = new Vector3(Input.GetAxisRaw("Horizontal"),
                (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0), Input.GetAxisRaw("Vertical"));
            transform.position += transform.TransformDirection(input.normalized) * moveSpeed * Time.deltaTime *
                (Input.GetKey(KeyCode.LeftShift) ? 4 : 1);
        }
        void OnGUI()
        {
            if (!showControls) return;
            GUI.color = new Color(0.9f, 0.93f, 0.96f, 0.8f);
            GUI.Label(new Rect(22, Screen.height - 36, 900, 28),
                "NUBIS  1 Sky / 2 Storm  3 Horizon  C Cirrus  TAB View  V Density  L Lighting  T Temporal  B History  RMB + WASD/QE Fly  SPACE Pause  H Hide");
        }
    }
}
