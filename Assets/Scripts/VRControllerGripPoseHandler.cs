using System;
using System.Collections.Generic;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// VRControllerGripPoseHandler: ควบคุมท่าทางของมือให้อยู่ในท่าถือ Remote (Controller Grip Pose) สมจริง
    /// - เมื่อผู้ใช้ถือ Meta Touch Controller (Link / Standalone): นิ้วมือจะโอบจับรอบด้ามรีโมทอย่างแนบเนียน
    /// - นิ้วชี้ (Index Finger) วางแนบอยู่บนปุ่มไกด้านหน้า (Front Index Trigger) และงอตามแรงกดของปุ่มจริง
    /// - นิ้วกลาง นิ้วนาง นิ้วก้อย (Middle, Ring, Pinky) กำรอบด้ามจับ และบีบกระชับตามปุ่ม Hand Grip Trigger
    /// - นิ้วโป้ง (Thumb) วางพาดอย่างเป็นธรรมชาติบนส่วนบนใกล้ Thumbstick/Buttons
    /// - เมื่อปล่อยรีโมทไปใช้มือเปล่า: ระบบจะคลี่มือกลับสู่ Optical Hand Tracking ตามปกติอย่างนุ่มนวล
    /// </summary>
    public class VRControllerGripPoseHandler : MonoBehaviour
    {
        [Header("Hand & Controller Configuration")]
        public bool isRightHand = true;
        public OVRInput.Controller controller = OVRInput.Controller.RTouch;

        [Header("Pose Blending")]
        [Range(0f, 1f)]
        public float gripWeight = 0f;
        public float blendSpeed = 12f;

        [Header("References")]
        public OVRSkeleton ovrSkeleton;
        public OVRHand ovrHand;

        // Cached Finger Bone Transforms
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
        private int retryFindFrames = 0;

        private void Awake()
        {
            if (ovrSkeleton == null) ovrSkeleton = GetComponent<OVRSkeleton>();
            if (ovrHand == null) ovrHand = GetComponent<OVRHand>();

            isRightHand = name.ToLower().Contains("right");
            if (ovrSkeleton != null)
            {
                var st = ovrSkeleton.GetSkeletonType();
                isRightHand = (st == OVRSkeleton.SkeletonType.XRHandRight || st == OVRSkeleton.SkeletonType.HandRight) || isRightHand;
            }
            controller = isRightHand ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
        }

        private void Start()
        {
            TryCacheBones();
        }

        private void Update()
        {
            if (!bonesCached && retryFindFrames < 180)
            {
                retryFindFrames++;
                TryCacheBones();
            }
        }

        private void TryCacheBones()
        {
            var transforms = GetComponentsInChildren<Transform>(true);
            if (transforms == null || transforms.Length < 10) return;

            var boneDict = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in transforms)
            {
                boneDict[t.name] = t;
            }

            // Index Finger
            Transform idxP = FindBone(boneDict, "index", "proximal", "index1");
            Transform idxI = FindBone(boneDict, "index", "intermediate", "index2");
            Transform idxD = FindBone(boneDict, "index", "distal", "index3");
            if (idxP != null)
            {
                indexFinger = new FingerJoints
                {
                    proximal = idxP,
                    intermediate = idxI,
                    distal = idxD,
                    defaultProximalRot = idxP.localRotation,
                    defaultIntermediateRot = idxI != null ? idxI.localRotation : Quaternion.identity,
                    defaultDistalRot = idxD != null ? idxD.localRotation : Quaternion.identity
                };
            }

            // Middle Finger
            Transform midP = FindBone(boneDict, "middle", "proximal", "middle1");
            Transform midI = FindBone(boneDict, "middle", "intermediate", "middle2");
            Transform midD = FindBone(boneDict, "middle", "distal", "middle3");
            if (midP != null)
            {
                middleFinger = new FingerJoints
                {
                    proximal = midP,
                    intermediate = midI,
                    distal = midD,
                    defaultProximalRot = midP.localRotation,
                    defaultIntermediateRot = midI != null ? midI.localRotation : Quaternion.identity,
                    defaultDistalRot = midD != null ? midD.localRotation : Quaternion.identity
                };
            }

            // Ring Finger
            Transform rngP = FindBone(boneDict, "ring", "proximal", "ring1");
            Transform rngI = FindBone(boneDict, "ring", "intermediate", "ring2");
            Transform rngD = FindBone(boneDict, "ring", "distal", "ring3");
            if (rngP != null)
            {
                ringFinger = new FingerJoints
                {
                    proximal = rngP,
                    intermediate = rngI,
                    distal = rngD,
                    defaultProximalRot = rngP.localRotation,
                    defaultIntermediateRot = rngI != null ? rngI.localRotation : Quaternion.identity,
                    defaultDistalRot = rngD != null ? rngD.localRotation : Quaternion.identity
                };
            }

            // Pinky Finger
            Transform pnkP = FindBone(boneDict, "little", "proximal", "pinky1") ?? FindBone(boneDict, "pinky", "proximal", "pinky1");
            Transform pnkI = FindBone(boneDict, "little", "intermediate", "pinky2") ?? FindBone(boneDict, "pinky", "intermediate", "pinky2");
            Transform pnkD = FindBone(boneDict, "little", "distal", "pinky3") ?? FindBone(boneDict, "pinky", "distal", "pinky3");
            if (pnkP != null)
            {
                pinkyFinger = new FingerJoints
                {
                    proximal = pnkP,
                    intermediate = pnkI,
                    distal = pnkD,
                    defaultProximalRot = pnkP.localRotation,
                    defaultIntermediateRot = pnkI != null ? pnkI.localRotation : Quaternion.identity,
                    defaultDistalRot = pnkD != null ? pnkD.localRotation : Quaternion.identity
                };
            }

            // Thumb Finger
            Transform thmP = FindBone(boneDict, "thumb", "proximal", "thumb1") ?? FindBone(boneDict, "thumb", "metacarpal", "thumb0");
            Transform thmD = FindBone(boneDict, "thumb", "distal", "thumb2");
            if (thmP != null)
            {
                thumbFinger = new FingerJoints
                {
                    proximal = thmP,
                    intermediate = null,
                    distal = thmD,
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

        /// <summary>
        /// ดำเนินการใน LateUpdate เพื่อให้เกิดผลหลังจาก OVRSkeleton ทำการคำนวณเสร็จสิ้น
        /// </summary>
        private void LateUpdate()
        {
            if (!bonesCached) return;

            // ตรวจสอบว่ากำลังถือ Remote อยู่หรือไม่
            bool isConnected = (OVRInput.GetConnectedControllers() & controller) != 0;
            bool isTracked = OVRInput.GetControllerPositionTracked(controller);
            bool hasActiveTouch = OVRInput.Get(OVRInput.Button.Any, controller)
                               || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.005f
                               || OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller) > 0.005f
                               || OVRInput.Get(OVRInput.Touch.Any, controller);

            // ผู้ใช้ถือ Remote เมื่อมีสัญญาณเชื่อมต่อ/Tracked
            bool isHoldingRemote = isConnected && (isTracked || hasActiveTouch || Application.isEditor);

            // ปรับน้ำหนักการจับอย่างนุ่มนวล
            gripWeight = Mathf.MoveTowards(gripWeight, isHoldingRemote ? 1.0f : 0.0f, Time.deltaTime * blendSpeed);
            if (gripWeight <= 0.001f) return;

            // อ่านค่าการกดปุ่มจาก Remote แบบ Real-time
            float indexTrigger = Mathf.Clamp01(OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller));
            float handTrigger = Mathf.Clamp01(OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller));
            Vector2 thumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, controller);

            // สำหรับ Editor Testing fallback
            if (Application.isEditor && !isConnected)
            {
                if (Input.GetMouseButton(0)) indexTrigger = 0.9f;
                if (Input.GetMouseButton(1)) handTrigger = 0.9f;
            }

            // กำหนดทิศทางการงอตาม Handedness (OpenXR Hand Standard)
            // สำหรับมือขวา: การงอนิ้ว (Flexion) เข้าหาฝ่ามือหมุนรอบแกน -Z
            float sign = isRightHand ? -1f : 1f;

            // 1. นิ้วกลาง (Middle Finger) - กำรอบด้ามจับรีโมท
            if (middleFinger != null)
            {
                float midProx = sign * Mathf.Lerp(48f, 74f, handTrigger);
                float midInter = sign * Mathf.Lerp(66f, 86f, handTrigger);
                float midDist = sign * Mathf.Lerp(36f, 52f, handTrigger);
                ApplyFingerJoint(middleFinger.proximal, middleFinger.defaultProximalRot, Quaternion.Euler(0f, 0f, midProx));
                ApplyFingerJoint(middleFinger.intermediate, middleFinger.defaultIntermediateRot, Quaternion.Euler(0f, 0f, midInter));
                ApplyFingerJoint(middleFinger.distal, middleFinger.defaultDistalRot, Quaternion.Euler(0f, 0f, midDist));
            }

            // 2. นิ้วนาง (Ring Finger) - กำรอบด้ามจับรีโมท
            if (ringFinger != null)
            {
                float rngProx = sign * Mathf.Lerp(52f, 78f, handTrigger);
                float rngInter = sign * Mathf.Lerp(70f, 90f, handTrigger);
                float rngDist = sign * Mathf.Lerp(40f, 56f, handTrigger);
                ApplyFingerJoint(ringFinger.proximal, ringFinger.defaultProximalRot, Quaternion.Euler(0f, 0f, rngProx));
                ApplyFingerJoint(ringFinger.intermediate, ringFinger.defaultIntermediateRot, Quaternion.Euler(0f, 0f, rngInter));
                ApplyFingerJoint(ringFinger.distal, ringFinger.defaultDistalRot, Quaternion.Euler(0f, 0f, rngDist));
            }

            // 3. นิ้วก้อย (Pinky Finger) - กำรอบด้ามจับรีโมท
            if (pinkyFinger != null)
            {
                float pnkProx = sign * Mathf.Lerp(56f, 82f, handTrigger);
                float pnkInter = sign * Mathf.Lerp(74f, 92f, handTrigger);
                float pnkDist = sign * Mathf.Lerp(44f, 60f, handTrigger);
                ApplyFingerJoint(pinkyFinger.proximal, pinkyFinger.defaultProximalRot, Quaternion.Euler(0f, 0f, pnkProx));
                ApplyFingerJoint(pinkyFinger.intermediate, pinkyFinger.defaultIntermediateRot, Quaternion.Euler(0f, 0f, pnkInter));
                ApplyFingerJoint(pinkyFinger.distal, pinkyFinger.defaultDistalRot, Quaternion.Euler(0f, 0f, pnkDist));
            }

            // 4. นิ้วชี้ (Index Finger) - วางพาดบนปุ่มไกด้านหน้า (Front Index Trigger) และงอตามแรงเหนี่ยวไก
            if (indexFinger != null)
            {
                // ท่าพัก: วางโค้งเบาๆ บนปุ่มไก (~26°) เมื่อเหนี่ยวไกจะงอแนบแน่นสนิท (~62°)
                float idxProx = sign * Mathf.Lerp(26f, 58f, indexTrigger);
                float idxInter = sign * Mathf.Lerp(36f, 76f, indexTrigger);
                float idxDist = sign * Mathf.Lerp(20f, 44f, indexTrigger);
                ApplyFingerJoint(indexFinger.proximal, indexFinger.defaultProximalRot, Quaternion.Euler(0f, 0f, idxProx));
                ApplyFingerJoint(indexFinger.intermediate, indexFinger.defaultIntermediateRot, Quaternion.Euler(0f, 0f, idxInter));
                ApplyFingerJoint(indexFinger.distal, indexFinger.defaultDistalRot, Quaternion.Euler(0f, 0f, idxDist));
            }

            // 5. นิ้วโป้ง (Thumb) - วางพาดบนหน้าปัดรีโมท และขยับตาม Thumbstick เล็กน้อย
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
