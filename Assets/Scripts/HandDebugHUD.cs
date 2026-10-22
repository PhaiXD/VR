using System;
using UnityEngine;
using UnityEngine.UI;

namespace UR3eVR
{
    /// <summary>
    /// HandDebugHUD: Stereoscopic VR HUD displaying real-time UR3e robot teleoperation telemetry.
    /// - Replaces legacy OnGUI() with a World Space Canvas rendered in BOTH eyes (Single Pass Instanced).
    /// - Follows peripheral headset view or attaches to tracking space cleanly.
    /// - Displays XYZ target vs actual coordinates, bridge connection, grip force, and pause state.
    /// </summary>
    public class HandDebugHUD : MonoBehaviour
    {
        [Header("References")]
        public PinchDetector pinchDetector;
        public GripperBridge gripperBridge;
        public VRHandMover vrHandMover;
        public UR3eStarterPoint starterPoint;
        public SafetyBoundaryVisualizer safetyVisualizer;
        public script webcamScript;
        public Transform centerEyeAnchor;

        [Header("HUD Canvas Settings")]
        public float hudDistance = 1.35f;
        public float hudHeightOffset = -0.18f;
        public float hudSideOffset = -0.42f; // Left peripheral view
        public float followSpeed = 6.0f;

        // UI GameObjects
        private GameObject hudCanvasObj;
        private bool hasPositionedDefault = false;
        private Canvas hudCanvas;
        private Text titleText;
        private Text telemetryText;
        private Text pauseBannerText;
        private Text helpText;
        private Image backgroundImage;
        private Material stereoUIMat;

        private void Start()
        {
            FindReferences();
            BuildStereoHUDCanvas();
        }

        private void FindReferences()
        {
            if (pinchDetector == null) pinchDetector = FindAnyObjectByType<PinchDetector>();
            if (gripperBridge == null) gripperBridge = FindAnyObjectByType<GripperBridge>();
            if (vrHandMover == null) vrHandMover = FindAnyObjectByType<VRHandMover>();
            if (starterPoint == null) starterPoint = FindAnyObjectByType<UR3eStarterPoint>();
            if (safetyVisualizer == null) safetyVisualizer = FindAnyObjectByType<SafetyBoundaryVisualizer>();
            if (webcamScript == null) webcamScript = FindAnyObjectByType<script>(FindObjectsInactive.Include);

            if (centerEyeAnchor == null)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) centerEyeAnchor = cec.transform;
                if (centerEyeAnchor == null && Camera.main != null) centerEyeAnchor = Camera.main.transform;
            }
        }

        private void BuildStereoHUDCanvas()
        {
            hudCanvasObj = new GameObject("StereoHandDebugHUD_Canvas");
            hudCanvasObj.transform.SetParent(transform, false);

            hudCanvas = hudCanvasObj.AddComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.WorldSpace;
            hudCanvasObj.AddComponent<CanvasScaler>();

            var gr = hudCanvasObj.AddComponent<GraphicRaycaster>();

            RectTransform canvasRT = hudCanvasObj.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(480f, 320f);
            canvasRT.localScale = Vector3.one * 0.0016f; // ~0.75m wide in VR

            Shader stereoShader = Shader.Find("UR3eVR/StereoUI") ?? Shader.Find("UI/Default");
            stereoUIMat = new Material(stereoShader);

            // Background Panel
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(hudCanvasObj.transform, false);
            RectTransform bgRT = bgObj.AddComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            backgroundImage = bgObj.AddComponent<Image>();
            backgroundImage.material = stereoUIMat;
            backgroundImage.color = new Color(0.04f, 0.07f, 0.12f, 0.88f);

            var ol = bgObj.AddComponent<Outline>();
            ol.effectColor = new Color(0.0f, 0.85f, 1.0f, 0.40f);
            ol.effectDistance = new Vector2(2, -2);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 16);

            // Title
            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(bgObj.transform, false);
            RectTransform titleRT = titleObj.AddComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0.04f, 0.86f);
            titleRT.anchorMax = new Vector2(0.96f, 0.98f);
            titleRT.offsetMin = Vector2.zero;
            titleRT.offsetMax = Vector2.zero;

            titleText = titleObj.AddComponent<Text>();
            titleText.material = stereoUIMat;
            titleText.font = font;
            titleText.fontSize = 18;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.color = new Color(0.0f, 1.0f, 0.85f, 1.0f);
            titleText.text = "UR3e TELEOPERATION TELEMETRY (STEREO)";

            // Pause Banner
            GameObject pauseObj = new GameObject("PauseBanner");
            pauseObj.transform.SetParent(bgObj.transform, false);
            RectTransform pauseRT = pauseObj.AddComponent<RectTransform>();
            pauseRT.anchorMin = new Vector2(0.55f, 0.86f);
            pauseRT.anchorMax = new Vector2(0.96f, 0.98f);
            pauseRT.offsetMin = Vector2.zero;
            pauseRT.offsetMax = Vector2.zero;

            pauseBannerText = pauseObj.AddComponent<Text>();
            pauseBannerText.material = stereoUIMat;
            pauseBannerText.font = font;
            pauseBannerText.fontSize = 16;
            pauseBannerText.fontStyle = FontStyle.Bold;
            pauseBannerText.alignment = TextAnchor.MiddleRight;
            pauseBannerText.color = Color.yellow;
            pauseBannerText.text = "";

            // Main Telemetry Text
            GameObject bodyObj = new GameObject("TelemetryText");
            bodyObj.transform.SetParent(bgObj.transform, false);
            RectTransform bodyRT = bodyObj.AddComponent<RectTransform>();
            bodyRT.anchorMin = new Vector2(0.04f, 0.16f);
            bodyRT.anchorMax = new Vector2(0.96f, 0.84f);
            bodyRT.offsetMin = Vector2.zero;
            bodyRT.offsetMax = Vector2.zero;

            telemetryText = bodyObj.AddComponent<Text>();
            telemetryText.material = stereoUIMat;
            telemetryText.font = font;
            telemetryText.fontSize = 13;
            telemetryText.lineSpacing = 1.15f;
            telemetryText.alignment = TextAnchor.UpperLeft;
            telemetryText.color = Color.white;

            // Bottom Grab Handle Bar with VRWindowDragger (Draggable via Left Remote Laser)
            GameObject grabBarObj = new GameObject("GrabHandleBar");
            grabBarObj.transform.SetParent(bgObj.transform, false);
            RectTransform grabRT = grabBarObj.AddComponent<RectTransform>();
            grabRT.anchorMin = new Vector2(0.20f, 0.02f);
            grabRT.anchorMax = new Vector2(0.80f, 0.12f);
            grabRT.offsetMin = Vector2.zero;
            grabRT.offsetMax = Vector2.zero;

            Image grabImg = grabBarObj.AddComponent<Image>();
            grabImg.material = stereoUIMat;
            grabImg.color = new Color(0.18f, 0.22f, 0.32f, 0.90f);

            var dragger = grabBarObj.AddComponent<VRWindowDragger>();
            dragger.windowTransform = hudCanvasObj.transform;

            GameObject grabLabel = new GameObject("Label");
            grabLabel.transform.SetParent(grabBarObj.transform, false);
            RectTransform glRT = grabLabel.AddComponent<RectTransform>();
            glRT.anchorMin = Vector2.zero;
            glRT.anchorMax = Vector2.one;
            glRT.offsetMin = Vector2.zero;
            glRT.offsetMax = Vector2.zero;
            Text glText = grabLabel.AddComponent<Text>();
            glText.font = font;
            glText.fontSize = 11;
            glText.fontStyle = FontStyle.Bold;
            glText.alignment = TextAnchor.MiddleCenter;
            glText.color = new Color(0.0f, 0.85f, 1.0f, 0.95f);
            glText.text = "--- GRAB TO MOVE ---";
        }

        public void RepositionDefault()
        {
            if (centerEyeAnchor == null) FindReferences();
            if (centerEyeAnchor == null || hudCanvasObj == null) return;

            Vector3 headForward = centerEyeAnchor.forward;
            headForward.y = 0f;
            if (headForward.sqrMagnitude > 0.001f) headForward.Normalize();
            else headForward = Vector3.forward;

            Vector3 headRight = Vector3.Cross(Vector3.up, headForward).normalized;

            Vector3 targetPos = centerEyeAnchor.position
                              + headForward * hudDistance
                              + headRight * hudSideOffset
                              + Vector3.up * hudHeightOffset;

            hudCanvasObj.transform.position = targetPos;
            hudCanvasObj.transform.rotation = Quaternion.LookRotation(hudCanvasObj.transform.position - centerEyeAnchor.position, Vector3.up);
        }

        private void LateUpdate()
        {
            if (centerEyeAnchor == null) FindReferences();

            // Refresh World Camera for Stereo Rendering in Both Eyes
            if (hudCanvas != null && centerEyeAnchor != null)
            {
                var cam = centerEyeAnchor.GetComponent<Camera>();
                if (cam != null && hudCanvas.worldCamera != cam)
                {
                    hudCanvas.worldCamera = cam;
                }
            }

            // Position once on startup, then stay completely still in World Space (Draggable via Left Laser)
            if (!hasPositionedDefault && centerEyeAnchor != null && hudCanvasObj != null)
            {
                RepositionDefault();
                hasPositionedDefault = true;
            }

            // Update Telemetry Display
            UpdateTelemetryContent();
        }

        private void UpdateTelemetryContent()
        {
            if (telemetryText == null) return;

            bool isPaused = vrHandMover != null && vrHandMover.isTeleoperationPaused;
            if (pauseBannerText != null)
            {
                pauseBannerText.text = isPaused ? "[PAUSED - FROZEN]" : "[TRACKING ACTIVE]";
                pauseBannerText.color = isPaused ? new Color(1f, 0.25f, 0.25f, 1f) : new Color(0.2f, 1f, 0.4f, 1f);
            }

            string bridgeStatus = (gripperBridge != null && gripperBridge.isConnected) ? "<color=#00FF88>CONNECTED</color>" : "<color=#FF4444>OFFLINE</color>";
            string modeStatus = "CLOSED-LOOP 6-DOF POSE";

            Vector3 tgtMm = (vrHandMover != null) ? vrHandMover.currentTargetPos * 1000f : Vector3.zero;
            Vector3 actMm = (gripperBridge != null) ? gripperBridge.actualRobotPos * 1000f : Vector3.zero;
            float posErrMm = Vector3.Distance(tgtMm, actMm);

            string homeStatus = (starterPoint != null && starterPoint.isAtHome) ? "<color=#00FF88>READY AT HOME</color>" : "<color=#00D4FF>OFF-HOME</color>";
            string pinchDesc = (pinchDetector != null) ? $"Pinch: {pinchDetector.normalizedPinch * 100f:F0}% (POS: {pinchDetector.gripperPos})" : "Pinch: N/A";
            string barrierDesc = (safetyVisualizer != null) ? $"Dist to Base: {safetyVisualizer.distToBase * 100f:F1} cm | Limit: {safetyVisualizer.outerRadius * 100f:F0} cm" : "";

            telemetryText.text = 
                $"Bridge: {bridgeStatus} | Mode: {modeStatus}\n" +
                $"Starter Point: {homeStatus} | {barrierDesc}\n\n" +
                $"<color=#00F0FF>Target XYZ:</color>  X:{tgtMm.x,5:F0}  Y:{tgtMm.y,5:F0}  Z:{tgtMm.z,5:F0} mm\n" +
                $"<color=#00FF88>Actual XYZ:</color>  X:{actMm.x,5:F0}  Y:{actMm.y,5:F0}  Z:{actMm.z,5:F0} mm  (Err: {posErrMm:F1} mm)\n\n" +
                $"{pinchDesc}";
        }
    }
}
