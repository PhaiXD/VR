using System;
using System.Collections.Generic;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// VRControllerGripPoseHandler: Controls hand mesh finger curling when holding Meta Quest Touch Controller.
    /// - Only curls fingers when the specific controller (Left/Right) is actually held/connected.
    /// - Middle, Ring, Pinky wrap around controller handle, flexing along local X-axis.
    /// - Index finger rests on the front index trigger, curling dynamically when trigger is pulled.
    /// - Thumb rests near thumbstick and face buttons.
    /// - Fully compatible with Meta Quest Natural controller-driven poses and OpenXR skeletal hands.
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
            public bool isCaptured;
        }

        private FingerJoints indexFinger;
        private FingerJoints middleFinger;
        private FingerJoints ringFinger;
        private FingerJoints pinkyFinger;
        private FingerJoints thumbFinger;

        private bool bonesCached = false;
        private bool hasValidBones = false;

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
            if (!bonesCached || !hasValidBones)
            {
                TryCacheBones();
            }
        }

        private void TryCacheBones()
        {
            if (ovrSkeleton == null) ovrSkeleton = GetComponent<OVRSkeleton>();

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

                // Index (Hand_Index1, Hand_Index2, Hand_Index3)
                Transform idx1 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Index1) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_IndexProximal);
                Transform idx2 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Index2) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_IndexIntermediate);
                Transform idx3 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Index3) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_IndexDistal);
                if (idx1 != null)
                {
                    indexFinger = new FingerJoints { proximal = idx1, intermediate = idx2, distal = idx3 };
                }

                // Middle
                Transform mid1 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Middle1) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_MiddleProximal);
                Transform mid2 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Middle2) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_MiddleIntermediate);
                Transform mid3 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Middle3) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_MiddleDistal);
                if (mid1 != null)
                {
                    middleFinger = new FingerJoints { proximal = mid1, intermediate = mid2, distal = mid3 };
                }

                // Ring
                Transform rng1 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Ring1) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_RingProximal);
                Transform rng2 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Ring2) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_RingIntermediate);
                Transform rng3 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Ring3) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_RingDistal);
                if (rng1 != null)
                {
                    ringFinger = new FingerJoints { proximal = rng1, intermediate = rng2, distal = rng3 };
                }

                // Pinky
                Transform pnk1 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Pinky1) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_LittleProximal);
                Transform pnk2 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Pinky2) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_LittleIntermediate);
                Transform pnk3 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Pinky3) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_LittleDistal);
                if (pnk1 != null)
                {
                    pinkyFinger = new FingerJoints { proximal = pnk1, intermediate = pnk2, distal = pnk3 };
                }

                // Thumb
                Transform thm1 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Thumb1) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_ThumbMetacarpal);
                Transform thm2 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Thumb2) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_ThumbProximal);
                Transform thm3 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Thumb3) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_ThumbDistal);
                if (thm1 != null)
                {
                    thumbFinger = new FingerJoints { proximal = thm1, intermediate = thm2, distal = thm3 };
                }

                if (indexFinger != null && middleFinger != null)
                {
                    bonesCached = true;
                    hasValidBones = true;
                }
            }
        }

        private Transform GetBone(Dictionary<OVRSkeleton.BoneId, Transform> map, OVRSkeleton.BoneId id)
        {
            Transform t;
            map.TryGetValue(id, out t);
            return t;
        }

        private void LateUpdate()
        {
            if (!bonesCached || !hasValidBones)
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

            // Flexion curl angle on anatomical local X-axis:
            // Middle, Ring, Pinky wrap handle tightly
            if (middleFinger != null)
            {
                float midProx = Mathf.Lerp(45f, 75f, handTrigger);
                float midInter = Mathf.Lerp(55f, 85f, handTrigger);
                float midDist = Mathf.Lerp(30f, 60f, handTrigger);
                CurlJoint(middleFinger.proximal, midProx);
                CurlJoint(middleFinger.intermediate, midInter);
                CurlJoint(middleFinger.distal, midDist);
            }

            if (ringFinger != null)
            {
                float rngProx = Mathf.Lerp(50f, 78f, handTrigger);
                float rngInter = Mathf.Lerp(60f, 88f, handTrigger);
                float rngDist = Mathf.Lerp(35f, 65f, handTrigger);
                CurlJoint(ringFinger.proximal, rngProx);
                CurlJoint(ringFinger.intermediate, rngInter);
                CurlJoint(ringFinger.distal, rngDist);
            }

            if (pinkyFinger != null)
            {
                float pnkProx = Mathf.Lerp(52f, 80f, handTrigger);
                float pnkInter = Mathf.Lerp(62f, 90f, handTrigger);
                float pnkDist = Mathf.Lerp(38f, 68f, handTrigger);
                CurlJoint(pinkyFinger.proximal, pnkProx);
                CurlJoint(pinkyFinger.intermediate, pnkInter);
                CurlJoint(pinkyFinger.distal, pnkDist);
            }

            // Index finger rests on trigger, curling when pulled
            if (indexFinger != null)
            {
                float idxProx = Mathf.Lerp(22f, 60f, indexTrigger);
                float idxInter = Mathf.Lerp(28f, 72f, indexTrigger);
                float idxDist = Mathf.Lerp(16f, 48f, indexTrigger);
                CurlJoint(indexFinger.proximal, idxProx);
                CurlJoint(indexFinger.intermediate, idxInter);
                CurlJoint(indexFinger.distal, idxDist);
            }

            // Thumb rests on faceplate / thumbstick
            if (thumbFinger != null)
            {
                float thmPitch = 15f + thumbstick.y * 8f;
                float thmYaw = 18f + thumbstick.x * 10f;
                float thmRoll = -22f;
                if (thumbFinger.proximal != null)
                {
                    Quaternion targetRot = thumbFinger.proximal.localRotation * Quaternion.Euler(thmPitch, thmYaw, thmRoll);
                    thumbFinger.proximal.localRotation = Quaternion.Slerp(thumbFinger.proximal.localRotation, targetRot, gripWeight * 0.7f);
                }
                if (thumbFinger.distal != null)
                {
                    CurlJoint(thumbFinger.distal, 20f);
                }
            }
        }

        private void CurlJoint(Transform joint, float angleDegrees)
        {
            if (joint == null) return;
            // Flexion around local X axis (anatomical finger curling)
            Quaternion curlOffset = Quaternion.Euler(angleDegrees, 0f, 0f);
            joint.localRotation = Quaternion.Slerp(joint.localRotation, joint.localRotation * curlOffset, gripWeight);
        }
    }
}
