using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace UR3eVR
{
    /// <summary>
    /// VRRemoteLaserPointer: Clean, straight, non-magnetic laser pointer for Meta Quest Touch Controller.
    /// Provides 100% reliable UI raycasting, hover highlights via EventSystem, and click/drag execution.
    /// Follows Meta Quest OS style interaction (as seen in standard VR interfaces).
    /// </summary>
    public class VRRemoteLaserPointer : MonoBehaviour
    {
        public static VRRemoteLaserPointer Instance { get; private set; }

        [Header("Controller Settings")]
        public OVRInput.Controller controller = OVRInput.Controller.LTouch;
        public float maxRayLength = 6.0f;
        public float tipForwardOffset = 0.055f;
        public float tipDownOffset = -0.015f;
        public float pointerPitchAngle = 10.0f; // Natural pointing tilt down in degrees

        [Header("Laser Colors")]
        public Color normalLaserColor = new Color(0.0f, 0.90f, 1.0f, 0.90f);    // Neon Cyan
        public Color hoverLaserColor = new Color(0.1f, 1.0f, 0.75f, 1.0f);      // Bright Aqua Hover
        public Color clickLaserColor = new Color(1.0f, 0.90f, 0.15f, 1.0f);     // Bright Gold Click

        [Header("References")]
        public Transform leftControllerAnchor;
        public Transform headTransform;
        public Transform trackingSpace;
        public BhapticsGloveFeedback bhapticsFeedback;

        private LineRenderer lineRenderer;
        private GameObject reticleObj;
        private Material lineMaterial;
        private Material reticleMaterial;

        // Current Hover & Drag States
        private Selectable currentHoverSelectable = null;
        private VRWindowDragger currentHoverDragger = null;
        private VRWindowDragger activeDraggingWindow = null;
        private float activeDragDistance = 1.0f;
        private Vector3 activeDragLocalOffset = Vector3.zero;

        private bool wasTriggerDown = false;
        private PointerEventData pointerEventData;

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
            EnsureEventSystem();
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current == null)
            {
                var esObj = new GameObject("EventSystem");
                esObj.AddComponent<EventSystem>();
                esObj.AddComponent<StandaloneInputModule>();
            }
            pointerEventData = new PointerEventData(EventSystem.current);
        }

        private void FindReferences()
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                if (trackingSpace == null) trackingSpace = rig.trackingSpace;
                if (headTransform == null) headTransform = rig.centerEyeAnchor;
                if (leftControllerAnchor == null)
                {
                    leftControllerAnchor = rig.leftControllerInHandAnchor ?? rig.leftControllerAnchor ?? rig.leftHandAnchor;
                }
            }

            if (leftControllerAnchor == null)
            {
                string[] possible = { "LeftControllerInHandAnchor", "LeftControllerAnchor", "LeftHandAnchor", "OVRControllerPrefab_Left" };
                foreach (var p in possible)
                {
                    var found = GameObject.Find(p);
                    if (found != null) { leftControllerAnchor = found.transform; break; }
                }
            }

            if (headTransform == null)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) headTransform = cec.transform;
                else if (Camera.main != null) headTransform = Camera.main.transform;
            }
        }

        private void BuildVisuals()
        {
            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null) lineRenderer = gameObject.AddComponent<LineRenderer>();

            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 2;
            lineRenderer.startWidth = 0.0035f;
            lineRenderer.endWidth = 0.0015f;

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Color");

            lineMaterial = new Material(sh);
            lineMaterial.color = normalLaserColor;
            if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", normalLaserColor);
            lineRenderer.material = lineMaterial;

            // Reticle Dot
            reticleObj = new GameObject("LaserReticle");
            reticleObj.transform.SetParent(transform, false);
            var mf = reticleObj.AddComponent<MeshFilter>();
            var mr = reticleObj.AddComponent<MeshRenderer>();
            mf.mesh = CreateDiscMesh(0.012f, 24);

            reticleMaterial = new Material(sh);
            reticleMaterial.color = normalLaserColor;
            if (reticleMaterial.HasProperty("_BaseColor")) reticleMaterial.SetColor("_BaseColor", normalLaserColor);
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

            bool isControllerTracked = OVRInput.GetControllerPositionTracked(controller);
            if (isControllerTracked && trackingSpace != null)
            {
                Vector3 localPos = OVRInput.GetLocalControllerPosition(controller);
                Quaternion localRot = OVRInput.GetLocalControllerRotation(controller);
                Vector3 worldPos = trackingSpace.TransformPoint(localPos);
                Quaternion worldRot = trackingSpace.rotation * localRot;

                // Aim pointer forward with standard Quest ergonomic pitch angle
                Quaternion aimRot = worldRot * Quaternion.Euler(pointerPitchAngle, 0f, 0f);
                rayOrigin = worldPos + worldRot * new Vector3(0f, tipDownOffset, tipForwardOffset);
                rayDir = aimRot * Vector3.forward;
            }
            else if (leftControllerAnchor != null)
            {
                Quaternion aimRot = leftControllerAnchor.rotation * Quaternion.Euler(pointerPitchAngle, 0f, 0f);
                rayOrigin = leftControllerAnchor.position + leftControllerAnchor.forward * tipForwardOffset + leftControllerAnchor.up * tipDownOffset;
                rayDir = aimRot * Vector3.forward;
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

            // Safe Trigger Input check (No legacy Input exceptions)
            bool isTriggerDown = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller)
                              || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.45f;

            try
            {
                isTriggerDown |= Input.GetMouseButton(0);
            }
            catch { }

            bool isTriggerJustPressed = isTriggerDown && !wasTriggerDown;
            bool isTriggerJustReleased = !isTriggerDown && wasTriggerDown;

            // Handle active window dragging
            if (activeDraggingWindow != null)
            {
                if (isTriggerDown)
                {
                    Vector3 targetWindowPos = rayOrigin + rayDir * activeDragDistance - activeDragLocalOffset;
                    activeDraggingWindow.transform.position = Vector3.Lerp(activeDraggingWindow.transform.position, targetWindowPos, Time.deltaTime * 25f);
                }
                else
                {
                    activeDraggingWindow = null;
                }
            }

            Ray ray = new Ray(rayOrigin, rayDir);
            Vector3 endPoint = rayOrigin + rayDir * maxRayLength;
            Vector3 hitNormal = -rayDir;
            bool hasHit = false;

            Selectable hitSelectable = null;
            VRWindowDragger hitDragger = null;

            // Raycast against World Space UI Canvases and Draggers
            hasHit = RaycastUIAndDraggers(ray, ref endPoint, ref hitNormal, ref hitSelectable, ref hitDragger);

            // Handle Hover State Transitions
            UpdateHoverStates(hitSelectable, hitDragger);

            // Trigger Pressed: Click UI Button or Begin Window Drag
            if (isTriggerJustPressed)
            {
                if (hitSelectable != null)
                {
                    ExecuteButtonClick(hitSelectable);
                }
                else if (hitDragger != null)
                {
                    activeDraggingWindow = hitDragger;
                    activeDragDistance = Vector3.Distance(rayOrigin, hitDragger.transform.position);
                    activeDragLocalOffset = endPoint - hitDragger.transform.position;
                    HapticPulse(0.5f, 0.08f);
                }
            }

            // Update Laser Beam Visuals
            lineRenderer.enabled = true;
            lineRenderer.SetPosition(0, rayOrigin);
            lineRenderer.SetPosition(1, endPoint);

            // Visual Colors
            Color activeColor = normalLaserColor;
            if (isTriggerDown) activeColor = clickLaserColor;
            else if (currentHoverSelectable != null || currentHoverDragger != null || activeDraggingWindow != null) activeColor = hoverLaserColor;

            SetVisualColors(activeColor);

            // Update Reticle Disc
            if (reticleObj != null)
            {
                reticleObj.SetActive(hasHit);
                if (hasHit)
                {
                    reticleObj.transform.position = endPoint + hitNormal * 0.003f;
                    reticleObj.transform.rotation = Quaternion.LookRotation(hitNormal, Vector3.up);
                }
            }

            wasTriggerDown = isTriggerDown;
        }

        private bool RaycastUIAndDraggers(Ray ray, ref Vector3 endPoint, ref Vector3 hitNormal, ref Selectable hitSelectable, ref VRWindowDragger hitDragger)
        {
            float closestDist = maxRayLength;
            bool hitAnything = false;

            // 1. Raycast World Space Canvases
            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (var c in canvases)
            {
                if (c == null || !c.gameObject.activeInHierarchy || c.renderMode != RenderMode.WorldSpace) continue;

                RectTransform crt = c.GetComponent<RectTransform>();
                if (crt == null) continue;

                Plane plane = new Plane(-c.transform.forward, c.transform.position);
                float enter;
                if (plane.Raycast(ray, out enter) && enter > 0.05f && enter < closestDist)
                {
                    Vector3 worldHit = ray.GetPoint(enter);
                    Vector3 localHit = crt.InverseTransformPoint(worldHit);
                    Rect r = crt.rect;

                    if (r.Contains(new Vector2(localHit.x, localHit.y)))
                    {
                        closestDist = enter;
                        endPoint = worldHit;
                        hitNormal = -c.transform.forward;
                        hitAnything = true;

                        // Check Buttons/Selectables within this canvas
                        var selectables = c.GetComponentsInChildren<Selectable>(false);
                        Selectable bestSel = null;
                        float bestSelDist = float.MaxValue;

                        foreach (var sel in selectables)
                        {
                            if (sel == null || !sel.gameObject.activeInHierarchy || !sel.interactable) continue;
                            RectTransform srt = sel.GetComponent<RectTransform>();
                            if (srt == null) continue;

                            Vector3 selLocal = srt.InverseTransformPoint(worldHit);
                            if (srt.rect.Contains(new Vector2(selLocal.x, selLocal.y)))
                            {
                                float d = Vector3.Distance(worldHit, srt.position);
                                if (d < bestSelDist)
                                {
                                    bestSelDist = d;
                                    bestSel = sel;
                                }
                            }
                        }
                        hitSelectable = bestSel;

                        // Check Window Draggers in canvas
                        var draggers = c.GetComponentsInChildren<VRWindowDragger>(false);
                        foreach (var d in draggers)
                        {
                            if (d == null || !d.gameObject.activeInHierarchy) continue;
                            RectTransform drt = d.GetComponent<RectTransform>();
                            if (drt != null)
                            {
                                Vector3 dLocal = drt.InverseTransformPoint(worldHit);
                                if (drt.rect.Contains(new Vector2(dLocal.x, dLocal.y)))
                                {
                                    hitDragger = d;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            // 2. Physics Raycast fallback for 3D Draggers / Colliders
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, closestDist))
            {
                closestDist = hit.distance;
                endPoint = hit.point;
                hitNormal = hit.normal;
                hitAnything = true;

                var btn = hit.collider.GetComponentInParent<Selectable>();
                if (btn != null && btn.interactable) hitSelectable = btn;

                var drag = hit.collider.GetComponentInParent<VRWindowDragger>();
                if (drag != null) hitDragger = drag;
            }

            return hitAnything;
        }

        private void UpdateHoverStates(Selectable newSelectable, VRWindowDragger newDragger)
        {
            // Transition Selectable Hover
            if (newSelectable != currentHoverSelectable)
            {
                if (currentHoverSelectable != null && EventSystem.current != null)
                {
                    ExecuteEvents.Execute(currentHoverSelectable.gameObject, pointerEventData, ExecuteEvents.pointerExitHandler);
                }

                currentHoverSelectable = newSelectable;

                if (currentHoverSelectable != null && EventSystem.current != null)
                {
                    ExecuteEvents.Execute(currentHoverSelectable.gameObject, pointerEventData, ExecuteEvents.pointerEnterHandler);
                    HapticPulse(0.18f, 0.04f);
                }
            }

            // Transition Dragger Hover
            if (newDragger != currentHoverDragger)
            {
                if (currentHoverDragger != null) currentHoverDragger.SetHovered(false);
                currentHoverDragger = newDragger;
                if (currentHoverDragger != null)
                {
                    currentHoverDragger.SetHovered(true);
                    HapticPulse(0.20f, 0.04f);
                }
            }
        }

        private void ExecuteButtonClick(Selectable sel)
        {
            HapticPulse(0.6f, 0.10f);

            if (bhapticsFeedback != null)
            {
                bhapticsFeedback.TriggerClickPulse(70, 60);
            }

            if (EventSystem.current != null)
            {
                ExecuteEvents.Execute(sel.gameObject, pointerEventData, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(sel.gameObject, pointerEventData, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(sel.gameObject, pointerEventData, ExecuteEvents.pointerClickHandler);
                ExecuteEvents.Execute(sel.gameObject, pointerEventData, ExecuteEvents.submitHandler);
            }

            var btn = sel as Button;
            if (btn != null && btn.onClick != null)
            {
                btn.onClick.Invoke();
            }

            StartCoroutine(PunchScale(sel.transform));
        }

        private IEnumerator PunchScale(Transform t)
        {
            if (t == null) yield break;
            Vector3 orig = t.localScale;
            t.localScale = orig * 0.92f;
            yield return new WaitForSeconds(0.06f);
            if (t != null) t.localScale = orig * 1.05f;
            yield return new WaitForSeconds(0.06f);
            if (t != null) t.localScale = orig;
        }

        private void SetVisualColors(Color col)
        {
            lineRenderer.startColor = col;
            lineRenderer.endColor = new Color(col.r, col.g, col.b, 0.25f);

            if (lineMaterial != null)
            {
                lineMaterial.color = col;
                if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", col);
            }

            if (reticleMaterial != null)
            {
                reticleMaterial.color = col;
                if (reticleMaterial.HasProperty("_BaseColor")) reticleMaterial.SetColor("_BaseColor", col);
            }
        }

        public void HapticPulse(float amplitude, float duration)
        {
            try
            {
                OVRInput.SetControllerVibration(amplitude, amplitude, controller);
                StartCoroutine(StopHapticsAfter(duration));
            }
            catch { }
        }

        private IEnumerator StopHapticsAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            try
            {
                OVRInput.SetControllerVibration(0f, 0f, controller);
            }
            catch { }
        }
    }
}
