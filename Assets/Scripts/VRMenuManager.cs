using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace UR3eVR
{
    /// <summary>
    /// VRMenuManager: ระบบ Quick Menu ลอยในพื้นที่ 3D สำหรับ Meta Quest Touch Remote
    /// - ปุ่ม Menu รีโมทมือซ้าย (3 ขีด / Hamburger): OVRInput.Button.Start / KeyCode.M
    /// - สี่เมนูหลัก: Set Origin, Open New Cam, Open Log (Auto-popup เมื่อมีเตือนเหลือง/แดง), Background
    /// - ปุ่มปิด [X] อยู่มุมขวาบนของทุกหน้าต่าง
    /// - แก้ไขปัญหา Single Pass Instanced: กำหนด worldCamera และ Material Instancing ให้มองเห็นครบทั้ง 2 ตา
    /// - ลำแสงเลเซอร์ (Laser Beam) ยิงพุ่งออกจากปลายรีโมทคอนโทรลเลอร์ขวาอย่างแม่นยำ พร้อมจุด Reticle
    /// </summary>
    public class VRMenuManager : MonoBehaviour
    {
        public static VRMenuManager Instance { get; private set; }

        [Header("Menu Key & Controller Bindings")]
        [Tooltip("ปุ่ม Menu รีโมทซ้าย (ขีด 3 ขีดบน LTouch)")]
        public OVRInput.Button menuButton = OVRInput.Button.Start;
        public OVRInput.RawButton rawMenuButton = OVRInput.RawButton.Start;
        public KeyCode keyboardMenuKey = KeyCode.M;

        [Header("References")]
        public Transform headTransform;
        public Camera centerEyeCamera;
        public Transform rightControllerAnchor;
        public Transform leftControllerAnchor;
        public VRHandMover vrHandMover;
        public GripperBridge gripperBridge;
        public script webcamScript;
        public PinchDetector pinchDetector;

        [Header("Menu State")]
        public bool isMenuOpen = false;
        public enum MenuPanel { Main, Camera, Log, Background }
        public MenuPanel currentPanel = MenuPanel.Main;

        [Header("Laser Pointer Settings")]
        public bool enableLaserPointer = true;
        public float laserLength = 3.0f;
        public Color laserColor = new Color(0.0f, 0.90f, 1.0f, 0.95f); // Bright Electric Cyan

        // UI Materials (Single Pass Instanced Stereo Fix)
        private Material stereoUIMaterial;
        private Material laserMaterial;

        // UI GameObjects
        private GameObject canvasObj;
        private Canvas canvas;
        private GraphicRaycaster raycaster;
        private Font uiFont;

        // Panels
        private GameObject panelMain;
        private GameObject panelCamera;
        private GameObject panelLog;
        private GameObject panelBg;

        // Main Panel UI
        private Text statusText;
        private Text toastText;
        private float toastTimer = 0f;

        // Camera Panel UI
        private Text camStatusText;
        private Transform camButtonContainer;
        private List<GameObject> dynamicCamButtons = new List<GameObject>();

        // Log Panel UI
        private GameObject alertBannerObj;
        private Text alertBannerText;
        private Image alertBannerImage;
        private Text logContentText;
        private ScrollRect logScrollRect;
        private float bannerDismissTimer = 0f;
        private List<string> logEntries = new List<string>();

        // Laser Pointer Components
        private GameObject laserObj;
        private LineRenderer laserLine;
        private GameObject reticleObj;
        private Button hoveredButton = null;
        private Button lastHoveredButton = null;
        private List<Button> allMenuButtons = new List<Button>();
        private float lastHandPokeTime = 0f;
        private Color lastBtnOrigColor = Color.clear;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            FindReferences();
            LoadFont();
            EnsureStereoMaterials();
            BuildVRCanvas();
            BuildLaserPointer();

            // Subscribe to GripperBridge alert and log events
            GripperBridge.OnAlertReceived += HandleAlertReceived;
            GripperBridge.OnLogReceived += HandleLogReceived;

            // Start closed by default
            SetMenuOpen(false);
            AddLog("SYSTEM", "VR Menu System Initialized (Stereo Dual-Eye Active).");
        }

        private void OnDestroy()
        {
            GripperBridge.OnAlertReceived -= HandleAlertReceived;
            GripperBridge.OnLogReceived -= HandleLogReceived;
        }

        private void FindReferences()
        {
            if (centerEyeCamera == null)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) centerEyeCamera = cec.GetComponent<Camera>();
                if (centerEyeCamera == null && Camera.main != null) centerEyeCamera = Camera.main;
            }

            if (headTransform == null)
            {
                if (centerEyeCamera != null) headTransform = centerEyeCamera.transform;
                else
                {
                    var cec = GameObject.Find("CenterEyeAnchor");
                    if (cec != null) headTransform = cec.transform;
                }
            }

            if (rightControllerAnchor == null)
            {
                GameObject rca = GameObject.Find("RightControllerAnchor");
                if (rca != null) rightControllerAnchor = rca.transform;
            }

            if (leftControllerAnchor == null)
            {
                GameObject lca = GameObject.Find("LeftControllerAnchor");
                if (lca != null) leftControllerAnchor = lca.transform;
            }

            if (vrHandMover == null) vrHandMover = FindAnyObjectByType<VRHandMover>();
            if (gripperBridge == null) gripperBridge = FindAnyObjectByType<GripperBridge>();
            if (webcamScript == null) webcamScript = FindAnyObjectByType<script>();
            if (pinchDetector == null) pinchDetector = FindAnyObjectByType<PinchDetector>();

            // Ensure EventSystem exists for UI clicks
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                esObj.AddComponent<EventSystem>();
                esObj.AddComponent<StandaloneInputModule>();
            }
        }

        private void LoadFont()
        {
            try
            {
                uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch
            {
                uiFont = null;
            }
        }

        private void EnsureStereoMaterials()
        {
            // CRITICAL UI STEREO & COLOR FIX:
            // For UI elements (Image, Text, Button), leaving material = null tells Unity uGUI
            // to use Graphic.defaultGraphicMaterial (UI/Default shader).
            // UI/Default natively handles vertex color tinting, font glyph atlases, and alpha blending.
            // When Canvas.worldCamera is assigned to CenterEyeAnchor, it renders in BOTH eyes in VR.
            stereoUIMaterial = null;

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit")
                              ?? Shader.Find("UR3eVR/StereoUnlitColor")
                              ?? Shader.Find("Unlit/Color");

            if (laserMaterial == null && unlitShader != null)
            {
                laserMaterial = new Material(unlitShader);
                laserMaterial.name = "Runtime_LaserMat";
                laserMaterial.enableInstancing = true;
                if (laserMaterial.HasProperty("_BaseColor")) laserMaterial.SetColor("_BaseColor", laserColor);
                if (laserMaterial.HasProperty("_Color")) laserMaterial.SetColor("_Color", laserColor);
            }
        }

        private void Update()
        {
            // 1. Toggle Menu Button: Left Controller (3 horizontal lines / Hamburger button) or 'M' Key
            bool menuPressed = false;
            try
            {
                if (OVRInput.GetDown(menuButton, OVRInput.Controller.LTouch) ||
                    OVRInput.GetDown(rawMenuButton) ||
                    OVRInput.GetDown(menuButton, OVRInput.Controller.Touch))
                {
                    menuPressed = true;
                }
            }
            catch { }

            if (!menuPressed && Input.GetKeyDown(keyboardMenuKey))
            {
                menuPressed = true;
            }

            if (menuPressed)
            {
                ToggleMenu();
            }

            // Keyboard direct shortcuts for quick Editor testing
            if (isMenuOpen)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) ActionSetOrigin();
                if (Input.GetKeyDown(KeyCode.Alpha2)) ShowPanel(MenuPanel.Camera);
                if (Input.GetKeyDown(KeyCode.Alpha3)) ShowPanel(MenuPanel.Log);
                if (Input.GetKeyDown(KeyCode.Alpha4)) ShowPanel(MenuPanel.Background);
                if (Input.GetKeyDown(KeyCode.Escape)) SetMenuOpen(false);
            }

            // Toast message timer
            if (toastTimer > 0f)
            {
                toastTimer -= Time.deltaTime;
                if (toastTimer <= 0f && toastText != null)
                {
                    toastText.gameObject.SetActive(false);
                }
            }

            // Alert banner timer
            if (bannerDismissTimer > 0f)
            {
                bannerDismissTimer -= Time.deltaTime;
                if (bannerDismissTimer <= 0f && alertBannerObj != null)
                {
                    alertBannerObj.SetActive(false);
                }
            }

            // Update Status text on Main Panel
            if (isMenuOpen && currentPanel == MenuPanel.Main && statusText != null)
            {
                UpdateMainStatusText();
            }

            // Update Laser Pointer interaction
            UpdateLaserPointer();

            // Direct Hand Poke interaction (ใช้นิ้วจิ้มปุ่มโดยตรง)
            CheckDirectHandPoke();
        }

        public void ToggleMenu()
        {
            SetMenuOpen(!isMenuOpen);
        }

        public void SetMenuOpen(bool open)
        {
            isMenuOpen = open;
            if (canvasObj != null)
            {
                canvasObj.SetActive(open);
            }

            if (open)
            {
                RepositionMenuInFrontOfHead();
                ShowPanel(MenuPanel.Main);
                HapticPulse(0.3f, 0.1f);
            }
            else
            {
                if (laserLine != null) laserLine.enabled = false;
                if (reticleObj != null) reticleObj.SetActive(false);
                if (lastHoveredButton != null)
                {
                    var prevImg = lastHoveredButton.GetComponent<Image>();
                    if (prevImg != null && lastBtnOrigColor != Color.clear)
                    {
                        prevImg.color = lastBtnOrigColor;
                    }
                    lastHoveredButton = null;
                }
            }
        }

        public void RepositionMenuInFrontOfHead()
        {
            if (headTransform == null) FindReferences();
            if (headTransform == null || canvasObj == null) return;

            Vector3 fwd = headTransform.forward;
            Vector3 fwdFlat = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized;
            if (fwdFlat.sqrMagnitude < 0.01f) fwdFlat = fwd;

            // Place 0.85m directly in front, slightly down by 0.05m
            canvasObj.transform.position = headTransform.position + fwdFlat * 0.85f + Vector3.up * -0.05f;
            canvasObj.transform.rotation = Quaternion.LookRotation(fwdFlat, Vector3.up);

            // Re-verify worldCamera for stereo rendering
            if (canvas != null && canvas.worldCamera == null && centerEyeCamera != null)
            {
                canvas.worldCamera = centerEyeCamera;
            }
        }

        public void ShowPanel(MenuPanel panel)
        {
            currentPanel = panel;
            if (panelMain != null) panelMain.SetActive(panel == MenuPanel.Main);
            if (panelCamera != null) panelCamera.SetActive(panel == MenuPanel.Camera);
            if (panelLog != null) panelLog.SetActive(panel == MenuPanel.Log);
            if (panelBg != null) panelBg.SetActive(panel == MenuPanel.Background);

            if (panel == MenuPanel.Camera)
            {
                PopulateCameraList();
            }
            else if (panel == MenuPanel.Log)
            {
                RefreshLogText();
            }
        }

        // =========================================================================
        // Button Actions
        // =========================================================================

        public void ActionSetOrigin()
        {
            if (vrHandMover == null) vrHandMover = FindAnyObjectByType<VRHandMover>();
            if (vrHandMover != null)
            {
                vrHandMover.RecenterAnchor();
                ShowToast("🎯 Origin Recentered to Current Pose!");
                HapticPulse(0.7f, 0.2f);
                AddLog("ACTION", "Origin Recentered to current hand/controller pose.");
            }
            else
            {
                ShowToast("⚠️ VRHandMover not found!");
            }
        }

        public void ActionOpenNewCam()
        {
            ShowPanel(MenuPanel.Camera);
            if (VRCameraWindow.Instance != null)
            {
                VRCameraWindow.Instance.SetWindowOpen(true);
                VRCameraWindow.Instance.RepositionWindowDefault();
            }
        }

        public void ActionOpenLog()
        {
            ShowPanel(MenuPanel.Log);
        }

        public void ActionBackground()
        {
            ShowPanel(MenuPanel.Background);
        }

        public void ActionCloseMenu()
        {
            SetMenuOpen(false);
        }

        public void ActionBackToMain()
        {
            ShowPanel(MenuPanel.Main);
        }

        public void ActionClearLogs()
        {
            logEntries.Clear();
            AddLog("SYSTEM", "Logs cleared by user.");
            RefreshLogText();
        }

        // =========================================================================
        // Camera Selector Logic
        // =========================================================================

        private void PopulateCameraList()
        {
            if (camButtonContainer == null) return;

            // Clear old dynamic buttons
            foreach (var btn in dynamicCamButtons)
            {
                if (btn != null) Destroy(btn);
            }
            dynamicCamButtons.Clear();

            if (webcamScript == null) webcamScript = FindAnyObjectByType<script>();

            var devices = WebCamTexture.devices;
            int activeIndex = (webcamScript != null) ? webcamScript.CurrentDeviceIndex : -1;

            if (camStatusText != null)
            {
                string activeName = (webcamScript != null) ? webcamScript.CurrentDeviceName : "None";
                camStatusText.text = $"Streaming: {activeName} (Index: {activeIndex})";
            }

            if (devices == null || devices.Length == 0)
            {
                CreateTextItem(camButtonContainer, "No webcam devices detected on system.", 16, Color.gray);
                return;
            }

            for (int i = 0; i < devices.Length; i++)
            {
                int deviceIdx = i;
                string devName = devices[i].name;
                bool isActive = (i == activeIndex);

                string label = isActive ? $"★ [ACTIVE] {deviceIdx}: {devName}" : $"📹 {deviceIdx}: {devName}";
                Color bgCol = isActive ? new Color(0.0f, 0.45f, 0.40f, 0.95f) : new Color(0.12f, 0.16f, 0.24f, 0.95f);
                Color textCol = isActive ? new Color(0.2f, 1.0f, 0.8f, 1.0f) : Color.white;

                var btnObj = CreateSimpleButton(camButtonContainer, label, 42, bgCol, textCol, () =>
                {
                    if (webcamScript != null)
                    {
                        webcamScript.OpenDeviceByIndex(deviceIdx);
                        ShowToast($"Switched to Camera {deviceIdx}: {devName}");
                        AddLog("CAMERA", $"Switched active camera to [{deviceIdx}] {devName}");
                        HapticPulse(0.5f, 0.15f);
                        PopulateCameraList(); // Refresh UI highlight
                    }
                });

                dynamicCamButtons.Add(btnObj);
            }
        }

        // =========================================================================
        // Log & Auto-Popup on Alert Logic
        // =========================================================================

        private void HandleAlertReceived(string level, string message)
        {
            string cleanLevel = string.IsNullOrEmpty(level) ? "WARNING" : level.ToUpper();
            AddLog(cleanLevel, message);

            // Auto-popup Log Viewer UI immediately when warning or error arrives
            if (cleanLevel == "WARNING" || cleanLevel == "ERROR")
            {
                ShowAlertPopup(cleanLevel, message);
            }
        }

        private void HandleLogReceived(string message)
        {
            AddLog("LOG", message);
        }

        public void ShowAlertPopup(string level, string message)
        {
            if (!isMenuOpen)
            {
                SetMenuOpen(true);
            }

            ShowPanel(MenuPanel.Log);

            if (alertBannerObj != null && alertBannerText != null && alertBannerImage != null)
            {
                alertBannerObj.SetActive(true);
                bool isError = (level == "ERROR");
                alertBannerImage.color = isError ? new Color(0.9f, 0.15f, 0.15f, 0.95f) : new Color(0.95f, 0.70f, 0.05f, 0.95f);
                string icon = isError ? "🛑 ERROR:" : "⚠️ WARNING:";
                alertBannerText.text = $"{icon} {message}";
                bannerDismissTimer = 8.0f;
            }

            HapticPulse(level == "ERROR" ? 0.9f : 0.6f, 0.35f);
        }

        public void AddLog(string tag, string message)
        {
            string timeStr = DateTime.Now.ToString("HH:mm:ss");
            string entry = $"[{timeStr}] [{tag}] {message}";
            logEntries.Add(entry);
            if (logEntries.Count > 100) logEntries.RemoveAt(0);

            if (currentPanel == MenuPanel.Log)
            {
                RefreshLogText();
            }
        }

        private void RefreshLogText()
        {
            if (logContentText == null) return;
            var sb = new System.Text.StringBuilder();
            for (int i = Mathf.Max(0, logEntries.Count - 25); i < logEntries.Count; i++)
            {
                string line = logEntries[i];
                if (line.Contains("[ERROR]"))
                    sb.AppendLine($"<color=#FF5555>{line}</color>");
                else if (line.Contains("[WARNING]"))
                    sb.AppendLine($"<color=#FFD54F>{line}</color>");
                else if (line.Contains("[ACTION]") || line.Contains("[CAMERA]"))
                    sb.AppendLine($"<color=#4DD0E1>{line}</color>");
                else
                    sb.AppendLine($"<color=#CCCCCC>{line}</color>");
            }
            logContentText.text = sb.ToString();

            if (logScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                logScrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private void ShowToast(string message)
        {
            if (toastText == null) return;
            toastText.text = message;
            toastText.gameObject.SetActive(true);
            toastTimer = 2.5f;
        }

        private void UpdateMainStatusText()
        {
            if (statusText == null) return;
            string conn = (gripperBridge != null && gripperBridge.isConnected) ? "<color=#00E5FF>CONNECTED</color>" : "<color=#FF5555>DISCONNECTED</color>";
            int speed = (gripperBridge != null) ? gripperBridge.currentSpeedPct : 100;
            int grip = (gripperBridge != null) ? gripperBridge.actualGripperPos : 0;
            string trackingSource = (vrHandMover != null && vrHandMover.isUsingController) ? "Touch Remote" : "Bare Hand";
            statusText.text = $"UR3e Bridge: {conn}  |  Speed: {speed}%  |  Grip: {grip}/255  |  Mode: {trackingSource}";
        }

        private void HapticPulse(float amplitude, float duration)
        {
            try
            {
                OVRInput.SetControllerVibration(amplitude, amplitude, OVRInput.Controller.RTouch);
                OVRInput.SetControllerVibration(amplitude, amplitude, OVRInput.Controller.LTouch);
                StartCoroutine(StopHapticsCoroutine(duration));
            }
            catch { }
        }

        private IEnumerator StopHapticsCoroutine(float duration)
        {
            yield return new WaitForSeconds(duration);
            try
            {
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
            }
            catch { }
        }

        // =========================================================================
        // VR Laser Pointer Implementation (Shoots from Remote Controller Tip)
        // =========================================================================

        private void BuildLaserPointer()
        {
            laserObj = new GameObject("VR_LaserPointer");
            laserObj.transform.SetParent(transform, false);

            laserLine = laserObj.AddComponent<LineRenderer>();
            laserLine.useWorldSpace = true;
            laserLine.startWidth = 0.005f;
            laserLine.endWidth = 0.002f;
            laserLine.material = laserMaterial;
            laserLine.startColor = laserColor;
            laserLine.endColor = new Color(laserColor.r, laserColor.g, laserColor.b, 0.25f);
            laserLine.positionCount = 2;
            laserLine.enabled = false;

            reticleObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            reticleObj.name = "VR_LaserReticle";
            reticleObj.transform.SetParent(transform, false);
            reticleObj.transform.localScale = Vector3.one * 0.015f;
            Collider col = reticleObj.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var rend = reticleObj.GetComponent<Renderer>();
            rend.material = laserMaterial;
            reticleObj.SetActive(false);
        }

        
        private void CheckDirectHandPoke()
        {
            if (!isMenuOpen || canvasObj == null) return;
            if (pinchDetector == null || pinchDetector.indexTipTransform == null)
            {
                if (pinchDetector == null) pinchDetector = FindAnyObjectByType<PinchDetector>();
                if (pinchDetector == null || pinchDetector.indexTipTransform == null) return;
            }

            if (Time.time - lastHandPokeTime < 0.35f) return;

            Vector3 fingerPos = pinchDetector.indexTipTransform.position;

            foreach (var btn in allMenuButtons)
            {
                if (btn == null || !btn.gameObject.activeInHierarchy || !btn.interactable) continue;

                RectTransform rt = btn.GetComponent<RectTransform>();
                if (rt == null) continue;

                Vector3 localPos = rt.InverseTransformPoint(fingerPos);
                Rect r = rt.rect;

                // ตรวจสอบว่าปลายนิ้วอยู่ในกรอบปุ่ม และแตะที่ระนาบปุ่ม (< 2.5 ซม.)
                if (r.Contains(new Vector2(localPos.x, localPos.y)) && Mathf.Abs(localPos.z) <= 0.025f)
                {
                    btn.onClick.Invoke();
                    lastHandPokeTime = Time.time;
                    HapticPulse(0.35f, 0.08f);
                    break;
                }
            }
        }

        private void UpdateLaserPointer()
        {
            if (!enableLaserPointer || !isMenuOpen || canvasObj == null)
            {
                if (laserLine != null) laserLine.enabled = false;
                if (reticleObj != null) reticleObj.SetActive(false);
                return;
            }

            // Determine Ray origin and forward direction:
            // When controller is tracked -> Shoots straight out of the front tip of the Remote!
            Vector3 rayOrigin;
            Vector3 rayDir;

            bool controllerTracked = rightControllerAnchor != null && OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch);
            if (controllerTracked)
            {
                // Tip of Remote Controller (slightly offset forward from grip center)
                rayOrigin = rightControllerAnchor.position + rightControllerAnchor.forward * 0.06f + rightControllerAnchor.up * 0.02f;
                rayDir = rightControllerAnchor.forward;
            }
            else
            {
                // Headset fallback if remote is not held
                Transform fallback = headTransform != null ? headTransform : Camera.main?.transform;
                if (fallback == null) return;
                rayOrigin = fallback.position + fallback.forward * 0.1f + Vector3.down * 0.05f;
                rayDir = fallback.forward;
            }

            laserLine.enabled = true;
            laserLine.SetPosition(0, rayOrigin);

            // Raycast against Canvas Plane
            Ray ray = new Ray(rayOrigin, rayDir);
            Plane canvasPlane = new Plane(-canvasObj.transform.forward, canvasObj.transform.position);

            Button hitButton = null;
            if (canvasPlane.Raycast(ray, out float enter) && enter <= laserLength && enter > 0f)
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                laserLine.SetPosition(1, hitPoint);
                reticleObj.SetActive(true);
                reticleObj.transform.position = hitPoint;

                // Find if hitting any Button in Canvas
                Vector3 localHit = canvasObj.transform.InverseTransformPoint(hitPoint);
                var buttons = canvasObj.GetComponentsInChildren<Button>();
                foreach (var btn in buttons)
                {
                    if (!btn.gameObject.activeInHierarchy || !btn.interactable) continue;
                    RectTransform rt = btn.GetComponent<RectTransform>();
                    if (rt == null) continue;

                    Vector3 btnLocalPos = canvasObj.transform.InverseTransformPoint(rt.position);
                    Rect r = rt.rect;
                    float scaleX = rt.lossyScale.x / canvasObj.transform.lossyScale.x;
                    float scaleY = rt.lossyScale.y / canvasObj.transform.lossyScale.y;

                    float halfW = (r.width * scaleX) * 0.5f;
                    float halfH = (r.height * scaleY) * 0.5f;

                    if (Mathf.Abs(localHit.x - btnLocalPos.x) <= halfW &&
                        Mathf.Abs(localHit.y - btnLocalPos.y) <= halfH)
                    {
                        hitButton = btn;
                        break;
                    }
                }
            }
            else
            {
                laserLine.SetPosition(1, rayOrigin + rayDir * laserLength);
                reticleObj.SetActive(false);
            }

            hoveredButton = hitButton;

            // Laser Beam Hover Visual Feedback (Button Glow)
            if (hitButton != lastHoveredButton)
            {
                if (lastHoveredButton != null)
                {
                    var prevImg = lastHoveredButton.GetComponent<Image>();
                    if (prevImg != null && lastBtnOrigColor != Color.clear)
                    {
                        prevImg.color = lastBtnOrigColor;
                    }
                }

                if (hitButton != null)
                {
                    var newImg = hitButton.GetComponent<Image>();
                    if (newImg != null)
                    {
                        lastBtnOrigColor = newImg.color;
                        newImg.color = new Color(
                            Mathf.Min(1f, lastBtnOrigColor.r * 1.5f + 0.15f),
                            Mathf.Min(1f, lastBtnOrigColor.g * 1.5f + 0.15f),
                            Mathf.Min(1f, lastBtnOrigColor.b * 1.5f + 0.25f),
                            1f
                        );
                        HapticPulse(0.15f, 0.03f);
                    }
                }

                lastHoveredButton = hitButton;
            }

            // Trigger click detection (Right Controller Index Trigger or Button One / A or Left Mouse Click)
            bool triggerClicked = false;
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
                    OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch) ||
                    OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
                {
                    triggerClicked = true;
                }
            }
            catch { }

            if (!triggerClicked && Input.GetMouseButtonDown(0))
            {
                triggerClicked = true;
            }

            if (triggerClicked && hoveredButton != null)
            {
                hoveredButton.onClick.Invoke();
                HapticPulse(0.4f, 0.1f);
            }
        }

        // =========================================================================
        // Dynamic World-Space UI Builder (High-contrast obsidian aesthetics + Stereo Fix)
        // =========================================================================

        private void BuildVRCanvas()
        {
            canvasObj = new GameObject("VR_Menu_Canvas");
            canvasObj.transform.SetParent(transform, false);

            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // CRITICAL FOR SINGLE PASS INSTANCED VR: Assign CenterEyeAnchor Camera!
            if (centerEyeCamera != null)
            {
                canvas.worldCamera = centerEyeCamera;
            }

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            raycaster = canvasObj.AddComponent<GraphicRaycaster>();

            RectTransform canvasRT = canvasObj.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(760f, 500f);
            canvasRT.localScale = Vector3.one * 0.0011f;

            // 1. Build Main Panel
            panelMain = CreatePanel(canvasRT, "Panel_MainMenu");
            BuildMainPanelContent(panelMain.transform);

            // 2. Build Camera Selection Panel
            panelCamera = CreatePanel(canvasRT, "Panel_CameraSelect");
            BuildCameraPanelContent(panelCamera.transform);

            // 3. Build Log Viewer Panel
            panelLog = CreatePanel(canvasRT, "Panel_LogViewer");
            BuildLogPanelContent(panelLog.transform);

            // 4. Build Background Selector Panel
            panelBg = CreatePanel(canvasRT, "Panel_Background");
            BuildBackgroundPanelContent(panelBg.transform);

            // 5. Meta Horizon OS Bottom Pill Grab Handle Bar
            GameObject grabBar = new GameObject("GrabHandleBar");
            grabBar.transform.SetParent(canvasObj.transform, false);
            RectTransform grabRT = grabBar.AddComponent<RectTransform>();
            grabRT.anchorMin = new Vector2(0.26f, -0.09f);
            grabRT.anchorMax = new Vector2(0.74f, -0.02f);
            grabRT.offsetMin = Vector2.zero;
            grabRT.offsetMax = Vector2.zero;

            Image grabImg = grabBar.AddComponent<Image>();
            grabImg.color = new Color(0.18f, 0.22f, 0.32f, 0.90f);

            Outline grabOutline = grabBar.AddComponent<Outline>();
            grabOutline.effectColor = new Color(0.0f, 0.85f, 1.0f, 0.35f);
            grabOutline.effectDistance = new Vector2(1f, -1f);

            GameObject grabLabelObj = new GameObject("GrabLabel");
            grabLabelObj.transform.SetParent(grabBar.transform, false);
            RectTransform glRT = grabLabelObj.AddComponent<RectTransform>();
            glRT.anchorMin = Vector2.zero;
            glRT.anchorMax = Vector2.one;
            glRT.offsetMin = Vector2.zero;
            glRT.offsetMax = Vector2.zero;
            Text grabLabel = grabLabelObj.AddComponent<Text>();
            grabLabel.font = uiFont;
            grabLabel.fontSize = 13;
            grabLabel.alignment = TextAnchor.MiddleCenter;
            grabLabel.color = new Color(0.8f, 0.9f, 1.0f, 0.85f);
            grabLabel.text = "\u2550\u2550\u2550  GRAB TO MOVE  \u2550\u2550\u2550";

            var dragger = grabBar.AddComponent<VRWindowDragger>();
            dragger.windowTransform = canvasObj.transform;
            dragger.handleImage = grabImg;
        }

        private GameObject CreatePanel(Transform parent, string name)
        {
            GameObject p = new GameObject(name);
            p.transform.SetParent(parent, false);

            RectTransform rt = p.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Background Card with Stereo-Compatible Material
            Image img = p.AddComponent<Image>();
            img.material = stereoUIMaterial;
            img.color = new Color(0.06f, 0.08f, 0.12f, 0.94f);

            // Subtle border outline
            Outline outline = p.AddComponent<Outline>();
            outline.effectColor = new Color(0.0f, 0.85f, 1.0f, 0.35f);
            outline.effectDistance = new Vector2(2f, -2f);

            return p;
        }

        private void BuildMainPanelContent(Transform p)
        {
            CreateHeader(p, "UR3e Quick Menu", () => ActionCloseMenu());

            // 4 Main Buttons Container
            GameObject btnGrid = new GameObject("ButtonGrid");
            btnGrid.transform.SetParent(p, false);
            RectTransform gridRT = btnGrid.AddComponent<RectTransform>();
            gridRT.anchorMin = new Vector2(0.08f, 0.22f);
            gridRT.anchorMax = new Vector2(0.92f, 0.82f);
            gridRT.offsetMin = Vector2.zero;
            gridRT.offsetMax = Vector2.zero;

            var vlg = btnGrid.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 14;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = true;

            // 1. Set Origin
            CreateSimpleButton(btnGrid.transform, "🎯  Set Origin (Calibrate to Hand Pose)", 52,
                new Color(0.0f, 0.38f, 0.55f, 0.95f), Color.white, ActionSetOrigin);

            // 2. Open New Cam
            CreateSimpleButton(btnGrid.transform, "📹  Open New Cam (Webcam Selector)", 52,
                new Color(0.12f, 0.22f, 0.40f, 0.95f), Color.white, ActionOpenNewCam);

            // 3. Open Log
            CreateSimpleButton(btnGrid.transform, "📋  Open Log (UR3e & Python Monitor)", 52,
                new Color(0.24f, 0.18f, 0.38f, 0.95f), Color.white, ActionOpenLog);

            // 4. Background
            CreateSimpleButton(btnGrid.transform, "🌌  Background (Scene & Skybox)", 52,
                new Color(0.14f, 0.24f, 0.26f, 0.95f), Color.white, ActionBackground);

            // Status Bar at Bottom
            GameObject statObj = new GameObject("StatusBar");
            statObj.transform.SetParent(p, false);
            RectTransform statRT = statObj.AddComponent<RectTransform>();
            statRT.anchorMin = new Vector2(0.05f, 0.04f);
            statRT.anchorMax = new Vector2(0.95f, 0.16f);
            statRT.offsetMin = Vector2.zero;
            statRT.offsetMax = Vector2.zero;

            statusText = statObj.AddComponent<Text>();
            statusText.material = stereoUIMaterial;
            statusText.font = uiFont;
            statusText.fontSize = 15;
            statusText.alignment = TextAnchor.MiddleCenter;
            statusText.color = new Color(0.85f, 0.85f, 0.85f, 1f);

            // Toast overlay
            GameObject toastObj = new GameObject("Toast");
            toastObj.transform.SetParent(p, false);
            RectTransform toastRT = toastObj.AddComponent<RectTransform>();
            toastRT.anchorMin = new Vector2(0.15f, 0.15f);
            toastRT.anchorMax = new Vector2(0.85f, 0.25f);
            toastRT.offsetMin = Vector2.zero;
            toastRT.offsetMax = Vector2.zero;

            Image tImg = toastObj.AddComponent<Image>();
            tImg.material = stereoUIMaterial;
            tImg.color = new Color(0.0f, 0.5f, 0.3f, 0.95f);
            toastText = CreateText(toastObj.transform, "", 16, Color.white, TextAnchor.MiddleCenter);
            toastObj.SetActive(false);
        }

        private void BuildCameraPanelContent(Transform p)
        {
            CreateHeader(p, "Select Camera Feed", () => ActionCloseMenu(), () => ActionBackToMain());

            GameObject subObj = new GameObject("CamStatus");
            subObj.transform.SetParent(p, false);
            RectTransform subRT = subObj.AddComponent<RectTransform>();
            subRT.anchorMin = new Vector2(0.06f, 0.74f);
            subRT.anchorMax = new Vector2(0.94f, 0.83f);
            subRT.offsetMin = Vector2.zero;
            subRT.offsetMax = Vector2.zero;
            camStatusText = subObj.AddComponent<Text>();
            camStatusText.material = stereoUIMaterial;
            camStatusText.font = uiFont;
            camStatusText.fontSize = 16;
            camStatusText.alignment = TextAnchor.MiddleLeft;
            camStatusText.color = new Color(0.0f, 0.85f, 1.0f, 1f);

            GameObject scrollObj = new GameObject("CamScroll");
            scrollObj.transform.SetParent(p, false);
            RectTransform scrollRT = scrollObj.AddComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0.06f, 0.16f);
            scrollRT.anchorMax = new Vector2(0.94f, 0.72f);
            scrollRT.offsetMin = Vector2.zero;
            scrollRT.offsetMax = Vector2.zero;

            var vlg = scrollObj.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            camButtonContainer = scrollObj.transform;

            GameObject bottomBar = new GameObject("BottomBar");
            bottomBar.transform.SetParent(p, false);
            RectTransform bRT = bottomBar.AddComponent<RectTransform>();
            bRT.anchorMin = new Vector2(0.06f, 0.04f);
            bRT.anchorMax = new Vector2(0.94f, 0.13f);
            bRT.offsetMin = Vector2.zero;
            bRT.offsetMax = Vector2.zero;

            var hlg = bottomBar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 16;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            CreateSimpleButton(bottomBar.transform, "🔄 Refresh Cameras", 38,
                new Color(0.18f, 0.22f, 0.32f, 0.95f), Color.white, PopulateCameraList);

            CreateSimpleButton(bottomBar.transform, "◀ Back to Menu", 38,
                new Color(0.24f, 0.14f, 0.18f, 0.95f), Color.white, ActionBackToMain);
        }

        private void BuildLogPanelContent(Transform p)
        {
            CreateHeader(p, "UR3e & Python System Log", () => ActionCloseMenu(), () => ActionBackToMain());

            alertBannerObj = new GameObject("AlertBanner");
            alertBannerObj.transform.SetParent(p, false);
            RectTransform abRT = alertBannerObj.AddComponent<RectTransform>();
            abRT.anchorMin = new Vector2(0.06f, 0.74f);
            abRT.anchorMax = new Vector2(0.94f, 0.84f);
            abRT.offsetMin = Vector2.zero;
            abRT.offsetMax = Vector2.zero;

            alertBannerImage = alertBannerObj.AddComponent<Image>();
            alertBannerImage.material = stereoUIMaterial;
            alertBannerImage.color = new Color(0.95f, 0.70f, 0.05f, 0.95f);
            alertBannerText = CreateText(alertBannerObj.transform, "⚠️ ALERT", 16, Color.black, TextAnchor.MiddleCenter);
            alertBannerText.fontStyle = FontStyle.Bold;
            alertBannerObj.SetActive(false);

            GameObject scrollObj = new GameObject("LogScroll");
            scrollObj.transform.SetParent(p, false);
            RectTransform scrollRT = scrollObj.AddComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0.06f, 0.16f);
            scrollRT.anchorMax = new Vector2(0.94f, 0.72f);
            scrollRT.offsetMin = Vector2.zero;
            scrollRT.offsetMax = Vector2.zero;

            Image scBg = scrollObj.AddComponent<Image>();
            scBg.material = stereoUIMaterial;
            scBg.color = new Color(0.03f, 0.04f, 0.06f, 0.95f);
            scrollObj.AddComponent<Mask>().showMaskGraphic = true;

            logScrollRect = scrollObj.AddComponent<ScrollRect>();
            logScrollRect.horizontal = false;
            logScrollRect.vertical = true;

            GameObject contentObj = new GameObject("LogContent");
            contentObj.transform.SetParent(scrollObj.transform, false);
            RectTransform contentRT = contentObj.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 0f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 0f);
            contentRT.offsetMin = new Vector2(12f, 12f);
            contentRT.offsetMax = new Vector2(-12f, -12f);

            logContentText = contentObj.AddComponent<Text>();
            logContentText.material = stereoUIMaterial;
            logContentText.font = uiFont;
            logContentText.fontSize = 14;
            logContentText.lineSpacing = 1.15f;
            logContentText.color = Color.white;
            logContentText.alignment = TextAnchor.LowerLeft;

            var csf = contentObj.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            logScrollRect.content = contentRT;

            GameObject bottomBar = new GameObject("LogBottomBar");
            bottomBar.transform.SetParent(p, false);
            RectTransform bRT = bottomBar.AddComponent<RectTransform>();
            bRT.anchorMin = new Vector2(0.06f, 0.04f);
            bRT.anchorMax = new Vector2(0.94f, 0.13f);
            bRT.offsetMin = Vector2.zero;
            bRT.offsetMax = Vector2.zero;

            var hlg = bottomBar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 16;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            CreateSimpleButton(bottomBar.transform, "🗑 Clear Logs", 38,
                new Color(0.24f, 0.18f, 0.22f, 0.95f), Color.white, ActionClearLogs);

            CreateSimpleButton(bottomBar.transform, "◀ Back to Menu", 38,
                new Color(0.18f, 0.22f, 0.32f, 0.95f), Color.white, ActionBackToMain);
        }

        private void BuildBackgroundPanelContent(Transform p)
        {
            CreateHeader(p, "Scene & Skybox Background", () => ActionCloseMenu(), () => ActionBackToMain());

            GameObject contentObj = new GameObject("BgContent");
            contentObj.transform.SetParent(p, false);
            RectTransform cRT = contentObj.AddComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0.08f, 0.20f);
            cRT.anchorMax = new Vector2(0.92f, 0.80f);
            cRT.offsetMin = Vector2.zero;
            cRT.offsetMax = Vector2.zero;

            var vlg = contentObj.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 16;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var activeCard = CreatePanel(contentObj.transform, "ActiveBgCard");
            activeCard.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 90f);
            activeCard.GetComponent<Image>().color = new Color(0.08f, 0.14f, 0.20f, 0.95f);
            CreateText(activeCard.transform, "Current Skybox Setting:\n★ Solid Black (#000000) [Default Active]", 18,
                new Color(0.0f, 0.85f, 1.0f, 1f), TextAnchor.MiddleCenter);

            var infoText = CreateTextItem(contentObj.transform,
                "Background is locked to pure solid black to ensure maximum contrast and visual focus on robot motion & teleoperation.\n\nCustom environment and skybox choices will be available in future updates.",
                15, new Color(0.75f, 0.75f, 0.75f, 1f));
            infoText.alignment = TextAnchor.MiddleCenter;

            CreateSimpleButton(p, "◀ Back to Menu", 42,
                new Color(0.18f, 0.22f, 0.32f, 0.95f), Color.white, ActionBackToMain);
        }

        // =========================================================================
        // UI Helpers
        // =========================================================================

        private void CreateHeader(Transform parent, string title, Action onClose, Action onBack = null)
        {
            GameObject header = new GameObject("Header");
            header.transform.SetParent(parent, false);

            RectTransform hRT = header.AddComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0f, 0.86f);
            hRT.anchorMax = new Vector2(1f, 1f);
            hRT.offsetMin = Vector2.zero;
            hRT.offsetMax = Vector2.zero;

            Image hImg = header.AddComponent<Image>();
            hImg.material = stereoUIMaterial;
            hImg.color = new Color(0.03f, 0.05f, 0.08f, 0.98f);

            if (onBack != null)
            {
                GameObject backObj = new GameObject("Btn_Back");
                backObj.transform.SetParent(header.transform, false);
                RectTransform bRT = backObj.AddComponent<RectTransform>();
                bRT.anchorMin = new Vector2(0.02f, 0.15f);
                bRT.anchorMax = new Vector2(0.14f, 0.85f);
                bRT.offsetMin = Vector2.zero;
                bRT.offsetMax = Vector2.zero;

                Image bImg = backObj.AddComponent<Image>();
                bImg.material = stereoUIMaterial;
                bImg.color = new Color(0.2f, 0.25f, 0.35f, 0.9f);

                Button bBtn = backObj.AddComponent<Button>();
                bBtn.onClick.AddListener(() => onBack());

                CreateText(backObj.transform, "◀ Back", 14, Color.white, TextAnchor.MiddleCenter);
            }

            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(header.transform, false);
            RectTransform tRT = titleObj.AddComponent<RectTransform>();
            tRT.anchorMin = new Vector2(0.16f, 0f);
            tRT.anchorMax = new Vector2(0.84f, 1f);
            tRT.offsetMin = Vector2.zero;
            tRT.offsetMax = Vector2.zero;

            Text titleText = titleObj.AddComponent<Text>();
            titleText.material = stereoUIMaterial;
            titleText.font = uiFont;
            titleText.fontSize = 20;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = Color.white;
            titleText.text = title;

            // Close button [X] at top-right
            GameObject closeObj = new GameObject("Btn_Close");
            closeObj.transform.SetParent(header.transform, false);
            RectTransform cRT = closeObj.AddComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0.88f, 0.15f);
            cRT.anchorMax = new Vector2(0.98f, 0.85f);
            cRT.offsetMin = Vector2.zero;
            cRT.offsetMax = Vector2.zero;

            Image cImg = closeObj.AddComponent<Image>();
            cImg.material = stereoUIMaterial;
            cImg.color = new Color(0.75f, 0.15f, 0.15f, 0.95f);

            Button cBtn = closeObj.AddComponent<Button>();
            cBtn.onClick.AddListener(() => onClose());

            Text xText = CreateText(closeObj.transform, "✕", 20, Color.white, TextAnchor.MiddleCenter);
            xText.fontStyle = FontStyle.Bold;
        }

        private GameObject CreateSimpleButton(Transform parent, string label, float height, Color bgCol, Color textCol, Action onClick)
        {
            GameObject btnObj = new GameObject("Btn_" + label);
            btnObj.transform.SetParent(parent, false);

            RectTransform rt = btnObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, height);

            Image img = btnObj.AddComponent<Image>();
            img.material = stereoUIMaterial;
            img.color = bgCol;

            Button btn = btnObj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.highlightedColor = bgCol * 1.35f;
            cb.pressedColor = bgCol * 0.75f;
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick());
            allMenuButtons.Add(btn);

            Outline ol = btnObj.AddComponent<Outline>();
            ol.effectColor = new Color(1f, 1f, 1f, 0.15f);
            ol.effectDistance = new Vector2(1f, -1f);

            Text txt = CreateText(btnObj.transform, label, 16, textCol, TextAnchor.MiddleCenter);
            txt.fontStyle = FontStyle.Bold;

            return btnObj;
        }

        private Text CreateText(Transform parent, string content, int fontSize, Color col, TextAnchor anchor)
        {
            GameObject txtObj = new GameObject("Text");
            txtObj.transform.SetParent(parent, false);

            RectTransform rt = txtObj.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Text txt = txtObj.AddComponent<Text>();
            txt.material = stereoUIMaterial;
            txt.font = uiFont;
            txt.fontSize = fontSize;
            txt.alignment = anchor;
            txt.color = col;
            txt.text = content;
            return txt;
        }

        private Text CreateTextItem(Transform parent, string content, int fontSize, Color col)
        {
            GameObject txtObj = new GameObject("TextItem");
            txtObj.transform.SetParent(parent, false);

            RectTransform rt = txtObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 60f);

            Text txt = txtObj.AddComponent<Text>();
            txt.material = stereoUIMaterial;
            txt.font = uiFont;
            txt.fontSize = fontSize;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.color = col;
            txt.text = content;
            return txt;
        }
    }
}
