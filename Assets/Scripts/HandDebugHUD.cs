using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// HandDebugHUD: แสดง GUI Debug Overlay บนจอ VR
    /// แสดงสถานะ Hand Tracking, Pinch %, Force Gauge, bHaptics Glove, Webcam, และ 6-DOF Robot Arm Movement
    /// </summary>
    public class HandDebugHUD : MonoBehaviour
    {
        [Header("References")]
        public PinchDetector pinchDetector;
        public GripperBridge gripperBridge;
        public BhapticsGloveFeedback bhapticsFeedback;
        public VRHandMover vrHandMover;
        public UR3eStarterPoint starterPoint;
        public SafetyBoundaryVisualizer safetyVisualizer;
        public script webcamScript;

        private GUIStyle headerStyle;
        private GUIStyle labelStyle;
        private GUIStyle subLabelStyle;
        private GUIStyle warningStyle;
        private bool stylesInitialized = false;

        private void Start()
        {
            if (pinchDetector == null) pinchDetector = FindAnyObjectByType<PinchDetector>();
            if (gripperBridge == null) gripperBridge = FindAnyObjectByType<GripperBridge>();
            if (bhapticsFeedback == null) bhapticsFeedback = FindAnyObjectByType<BhapticsGloveFeedback>();
            if (vrHandMover == null) vrHandMover = FindAnyObjectByType<VRHandMover>();
            if (starterPoint == null) starterPoint = FindAnyObjectByType<UR3eStarterPoint>();
            if (safetyVisualizer == null) safetyVisualizer = FindAnyObjectByType<SafetyBoundaryVisualizer>();
            if (webcamScript == null) webcamScript = FindAnyObjectByType<script>();
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;
            stylesInitialized = true;

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.cyan }
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            subLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }
            };

            warningStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.3f, 0.3f) }
            };
        }

        private void OnGUI()
        {
            InitStyles();

            GUILayout.BeginArea(new Rect(20, 20, 460, 740), GUI.skin.box);
            GUILayout.Label("VR-HaptiLab | Hazardous Chemical Mixing", headerStyle);
            GUILayout.Space(2);

            // Left Remote Pause Status Banner
            if (vrHandMover != null && vrHandMover.isTeleoperationPaused)
            {
                GUI.color = new Color(1f, 0.45f, 0.15f);
                GUILayout.Box("⏸️ ROBOT TELEOPERATION PAUSED\n(Press [Y] on Left Controller to resume motion)", warningStyle);
                GUI.color = Color.white;
                GUILayout.Space(3);
            }

            // Joint Rotation Limit Warning / Deceleration Banner (100% ENGLISH)
            if (gripperBridge != null && gripperBridge.safetyRotLimitRatio > 0.05f)
            {
                float degLeft = gripperBridge.safetyRotLimitDegLeft;
                string jointName = gripperBridge.safetyRotLimitJointName;
                bool isDanger = gripperBridge.safetyRotLimitRatio >= 0.70f;
                int remainingSpeedPct = Mathf.RoundToInt(Mathf.Clamp01(1f - gripperBridge.safetyRotLimitRatio) * 100f);

                GUI.color = isDanger ? new Color(1f, 0.25f, 0.25f) : new Color(1f, 0.75f, 0.15f);
                string title = isDanger ? "🛑 ROTATION LIMIT REACHED" : "⚠️ ROTATION LIMIT WARNING";
                string warningMsg = string.Format(
                    "{0}\nJoint: {1} nearing full rotation limit ({2:F1}° remaining)\nDecelerating speed to {3}% to protect joints & cables.\n👉 Rotate wrist in the opposite direction to continue.",
                    title, jointName, degLeft, remainingSpeedPct
                );
                GUILayout.Box(warningMsg, warningStyle);
                GUI.color = Color.white;
                GUILayout.Space(3);
            }

            // Home engagement status banner
            if (vrHandMover != null && !vrHandMover.isTrackingEngaged)
            {
                float dist = (starterPoint != null) ? starterPoint.distanceToHome * 100f : 0f;
                GUI.color = new Color(1f, 0.85f, 0.2f);
                GUILayout.Box(string.Format("👉 Please place hand at Home point to engage robot control\n   (Touch the cyan/green circle | {0:F1} cm away)", dist), warningStyle);
                GUI.color = Color.white;
                GUILayout.Space(4);
            }
            else
            {
                GUI.color = new Color(0.2f, 1f, 0.5f);
                GUILayout.Label("✅ ROBOT ENGAGED (Tracking hand in real-time)", labelStyle);
                GUI.color = Color.white;
                GUILayout.Space(2);
            }

            // 1. HMD Status
            bool hmdPresent = OVRManager.isHmdPresent;
            bool userPresent = OVRManager.instance != null && OVRManager.instance.isUserPresent;

            if (hmdPresent)
            {
                GUI.color = userPresent ? Color.green : Color.yellow;
                GUILayout.Label(userPresent ? "[HMD] Active & Tracked" : "[HMD] Connected (Standby / Proximity Off)", labelStyle);
            }
            else
            {
                GUI.color = new Color(1f, 0.4f, 0.4f);
                GUILayout.Label("[HMD] Not Detected / Quest Link Sleep", warningStyle);
            }
            GUI.color = Color.white;

            // 2. Hand Tracking Status & Active Source
            bool tracked = pinchDetector != null && pinchDetector.isTracked;
            GUI.color = tracked ? Color.green : Color.yellow;
            GUILayout.Label(tracked ? "[Hand Tracking] ACTIVE" : "[Hand Tracking] SEARCHING FOR HANDS...", labelStyle);
            GUI.color = Color.white;

            if (pinchDetector != null)
            {
                GUILayout.Label("Source: " + pinchDetector.trackingSource, subLabelStyle);
                GUILayout.Label(string.Format("Finger Distance: {0:F1} cm | Pinch: {1:F0}%", pinchDetector.rawDistanceCm, pinchDetector.normalizedPinch * 100f), labelStyle);
                bool isCalibrated = (pinchDetector.gripperPos <= 35) || (gripperBridge != null && gripperBridge.isCalibratedAtOpen);
                string calibBadge = isCalibrated ? " [Auto-Calibrated]" : "";
                GUI.color = isCalibrated ? Color.green : Color.white;
                GUILayout.Label(string.Format("Gripper Target POS: {0} / 255{1}", pinchDetector.gripperPos, calibBadge), labelStyle);
                GUI.color = Color.white;

                // Visual pinch bar
                Rect barRect = GUILayoutUtility.GetRect(400, 16);
                GUI.Box(barRect, "");
                float fillWidth = barRect.width * pinchDetector.normalizedPinch;
                GUI.color = Color.Lerp(Color.cyan, Color.red, pinchDetector.normalizedPinch);
                GUI.DrawTexture(new Rect(barRect.x, barRect.y, fillWidth, barRect.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            GUILayout.Space(4);

            // 3. Robotiq Dynamic Force Feedback (0 N - 235 N)
            if (gripperBridge != null)
            {
                float force = gripperBridge.actualGripForce;
                string forceDesc;
                Color forceColor;

                if (force < 5.0f)
                {
                    forceDesc = "0.0 N (In Air)";
                    forceColor = Color.gray;
                }
                else if (force < 45.0f)
                {
                    forceDesc = string.Format("{0:F1} N (Gentle Touch - Light Grip)", force);
                    forceColor = Color.green;
                }
                else if (force < 95.0f)
                {
                    forceDesc = string.Format("{0:F1} N (Medium Grip)", force);
                    forceColor = Color.yellow;
                }
                else if (force < 165.0f)
                {
                    forceDesc = string.Format("{0:F1} N (Firm Grip - Secure Hold)", force);
                    forceColor = new Color(1f, 0.5f, 0f); // Orange
                }
                else
                {
                    forceDesc = string.Format("{0:F1} N (Max Force - Full Clamp)", force);
                    forceColor = Color.red;
                }

                GUI.color = forceColor;
                GUILayout.Label(string.Format("Actual Force: {0}", forceDesc), labelStyle);
                GUI.color = Color.white;

                // Visual force gauge bar (0 to 235 N)
                Rect forceBarRect = GUILayoutUtility.GetRect(400, 16);
                GUI.Box(forceBarRect, "");
                float forceRatio = Mathf.Clamp01(force / 235.0f);
                float forceFillWidth = forceBarRect.width * forceRatio;
                GUI.color = forceColor;
                GUI.DrawTexture(new Rect(forceBarRect.x, forceBarRect.y, forceFillWidth, forceBarRect.height), Texture2D.whiteTexture);
                GUI.color = Color.white;

                // bHaptics Glove Telemetry
                if (bhapticsFeedback != null)
                {
                    bool isHaptic = bhapticsFeedback.isVibrating;
                    GUI.color = isHaptic ? new Color(1f, 0.4f, 1f) : Color.gray;
                    GUILayout.Label(string.Format("bHaptics Glove: {0} ({1}%) | Fz: {2:F1} N",
                        isHaptic ? "VIBRATING" : "IDLE", bhapticsFeedback.currentMotorIntensity, bhapticsFeedback.currentGripForceN), labelStyle);
                    GUI.color = Color.white;
                }

                GUILayout.Space(2);
                GUI.color = gripperBridge.isConnected ? Color.green : new Color(1f, 0.4f, 0.4f);
                GUILayout.Label(string.Format("Bridge: {0} ({1}:{2})", gripperBridge.isConnected ? "CONNECTED" : "OFFLINE", gripperBridge.serverIp, gripperBridge.serverPort), labelStyle);
                GUI.color = Color.white;
            }

            // 4. VR Hand Mover (Robot Arm 6-DOF Target Pose / Velocity)
            if (vrHandMover != null)
            {
                GUILayout.Space(4);
                string speedStatus = (gripperBridge != null && gripperBridge.currentSpeedPct >= 200) ? "MAX ⚡ (Real-time)" : ((gripperBridge != null) ? (gripperBridge.currentSpeedPct + "%") : "100%");
                
                if (vrHandMover.trackingMode == VRTrackingMode.TargetPose)
                {
                    GUI.color = vrHandMover.isHandTracked ? Color.cyan : Color.yellow;
                    GUILayout.Label(string.Format("Mode: TARGET POSE (Closed-Loop 6-DOF) | Speed: {0}", speedStatus), labelStyle);
                    string filterBadge = (vrHandMover != null && vrHandMover.enableOneEuroFilter) ? "1€ Filter: ACTIVE (Smooth & Zero-Lag)" : "1€ Filter: OFF";
                    string sigBadge = (gripperBridge != null) ? string.Format("Tx Rate: {0}Hz | Sig Δ: >={1:F1}mm, >={2:F1}° (Anti-Delay)", gripperBridge.sendRateHz, gripperBridge.posChangeThreshold * 1000f, gripperBridge.rotChangeThreshold) : "";
                    GUI.color = (vrHandMover != null && vrHandMover.enableOneEuroFilter) ? Color.green : Color.gray;
                    GUILayout.Label(filterBadge, subLabelStyle);
                    if (!string.IsNullOrEmpty(sigBadge)) GUILayout.Label(sigBadge, subLabelStyle);
                    if (starterPoint == null && vrHandMover != null) starterPoint = vrHandMover.starterPoint;
                    string homeDistStr = (starterPoint != null && starterPoint.isAtHome) ? "★ AT HOME (0.0 cm)" : (starterPoint != null ? string.Format("{0:F1} cm from Home", starterPoint.distanceToHome * 100f) : "Active");
                    GUI.color = (starterPoint != null && starterPoint.isAtHome) ? Color.green : Color.cyan;
                    GUILayout.Label(string.Format("Starter Point: {0} | Home: [305, -131, 311] mm", homeDistStr), subLabelStyle);
                    if (safetyVisualizer == null) safetyVisualizer = FindAnyObjectByType<SafetyBoundaryVisualizer>();
                    if (safetyVisualizer != null)
                    {
                        GUI.color = new Color(0f, 0.85f, 1f);
                        GUILayout.Label(string.Format("Robot Base (0,0,0): {0:F1} cm | Barrier: {1:F0} cm", safetyVisualizer.distToBase * 100f, safetyVisualizer.outerRadius * 100f), subLabelStyle);
                        GUI.color = Color.white;
                    }
                    GUI.color = Color.white;
                    GUI.color = Color.white;
                    GUI.color = Color.white;

                    Vector3 tgtMm = vrHandMover.currentTargetPos * 1000f;
                    Vector3 actMm = (gripperBridge != null && gripperBridge.hasReceivedRobotPose) ? (gripperBridge.actualRobotPos * 1000f) : tgtMm;
                    float posErrMm = Vector3.Distance(tgtMm, actMm);

                    GUILayout.Label(string.Format("Target XYZ:  X:{0:F0}  Y:{1:F0}  Z:{2:F0} mm", tgtMm.x, tgtMm.y, tgtMm.z), subLabelStyle);
                    GUILayout.Label(string.Format("Actual XYZ:  X:{0:F0}  Y:{1:F0}  Z:{2:F0} mm | Err: {3:F1} mm", actMm.x, actMm.y, actMm.z, posErrMm), subLabelStyle);

                    Vector3 tgtRotDeg = vrHandMover.currentTargetRotAxisAngle * Mathf.Rad2Deg;
                    Vector3 actRotDeg = (gripperBridge != null && gripperBridge.hasReceivedRobotPose) ? (gripperBridge.actualRobotRot * Mathf.Rad2Deg) : tgtRotDeg;
                    GUILayout.Label(string.Format("Wrist Rot:   Rx:{0:F1}° Ry:{1:F1}° Rz:{2:F1}°", tgtRotDeg.x, tgtRotDeg.y, tgtRotDeg.z), subLabelStyle);

                    if (GUILayout.Button("🎯 Recenter Anchor Hand [R]", GUILayout.Height(22)))
                    {
                        vrHandMover.RecenterAnchor();
                    }
                }
                else
                {
                    bool armMoving = vrHandMover.isHandTracked && vrHandMover.mappedVelocity.sqrMagnitude > 0.00001f;
                    bool armRotating = vrHandMover.isHandTracked && vrHandMover.mappedAngularVelocity.sqrMagnitude > 0.0001f;
                    bool armActive = armMoving || armRotating;

                    GUI.color = armActive ? Color.cyan : (vrHandMover.isHandTracked ? Color.green : Color.yellow);
                    GUILayout.Label(string.Format("Speed: {0} | Arm 6-DOF: {1} | Tracked: {2}", speedStatus, armActive ? "ACTIVE" : "IDLE", vrHandMover.isHandTracked ? "YES" : "NO"), labelStyle);
                    GUI.color = Color.white;

                    string xDir = vrHandMover.mappedVelocity.x > 0.01f ? "FWD" : (vrHandMover.mappedVelocity.x < -0.01f ? "BACK" : "-");
                    string yDir = vrHandMover.mappedVelocity.y > 0.01f ? "LEFT" : (vrHandMover.mappedVelocity.y < -0.01f ? "RIGHT" : "-");
                    string zDir = vrHandMover.mappedVelocity.z > 0.01f ? "UP" : (vrHandMover.mappedVelocity.z < -0.01f ? "DOWN" : "-");
                    GUILayout.Label(string.Format("Linear:  X:{0:+0.00;-0.00;0.00}[{3}] Y:{1:+0.00;-0.00;0.00}[{4}] Z:{2:+0.00;-0.00;0.00}[{5}]", 
                        vrHandMover.mappedVelocity.x, vrHandMover.mappedVelocity.y, vrHandMover.mappedVelocity.z, xDir, yDir, zDir), subLabelStyle);

                    string rollDir = vrHandMover.mappedAngularVelocity.x > 0.03f ? "CW" : (vrHandMover.mappedAngularVelocity.x < -0.03f ? "CCW" : "-");
                    string pitchDir = vrHandMover.mappedAngularVelocity.y > 0.03f ? "UP" : (vrHandMover.mappedAngularVelocity.y < -0.03f ? "DOWN" : "-");
                    string yawDir = vrHandMover.mappedAngularVelocity.z > 0.03f ? "LEFT" : (vrHandMover.mappedAngularVelocity.z < -0.03f ? "RIGHT" : "-");
                    GUILayout.Label(string.Format("Angular: Roll:{0:+0.00;-0.00;0.00}[{3}] Pitch:{1:+0.00;-0.00;0.00}[{4}] Yaw:{2:+0.00;-0.00;0.00}[{5}]", 
                        vrHandMover.mappedAngularVelocity.x, vrHandMover.mappedAngularVelocity.y, vrHandMover.mappedAngularVelocity.z, rollDir, pitchDir, yawDir), subLabelStyle);
                }
            }

            // 5. Webcam Feed & Camera Selector
            if (webcamScript != null)
            {
                GUILayout.Space(4);
                GUI.color = webcamScript.IsStreaming ? Color.green : Color.yellow;
                GUILayout.Label(string.Format("Webcam: [{0}/{1}] {2}", 
                    webcamScript.CurrentDeviceIndex + 1, webcamScript.DeviceCount, webcamScript.CurrentDeviceName), labelStyle);
                GUILayout.Label(string.Format("Status: {0} ({1})", 
                    webcamScript.StatusDescription, webcamScript.CurrentResolution), subLabelStyle);
                GUI.color = Color.white;

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("◀ Prev Cam [V]", GUILayout.Height(24)))
                {
                    webcamScript.PrevCamera();
                }
                if (GUILayout.Button("Next Cam ▶ [C]", GUILayout.Height(24)))
                {
                    webcamScript.NextCamera();
                }
                GUILayout.EndHorizontal();
            }

            if (!hmdPresent)
            {
                GUILayout.Label("Note: Put on Quest headset to wake Link & tracking", warningStyle);
            }

            GUILayout.Space(4);
            GUI.color = new Color(0.7f, 0.9f, 1f);
            GUILayout.Label("🎮 Left Remote: [X] Set Origin/Home | [Y] Pause/Resume | [L-Stick] Auto-Shake (3x)", subLabelStyle);
            GUI.color = Color.white;
            GUILayout.EndArea();
        }
    }
}
