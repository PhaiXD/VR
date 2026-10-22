using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace UR3eVR
{
    /// <summary>
    /// VRRemoteLaserPointer: Laser pointer continuously emanating from Meta Quest Touch Remote (Left Hand LTouch).
    /// - Always active and vividly visible at all times
    /// - Robust anchor tracking from LeftControllerAnchor / LeftHandAnchor
    /// - Magnetic Curved Snap (suction curve): curves toward UI buttons and grab bars
    /// - Front Index Trigger selects/clicks targeted UI buttons
    /// - Stereo Single Pass Instanced compatible
    /// </summary>
    public class VRRemoteLaserPointer : MonoBehaviour
    {
        public static VRRemoteLaserPointer Instance { get; private set; }

        [Header("Controller & Ray Settings")]
        [Tooltip("Controller to bind laser pointer to (Default: Left Hand LTouch)")]
        public OVRInput.Controller controller = OVRInput.Controller.LTouch;
        public float maxRayLength = 4.0f;
        public float tipForwardOffset = 0.065f;
        public float tipUpOffset = 0.018f;

        [Header("Magnetic Suction Snap Settings")]
        [Tooltip("Radius around UI buttons where laser curves and snaps to target (meters)")]
        public float magneticSnapRadius = 0.28f;
        [Tooltip("Max angle between controller forward and button to engage suction (degrees)")]
        public float magneticMaxAngle = 22.0f;
        [Tooltip("Speed of curve smoothing")]
        public float snapSmoothSpeed = 20.0f;

        [Header("Colors & Styling")]
        public Color normalLaserColor = new Color(0.0f, 1.0f, 1.0f, 0.95f);     // Vivid Electric Cyan (always visible)
        public Color magneticLaserColor = new Color(0.0f, 1.0f, 0.75f, 1.0f);   // Neon Aqua Snap
        public Color pressLaserColor = new Color(1.0f, 0.95f, 0.25f, 1.0f);     // Trigger Click Yellow

        [Header("References")]
        public Transform leftControllerAnchor;
        public Transform headTransform;
        public BhapticsGloveFeedback bhapticsFeedback;

        // Visual Elements
        private LineRenderer lineRenderer;
        private GameObject reticleObj;
        private Material lineMaterial;
        private Material reticleMaterial;

        // Curve Sampling Resolution
        private const int CURVE_POINTS = 28;
        private Vector3[] curvePoints = new Vector3[CURVE_POINTS];

        // State
        public bool isLaserActive = true;
        public bool isSnapped = false;
        public Selectable hoveredSelectable = null;
        public VRWindowDragger hoveredDragger = null;
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
            if (leftControllerAnchor == null)
            {
                // Search across root and nested objects
                string[] possibleNames = new string[] {
                    "LeftControllerAnchor", "LeftControllerInHandAnchor", "LeftHandAnchor", "LeftHandAnchorDetached"
                };

                foreach (var name in possibleNames)
                {
                    var found = GameObject.Find(name);
                    if (found != null)
                    {
                        leftControllerAnchor = found.transform;
                        break;
                    }
                }

                if (leftControllerAnchor == null)
                {
                    var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
                    foreach (var t in allTransforms)
                    {
                        if (t.name == "LeftControllerAnchor" || t.name == "LeftHandAnchor")
                        {
                            leftControllerAnchor = t;
                            break;
                        }
                    }
                }
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
            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null) lineRenderer = gameObject.AddComponent<LineRenderer>();

            Shader stereoShader = Shader.Find("UR3eVR/StereoUnlitColor") 
                               ?? Shader.Find("Universal Render Pipeline/Unlit")
                               ?? Shader.Find("Unlit/Color");

            lineMaterial = new Material(stereoShader);
            lineMaterial.name = "VR_Laser_LineMaterial";
            lineMaterial.enableInstancing = true;
            if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", normalLaserColor);
            if (lineMaterial.HasProperty("_Color")) lineMaterial.SetColor("_Color", normalLaserColor);

            lineRenderer.material = lineMaterial;
            lineRenderer.startWidth = 0.0075f; // 7.5mm start (crisp and clearly visible in VR)
            lineRenderer.endWidth = 0.0035f;   // 3.5mm end
            lineRenderer.positionCount = CURVE_POINTS;
            lineRenderer.useWorldSpace = true;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            // Reticle Disc
            reticleObj = new GameObject("VR_Laser_Reticle");
            reticleObj.transform.SetParent(transform, false);

            var mf = reticleObj.AddComponent<MeshFilter>();
            var mr = reticleObj.AddComponent<MeshRenderer>();
            mf.mesh = CreateDiscMesh(0.018f, 32);

            reticleMaterial = new Material(stereoShader);
            reticleMaterial.name = "VR_Laser_ReticleMaterial";
            reticleMaterial.enableInstancing = true;
            if (reticleMaterial.HasProperty("_BaseColor")) reticleMaterial.SetColor("_BaseColor", normalLaserColor);
            if (reticleMaterial.HasProperty("_Color")) reticleMaterial.SetColor("_Color", normalLaserColor);
            mr.material = reticleMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            reticleObj.SetActive(true);
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
            // Laser pointer is ALWAYS ACTIVE continuously on Left Hand
            isLaserActive = true;

            if (leftControllerAnchor == null || headTransform == null)
            {
                FindReferences();
            }

            Vector3 rayOrigin;
            Vector3 rayDir;

            if (leftControllerAnchor != null)
            {
                rayOrigin = leftControllerAnchor.position + leftControllerAnchor.forward * tipForwardOffset + leftControllerAnchor.up * tipUpOffset;
                rayDir = leftControllerAnchor.forward;
            }
            else if (headTransform != null)
            {
                rayOrigin = headTransform.position + headTransform.forward * 0.15f + Vector3.down * 0.08f;
                rayDir = headTransform.forward;
            }
            else
            {
                return;
            }

            lineRenderer.enabled = true;

            // 1. Magnetic Suction Snap Check
            Vector3 snapTargetPos = Vector3.zero;
            Vector3 snapTargetNormal = -rayDir;
            Selectable bestSelectable = null;
            VRWindowDragger bestDragger = null;

            bool foundMagneticTarget = FindBestMagneticTarget(rayOrigin, rayDir, out snapTargetPos, out snapTargetNormal, out bestSelectable, out bestDragger);

            // 2. Trigger Input (Left Index Trigger)
            bool isTriggerDownNow = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller)
                                 || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.40f
                                 || Input.GetMouseButton(0);

            bool isTriggerJustPressed = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, controller)
                                     || (isTriggerDownNow && !wasTriggerDown)
                                     || Input.GetMouseButtonDown(0);

            // 3. Render Laser (Suction Bezier Curve vs Straight Ray)
            if (foundMagneticTarget)
            {
                isSnapped = true;
                hoveredSelectable = bestSelectable;
                hoveredDragger = bestDragger;

                smoothedEndpoint = Vector3.Lerp(smoothedEndpoint, snapTargetPos, Time.deltaTime * snapSmoothSpeed);

                float totalDist = Vector3.Distance(rayOrigin, snapTargetPos);
                Vector3 p0 = rayOrigin;
                Vector3 p1 = rayOrigin + rayDir * (totalDist * 0.40f);
                Vector3 p2 = snapTargetPos - snapTargetNormal * (totalDist * 0.25f);
                Vector3 p3 = snapTargetPos;

                for (int i = 0; i < CURVE_POINTS; i++)
                {
                    float t = (float)i / (CURVE_POINTS - 1);
                    curvePoints[i] = EvaluateCubicBezier(p0, p1, p2, p3, t);
                }
                lineRenderer.SetPositions(curvePoints);

                Color activeCol = isTriggerDownNow ? pressLaserColor : magneticLaserColor;
                SetLaserColor(activeCol, 0.008f, 0.004f);

                if (reticleObj != null)
                {
                    reticleObj.SetActive(true);
                    reticleObj.transform.position = snapTargetPos + snapTargetNormal * 0.003f;
                    reticleObj.transform.rotation = Quaternion.LookRotation(snapTargetNormal, Vector3.up);
                    float pulseScale = 1.0f + 0.15f * Mathf.Sin(Time.time * 8.0f);
                    reticleObj.transform.localScale = Vector3.one * (pulseScale * (isTriggerDownNow ? 1.4f : 1.2f));
                    SetReticleColor(activeCol);
                }

                if (isTriggerJustPressed)
                {
                    ExecuteSelectionClick(bestSelectable, bestDragger);
                }
            }
            else
            {
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

                for (int i = 0; i < CURVE_POINTS; i++)
                {
                    float t = (float)i / (CURVE_POINTS - 1);
                    curvePoints[i] = Vector3.Lerp(rayOrigin, endPoint, t);
                }
                lineRenderer.SetPositions(curvePoints);

                // Always vivid and thick enough to be seen in VR against skybox
                Color defaultCol = isTriggerDownNow ? pressLaserColor : normalLaserColor;
                SetLaserColor(defaultCol, 0.0075f, 0.0035f);

                if (reticleObj != null)
                {
                    reticleObj.SetActive(true);
                    reticleObj.transform.position = endPoint + hitNormal * 0.002f;
                    reticleObj.transform.rotation = Quaternion.LookRotation(hitNormal, Vector3.up);
                    reticleObj.transform.localScale = Vector3.one * (isTriggerDownNow ? 1.3f : 1.0f);
                    SetReticleColor(defaultCol);
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

            // 1. UI Selectables
            var selectables = FindObjectsByType<Selectable>(FindObjectsSortMode.None);
            foreach (var sel in selectables)
            {
                if (sel == null || !sel.gameObject.activeInHierarchy || !sel.interactable) continue;

                Vector3 bPos = sel.transform.position;
                Vector3 toButton = bPos - origin;
                float forwardProj = Vector3.Dot(toButton, dir);
                if (forwardProj < 0.10f || forwardProj > maxRayLength) continue;

                Vector3 rayPoint = origin + dir * forwardProj;
                float perpDist = Vector3.Distance(rayPoint, bPos);
                float angle = Vector3.Angle(dir, toButton);

                if (perpDist <= magneticSnapRadius && angle <= magneticMaxAngle)
                {
                    float score = perpDist + angle * 0.01f;
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

            // 2. VR Window Draggers
            var draggers = FindObjectsByType<VRWindowDragger>(FindObjectsSortMode.None);
            foreach (var drag in draggers)
            {
                if (drag == null || !drag.gameObject.activeInHierarchy) continue;

                Vector3 dPos = drag.transform.position;
                Vector3 toDrag = dPos - origin;
                float forwardProj = Vector3.Dot(toDrag, dir);
                if (forwardProj < 0.10f || forwardProj > maxRayLength) continue;

                Vector3 rayPoint = origin + dir * forwardProj;
                float perpDist = Vector3.Distance(rayPoint, dPos);
                float angle = Vector3.Angle(dir, toDrag);

                if (perpDist <= magneticSnapRadius * 1.3f && angle <= magneticMaxAngle * 1.3f)
                {
                    float score = perpDist + angle * 0.01f;
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

        private void ExecuteSelectionClick(Selectable sel, VRWindowDragger drag)
        {
            if (sel != null)
            {
                var btn = sel as Button;
                if (btn != null)
                {
                    btn.onClick.Invoke();
                    StartCoroutine(AnimateButtonClick(btn));
                }
                else
                {
                    ExecuteEvents.Execute(sel.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
                }

                HapticPulse(0.6f, 0.12f);
            }
            else if (drag != null)
            {
                drag.ToggleDirectGrab();
                HapticPulse(0.4f, 0.08f);
            }
        }

        private IEnumerator AnimateButtonClick(Button btn)
        {
            if (btn == null) yield break;
            var rt = btn.GetComponent<RectTransform>();
            if (rt == null) yield break;

            Vector3 origScale = rt.localScale;
            rt.localScale = origScale * 0.88f;
            yield return new WaitForSeconds(0.08f);
            if (rt != null) rt.localScale = origScale;
        }

        private void SetLaserColor(Color col, float startW, float endW)
        {
            if (lineRenderer == null) return;
            lineRenderer.startWidth = startW;
            lineRenderer.endWidth = endW;
            if (lineMaterial != null)
            {
                if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", col);
                if (lineMaterial.HasProperty("_Color")) lineMaterial.SetColor("_Color", col);
            }
        }

        private void SetReticleColor(Color col)
        {
            if (reticleMaterial != null)
            {
                if (reticleMaterial.HasProperty("_BaseColor")) reticleMaterial.SetColor("_BaseColor", col);
                if (reticleMaterial.HasProperty("_Color")) reticleMaterial.SetColor("_Color", col);
            }
        }

        private Vector3 EvaluateCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            return uuu * p0 + 3f * uu * t * p1 + 3f * u * tt * p2 + ttt * p3;
        }

        public void HapticPulse(float amplitude, float duration)
        {
            try
            {
                OVRInput.SetControllerVibration(amplitude * 0.5f, amplitude, controller);
                StartCoroutine(StopHapticAfter(duration));
            }
            catch { }

            if (bhapticsFeedback != null)
            {
                bhapticsFeedback.TriggerClickPulse((int)(amplitude * 100), (int)(duration * 1000));
            }
        }

        private IEnumerator StopHapticAfter(float duration)
        {
            yield return new WaitForSeconds(duration);
            try
            {
                OVRInput.SetControllerVibration(0f, 0f, controller);
            }
            catch { }
        }
    }
}
