using System;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// VRHandStereoFixer: แก้ปัญหาการมองไม่เห็นมือทั้งใน Editor และ Standalone Build (.exe ผ่าน Meta Horizon Link)
    /// 
    /// สาเหตุสำคัญที่ Build แล้วไม่เห็นมือ:
    /// 1. Meta OVRMesh & OVRMeshRenderer Bug ใน Standalone Build:
    ///    - เมต้าเขียนโค้ด Update() { ShouldInitialize(); } ครอบด้วย #if UNITY_EDITOR ไว้
    ///    - ใน Standalone Build โค้ด Update() ดังกล่าวจึงถูกตัดออก (Stripped)
    ///    - เมื่อเปิดเกมผ่าน Link ถ้า Hand Tracking ยัง Handshake ไม่เสร็จที่ Frame 0 (Start)
    ///      OVRMesh และ OVRMeshRenderer จะไม่ถูก Initialize ตลอดกาล ทำให้โมเดลมือหายสนิท
    ///    - สคริปต์นี้จะบังคับตรวจจับและเรียก Initialize() / ForceRebind() ทันทีที่ข้อมูลพร้อมใน Build
    /// 
    /// 2. OVRMeshRenderer _confidenceBehavior = ToggleRenderer:
    ///    - เมื่อค่า Confidence ดรอปชั่วขณะ หรือเมื่อหยิบ Controller, OVRMeshRenderer จะปิด smr.enabled = false ทันที
    ///    - สคริปต์นี้จะปลดล็อคให้เป็น ConfidenceBehavior.None เพื่อให้โมเดลมือแสดงผลต่อเนื่อง
    /// 
    /// 3. Single Pass Instanced Stereo Rendering:
    ///    - บังคับใช้ URPHandOutline Shader พร้อม GPU Instancing และ Two-sided Rendering (Cull Off)
    /// </summary>
    public class VRHandStereoFixer : MonoBehaviour
    {
        [Tooltip("Custom URP Hand Material (optional, will auto-create or use URP fallback if null)")]
        public Material urpHandMaterial;

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
        }

        private void Update()
        {
            EnableControllerDrivenHandPoses();
            FixOVRMeshRenderers();
            ApplyStereoFix();
        }

        private void LateUpdate()
        {
            ApplyStereoFix();
        }

        private void EnableControllerDrivenHandPoses()
        {
            var mgr = OVRManager.instance ?? FindAnyObjectByType<OVRManager>();
            if (mgr != null && mgr.controllerDrivenHandPosesType != OVRManager.ControllerDrivenHandPosesType.ConformingToController)
            {
                mgr.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.ConformingToController;
            }

            var helpers = FindObjectsByType<OVRControllerHelper>(FindObjectsSortMode.None);
            foreach (var h in helpers)
            {
                if (h.m_showState != OVRInput.InputDeviceShowState.Always)
                {
                    h.m_showState = OVRInput.InputDeviceShowState.Always;
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

                    // Crisp white/silver contour silhouette outline
                    if (urpHandMaterial.HasProperty("_ColorPrimary"))
                    {
                        urpHandMaterial.SetColor("_ColorPrimary", new Color(0.95f, 0.98f, 1.0f, 1.0f));
                    }

                    // Translucent dark slate frosted glass hand body
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

                var ovrMesh = omr.GetComponent<OVRMesh>();
                var ovrSkeleton = omr.GetComponent<OVRSkeleton>();

                // 1. CRITICAL FIX FOR STANDALONE BUILDS:
                // OVRMesh only checks ShouldInitialize() in Update() under #if UNITY_EDITOR!
                // In a standalone build, if OVRMesh wasn't ready at frame 0 (Start), it never initializes!
                // We force OVRMesh to initialize as soon as hand tracking connects over Link:
                if (ovrMesh != null && !ovrMesh.IsInitialized)
                {
                    try
                    {
                        var mInit = typeof(OVRMesh).GetMethod("Initialize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (mInit != null)
                        {
                            var fType = typeof(OVRMesh).GetField("_meshType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var meshTypeVal = fType != null ? fType.GetValue(ovrMesh) : null;
                        if (meshTypeVal != null)
                        {
                            mInit.Invoke(ovrMesh, new object[] { meshTypeVal });
                        }
                        }
                    }
                    catch { }
                }

                // 2. Prevent OVRMeshRenderer from hiding the hand when confidence fluctuates or controller is held
                try
                {
                    var confField = typeof(OVRMeshRenderer).GetField("_confidenceBehavior", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (confField != null)
                    {
                        confField.SetValue(omr, OVRMeshRenderer.ConfidenceBehavior.None);
                    }
                }
                catch { }

                // 3. Force OVRMeshRenderer to bind mesh and skeleton
                if (!omr.IsInitialized)
                {
                    bool meshReady = ovrMesh != null && ovrMesh.IsInitialized;
                    bool skeletonReady = ovrSkeleton == null || ovrSkeleton.IsInitialized;

                    if (meshReady && skeletonReady)
                    {
                        omr.ForceRebind();
                    }
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
    }
}
