using System;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// Guarantees that Meta XR Hand Tracking meshes render correctly in BOTH eyes
    /// under URP Single Pass Instanced (Multiview) stereo rendering.
    /// Fixes the issue where hands are only visible in the left eye.
    /// </summary>
    public class VRHandStereoFixer : MonoBehaviour
    {
        [Tooltip("Custom URP Hand Material (optional, will auto-create or use URP fallback if null)")]
        public Material urpHandMaterial;

        private void Awake()
        {
            if (urpHandMaterial == null)
            {
                // Try to load URPHandMaterial from Assets
                Shader urpShader = Shader.Find("UR3eVR/URPHandOutline")
                                ?? Shader.Find("Universal Render Pipeline/Lit")
                                ?? Shader.Find("Universal Render Pipeline/Simple Lit");

                if (urpShader != null)
                {
                    urpHandMaterial = new Material(urpShader);
                    urpHandMaterial.name = "RuntimeURPHandMaterial";
                    urpHandMaterial.enableInstancing = true;

                    if (urpHandMaterial.HasProperty("_Cull"))
                    {
                        urpHandMaterial.SetFloat("_Cull", 0f); // Two-sided
                    }
                    if (urpHandMaterial.HasProperty("_BaseColor"))
                    {
                        urpHandMaterial.SetColor("_BaseColor", new Color(0.25f, 0.70f, 1.0f, 0.90f));
                    }
                }
            }
        }

        private void Start()
        {
            ApplyStereoFix();
        }

        private void LateUpdate()
        {
            // OVRMeshRenderer may enable SkinnedMeshRenderer asynchronously once hands are tracked
            ApplyStereoFix();
        }

        public void ApplyStereoFix()
        {
            var smrs = FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            if (smrs == null || smrs.Length == 0) return;

            foreach (var smr in smrs)
            {
                if (smr == null || smr.gameObject == null) continue;

                // Target hand tracking GameObjects (Left Hand, Right Hand)
                string goName = smr.gameObject.name.ToLower();
                bool isHand = goName.Contains("hand") || 
                              smr.GetComponent<OVRHand>() != null || 
                              smr.GetComponent<OVRSkeleton>() != null;

                if (isHand)
                {
                    // 1. Never cull hands offscreen in VR stereo
                    if (!smr.updateWhenOffscreen)
                    {
                        smr.updateWhenOffscreen = true;
                    }

                    // 2. Fix materials for VR Single Pass Instanced
                    var mats = smr.sharedMaterials;
                    if (mats == null || mats.Length == 0) continue;

                    bool needReassign = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        Material m = mats[i];
                        if (m == null) continue;

                        // Check if material is using legacy BIRP shader or has GPU instancing disabled
                        bool isLegacy = m.shader != null && (m.shader.name.Contains("Oculus Sample") || m.shader.name.Contains("Alpha Hand Outline"));
                        if (isLegacy || !m.enableInstancing)
                        {
                            if (urpHandMaterial != null)
                            {
                                mats[i] = urpHandMaterial;
                                needReassign = true;
                            }
                            else
                            {
                                Shader s = Shader.Find("UR3eVR/URPHandOutline") 
                                        ?? Shader.Find("Universal Render Pipeline/Lit");
                                if (s != null)
                                {
                                    m.shader = s;
                                    m.enableInstancing = true;
                                    if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
                                    needReassign = true;
                                }
                            }
                        }
                    }

                    if (needReassign)
                    {
                        smr.sharedMaterials = mats;

                        // Also notify OVRMeshRenderer if present
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
