using System;
using System.Collections.Generic;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// VRControllerGripPoseHandler: Controls hand mesh finger curling when holding Meta Quest Touch Controller.
    /// - Only curls fingers when the specific controller (Left/Right) is actually held/connected.
    /// - Middle, Ring, Pinky wrap around controller handle, reacting to grip trigger.
    /// - Index finger rests on the front index trigger, curling dynamically when trigger is pulled.
    /// - Thumb rests near thumbstick and face buttons.
    /// - When bare hand tracking is active (e.g. right hand), grip weight is 0 so hand is in natural tracking pose.
    /// </summary>
    public class VRControllerGripPoseHandler : MonoBehaviour
    {
        [Header("Hand & Controller Configuration")]
        public bool isRightHand = false;
        public OVRInput.Controller controller = OVRInput.Controller.LTouch;

        [Header("Pose Blending")]
        [Range(0f, 1f)]
        public float gripWeight = 0f;
        public float blendSpeed = 14f;

        [Header("References")]
        public OVRSkeleton ovrSkeleton;
        public OVRHand ovrHand;

        private class FingerJoints
        {
            public Transform proximal;
            public Transform intermediate;
            public Transform distal;
            public Quaternion defaultProximalRot;
            public Quaternion defaultIntermediateRot;
            public Quaternion defaultDistalRot;
        }

        private FingerJoints indexFinger;
        private FingerJoints middleFinger;
        private FingerJoints ringFinger;
        private FingerJoints pinkyFinger;
        private FingerJoints thumbFinger;

        private bool bonesCached = false;

        private void Awake()
        {
            if (ovrSkeleton == null) ovrSkeleton = GetComponent<OVRSkeleton>();
            if (ovrHand == null) ovrHand = GetComponent<OVRHand>();

            DetectHandSide();
        }

        private void Start()
        {
            DetectHandSide();
            TryCacheBones();
        }

        private void DetectHandSide()
        {
            if (ovrSkeleton != null)
            {
                var st = ovrSkeleton.GetSkeletonType();
                if (st == OVRSkeleton.SkeletonType.XRHandRight || st == OVRSkeleton.SkeletonType.HandRight)
                {
                    isRightHand = true;
                }
                else if (st == OVRSkeleton.SkeletonType.XRHandLeft || st == OVRSkeleton.SkeletonType.HandLeft)
                {
                    isRightHand = false;
                }
                else
                {
                    isRightHand = name.ToLower().Contains("right");
                }
            }
            else
            {
                isRightHand = name.ToLower().Contains("right");
            }

            controller = isRightHand ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
        }

        private void Update()
        {
            if (!bonesCached)
            {
                TryCacheBones();
            }
        }

        private void TryCacheBones()
        {
            if (ovrSkeleton == null) ovrSkeleton = GetComponent<OVRSkeleton>();

            // Method A: Check OVRSkeleton.Bones
            if (ovrSkeleton != null && ovrSkeleton.IsInitialized && ovrSkeleton.Bones != null && ovrSkeleton.Bones.Count > 0)
            {
                var boneMap = new Dictionary<OVRSkeleton.BoneId, Transform>();
                foreach (var b in ovrSkeleton.Bones)
                {
                    if (b != null && b.Transform != null)
                    {
                        boneMap[b.Id] = b.Transform;
                    }
                }

                // Index
                Transform idx1 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Index1);
                Transform idx2 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Index2);
                Transform idx3 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Index3);
                if (idx1 != null)
                {
                    indexFinger = new FingerJoints
                    {
                        proximal = idx1, intermediate = idx2, distal = idx3,
                        defaultProximalRot = idx1.localRotation,
                        defaultIntermediateRot = idx2 != null ? idx2.localRotation : Quaternion.identity,
                        defaultDistalRot = idx3 != null ? idx3.localRotation : Quaternion.identity
                    };
                }

                // Middle
                Transform mid1 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Middle1);
                Transform mid2 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Middle2);
                Transform mid3 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Middle3);
                if (mid1 != null)
                {
                    middleFinger = new FingerJoints
                    {
                        proximal = mid1, intermediate = mid2, distal = mid3,
                        defaultProximalRot = mid1.localRotation,
                        defaultIntermediateRot = mid2 != null ? mid2.localRotation : Quaternion.identity,
                        defaultDistalRot = mid3 != null ? mid3.localRotation : Quaternion.identity
                    };
                }

                // Ring
                Transform rng1 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Ring1);
                Transform rng2 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Ring2);
                Transform rng3 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Ring3);
                if (rng1 != null)
                {
                    ringFinger = new FingerJoints
                    {
                        proximal = rng1, intermediate = rng2, distal = rng3,
                        defaultProximalRot = rng1.localRotation,
                        defaultIntermediateRot = rng2 != null ? rng2.localRotation : Quaternion.identity,
                        defaultDistalRot = rng3 != null ? rng3.localRotation : Quaternion.identity
                    };
                }

                // Pinky
                Transform pnk1 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Pinky1);
                Transform pnk2 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Pinky2);
                Transform pnk3 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Pinky3);
                if (pnk1 != null)
                {
                    pinkyFinger = new FingerJoints
                    {
                        proximal = pnk1, intermediate = pnk2, distal = pnk3,
                        defaultProximalRot = pnk1.localRotation,
                        defaultIntermediateRot = pnk2 != null ? pnk2.localRotation : Quaternion.identity,
                        defaultDistalRot = pnk3 != null ? pnk3.localRotation : Quaternion.identity
                    };
                }

                // Thumb
                Transform thm1 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Thumb1) ?? GetBone(boneMap, OVRSkeleton.BoneId.Hand_Thumb0);
                Transform thm2 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Thumb2);
                Transform thm3 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Thumb3);
                if (thm1 != null)
                {
                    thumbFinger = new FingerJoints
                    {
                        proximal = thm1, intermediate = thm2, distal = thm3,
                        defaultProximalRot = thm1.localRotation,
                        defaultIntermediateRot = thm2 != null ? thm2.localRotation : Quaternion.identity,
                        defaultDistalRot = thm3 != null ? thm3.localRotation : Quaternion.identity
                    };
                }

                if (indexFinger != null && middleFinger != null)
                {
                    bonesCached = true;
                    return;
                }
            }

            // Method B: Fallback search on child transforms
            var transforms = GetComponentsInChildren<Transform>(true);
            if (transforms != null && transforms.Length > 10)
            {
                var dict = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in transforms) dict[t.name] = t;

                Transform idxP = FindBone(dict, "index", "proximal", "index1");
                Transform idxI = FindBone(dict, "index", "intermediate", "index2");
                Transform idxD = FindBone(dict, "index", "distal", "index3");
                if (idxP != null)
                {
                    indexFinger = new FingerJoints
                    {
                        proximal = idxP, intermediate = idxI, distal = idxD,
                        defaultProximalRot = idxP.localRotation,
                        defaultIntermediateRot = idxI != null ? idxI.localRotation : Quaternion.identity,
                        defaultDistalRot = idxD != null ? idxD.localRotation : Quaternion.identity
                    };
                }

                Transform midP = FindBone(dict, "middle", "proximal", "middle1");
                Transform midI = FindBone(dict, "middle", "intermediate", "middle2");
                Transform midD = FindBone(dict, "middle", "distal", "middle3");
                if (midP != null)
                {
                    middleFinger = new FingerJoints
                    {
                        proximal = midP, intermediate = midI, distal = midD,
                        defaultProximalRot = midP.localRotation,
                        defaultIntermediateRot = midI != null ? midI.localRotation : Quaternion.identity,
                        defaultDistalRot = midD != null ? midD.localRotation : Quaternion.identity
                    };
                }

                Transform rngP = FindBone(dict, "ring", "proximal", "ring1");
                Transform rngI = FindBone(dict, "ring", "intermediate", "ring2");
                Transform rngD = FindBone(dict, "ring", "distal", "ring3");
                if (rngP != null)
                {
                    ringFinger = new FingerJoints
                    {
                        proximal = rngP, intermediate = rngI, distal = rngD,
                        defaultProximalRot = rngP.localRotation,
                        defaultIntermediateRot = rngI != null ? rngI.localRotation : Quaternion.identity,
                        defaultDistalRot = rngD != null ? rngD.localRotation : Quaternion.identity
                    };
                }

                Transform pnkP = FindBone(dict, "little", "proximal", "pinky1") ?? FindBone(dict, "pinky", "proximal", "pinky1");
                Transform pnkI = FindBone(dict, "little", "intermediate", "pinky2") ?? FindBone(dict, "pinky", "intermediate", "pinky2");
                Transform pnkD = FindBone(dict, "little", "distal", "pinky3") ?? FindBone(dict, "pinky", "distal", "pinky3");
                if (pnkP != null)
                {
                    pinkyFinger = new FingerJoints
                    {
                        proximal = pnkP, intermediate = pnkI, distal = pnkD,
                        defaultProximalRot = pnkP.localRotation,
                        defaultIntermediateRot = pnkI != null ? pnkI.localRotation : Quaternion.identity,
                        defaultDistalRot = pnkD != null ? pnkD.localRotation : Quaternion.identity
                    };
                }

                Transform thmP = FindBone(dict, "thumb", "proximal", "thumb1") ?? FindBone(dict, "thumb", "metacarpal", "thumb0");
                Transform thmD = FindBone(dict, "thumb", "distal", "thumb2");
                if (thmP != null)
                {
                    thumbFinger = new FingerJoints
                    {
                        proximal = thmP, intermediate = null, distal = thmD,
                        defaultProximalRot = thmP.localRotation,
                        defaultIntermediateRot = Quaternion.identity,
                        defaultDistalRot = thmD != null ? thmD.localRotation : Quaternion.identity
                    };
                }

                if (indexFinger != null && middleFinger != null)
                {
                    bonesCached = true;
                }
            }
        }

        private Transform GetBone(Dictionary<OVRSkeleton.BoneId, Transform> map, OVRSkeleton.BoneId id)
        {
            Transform t;
            map.TryGetValue(id, out t);
            return t;
        }

        private Transform FindBone(Dictionary<string, Transform> dict, string finger, string joint, string legacy)
        {
            foreach (var kvp in dict)
            {
                string n = kvp.Key.ToLowerInvariant();
                if (n.Contains(finger) && n.Contains(joint)) return kvp.Value;
                if (n.Contains(legacy)) return kvp.Value;
            }
            return null;
        }

        private void LateUpdate()
        {
            if (!bonesCached)
            {
                TryCacheBones();
                if (!bonesCached) return;
            }

            // Check if THIS specific controller is held:
            bool isConnected = (OVRInput.GetConnectedControllers() & controller) != 0;
            bool isTracked = OVRInput.GetControllerPositionTracked(controller);
            bool hasInput = OVRInput.Get(OVRInput.Button.Any, controller)
                         || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.01f
                         || OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller) > 0.01f
                         || OVRInput.Get(OVRInput.Touch.Any, controller);

            // True ONLY if this specific controller is actively connected and held in this hand
            bool isHoldingRemote = isConnected && (isTracked || hasInput);

            // In Editor testing: left hand defaults to holding remote if left controller is assigned
            if (Application.isEditor && !isRightHand)
            {
                if (isTracked || hasInput || Input.GetKey(KeyCode.LeftShift) || Input.GetMouseButton(0))
                {
                    isHoldingRemote = true;
                }
            }

            // Smooth blend
            gripWeight = Mathf.MoveTowards(gripWeight, isHoldingRemote ? 1.0f : 0.0f, Time.deltaTime * blendSpeed);
            if (gripWeight <= 0.001f) return;

            float indexTrigger = Mathf.Clamp01(OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller));
            float handTrigger = Mathf.Clamp01(OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller));
            Vector2 thumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, controller);

            if (Application.isEditor && !isConnected)
            {
                if (Input.GetMouseButton(0)) indexTrigger = 0.9f;
                if (Input.GetMouseButton(1)) handTrigger = 0.9f;
            }

            float sign = isRightHand ? -1f : 1f;

            // 1. Middle Finger - Wraps around controller handle
            if (middleFinger != null)
            {
                float midProx = sign * Mathf.Lerp(48f, 74f, handTrigger);
                float midInter = sign * Mathf.Lerp(66f, 86f, handTrigger);
                float midDist = sign * Mathf.Lerp(36f, 52f, handTrigger);
                ApplyFingerJoint(middleFinger.proximal, middleFinger.defaultProximalRot, Quaternion.Euler(0f, 0f, midProx));
                ApplyFingerJoint(middleFinger.intermediate, middleFinger.defaultIntermediateRot, Quaternion.Euler(0f, 0f, midInter));
                ApplyFingerJoint(middleFinger.distal, middleFinger.defaultDistalRot, Quaternion.Euler(0f, 0f, midDist));
            }

            // 2. Ring Finger - Wraps around controller handle
            if (ringFinger != null)
            {
                float rngProx = sign * Mathf.Lerp(52f, 78f, handTrigger);
                float rngInter = sign * Mathf.Lerp(70f, 90f, handTrigger);
                float rngDist = sign * Mathf.Lerp(40f, 56f, handTrigger);
                ApplyFingerJoint(ringFinger.proximal, ringFinger.defaultProximalRot, Quaternion.Euler(0f, 0f, rngProx));
                ApplyFingerJoint(ringFinger.intermediate, ringFinger.defaultIntermediateRot, Quaternion.Euler(0f, 0f, rngInter));
                ApplyFingerJoint(ringFinger.distal, ringFinger.defaultDistalRot, Quaternion.Euler(0f, 0f, rngDist));
            }

            // 3. Pinky Finger - Wraps around controller handle
            if (pinkyFinger != null)
            {
                float pnkProx = sign * Mathf.Lerp(56f, 82f, handTrigger);
                float pnkInter = sign * Mathf.Lerp(74f, 92f, handTrigger);
                float pnkDist = sign * Mathf.Lerp(44f, 60f, handTrigger);
                ApplyFingerJoint(pinkyFinger.proximal, pinkyFinger.defaultProximalRot, Quaternion.Euler(0f, 0f, pnkProx));
                ApplyFingerJoint(pinkyFinger.intermediate, pinkyFinger.defaultIntermediateRot, Quaternion.Euler(0f, 0f, pnkInter));
                ApplyFingerJoint(pinkyFinger.distal, pinkyFinger.defaultDistalRot, Quaternion.Euler(0f, 0f, pnkDist));
            }

            // 4. Index Finger - Rests on front index trigger, curls when pressed
            if (indexFinger != null)
            {
                float idxProx = sign * Mathf.Lerp(26f, 58f, indexTrigger);
                float idxInter = sign * Mathf.Lerp(36f, 76f, indexTrigger);
                float idxDist = sign * Mathf.Lerp(20f, 44f, indexTrigger);
                ApplyFingerJoint(indexFinger.proximal, indexFinger.defaultProximalRot, Quaternion.Euler(0f, 0f, idxProx));
                ApplyFingerJoint(indexFinger.intermediate, indexFinger.defaultIntermediateRot, Quaternion.Euler(0f, 0f, idxInter));
                ApplyFingerJoint(indexFinger.distal, indexFinger.defaultDistalRot, Quaternion.Euler(0f, 0f, idxDist));
            }

            // 5. Thumb - Rests on controller faceplate / thumbstick
            if (thumbFinger != null)
            {
                float thmPitch = 16f + thumbstick.y * 7f;
                float thmYaw = sign * (22f + thumbstick.x * 9f);
                float thmRoll = sign * -28f;
                Quaternion thumbCurl = Quaternion.Euler(thmPitch, thmYaw, thmRoll);
                ApplyFingerJoint(thumbFinger.proximal, thumbFinger.defaultProximalRot, thumbCurl);

                if (thumbFinger.distal != null)
                {
                    ApplyFingerJoint(thumbFinger.distal, thumbFinger.defaultDistalRot, Quaternion.Euler(0f, 0f, sign * -16f));
                }
            }
        }

        private void ApplyFingerJoint(Transform joint, Quaternion defaultRot, Quaternion targetOffset)
        {
            if (joint == null) return;
            Quaternion targetRot = defaultRot * targetOffset;
            joint.localRotation = Quaternion.Slerp(joint.localRotation, targetRot, gripWeight);
        }
    }
}
