using System;
using UnityEngine;

namespace UR3eVR
{
    public enum VRTrackingMode
    {
        TargetPose,
        Velocity
    }

    public class VRHandMover : MonoBehaviour
    {
        [Header("Tracking Mode")]
        [Tooltip("TargetPose: Closed-loop | Velocity: Open-loop")]
        public VRTrackingMode trackingMode = VRTrackingMode.TargetPose;

        [Header("Hand Tracking Reference")]
        public Transform handTransform;
        public OVRHand ovrHand;
        public Transform headTransform;

        [Header("Orientation Settings")]
        public bool useHeadsetOrientation = true;

        [Header("One Euro Filter Settings")]
        public bool enableOneEuroFilter = true;
        [Range(0.1f, 5.0f)] public float posMinCutoff = 2.8f;
        [Range(0.0f, 50.0f)] public float posBeta = 15.0f;
        [Range(0.1f, 5.0f)] public float rotMinCutoff = 2.5f;
        [Range(0.0f, 20.0f)] public float rotBeta = 10.0f;
        public float dCutoff = 3.5f;

        [Header("Starter Point (UR3e Home Visualizer)")]
        public bool showStarterPoint = true;
        public UR3eStarterPoint starterPoint;
        public LeftHandHomeTrigger leftHandHomeTrigger;

        [Header("Target Pose and Anchor Settings")]
        [Range(0.2f, 3.0f)] public float posSensitivity = 1.0f;
        public Vector3 homeRobotPos = new Vector3(0.3053f, -0.1313f, 0.3108f);
        public Vector3 homeRobotRotAxisAngle = new Vector3(0.0f, 0.0f, -1.5708f);
        public KeyCode recenterKey = KeyCode.R;

        [Header("Cartesian Target Bounds (m)")]
        public Vector3 minTargetPos = new Vector3(0.08f, -0.45f, -0.05f);
        public Vector3 maxTargetPos = new Vector3(0.50f, 0.25f, 0.60f);

        [Header("Linear Axis Inversions")]
        public bool invertForwardBackward = false;
        public bool invertLeftRight = false;
        public bool invertUpDown = false;

        [Header("Rotation Settings")]
        public bool enableRotation = true;
        [Range(0.5f, 4.0f)] public float rotSensitivity = 1.0f;

        [Header("Rotation Axis Inversions (Custom Overrides)")]
        public bool invertRoll = false;
        public bool invertPitch = false;
        public bool invertYaw = false;

        [Header("Velocity Mode Settings")]
        [Range(0.01f, 0.6f)] public float maxSpeed = 0.30f;
        [Range(0.2f, 3.0f)] public float maxRotSpeed = 1.60f;
        [Range(0f, 0.95f)] public float smoothing = 0.4f;
        [Range(0f, 0.95f)] public float rotSmoothing = 0.2f;

        [Header("Live Output (Read Only)")]
        public bool isHandTracked = false;
        public bool isAnchorSet = false;
        public Vector3 anchorHandWorldPos;
        public Quaternion anchorHeadYaw = Quaternion.identity;
        public Vector3 anchorHandPos;
        public Quaternion anchorHandRot = Quaternion.identity;
        public Vector3 anchorRobotPos;
        public Vector3 anchorRobotRot;
        public Vector3 currentTargetPos;
        public Vector3 currentTargetRotAxisAngle;
        public Vector3 rawVelocity;
        public Vector3 mappedVelocity;
        public Vector3 mappedAngularVelocity;

        [Header("Robot Pose Feedback (Read Only)")]
        public Vector3 actualRobotPos = Vector3.zero;
        public Vector3 actualRobotRot = Vector3.zero;
        public bool hasReceivedRobotPose = false;

        private Vector3 prevHandPos;
        private Quaternion prevHandRot;
        private bool hasPrevState = false;
        private Vector3 smoothedVel = Vector3.zero;
        private Vector3 smoothedRotVel = Vector3.zero;
        private GripperBridge gripperBridge;
        private bool hasSyncedInitialRobotPose = false;
        private OneEuroFilterVector3 posFilter = new OneEuroFilterVector3();
        private OneEuroFilterQuaternion rotFilter = new OneEuroFilterQuaternion();

        private void Start()
        {
            gripperBridge = FindAnyObjectByType<GripperBridge>();
            if (gripperBridge == null)
                Debug.LogWarning("[VRHandMover] GripperBridge not found!");

            if (handTransform == null) handTransform = transform;

            if (ovrHand == null && handTransform != null)
            {
                ovrHand = handTransform.GetComponent<OVRHand>();
                if (ovrHand == null) ovrHand = handTransform.GetComponentInParent<OVRHand>();
            }

            if (headTransform == null)
            {
                if (Camera.main != null) headTransform = Camera.main.transform;
                else
                {
                    GameObject centerEye = GameObject.Find("CenterEyeAnchor");
                    if (centerEye != null) headTransform = centerEye.transform;
                }
            }

            anchorRobotPos = homeRobotPos;
            anchorRobotRot = homeRobotRotAxisAngle;
            currentTargetPos = homeRobotPos;
            currentTargetRotAxisAngle = homeRobotRotAxisAngle;

            if (handTransform != null)
            {
                anchorHandWorldPos = handTransform.position;
            }

            if (showStarterPoint && starterPoint == null)
            {
                starterPoint = FindAnyObjectByType<UR3eStarterPoint>();
                if (starterPoint == null)
                {
                    GameObject spObj = new GameObject("UR3e_Home_StarterPoint");
                    starterPoint = spObj.AddComponent<UR3eStarterPoint>();
                    starterPoint.vrHandMover = this;
                    starterPoint.handTransform = handTransform;
                    starterPoint.headTransform = headTransform;
                }
            }

            if (leftHandHomeTrigger == null)
            {
                leftHandHomeTrigger = FindAnyObjectByType<LeftHandHomeTrigger>();
                if (leftHandHomeTrigger == null)
                {
                    GameObject lhObj = new GameObject("LeftHand_HomeTrigger");
                    leftHandHomeTrigger = lhObj.AddComponent<LeftHandHomeTrigger>();
                    leftHandHomeTrigger.vrHandMover = this;
                    leftHandHomeTrigger.gripperBridge = gripperBridge;
                    leftHandHomeTrigger.headTransform = headTransform;
                }
            }
        }

        /// <summary>
        /// Recenter Anchor at current hand position (palm center of right hand).
        /// Starting pose reference: right hand tilted like holding a water bottle.
        /// Center Point in Unity will instantly snap to the hand's current position.
        /// </summary>
        public void RecenterAnchor()
        {
            if (handTransform == null) return;

            // 1. บันทึกตำแหน่ง World Position ของมือขวาจริง (Center Point ของมือขวา)
            anchorHandWorldPos = handTransform.position;

            // 2. แกนทิศทางของศีรษะ (Headset Horizontal Yaw)
            Quaternion headYaw = Quaternion.identity;
            if (useHeadsetOrientation && headTransform != null)
            {
                headYaw = Quaternion.Euler(0f, headTransform.eulerAngles.y, 0f);
            }
            anchorHeadYaw = headYaw;

            // 3. บันทึกการหมุนของมือเทียบกับทิศทางศีรษะ
            anchorHandRot = Quaternion.Inverse(headYaw) * handTransform.rotation;
            anchorHandPos = Quaternion.Inverse(headYaw) * (handTransform.position - (headTransform != null ? headTransform.position : Vector3.zero));

            // 4. เมื่อ Recenter หรือกด Home ให้รีเซ็ตจุดอ้างอิงของหุ่นยนต์กลับสู่ Safe Home เสมอ
            anchorRobotPos = homeRobotPos;
            anchorRobotRot = homeRobotRotAxisAngle;

            currentTargetPos = anchorRobotPos;
            currentTargetRotAxisAngle = anchorRobotRot;
            isAnchorSet = true;
            posFilter.Reset();
            rotFilter.Reset();
            hasPrevState = false;

            // 5. สั่งให้ StarterPoint อัปเดตพิกัดมาร์กเกอร์กลับมาที่มือขวาทันที
            if (starterPoint != null)
            {
                starterPoint.UpdateAnchor(anchorHandWorldPos, headYaw, anchorRobotPos);
            }

            Debug.Log(string.Format("[VRHandMover] Anchor Recentered at hand palm! World: ({0:F3}, {1:F3}, {2:F3}), Robot Home: ({3:F3}, {4:F3}, {5:F3})",
                anchorHandWorldPos.x, anchorHandWorldPos.y, anchorHandWorldPos.z,
                anchorRobotPos.x, anchorRobotPos.y, anchorRobotPos.z));
        }

        private void Update()
        {
            if (ovrHand != null) isHandTracked = ovrHand.IsTracked;
            else if (handTransform != null) isHandTracked = true;
            else { isHandTracked = false; ResetOutputs(); return; }

            if (!isHandTracked || handTransform == null) { ResetOutputs(); return; }

#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame) RecenterAnchor();
            if (kb != null && kb.fKey.wasPressedThisFrame) ToggleFilter();
#endif
            try { if (Input.GetKeyDown(recenterKey)) RecenterAnchor(); } catch { }
            try { if (Input.GetKeyDown(KeyCode.F)) ToggleFilter(); } catch { }

            // ซิงค์ตำแหน่งจริงของหุ่นยนต์ตอนเริ่มต้นเชื่อมต่อครั้งแรก
            if (!hasSyncedInitialRobotPose && gripperBridge != null && gripperBridge.hasReceivedRobotPose)
            {
                actualRobotPos = gripperBridge.actualRobotPos;
                actualRobotRot = gripperBridge.actualRobotRot;
                hasReceivedRobotPose = true;
                if (!isAnchorSet)
                {
                    RecenterAnchor();
                    hasSyncedInitialRobotPose = true;
                    Debug.Log("[VRHandMover] Initial Live Robot Pose Synced!");
                }
            }

            // คำนวณ Headset Yaw ในระนาบแนวนอน
            Quaternion currentHeadYaw = Quaternion.identity;
            if (useHeadsetOrientation && headTransform != null)
            {
                currentHeadYaw = Quaternion.Euler(0f, headTransform.eulerAngles.y, 0f);
            }

            // กรอง One Euro Filter
            if (enableOneEuroFilter)
            {
                posFilter.UpdateParams(posMinCutoff, posBeta, dCutoff);
                rotFilter.UpdateParams(rotMinCutoff, rotBeta, dCutoff);
            }
            Vector3 filteredWorldPos = enableOneEuroFilter ? posFilter.Filter(handTransform.position) : handTransform.position;
            Quaternion filteredWorldRot = enableOneEuroFilter ? rotFilter.Filter(handTransform.rotation) : handTransform.rotation;

            // ถ้ายังไม่เคยเซ็ต Anchor ให้ใช้ตำแหน่งปัจจุบันเป็นจุดเริ่มต้น
            if (!isAnchorSet)
            {
                anchorHandWorldPos = filteredWorldPos;
                anchorHeadYaw = currentHeadYaw;
                anchorHandRot = Quaternion.Inverse(currentHeadYaw) * filteredWorldRot;
                anchorRobotPos = homeRobotPos;
                anchorRobotRot = homeRobotRotAxisAngle;
                isAnchorSet = true;
                if (starterPoint != null)
                {
                    starterPoint.UpdateAnchor(anchorHandWorldPos, currentHeadYaw, anchorRobotPos);
                }
            }

            float dt = Time.deltaTime;
            if (dt <= 1e-6f) dt = 0.01f;

            // ========================================================
            // 1. TARGET POSE TRACKING (Closed-Loop แม่นยำระดับมิลลิเมตร)
            // ========================================================
            if (trackingMode == VRTrackingMode.TargetPose)
            {
                // เวกเตอร์การเคลื่อนที่จากจุด Anchor World Space -> แปลงเข้าสู่แกนหน้าแว่น
                // Z = พุ่งออกจากแว่น (Forward), X = ไปทางขวาของแว่น (Right), Y = ขึ้นบน (Up)
                Vector3 worldDelta = filteredWorldPos - anchorHandWorldPos;
                Vector3 handDelta = Quaternion.Inverse(currentHeadYaw) * worldDelta;

                // Deadband ขั้นต่ำสำหรับตัด Hand Tremor ตอนมืออยู่นิ่ง
                if (Mathf.Abs(handDelta.x) < 0.0008f) handDelta.x = 0f;
                if (Mathf.Abs(handDelta.y) < 0.0008f) handDelta.y = 0f;
                if (Mathf.Abs(handDelta.z) < 0.0008f) handDelta.z = 0f;

                // Position Mapping (VR Head-Relative -> UR3e Base Frame)
                // handDelta.z (พุ่งออกจากแว่น) -> Robot +X (Forward)
                // -handDelta.x (ไปทางซ้ายของแว่น) -> Robot +Y (Left)
                // handDelta.y (ขึ้นบน) -> Robot +Z (Up)
                float dX = handDelta.z * posSensitivity * (invertForwardBackward ? -1f : 1f);
                float dY = -handDelta.x * posSensitivity * (invertLeftRight ? -1f : 1f);
                float dZ = handDelta.y * posSensitivity * (invertUpDown ? -1f : 1f);

                Vector3 targetP = anchorRobotPos + new Vector3(dX, dY, dZ);
                targetP.x = Mathf.Clamp(targetP.x, minTargetPos.x, maxTargetPos.x);
                targetP.y = Mathf.Clamp(targetP.y, minTargetPos.y, maxTargetPos.y);
                targetP.z = Mathf.Clamp(targetP.z, minTargetPos.z, maxTargetPos.z);
                currentTargetPos = targetP;

                // Rotation Mapping (VR Headset Frame -> UR3e Base Frame)
                Vector3 targetRotVec = anchorRobotRot;
                if (enableRotation)
                {
                    Quaternion currentRotRel = Quaternion.Inverse(currentHeadYaw) * filteredWorldRot;
                    Quaternion deltaRot = currentRotRel * Quaternion.Inverse(anchorHandRot);
                    deltaRot.ToAngleAxis(out float angleDeg, out Vector3 axis);
                    if (angleDeg > 180f) angleDeg -= 360f;

                    if (Mathf.Abs(angleDeg) > 0.25f && !float.IsNaN(axis.x) && !float.IsInfinity(axis.x))
                    {
                        Vector3 vrRotVec = axis.normalized * (angleDeg * Mathf.Deg2Rad * rotSensitivity);

                        // แก้ไขการหมุนข้อมือกลับด้านทุกแกน (Chirality Correction: Left-Handed Unity -> Right-Handed UR3e)
                        // การแปลงเวกเตอร์การหมุนจาก VR Headset Frame -> UR3e Robot Base Frame:
                        //   VR +Z (พุ่งออกจากแว่น) -> Robot +X (Roll): กลับเครื่องหมาย (-)
                        //   VR -X (ไปทางซ้าย)     -> Robot +Y (Pitch): กลับเครื่องหมาย จาก -(-X) เป็น +X
                        //   VR +Y (ชี้ขึ้น)        -> Robot +Z (Yaw): กลับเครื่องหมาย (-)
                        float d_wx = -vrRotVec.z * (invertRoll ? -1f : 1f);
                        float d_wy = vrRotVec.x * (invertPitch ? -1f : 1f);
                        float d_wz = -vrRotVec.y * (invertYaw ? -1f : 1f);

                        Vector3 d_rot = new Vector3(d_wx, d_wy, d_wz);
                        float d_mag = d_rot.magnitude;
                        if (d_mag > 1e-4f)
                        {
                            Vector3 d_axis = d_rot / d_mag;
                            Quaternion q_delta = Quaternion.AngleAxis(d_mag * Mathf.Rad2Deg, d_axis);

                            float anchorAngRad = anchorRobotRot.magnitude;
                            Vector3 anchorAxis = anchorAngRad > 1e-5f ? (anchorRobotRot / anchorAngRad) : Vector3.up;
                            Quaternion q_anchor = Quaternion.AngleAxis(anchorAngRad * Mathf.Rad2Deg, anchorAxis);

                            Quaternion q_target = q_delta * q_anchor;
                            q_target.ToAngleAxis(out float tgtAngleDeg, out Vector3 tgtAxis);
                            if (tgtAngleDeg > 180f) tgtAngleDeg -= 360f;
                            targetRotVec = tgtAxis.normalized * (tgtAngleDeg * Mathf.Deg2Rad);
                        }
                    }
                }
                currentTargetRotAxisAngle = targetRotVec;

                if (gripperBridge != null)
                {
                    gripperBridge.SetTargetPose(
                        currentTargetPos.x, currentTargetPos.y, currentTargetPos.z,
                        currentTargetRotAxisAngle.x, currentTargetRotAxisAngle.y, currentTargetRotAxisAngle.z
                    );
                }
            }
            // ========================================================
            // 2. VELOCITY TRACKING (Open-Loop backup)
            // ========================================================
            else
            {
                Vector3 deltaPos = filteredWorldPos - prevHandPos;
                Vector3 headRelDelta = Quaternion.Inverse(currentHeadYaw) * deltaPos;
                Vector3 linVel = (headRelDelta / dt) * posSensitivity;
                smoothedVel = Vector3.Lerp(linVel, smoothedVel, smoothing);

                float robotVx = smoothedVel.z * (invertForwardBackward ? -1f : 1f);
                float robotVy = -smoothedVel.x * (invertLeftRight ? -1f : 1f);
                float robotVz = smoothedVel.y * (invertUpDown ? -1f : 1f);

                Vector3 robotLinVel = new Vector3(robotVx, robotVy, robotVz);
                if (robotLinVel.magnitude > maxSpeed) robotLinVel = robotLinVel.normalized * maxSpeed;
                mappedVelocity = robotLinVel;

                Vector3 robotRotVel = Vector3.zero;
                if (enableRotation)
                {
                    Quaternion currentRotRel = Quaternion.Inverse(currentHeadYaw) * filteredWorldRot;
                    Quaternion deltaRot = currentRotRel * Quaternion.Inverse(prevHandRot);
                    deltaRot.ToAngleAxis(out float angleDeg, out Vector3 axis);
                    if (angleDeg > 180f) angleDeg -= 360f;

                    Vector3 angVel = Vector3.zero;
                    if (Mathf.Abs(angleDeg) > 0.02f && !float.IsNaN(axis.x) && !float.IsInfinity(axis.x))
                        angVel = axis.normalized * (angleDeg * Mathf.Deg2Rad / dt);

                    smoothedRotVel = Vector3.Lerp(angVel * rotSensitivity, smoothedRotVel, rotSmoothing);

                    // แก้การกลับด้านของข้อมือใน Velocity Mode
                    float robotWx = -smoothedRotVel.z * (invertRoll ? -1f : 1f);
                    float robotWy = smoothedRotVel.x * (invertPitch ? -1f : 1f);
                    float robotWz = -smoothedRotVel.y * (invertYaw ? -1f : 1f);

                    robotRotVel = new Vector3(robotWx, robotWy, robotWz);
                    if (robotRotVel.magnitude > maxRotSpeed) robotRotVel = robotRotVel.normalized * maxRotSpeed;
                }
                mappedAngularVelocity = robotRotVel;

                if (gripperBridge != null)
                {
                    gripperBridge.SetTargetVelocity(
                        robotLinVel.x, robotLinVel.y, robotLinVel.z,
                        robotRotVel.x, robotRotVel.y, robotRotVel.z
                    );
                }
            }

            prevHandPos = filteredWorldPos;
            prevHandRot = Quaternion.Inverse(currentHeadYaw) * filteredWorldRot;
        }

        /// <summary>
        /// สลับเปิด/ปิด One Euro Filter แบบ Real-time (กดปุ่ม F)
        /// </summary>
        public void ToggleFilter()
        {
            enableOneEuroFilter = !enableOneEuroFilter;
            posFilter?.Reset();
            rotFilter?.Reset();
            Debug.Log(string.Format("[VRHandMover] One Euro Filter is now: {0} (Press 'F' to toggle)",
                enableOneEuroFilter ? "ENABLED" : "DISABLED"));
        }

        private void ResetOutputs()
        {
            hasPrevState = false;
            posFilter?.Reset();
            rotFilter?.Reset();
            rawVelocity = Vector3.zero;
            mappedVelocity = Vector3.zero;
            smoothedVel = Vector3.zero;
            mappedAngularVelocity = Vector3.zero;
            smoothedRotVel = Vector3.zero;
        }
    }
}
