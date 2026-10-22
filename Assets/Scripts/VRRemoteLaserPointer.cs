using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace UR3eVR
{
    /// <summary>
    /// VRRemoteLaserPointer: Laser pointer continuously emanating from Left Touch Controller.
    /// - Always active and smoothly moving with the left remote
    /// - Interacts with and clicks World Space UI Buttons via Front Index Trigger
    /// - Can grab and move VR UI windows via bottom grab handle
    /// - Vivid electric cyan line with aiming reticle
    /// </summary>
    public class VRRemoteLaserPointer : MonoBehaviour
    {
        public static VRRemoteLaserPointer Instance { get; private set; }

        [Header("Controller & Ray Settings")]
        public OVRInput.Controller controller = OVRInput.Controller.LTouch;
        public float maxRayLength = 4.5f;
        public float tipForwardOffset = 0.055f;
        public float tipUpOffset = 0.015f;

        [Header("Visual Colors")]
        public Color normalLaserColor = new Color(0.0f, 1.0f, 1.0f, 0.95f);     // Vivid Neon Cyan
        public Color hoverLaserColor = new Color(0.0f, 1.0f, 0.70f, 1.0f);      // Aqua hover
        public Color clickLaserColor = new Color(1.0f, 0.95f, 0.20f, 1.0f);      // Yellow click

        [Header("References")]
        public Transform leftControllerAnchor;
        public Transform headTransform;
        public BhapticsGloveFeedback bhapticsFeedback;

        private LineRenderer lineRenderer;
        private GameObject reticleObj;
        private Material lineMaterial;
        private Material reticleMaterial;

        // Current Hover Target
        public Button hoveredButton = null;
        public VRWindowDragger hoveredDragger = null;
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
                string[] possibleNames = { "LeftControllerAnchor", "LeftControllerInHandAnchor", "LeftHandAnchor" };
                foreach (var n in possibleNames)
                {
                    var found = GameObject.Find(n);
                    if (found != null) { leftControllerAnchor = found.transform; break; }
                }
            }

            if (headTransform == null)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) headTransform = cec.transform;
                else if (Camera.main != null) headTransform = Camera.main.transform;
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
            lineRenderer.startWidth = 0.006f; // 6mm
            lineRenderer.endWidth = 0.003f;   // 3mm
            lineRenderer.positionCount = 2;
            lineRenderer.useWorldSpace = true;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            // Reticle
            reticleObj = new GameObject("VR_Laser_Reticle");
            reticleObj.transform.SetParent(transform, false);

            var mf = reticleObj.AddComponent<MeshFilter>();
            var mr = reticleObj.AddComponent<MeshRenderer>();
            mf.mesh = CreateDiscMesh(0.016f, 24);

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

        private void LateUpdate()
        {
            if (leftControllerAnchor == null || headTransform == null) FindReferences();

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

            // Continuous 100% responsive raycast
            Ray ray = new Ray(rayOrigin, rayDir);
            Vector3 endPoint = rayOrigin + rayDir * maxRayLength;
            Vector3 hitNormal = -rayDir;

            hoveredButton = null;
            hoveredDragger = null;

            // 1. Raycast against World Space UI Canvases
            bool hitUI = CheckWorldSpaceUIRaycast(ray, ref endPoint, ref hitNormal, ref hoveredButton, ref hoveredDragger);

            // 2. Physics 3D Raycast fallback
            if (!hitUI)
            {
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, maxRayLength))
                {
                    endPoint = hit.point;
                    hitNormal = hit.normal;
                    var btn = hit.collider.GetComponentInParent<Button>();
                    if (btn != null && btn.interactable) hoveredButton = btn;
                    var dragger = hit.collider.GetComponentInParent<VRWindowDragger>();
                    if (dragger != null) hoveredDragger = dragger;
                }
            }

            // Update Laser Line
            lineRenderer.enabled = true;
            lineRenderer.SetPosition(0, rayOrigin);
            lineRenderer.SetPosition(1, endPoint);

            // Trigger Input (Left Index Trigger)
            bool isTriggerDown = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller)
                              || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.40f
                              || Input.GetMouseButton(0);

            bool isTriggerJustPressed = (isTriggerDown && !wasTriggerDown)
                                     || OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, controller)
                                     || Input.GetMouseButtonDown(0);

            // Colors
            Color activeColor = normalLaserColor;
            if (isTriggerDown) activeColor = clickLaserColor;
            else if (hoveredButton != null || hoveredDragger != null) activeColor = hoverLaserColor;

            SetVisualColors(activeColor);

            // Update Reticle
            if (reticleObj != null)
            {
                reticleObj.SetActive(true);
                reticleObj.transform.position = endPoint + hitNormal * 0.003f;
                reticleObj.transform.rotation = Quaternion.LookRotation(hitNormal, Vector3.up);
            }

            // Execute Click / Drag
            if (isTriggerJustPressed)
            {
                if (hoveredButton != null)
                {
                    hoveredButton.onClick.Invoke();
                    HapticPulse(0.5f, 0.12f);
                }
                else if (hoveredDragger != null)
                {
                    hoveredDragger.ToggleDirectGrab(leftControllerAnchor);
                    HapticPulse(0.4f, 0.08f);
                }
            }

            wasTriggerDown = isTriggerDown;
        }

        private bool CheckWorldSpaceUIRaycast(Ray ray, ref Vector3 endPoint, ref Vector3 hitNormal, ref Button outBtn, ref VRWindowDragger outDragger)
        {
            var allCanvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            float nearestDist = float.MaxValue;
            bool found = false;

            foreach (var c in allCanvases)
            {
                if (c == null || c.renderMode != RenderMode.WorldSpace || !c.gameObject.activeInHierarchy) continue;

                Plane plane = new Plane(-c.transform.forward, c.transform.position);
                if (plane.Raycast(ray, out float enter))
                {
                    if (enter > 0f && enter < maxRayLength && enter < nearestDist)
                    {
                        Vector3 hitPos = ray.GetPoint(enter);
                        Vector3 localPos = c.transform.InverseTransformPoint(hitPos);
                        RectTransform canvasRT = c.GetComponent<RectTransform>();

                        if (canvasRT != null && canvasRT.rect.Contains(new Vector2(localPos.x, localPos.y)))
                        {
                            nearestDist = enter;
                            endPoint = hitPos;
                            hitNormal = -c.transform.forward;
                            found = true;

                            // Check buttons inside this canvas
                            var buttons = c.GetComponentsInChildren<Button>();
                            foreach (var b in buttons)
                            {
                                if (b == null || !b.gameObject.activeInHierarchy || !b.interactable) continue;
                                RectTransform bRT = b.GetComponent<RectTransform>();
                                Vector3 bLocal = bRT.InverseTransformPoint(hitPos);
                                if (bRT.rect.Contains(new Vector2(bLocal.x, bLocal.y)))
                                {
                                    outBtn = b;
                                    break;
                                }
                            }

                            // Check draggers
                            var draggers = c.GetComponentsInChildren<VRWindowDragger>();
                            foreach (var d in draggers)
                            {
                                if (d == null || !d.gameObject.activeInHierarchy) continue;
                                RectTransform dRT = d.GetComponent<RectTransform>();
                                Vector3 dLocal = dRT.InverseTransformPoint(hitPos);
                                if (dRT.rect.Contains(new Vector2(dLocal.x, dLocal.y)))
                                {
                                    outDragger = d;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            return found;
        }

        private void SetVisualColors(Color c)
        {
            if (lineMaterial != null)
            {
                if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", c);
                if (lineMaterial.HasProperty("_Color")) lineMaterial.SetColor("_Color", c);
            }
            if (reticleMaterial != null)
            {
                if (reticleMaterial.HasProperty("_BaseColor")) reticleMaterial.SetColor("_BaseColor", c);
                if (reticleMaterial.HasProperty("_Color")) reticleMaterial.SetColor("_Color", c);
            }
        }

        public void HapticPulse(float amplitude, float duration)
        {
            try
            {
                OVRInput.SetControllerVibration(amplitude, amplitude, controller);
            }
            catch { }
        }
    }
}
