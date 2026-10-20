using System;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// UR3eStarterPoint: แสดงจุด Starter Point / Home Point ของ UR3e ใน Unity VR Scene
    /// - พิกัด Safe Home ของ UR3e: X=0.3053m (305mm), Y=-0.1313m (-131mm), Z=0.3108m (311mm)
    /// - แสดงเป็น Hologram ทรงกลมพร้อมวงแหวน Home Zone
    /// - แกนพิกัด 3 มิติ หันหน้าไปในทิศทางพุ่งออกจากแว่น VR เสมอ (แดง=Forward ออกจากแว่น, เขียว=Left, น้ำเงิน=Up)
    /// - มี Dynamic Guide Laser ชี้จากมือมายังจุด Starter Point พร้อมวัดระยะห่าง
    /// - เมื่อกด Home หรือ Recenter จุด Starter Point จะกระโดดกลับมาอยู่ที่มือขวาทันที
    /// </summary>
    public class UR3eStarterPoint : MonoBehaviour
    {
        [Header("Target References")]
        public VRHandMover vrHandMover;
        public Transform handTransform;
        public Transform headTransform;

        [Header("Display Settings")]
        [Tooltip("เปิด/ปิด การแสดงผลจุด Starter Point")]
        public bool isVisible = true;

        [Tooltip("แสดงเส้น Laser นำทางเชื่อมระหว่างมือกับจุด Starter Point")]
        public bool showTetherLine = true;

        [Tooltip("แสดงแกนพิกัด 3 มิติ (Robot X=แดง, Y=เขียว, Z=น้ำเงิน)")]
        public bool showCoordinateAxes = true;

        [Tooltip("แสดงป้ายข้อความ 3D ลอยตัว")]
        public bool showLabel = true;

        [Tooltip("ขนาดของจุด Starter Point")]
        [Range(0.01f, 0.15f)]
        public float markerScale = 0.04f;

        [Header("Home Zone Threshold")]
        [Tooltip("ระยะรัศมีสำหรับตรวจจับว่ามืออยู่ที่จุด Home (เมตร)")]
        public float homeZoneRadius = 0.035f;

        [Header("Live Status (Read Only)")]
        public bool isAtHome = false;
        public float distanceToHome = 0f;
        public Vector3 homeRobotCoordinates = new Vector3(0.3053f, -0.1313f, 0.3108f);

        // Visual elements
        private GameObject coreSphere;
        private GameObject ringVisual;
        private GameObject axesRoot;
        private GameObject labelObj;
        private TextMesh textMesh;
        private LineRenderer tetherLine;
        private Material sphereMat;
        private Material ringMat;
        private Material lineMat;

        private Color homeColor = new Color(0.0f, 1.0f, 0.55f, 0.85f);     // Vibrant Emerald Green
        private Color awayColor = new Color(0.0f, 0.85f, 1.0f, 0.70f);     // Holographic Cyan
        private Color pulseColor = new Color(1.0f, 0.9f, 0.0f, 0.85f);     // Warning / Approaching

        private void Awake()
        {
            CreateVisuals();
        }

        private void Start()
        {
            if (vrHandMover == null) vrHandMover = FindAnyObjectByType<VRHandMover>();
            if (vrHandMover != null)
            {
                if (handTransform == null) handTransform = vrHandMover.handTransform;
                if (headTransform == null) headTransform = vrHandMover.headTransform;
                homeRobotCoordinates = vrHandMover.homeRobotPos;

                if (vrHandMover.isAnchorSet)
                {
                    transform.position = vrHandMover.anchorHandWorldPos;
                }
                else if (handTransform != null)
                {
                    transform.position = handTransform.position;
                }
            }

            if (headTransform == null && Camera.main != null)
            {
                headTransform = Camera.main.transform;
            }

            if (headTransform != null)
            {
                transform.rotation = Quaternion.Euler(0f, headTransform.eulerAngles.y, 0f);
            }
        }

        /// <summary>
        /// อัปเดตจุด Anchor ให้ตรงกับตำแหน่งมือขวาใน World Space ทันที
        /// </summary>
        public void UpdateAnchor(Vector3 worldPos, Quaternion headingRot, Vector3 robotPos)
        {
            transform.position = worldPos;
            transform.rotation = headingRot;
            homeRobotCoordinates = robotPos;
        }

        private void Update()
        {
            if (!isVisible)
            {
                SetVisualsActive(false);
                return;
            }
            SetVisualsActive(true);

            // แกนพิกัด Starter Point หันหน้าไปในทิศทางพุ่งออกจากแว่นเสมอ
            if (headTransform != null)
            {
                transform.rotation = Quaternion.Euler(0f, headTransform.eulerAngles.y, 0f);
            }

            // คำนวณระยะห่างระหว่างมือปัจจุบันกับจุด Starter Point (World Space ทั้งคู่)
            Vector3 currentHandPos = (handTransform != null) ? handTransform.position : transform.position;
            distanceToHome = Vector3.Distance(currentHandPos, transform.position);
            isAtHome = (distanceToHome <= homeZoneRadius);

            // ปรับสีและเอฟเฟกต์การแสดงผล (Home vs Moving)
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 4f);
            Color activeColor = isAtHome ? homeColor : Color.Lerp(awayColor, pulseColor, Mathf.Clamp01(distanceToHome / 0.35f));
            activeColor.a *= pulse;

            if (sphereMat != null) sphereMat.color = activeColor;
            if (ringMat != null) ringMat.color = isAtHome ? homeColor : awayColor;

            // อัปเดตเส้น Tether Laser นำทาง
            if (tetherLine != null)
            {
                bool shouldShowLine = showTetherLine && (distanceToHome > 0.005f) && (handTransform != null);
                tetherLine.enabled = shouldShowLine;
                if (shouldShowLine)
                {
                    tetherLine.SetPosition(0, transform.position);
                    tetherLine.SetPosition(1, currentHandPos);
                    Color startC = isAtHome ? homeColor : awayColor;
                    Color endC = isAtHome ? homeColor : pulseColor;
                    tetherLine.startColor = startC;
                    tetherLine.endColor = endC;
                }
            }

            // อัปเดตป้ายข้อความ 3D Billboard
            if (textMesh != null && labelObj != null)
            {
                labelObj.SetActive(showLabel);
                if (showLabel && headTransform != null)
                {
                    Vector3 lookDir = labelObj.transform.position - headTransform.position;
                    if (lookDir.sqrMagnitude > 0.001f)
                    {
                        labelObj.transform.rotation = Quaternion.LookRotation(lookDir);
                    }

                    string statusStr = isAtHome 
                        ? "<color=#00FF88>★ AT HOME (จุดเริ่มต้น) ★</color>" 
                        : string.Format("<color=#00E5FF>ห่าง Home: {0:F1} cm</color>", distanceToHome * 100f);

                    textMesh.text = string.Format(
                        "<b>🏠 UR3e Starter Point (Home)</b>\n" +
                        "Robot Home: [X:{0:F0}, Y:{1:F0}, Z:{2:F0}] mm\n" +
                        "{3}",
                        homeRobotCoordinates.x * 1000f,
                        homeRobotCoordinates.y * 1000f,
                        homeRobotCoordinates.z * 1000f,
                        statusStr
                    );
                }
            }

            // อัปเดตการแสดงผลแกนพิกัด 3D
            if (axesRoot != null)
            {
                axesRoot.SetActive(showCoordinateAxes);
            }
        }

        private void SetVisualsActive(bool active)
        {
            if (coreSphere != null) coreSphere.SetActive(active);
            if (ringVisual != null) ringVisual.SetActive(active);
            if (axesRoot != null) axesRoot.SetActive(active && showCoordinateAxes);
            if (labelObj != null) labelObj.SetActive(active && showLabel);
            if (tetherLine != null) tetherLine.enabled = active && showTetherLine;
        }

        private void CreateVisuals()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            // 1. Central Hologram Sphere
            coreSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            coreSphere.name = "Home_CoreSphere";
            coreSphere.transform.SetParent(transform, false);
            coreSphere.transform.localPosition = Vector3.zero;
            coreSphere.transform.localScale = Vector3.one * markerScale;
            DestroyCollider(coreSphere);

            sphereMat = new Material(shader) { color = awayColor };
            coreSphere.GetComponent<Renderer>().material = sphereMat;

            // 2. Horizontal Home Zone Ring
            ringVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ringVisual.name = "Home_ZoneRing";
            ringVisual.transform.SetParent(transform, false);
            ringVisual.transform.localPosition = new Vector3(0f, -0.005f, 0f);
            ringVisual.transform.localScale = new Vector3(homeZoneRadius * 2f, 0.0015f, homeZoneRadius * 2f);
            DestroyCollider(ringVisual);

            ringMat = new Material(shader) { color = new Color(0f, 0.85f, 1f, 0.35f) };
            ringVisual.GetComponent<Renderer>().material = ringMat;

            // 3. 3D Coordinate Frame (RGB Axes for Robot Motion)
            // Robot +X Forward (Red)   -> Local Forward (+Z) หันพุ่งออกจากแว่น
            // Robot +Y Left (Green)    -> Local Left (-X) หันไปทางซ้ายของแว่น
            // Robot +Z Up (Blue)       -> Local Up (+Y) หันขึ้นบน
            axesRoot = new GameObject("Home_CoordinateAxes");
            axesRoot.transform.SetParent(transform, false);
            axesRoot.transform.localPosition = Vector3.zero;
            axesRoot.transform.localRotation = Quaternion.identity;

            float axisLen = 0.055f;
            float axisThick = 0.0035f;

            CreateAxisBranch(axesRoot.transform, "Axis_X_Forward_Red", new Vector3(0f, 0f, axisLen / 2f), new Vector3(axisThick, axisLen / 2f, axisThick), Quaternion.Euler(90f, 0f, 0f), Color.red, shader);
            CreateAxisBranch(axesRoot.transform, "Axis_Y_Left_Green", new Vector3(-axisLen / 2f, 0f, 0f), new Vector3(axisThick, axisLen / 2f, axisThick), Quaternion.Euler(0f, 0f, 90f), Color.green, shader);
            CreateAxisBranch(axesRoot.transform, "Axis_Z_Up_Blue", new Vector3(0f, axisLen / 2f, 0f), new Vector3(axisThick, axisLen / 2f, axisThick), Quaternion.identity, Color.blue, shader);

            // 4. Tether LineRenderer
            tetherLine = gameObject.AddComponent<LineRenderer>();
            tetherLine.useWorldSpace = true;
            tetherLine.startWidth = 0.0035f;
            tetherLine.endWidth = 0.0015f;
            lineMat = new Material(shader);
            tetherLine.material = lineMat;
            tetherLine.positionCount = 2;
            tetherLine.enabled = false;

            // 5. 3D Text Label
            labelObj = new GameObject("Home_Label");
            labelObj.transform.SetParent(transform, false);
            labelObj.transform.localPosition = new Vector3(0f, markerScale * 0.75f + 0.045f, 0f);

            textMesh = labelObj.AddComponent<TextMesh>();
            textMesh.characterSize = 0.0028f;
            textMesh.fontSize = 40;
            textMesh.fontStyle = FontStyle.Bold;
            textMesh.alignment = TextAlignment.Center;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.color = Color.white;
            textMesh.text = "🏠 UR3e Starter Point (Home)";
        }

        private void CreateAxisBranch(Transform parent, string name, Vector3 pos, Vector3 scale, Quaternion rot, Color col, Shader shader)
        {
            GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = name;
            cyl.transform.SetParent(parent, false);
            cyl.transform.localPosition = pos;
            cyl.transform.localRotation = rot;
            cyl.transform.localScale = scale;
            DestroyCollider(cyl);

            Material m = new Material(shader) { color = col };
            cyl.GetComponent<Renderer>().material = m;
        }

        private void DestroyCollider(GameObject go)
        {
            Collider c = go.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);
        }

        private void OnDestroy()
        {
            if (sphereMat != null) Destroy(sphereMat);
            if (ringMat != null) Destroy(ringMat);
            if (lineMat != null) Destroy(lineMat);
        }
    }
}
