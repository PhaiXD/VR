using System;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// VRHandStereoFixer: Comprehensive Fix for Meta Quest Hand & Controller Tracking in Unity VR
    /// - Guarantees hand mesh visibility in standalone builds (.exe & Link)
    /// - Prevents phantom controller models: Only shows controller 3D mesh when actually in hand (ControllerInHand)
    /// - Fixes Single Pass Instanced stereo rendering: ensures World Space Canvases and Text render in BOTH eyes
    /// - Configures Left Hand Remote Laser Pointer and finger grip pose handlers
    /// </summary>
    public class VRHandStereoFixer : MonoBehaviour
    {
        [Tooltip("Custom URP Hand Material (optional, will auto-create or use URP fallback if null)")]
        public Material urpHandMaterial;

        private Camera centerEyeCamera;
        private Material stereoTextMaterial;

        private void Awake()
        {
            EnsureMaterial();
            EnableControllerDrivenHandPoses();
        }

        private void Start()
        {
            EnsureMaterial();
            EnableControllerDrivenHandPoses();
            FixOVRMeshRenderers();
            ApplyStereoFix();
            FixStereoWorldCanvasesAndText();
            EnsureGripPoseAndLaserPointer();
        }

        private void Update()
        {
            EnableControllerDrivenHandPoses();
            FixOVRMeshRenderers();
            ApplyStereoFix();
            FixStereoWorldCanvasesAndText();
            EnsureGripPoseAndLaserPointer();
        }

        private void LateUpdate()
        {
            ApplyStereoFix();
            FixStereoWorldCanvasesAndText();
            EnsureGripPoseAndLaserPointer();
        }

        private void EnableControllerDrivenHandPoses()
        {
            var mgr = OVRManager.instance ?? FindAnyObjectByType<OVRManager>();
            if (mgr != null && mgr.controllerDrivenHandPosesType != OVRManager.ControllerDrivenHandPosesType.ConformingToController)
            {
                mgr.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.ConformingToController;
            }

            // CRITICAL FIX FOR PHANTOM CONTROLLERS:
            // Use ControllerInHand so that the 3D controller model ONLY appears
            // on the hand that is actually holding a physical remote!
            var helpers = FindObjectsByType<OVRControllerHelper>(FindObjectsSortMode.None);
            foreach (var h in helpers)
            {
                if (h.m_showState != OVRInput.InputDeviceShowState.ControllerInHand)
                {
                    h.m_showState = OVRInput.InputDeviceShowState.ControllerInHand;
                }
                if (!h.showWhenHandsArePoweredByNaturalControllerPoses)
                {
                    h.showWhenHandsArePoweredByNaturalControllerPoses = true;
                }
            }
        }

        private void EnsureMaterial()
        {
            if (urpHandMaterial == null)
            {
                Shader urpShader = Shader.Find("UR3eVR/URPHandOutline")
                                ?? Shader.Find("UR3eVR/StereoUnlitColor")
                                ?? Shader.Find("Universal Render Pipeline/Lit")
                                ?? Shader.Find("Universal Render Pipeline/Unlit");

                if (urpShader != null)
                {
                    urpHandMaterial = new Material(urpShader);
                    urpHandMaterial.name = "RuntimeURPHandMaterial";
                    urpHandMaterial.enableInstancing = true;

                    if (urpHandMaterial.HasProperty("_Cull"))
                    {
                        urpHandMaterial.SetFloat("_Cull", 0f); // Two-sided
                    }

                    if (urpHandMaterial.HasProperty("_ColorPrimary"))
                    {
                        urpHandMaterial.SetColor("_ColorPrimary", new Color(0.95f, 0.98f, 1.0f, 1.0f));
                    }
                    if (urpHandMaterial.HasProperty("_ColorTop"))
                    {
                        urpHandMaterial.SetColor("_ColorTop", new Color(0.12f, 0.16f, 0.22f, 0.85f));
                    }
                    if (urpHandMaterial.HasProperty("_ColorBottom"))
                    {
                        urpHandMaterial.SetColor("_ColorBottom", new Color(0.04f, 0.06f, 0.10f, 0.85f));
                    }
                    if (urpHandMaterial.HasProperty("_RimFactor"))
                    {
                        urpHandMaterial.SetFloat("_RimFactor", 2.2f);
                    }
                    if (urpHandMaterial.HasProperty("_FresnelPower"))
                    {
                        urpHandMaterial.SetFloat("_FresnelPower", 1.5f);
                    }
                    if (urpHandMaterial.HasProperty("_HandAlpha"))
                    {
                        urpHandMaterial.SetFloat("_HandAlpha", 0.85f);
                    }
                    if (urpHandMaterial.HasProperty("_BaseAlpha"))
                    {
                        urpHandMaterial.SetFloat("_BaseAlpha", 0.35f);
                    }
                }
            }
        }

        public void FixOVRMeshRenderers()
        {
            var omrs = FindObjectsByType<OVRMeshRenderer>(FindObjectsSortMode.None);
            if (omrs == null || omrs.Length == 0) return;

            foreach (var omr in omrs)
            {
                if (omr == null) continue;

                // 1. Prevent OVRMeshRenderer from hiding the hand when confidence fluctuates or controller is held
                try
                {
                    var confField = typeof(OVRMeshRenderer).GetField("_confidenceBehavior", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (confField != null)
                    {
                        confField.SetValue(omr, OVRMeshRenderer.ConfidenceBehavior.None);
                    }
                }
                catch { }

                // 2. Prevent swapping to uninstanced system gesture material
                try
                {
                    var gestField = typeof(OVRMeshRenderer).GetField("_systemGestureBehavior", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (gestField != null)
                    {
                        gestField.SetValue(omr, OVRMeshRenderer.SystemGestureBehavior.None);
                    }
                }
                catch { }

                // 3. Set original material reference to instanced URP material
                try
                {
                    var origMatField = typeof(OVRMeshRenderer).GetField("_originalMaterial", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (origMatField != null && urpHandMaterial != null)
                    {
                        origMatField.SetValue(omr, urpHandMaterial);
                    }
                }
                catch { }

                // 4. Force bind mesh and skeleton once both are ready
                var ovrMesh = omr.GetComponent<OVRMesh>();
                var ovrSkeleton = omr.GetComponent<OVRSkeleton>();
                if (!omr.IsInitialized && ovrMesh != null && ovrMesh.IsInitialized && ovrSkeleton != null && ovrSkeleton.IsInitialized)
                {
                    omr.ForceRebind();
                }
            }
        }

        public void ApplyStereoFix()
        {
            var smrs = FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            if (smrs == null || smrs.Length == 0) return;

            foreach (var smr in smrs)
            {
                if (smr == null || smr.gameObject == null) continue;

                string goName = smr.gameObject.name.ToLower();
                bool isHand = goName.Contains("hand") || 
                              smr.GetComponent<OVRHand>() != null || 
                              smr.GetComponent<OVRSkeleton>() != null ||
                              smr.GetComponent<OVRMeshRenderer>() != null;

                if (isHand)
                {
                    // 1. Never cull hands offscreen in VR stereo
                    if (!smr.updateWhenOffscreen)
                    {
                        smr.updateWhenOffscreen = true;
                    }

                    // 2. Keep hand mesh enabled so hand is visible in builds
                    if (smr.sharedMesh != null && !smr.enabled)
                    {
                        smr.enabled = true;
                    }

                    // 3. Fix materials for VR Single Pass Instanced
                    var mats = smr.sharedMaterials;
                    if (mats == null || mats.Length == 0) continue;

                    bool needReassign = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        Material m = mats[i];
                        if (m == null)
                        {
                            if (urpHandMaterial != null)
                            {
                                mats[i] = urpHandMaterial;
                                needReassign = true;
                            }
                            continue;
                        }

                        bool isLegacy = m.shader != null && (m.shader.name.Contains("Oculus Sample") || m.shader.name.Contains("Alpha Hand Outline") || m.shader.name.Contains("Sprites/Default"));
                        if (isLegacy || !m.enableInstancing)
                        {
                            if (urpHandMaterial != null)
                            {
                                mats[i] = urpHandMaterial;
                                needReassign = true;
                            }
                        }
                    }

                    if (needReassign)
                    {
                        smr.sharedMaterials = mats;

                        var ovrRenderer = smr.GetComponent<OVRMeshRenderer>();
                        if (ovrRenderer != null && mats.Length > 0 && mats[0] != null)
                        {
                            ovrRenderer.SetMaterial(mats[0]);
                        }
                    }
                }
            }
        }

        public void FixStereoWorldCanvasesAndText()
        {
            if (centerEyeCamera == null || !centerEyeCamera.gameObject.activeInHierarchy)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) centerEyeCamera = cec.GetComponent<Camera>();
                if (centerEyeCamera == null && Camera.main != null) centerEyeCamera = Camera.main;
            }

            if (centerEyeCamera != null)
            {
                // Include inactive canvases (such as VRMenuManager when closed)
                var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var c in canvases)
                {
                    if (c == null) continue;
                    if (c.renderMode == RenderMode.WorldSpace)
                    {
                        if (c.worldCamera != centerEyeCamera)
                        {
                            c.worldCamera = centerEyeCamera;
                        }
                    }
                }
            }

            if (stereoTextMaterial == null)
            {
                Shader s = Shader.Find("UR3eVR/StereoTextShader");
                if (s != null)
                {
                    stereoTextMaterial = new Material(s);
                    stereoTextMaterial.name = "VR_StereoText_Material";
                    stereoTextMaterial.enableInstancing = true;
                }
            }

            if (stereoTextMaterial != null)
            {
                var textMeshes = FindObjectsByType<TextMesh>(FindObjectsSortMode.None);
                foreach (var tm in textMeshes)
                {
                    if (tm == null) continue;
                    var mr = tm.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        var sm = mr.sharedMaterial;
                        if (sm == null || sm.shader == null || sm.shader.name != "UR3eVR/StereoTextShader")
                        {
                            Texture fTex = (tm.font != null && tm.font.material != null) ? tm.font.material.mainTexture : (sm != null ? sm.mainTexture : null);
                            Material m = new Material(stereoTextMaterial);
                            if (fTex != null) m.mainTexture = fTex;
                            m.enableInstancing = true;
                            mr.sharedMaterial = m;
                        }
                    }
                }
            }
        }

        public void EnsureGripPoseAndLaserPointer()
        {
            // Ensure VRControllerGripPoseHandler on both hands
            var hands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
            foreach (var h in hands)
            {
                if (h == null) continue;
                if (h.GetComponent<VRControllerGripPoseHandler>() == null)
                {
                    var grip = h.gameObject.AddComponent<VRControllerGripPoseHandler>();
                    grip.ovrHand = h;
                    grip.ovrSkeleton = h.GetComponent<OVRSkeleton>();
                }
            }

            // CRITICAL: Ensure VRRemoteLaserPointer is on LEFT CONTROLLER ANCHOR
            var lca = GameObject.Find("LeftControllerAnchor");
            var laser = VRRemoteLaserPointer.Instance ?? FindAnyObjectByType<VRRemoteLaserPointer>();

            if (laser == null)
            {
                GameObject laserObj = new GameObject("VR_Remote_LaserPointer");
                if (lca != null) laserObj.transform.SetParent(lca.transform, false);
                laser = laserObj.AddComponent<VRRemoteLaserPointer>();
                laser.controller = OVRInput.Controller.LTouch;
                laser.leftControllerAnchor = lca != null ? lca.transform : null;
            }
            else
            {
                // Ensure properly parented to LeftControllerAnchor
                if (lca != null && laser.transform.parent != lca.transform)
                {
                    laser.transform.SetParent(lca.transform, false);
                    laser.transform.localPosition = Vector3.zero;
                    laser.transform.localRotation = Quaternion.identity;
                }
                laser.controller = OVRInput.Controller.LTouch;
                if (laser.leftControllerAnchor == null && lca != null)
                {
                    laser.leftControllerAnchor = lca.transform;
                }
            }
        }
    }
}
