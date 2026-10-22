using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace UR3eVR
{
    /// <summary>
    /// VRRemoteLaserPointer: ลำแสงเลเซอร์ชี้เลือกอัจฉริยะจาก Remote Controller
    /// - พุ่งออกจากส่วนปลายของรีโมทมือขวา (Right Controller) อย่างแม่นยำ
    /// - Magnetic Curved Snap (เหมือนดูดเส้น): เมื่อปลายเส้นเลเซอร์เข้าใกล้ปุ่ม UI หรือ Grab Bar ในระยะ 20cm
    ///   เส้นเลเซอร์จะเอียงโค้งดัดรูปทรงเป็นเส้น Bezier Curve พุ่งเข้าดูดติดที่กึ่งกลางของปุ่มนั้นทันที
    /// - ปุ่มด้านหน้า (Front Index Trigger): ใช้นิ้วชี้กดเพื่อเลือก/คลิกปุ่ม UI นั้นๆ หรือเริ่มลากหน้าต่าง
    /// - มีจุด Reticle Indicator ทรงกลมเรืองแสงที่จุดกระทบ พร้อมแสดงสถานะ Hover และ Click Animation
    /// - รองรับระบบเรนเดอร์ Stereo Single Pass Instanced (มองเห็นได้คมชัดทั้ง 2 ตา)
    /// </summary>
    public class VRRemoteLaserPointer : MonoBehaviour
    {
        public static VRRemoteLaserPointer Instance { get; private set; }

        [Header("Controller & Ray Settings")]
        public OVRInput.Controller controller = OVRInput.Controller.RTouch;
        public float maxRayLength = 3.0f;
        public float tipForwardOffset = 0.055f;
        public float tipUpOffset = 0.015f;

        [Header("Magnetic Suction Snap (การดูดเส้นเข้าหาปุ่ม)")]
        [Tooltip("ระยะรัศมีการดูดเส้นเข้าหาปุ่ม UI (เมตร)")]
        public float magneticSnapRadius = 0.22f;
        [Tooltip("มุมกรวยสูงสุดในการดูดเส้น (องศา)")]
        public float magneticMaxAngle = 14.0f;
        [Tooltip("ความนุ่มนวลในการดูดเส้นเข้าหาปุ่ม")]
        public float snapSmoothSpeed = 16.0f;

        [Header("Colors & Styling")]
        public Color normalLaserColor = new Color(0.0f, 0.90f, 1.0f, 0.90f);     // Electric Cyan
        public Color magneticLaserColor = new Color(0.0f, 1.0f, 0.65f, 1.0f);     // Suction Neon Aqua
        public Color pressLaserColor = new Color(0.95f, 1.0f, 0.35f, 1.0f);       // Bright Trigger Yellow

        [Header("References")]
        public Transform rightControllerAnchor;
        public Transform headTransform;
        public BhapticsGloveFeedback bhapticsFeedback;

        // Visual Elements
        private LineRenderer lineRenderer;
        private GameObject reticleObj;
        private SpriteRenderer reticleRenderer;
        private Material lineMaterial;
        private Material reticleMaterial;

        // Curve Sampling Resolution
        private const int CURVE_POINTS = 24;
        private Vector3[] curvePoints = new Vector3[CURVE_POINTS];

        // State
        public bool isLaserActive = true;
        public bool isSnapped = false;
        public Selectable hoveredSelectable = null;
        public VRWindowDragger hoveredDragger = null;
        private Vector3 currentSnappedPoint;
        private Vector3 smoothedEndpoint;
        private bool wasTriggerDown = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            BuildVisuals();
        }

        private void Start()
        {
            FindReferences();
        }

        private void FindReferences()
        {
            if (rightControllerAnchor == null)
            {
                var rca = GameObject.Find("RightControllerAnchor");
                if (rca != null) rightControllerAnchor = rca.transform;
            }

            if (headTransform == null)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) headTransform = cec.transform;
                if (headTransform == null && Camera.main != null) headTransform = Camera.main.transform;
            }

            if (bhapticsFeedback == null) bhapticsFeedback = FindAnyObjectByType<BhapticsGloveFeedback>();
        }

        private void BuildVisuals()
        {
            // 1. LineRenderer สำหรับเส้นเลเซอร์ (รองรับทั้งเส้นตรงและเส้นโค้ง Bezier)
            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null) lineRenderer = gameObject.AddComponent<LineRenderer>();

            Shader stereoShader = Shader.Find("UR3eVR/StereoUnlitColor") ?? Shader.Find("Universal Render Pipeline/Unlit");
            lineMaterial = new Material(stereoShader);
            lineMaterial.enableInstancing = true;
            if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", normalLaserColor);
            if (lineMaterial.HasProperty("_Color")) lineMaterial.SetColor("_Color", normalLaserColor);

            lineRenderer.material = lineMaterial;
            lineRenderer.startWidth = 0.0040f;
            lineRenderer.endWidth = 0.0015f;
            lineRenderer.positionCount = CURVE_POINTS;
            lineRenderer.useWorldSpace = true;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            // 2. Reticle Dot (จุดกระทบที่ปลายเส้นเลเซอร์)
            reticleObj = new GameObject("VR_Laser_Reticle");
            reticleObj.transform.SetParent(transform, false);

            var mf = reticleObj.AddComponent<MeshFilter>();
            var mr = reticleObj.AddComponent<MeshRenderer>();
            mf.mesh = CreateDiscMesh(0.016f, 32);

            reticleMaterial = new Material(stereoShader);
            reticleMaterial.enableInstancing = true;
            if (reticleMaterial.HasProperty("_BaseColor")) reticleMaterial.SetColor("_BaseColor", normalLaserColor);
            if (reticleMaterial.HasProperty("_Color")) reticleMaterial.SetColor("_Color", normalLaserColor);
            mr.material = reticleMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            reticleObj.SetActive(false);
        }

        private Mesh CreateDiscMesh(float radius, int segments)
        {
            Mesh mesh = new Mesh { name = "ReticleDisc" };
            Vector3[] verts = new Vector3[segments + 1];
            int[] tris = new int[segments * 3];

            verts[0] = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2.0f / segments;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
            }

            for (int i = 0; i < segments; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segments + 1;
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            return mesh;
        }

        private void Update()
        {
            if (!isLaserActive)
            {
                if (lineRenderer != null) lineRenderer.enabled = false;
                if (reticleObj != null) reticleObj.SetActive(false);
                return;
            }

            if (rightControllerAnchor == null || headTransform == null)
            {
                FindReferences();
            }

            // คำนวณจุดกำเนิดและทิศทางของลำแสงจากรีโมท
            Vector3 rayOrigin;
            Vector3 rayDir;

            bool isRemoteTracked = rightControllerAnchor != null && OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch);
            if (isRemoteTracked || (rightControllerAnchor != null && Application.isEditor))
            {
                rayOrigin = rightControllerAnchor.position + rightControllerAnchor.forward * tipForwardOffset + rightControllerAnchor.up * tipUpOffset;
                rayDir = rightControllerAnchor.forward;
            }
            else if (headTransform != null)
            {
                rayOrigin = headTransform.position + headTransform.forward * 0.12f + Vector3.down * 0.06f;
                rayDir = headTransform.forward;
            }
            else
            {
                return;
            }

            lineRenderer.enabled = true;

            // 1. ค้นหาเป้าหมาย UI ที่อยู่ในโซนดูดเส้น (Magnetic Snap Candidates)
            Vector3 snapTargetPos = Vector3.zero;
            Vector3 snapTargetNormal = -rayDir;
            Selectable bestSelectable = null;
            VRWindowDragger bestDragger = null;

            bool foundMagneticTarget = FindBestMagneticTarget(rayOrigin, rayDir, out snapTargetPos, out snapTargetNormal, out bestSelectable, out bestDragger);

            // 2. ตรวจสอบการกดปุ่มไกด้านหน้า (Front Index Trigger)
            bool isTriggerDownNow = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller)
                                 || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.50f
                                 || Input.GetMouseButton(0);

            bool isTriggerJustPressed = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, controller)
                                     || (isTriggerDownNow && !wasTriggerDown)
                                     || Input.GetMouseButtonDown(0);

            // 3. วาดเส้นเลเซอร์ (เส้นโค้ง Bezier เมื่อดูดเส้น vs เส้นตรงเมื่อชี้ปกติ)
            if (foundMagneticTarget)
            {
                isSnapped = true;
                hoveredSelectable = bestSelectable;
                hoveredDragger = bestDragger;

                // อัปเดตจุดกึ่งกลางของปุ่มอย่างราบรื่น
                smoothedEndpoint = Vector3.Lerp(smoothedEndpoint, snapTargetPos, Time.deltaTime * snapSmoothSpeed);

                // สร้างเส้นโค้ง Bezier Curve พุ่งเข้าหาปุ่ม
                float totalDist = Vector3.Distance(rayOrigin, snapTargetPos);
                Vector3 p0 = rayOrigin;
                Vector3 p1 = rayOrigin + rayDir * (totalDist * 0.42f);
                Vector3 p2 = snapTargetPos - snapTargetNormal * (totalDist * 0.22f);
                Vector3 p3 = snapTargetPos;

                for (int i = 0; i < CURVE_POINTS; i++)
                {
                    float t = (float)i / (CURVE_POINTS - 1);
                    curvePoints[i] = EvaluateCubicBezier(p0, p1, p2, p3, t);
                }
                lineRenderer.SetPositions(curvePoints);

                // ปรับแต่งสีและสไตล์เมื่อดูดเส้นติดปุ่ม
                Color activeCol = isTriggerDownNow ? pressLaserColor : magneticLaserColor;
                SetLaserColor(activeCol, 0.0050f, 0.0025f);

                // แสดงจุด Reticle ที่หน้าปุ่ม
                if (reticleObj != null)
                {
                    reticleObj.SetActive(true);
                    reticleObj.transform.position = snapTargetPos + snapTargetNormal * 0.003f;
                    reticleObj.transform.rotation = Quaternion.LookRotation(snapTargetNormal, Vector3.up);
                    float pulseScale = 1.0f + 0.12f * Mathf.Sin(Time.time * 8.0f);
                    reticleObj.transform.localScale = Vector3.one * (pulseScale * (isTriggerDownNow ? 1.35f : 1.15f));
                    SetReticleColor(activeCol);
                }

                // ถ้าเหนี่ยวไกนิ้วชี้ด้านหน้า -> กดปุ่มทันที!
                if (isTriggerJustPressed)
                {
                    ExecuteSelectionClick(bestSelectable, bestDragger);
                }
            }
            else
            {
                // ไม่ได้อยู่ในโซนดูดเส้น -> ยิงเส้นตรงปกติ
                isSnapped = false;
                hoveredSelectable = null;
                hoveredDragger = null;

                RaycastHit hit;
                Vector3 endPoint;
                Vector3 hitNormal = -rayDir;
                bool hasHit = Physics.Raycast(rayOrigin, rayDir, out hit, maxRayLength);

                if (hasHit)
                {
                    endPoint = hit.point;
                    hitNormal = hit.normal;

                    // ตรวจสอบว่าโดน Collider ของ Button หรือไม่
                    var sel = hit.collider.GetComponentInParent<Selectable>();
                    var drag = hit.collider.GetComponentInParent<VRWindowDragger>();
                    if (sel != null || drag != null)
                    {
                        hoveredSelectable = sel;
                        hoveredDragger = drag;
                        if (isTriggerJustPressed) ExecuteSelectionClick(sel, drag);
                    }
                }
                else
                {
                    endPoint = rayOrigin + rayDir * maxRayLength;
                }

                smoothedEndpoint = endPoint;

                // เติมเส้นตรง 24 จุด
                for (int i = 0; i < CURVE_POINTS; i++)
                {
                    float t = (float)i / (CURVE_POINTS - 1);
                    curvePoints[i] = Vector3.Lerp(rayOrigin, endPoint, t);
                }
                lineRenderer.SetPositions(curvePoints);

                SetLaserColor(normalLaserColor, 0.0035f, 0.0012f);

                if (reticleObj != null)
                {
                    reticleObj.SetActive(hasHit);
                    if (hasHit)
                    {
                        reticleObj.transform.position = endPoint + hitNormal * 0.002f;
                        reticleObj.transform.rotation = Quaternion.LookRotation(hitNormal, Vector3.up);
                        reticleObj.transform.localScale = Vector3.one;
                        SetReticleColor(normalLaserColor);
                    }
                }
            }

            wasTriggerDown = isTriggerDownNow;
        }

        private bool FindBestMagneticTarget(Vector3 origin, Vector3 dir, out Vector3 targetPos, out Vector3 targetNormal, out Selectable bestSel, out VRWindowDragger bestDrag)
        {
            targetPos = Vector3.zero;
            targetNormal = -dir;
            bestSel = null;
            bestDrag = null;

            float bestScore = float.MaxValue;
            bool found = false;

            // 1. ตรวจสอบ UI Selectables (Buttons, Toggles, Sliders) ทั้งหมดใน World Space Canvases
            var selectables = FindObjectsByType<Selectable>(FindObjectsSortMode.None);
            foreach (var sel in selectables)
            {
                if (sel == null || !sel.gameObject.activeInHierarchy || !sel.interactable) continue;

                Vector3 bPos = sel.transform.position;
                Vector3 toButton = bPos - origin;
                float forwardProj = Vector3.Dot(toButton, dir);
                if (forwardProj < 0.15f || forwardProj > maxRayLength) continue;

                Vector3 rayPoint = origin + dir * forwardProj;
                float perpDist = Vector3.Distance(rayPoint, bPos);
                float angle = Vector3.Angle(dir, toButton);

                if (perpDist <= magneticSnapRadius && angle <= magneticMaxAngle)
                {
                    float score = perpDist * 2.0f + (angle / magneticMaxAngle) * 0.15f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        targetPos = bPos;
                        targetNormal = -sel.transform.forward;
                        bestSel = sel;
                        bestDrag = null;
                        found = true;
                    }
                }
            }

            // 2. ตรวจสอบ VRWindowDragger Bars
            var draggers = FindObjectsByType<VRWindowDragger>(FindObjectsSortMode.None);
            foreach (var drag in draggers)
            {
                if (drag == null || !drag.gameObject.activeInHierarchy) continue;

                Vector3 dPos = drag.transform.position;
                Vector3 toDrag = dPos - origin;
                float forwardProj = Vector3.Dot(toDrag, dir);
                if (forwardProj < 0.15f || forwardProj > maxRayLength) continue;

                Vector3 rayPoint = origin + dir * forwardProj;
                float perpDist = Vector3.Distance(rayPoint, dPos);
                float angle = Vector3.Angle(dir, toDrag);

                if (perpDist <= magneticSnapRadius * 1.3f && angle <= magneticMaxAngle)
                {
                    float score = perpDist * 2.0f + (angle / magneticMaxAngle) * 0.15f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        targetPos = dPos;
                        targetNormal = -drag.transform.forward;
                        bestSel = null;
                        bestDrag = drag;
                        found = true;
                    }
                }
            }

            return found;
        }

        private Vector3 EvaluateCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1.0f - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            Vector3 p = uuu * p0;
            p += 3.0f * uu * t * p1;
            p += 3.0f * u * tt * p2;
            p += ttt * p3;
            return p;
        }

        private void ExecuteSelectionClick(Selectable sel, VRWindowDragger drag)
        {
            // การสั่นตอบสนองที่มือและรีโมท (Haptic Pulse)
            OVRInput.SetControllerVibration(0.6f, 0.5f, controller);
            StartCoroutine(StopHapticsAfter(0.08f));

            if (bhapticsFeedback != null)
            {
                bhapticsFeedback.TriggerClickPulse(70, 70);
            }

            if (sel != null)
            {
                // เอฟเฟกต์กดปุ่มยุบลงเล็กน้อยแล้วเด้งกลับ (Punch Scale)
                StartCoroutine(PunchScale(sel.transform));

                var btn = sel as Button;
                if (btn != null && btn.onClick != null)
                {
                    btn.onClick.Invoke();
                }

                // ส่ง PointerEventData ให้ UI System
                var pointerEvent = new PointerEventData(EventSystem.current)
                {
                    button = PointerEventData.InputButton.Left
                };
                ExecuteEvents.Execute(sel.gameObject, pointerEvent, ExecuteEvents.pointerClickHandler);
                ExecuteEvents.Execute(sel.gameObject, pointerEvent, ExecuteEvents.submitHandler);
            }

            if (drag != null)
            {
                StartCoroutine(PunchScale(drag.transform));
            }
        }

        private IEnumerator StopHapticsAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            OVRInput.SetControllerVibration(0f, 0f, controller);
        }

        private IEnumerator PunchScale(Transform t)
        {
            if (t == null) yield break;
            Vector3 orig = t.localScale;
            t.localScale = orig * 0.92f;
            yield return new WaitForSeconds(0.07f);
            if (t != null) t.localScale = orig * 1.06f;
            yield return new WaitForSeconds(0.07f);
            if (t != null) t.localScale = orig;
        }

        private void SetLaserColor(Color col, float startW, float endW)
        {
            if (lineRenderer == null) return;
            lineRenderer.startWidth = startW;
            lineRenderer.endWidth = endW;

            Color endC = new Color(col.r, col.g, col.b, 0.35f);
            lineRenderer.startColor = col;
            lineRenderer.endColor = endC;

            if (lineMaterial != null)
            {
                if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", col);
                if (lineMaterial.HasProperty("_Color")) lineMaterial.SetColor("_Color", col);
            }
        }

        private void SetReticleColor(Color col)
        {
            if (reticleMaterial == null) return;
            if (reticleMaterial.HasProperty("_BaseColor")) reticleMaterial.SetColor("_BaseColor", col);
            if (reticleMaterial.HasProperty("_Color")) reticleMaterial.SetColor("_Color", col);
        }
    }
}
