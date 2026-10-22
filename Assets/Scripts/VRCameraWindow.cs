using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UR3eVR
{
    /// <summary>
    /// VRCameraWindow: หน้าต่างแสดงภาพกล้อง Webcam แบบลอยตัวสไตล์ Meta Horizon OS
    /// - เปิดแสดงผลอัตโนมัติตั้งแต่เริ่มระบบ (Auto-start on startup)
    /// - แสดงภาพสตรีม 16:9 คมชัด พร้อม Badge สด 🟢 LIVE, ชื่อกล้อง, และ Resolution
    /// - มีแถบ Grab Bar ด้านล่างสำหรับหยิบจับลากย้ายตำแหน่งใน 3D Space (รองรับทั้งรีโมทและมือเปล่า)
    /// - ปุ่มควบคุม (Switch Cam, Close) กดได้ทั้งยิงเลเซอร์จากรีโมท และใช้นิ้วจิ้มตรงๆ จากมือ (Direct Poke)
    /// </summary>
    public class VRCameraWindow : MonoBehaviour
    {
        public static VRCameraWindow Instance { get; private set; }

        [Header("Camera Configuration")]
        [Tooltip("ความกว้างของหน้าจอใน VR (เมตร)")]
        public float windowWidthMeters = 0.65f;

        [Tooltip("ความสูงของหน้าจอใน VR (เมตร)")]
        public float windowHeightMeters = 0.44f;

        [Tooltip("ระยะห่างเริ่มต้นจากสายตา (เมตร)")]
        public float defaultDistance = 1.05f;

        [Header("State")]
        public bool isOpen = true;
        public int currentCameraIndex = 0;

        // UI References
        private GameObject canvasObj;
        private Canvas canvas;
        private RawImage videoRawImage;
        private Text titleText;
        private Text subtitleText;
        private Image liveDotImage;
        private GameObject grabBarObj;
        private VRWindowDragger dragger;

        // Webcam runtime
        private WebCamTexture webcamTexture;
        private WebCamDevice[] availableDevices;
        private Font uiFont;

        // References
        private Camera centerEyeCamera;
        private Transform rightControllerAnchor;
        private PinchDetector pinchDetector;

        // Poke Debounce
        private float lastPokeTime = 0f;
        private bool hasPositionedDefault = false;
        private List<Button> interactiveButtons = new List<Button>();

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
            BuildCameraWindowUI();
            InitializeWebcam();

            // เริ่มต้นเปิดแสดงผลทันทีตามที่ผู้ใช้ต้องการ
            SetWindowOpen(true);
        }

        private void FindReferences()
        {
            if (centerEyeCamera == null)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) centerEyeCamera = cec.GetComponent<Camera>();
                if (centerEyeCamera == null && Camera.main != null) centerEyeCamera = Camera.main;
            }

            if (rightControllerAnchor == null)
            {
                var rca = GameObject.Find("RightControllerAnchor");
                if (rca != null) rightControllerAnchor = rca.transform;
            }

            if (pinchDetector == null) pinchDetector = FindAnyObjectByType<PinchDetector>();
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

        private void BuildCameraWindowUI()
        {
            canvasObj = new GameObject("VR_Camera_Window_Canvas");
            canvasObj.transform.SetParent(transform, false);

            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (centerEyeCamera != null) canvas.worldCamera = centerEyeCamera;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            canvasObj.AddComponent<GraphicRaycaster>();

            RectTransform canvasRT = canvasObj.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(650f, 440f);
            canvasRT.localScale = Vector3.one * (windowWidthMeters / 650f);

            // 1. Frosted Obsidian Glass Background Card
            Image bgImg = canvasObj.AddComponent<Image>();
            bgImg.color = new Color(0.07f, 0.08f, 0.12f, 0.92f);

            Outline bgOutline = canvasObj.AddComponent<Outline>();
            bgOutline.effectColor = new Color(0.0f, 0.85f, 1.0f, 0.40f);
            bgOutline.effectDistance = new Vector2(2f, -2f);

            // 2. Header Bar
            GameObject header = new GameObject("HeaderBar");
            header.transform.SetParent(canvasObj.transform, false);
            RectTransform hRT = header.AddComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0f, 0.84f);
            hRT.anchorMax = new Vector2(1f, 1f);
            hRT.offsetMin = Vector2.zero;
            hRT.offsetMax = Vector2.zero;

            Image hImg = header.AddComponent<Image>();
            hImg.color = new Color(0.04f, 0.05f, 0.08f, 0.98f);

            // 2.1 Live Indicator Dot
            GameObject liveDot = new GameObject("LiveDot");
            liveDot.transform.SetParent(header.transform, false);
            RectTransform dotRT = liveDot.AddComponent<RectTransform>();
            dotRT.anchorMin = new Vector2(0.03f, 0.35f);
            dotRT.anchorMax = new Vector2(0.03f, 0.35f);
            dotRT.sizeDelta = new Vector2(12f, 12f);
            liveDotImage = liveDot.AddComponent<Image>();
            liveDotImage.color = new Color(0.1f, 1.0f, 0.4f, 1.0f);

            // 2.2 Title & Subtitle Texts
            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(header.transform, false);
            RectTransform titleRT = titleObj.AddComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0.06f, 0.45f);
            titleRT.anchorMax = new Vector2(0.55f, 0.90f);
            titleRT.offsetMin = Vector2.zero;
            titleRT.offsetMax = Vector2.zero;
            titleText = titleObj.AddComponent<Text>();
            titleText.font = uiFont;
            titleText.fontSize = 17;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = Color.white;
            titleText.text = "Camera Feed [Live]";

            GameObject subObj = new GameObject("SubtitleText");
            subObj.transform.SetParent(header.transform, false);
            RectTransform subRT = subObj.AddComponent<RectTransform>();
            subRT.anchorMin = new Vector2(0.06f, 0.10f);
            subRT.anchorMax = new Vector2(0.55f, 0.45f);
            subRT.offsetMin = Vector2.zero;
            subRT.offsetMax = Vector2.zero;
            subtitleText = subObj.AddComponent<Text>();
            subtitleText.font = uiFont;
            subtitleText.fontSize = 12;
            subtitleText.color = new Color(0.0f, 0.85f, 1.0f, 0.85f);
            subtitleText.text = "Detecting devices...";

            // 2.3 Action Buttons on Header (Switch Camera, Close)
            CreateHeaderButton(header.transform, "SwitchCam", "\u21bb Switch Cam", new Vector2(0.58f, 0.18f), new Vector2(0.82f, 0.82f),
                new Color(0.12f, 0.22f, 0.38f, 0.95f), () => CycleNextCamera());

            CreateHeaderButton(header.transform, "CloseBtn", "\u2715", new Vector2(0.86f, 0.18f), new Vector2(0.97f, 0.82f),
                new Color(0.40f, 0.15f, 0.18f, 0.95f), () => SetWindowOpen(false));

            // 3. Center Screen: Video Feed (RawImage)
            GameObject videoObj = new GameObject("VideoDisplay");
            videoObj.transform.SetParent(canvasObj.transform, false);
            RectTransform vidRT = videoObj.AddComponent<RectTransform>();
            vidRT.anchorMin = new Vector2(0.03f, 0.14f);
            vidRT.anchorMax = new Vector2(0.97f, 0.82f);
            vidRT.offsetMin = Vector2.zero;
            vidRT.offsetMax = Vector2.zero;

            videoRawImage = videoObj.AddComponent<RawImage>();
            videoRawImage.color = Color.white;

            Shader stShader = Shader.Find("UR3eVR/StereoUITexture");
            if (stShader != null)
            {
                Material vidMat = new Material(stShader);
                vidMat.enableInstancing = true;
                videoRawImage.material = vidMat;
            }

            // Video Border Frame
            Outline vidOutline = videoObj.AddComponent<Outline>();
            vidOutline.effectColor = new Color(0.15f, 0.25f, 0.38f, 0.70f);
            vidOutline.effectDistance = new Vector2(1f, -1f);

            // 4. Meta Horizon OS Bottom Pill Grab Handle Bar
            grabBarObj = new GameObject("GrabHandleBar");
            grabBarObj.transform.SetParent(canvasObj.transform, false);
            RectTransform grabRT = grabBarObj.AddComponent<RectTransform>();
            grabRT.anchorMin = new Vector2(0.25f, 0.02f);
            grabRT.anchorMax = new Vector2(0.75f, 0.10f);
            grabRT.offsetMin = Vector2.zero;
            grabRT.offsetMax = Vector2.zero;

            Image grabImg = grabBarObj.AddComponent<Image>();
            grabImg.color = new Color(0.18f, 0.22f, 0.32f, 0.90f);

            Outline grabOutline = grabBarObj.AddComponent<Outline>();
            grabOutline.effectColor = new Color(0.0f, 0.85f, 1.0f, 0.30f);
            grabOutline.effectDistance = new Vector2(1f, -1f);

            // Grab Bar Label
            GameObject grabLabelObj = new GameObject("GrabLabel");
            grabLabelObj.transform.SetParent(grabBarObj.transform, false);
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

            // Attach VRWindowDragger
            dragger = grabBarObj.AddComponent<VRWindowDragger>();
            dragger.windowTransform = canvasObj.transform;
            dragger.handleImage = grabImg;

            // Initial Position in Front of User
            RepositionWindowDefault();
        }

        private void CreateHeaderButton(Transform parent, string name, string label, Vector2 min, Vector2 max, Color bgCol, Action onClick)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);

            RectTransform rt = btnObj.AddComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = btnObj.AddComponent<Image>();
            img.color = bgCol;

            Button btn = btnObj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.highlightedColor = bgCol * 1.35f;
            cb.pressedColor = bgCol * 0.75f;
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick());

            Outline ol = btnObj.AddComponent<Outline>();
            ol.effectColor = new Color(1f, 1f, 1f, 0.20f);
            ol.effectDistance = new Vector2(1f, -1f);

            GameObject txtObj = new GameObject("Text");
            txtObj.transform.SetParent(btnObj.transform, false);
            RectTransform tRT = txtObj.AddComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.offsetMin = Vector2.zero;
            tRT.offsetMax = Vector2.zero;
            Text txt = txtObj.AddComponent<Text>();
            txt.font = uiFont;
            txt.fontSize = 14;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.text = label;

            interactiveButtons.Add(btn);
        }

        public void RepositionWindowDefault()
        {
            if (centerEyeCamera == null) FindReferences();
            Transform refT = centerEyeCamera != null ? centerEyeCamera.transform : Camera.main?.transform;
            if (refT == null || canvasObj == null) return;

            Vector3 fwd = Vector3.ProjectOnPlane(refT.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = refT.forward;

            // วางลอยด้านหน้าทางขวาเล็กน้อยที่ระดับสายตา ไม่บดบังพื้นที่ทำงานตรงกลาง
            canvasObj.transform.position = refT.position + fwd * defaultDistance + refT.right * 0.42f + Vector3.up * 0.10f;
            canvasObj.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

            if (canvas != null && canvas.worldCamera == null && centerEyeCamera != null)
            {
                canvas.worldCamera = centerEyeCamera;
            }
        }

        private void InitializeWebcam()
        {
            availableDevices = WebCamTexture.devices;
            if (availableDevices == null || availableDevices.Length == 0)
            {
                if (subtitleText != null) subtitleText.text = "No Physical Webcam Detected";
                if (liveDotImage != null) liveDotImage.color = new Color(1f, 0.7f, 0.1f, 1f); // Yellow standby
                return;
            }

            int selected = 0;
            // เลือกกล้องที่ชื่อไม่ใช่อุปกรณ์เสมือน (Quest Virtual)
            for (int i = 0; i < availableDevices.Length; i++)
            {
                string n = availableDevices[i].name.ToLower();
                if (!n.Contains("quest") && !n.Contains("virtual"))
                {
                    selected = i;
                    break;
                }
            }
            OpenWebcamIndex(selected);
        }

        public void OpenWebcamIndex(int idx)
        {
            if (availableDevices == null || availableDevices.Length == 0)
                availableDevices = WebCamTexture.devices;

            if (availableDevices == null || availableDevices.Length == 0) return;

            currentCameraIndex = Mathf.Clamp(idx, 0, availableDevices.Length - 1);
            string devName = availableDevices[currentCameraIndex].name;

            StopCurrentStream();

            try
            {
                webcamTexture = new WebCamTexture(devName, 1280, 720, 30);
                if (videoRawImage != null)
                {
                    videoRawImage.texture = webcamTexture;
                    if (videoRawImage.material != null)
                    {
                        videoRawImage.material.mainTexture = webcamTexture;
                    }
                }
                webcamTexture.Play();

                if (titleText != null) titleText.text = $"Camera [{currentCameraIndex + 1}/{availableDevices.Length}]";
                if (subtitleText != null) subtitleText.text = $"{devName} (Streaming)";
                if (liveDotImage != null) liveDotImage.color = new Color(0.1f, 1.0f, 0.4f, 1.0f);
            }
            catch (Exception ex)
            {
                if (subtitleText != null) subtitleText.text = $"Error: {ex.Message}";
                if (liveDotImage != null) liveDotImage.color = Color.red;
            }
        }

        public void CycleNextCamera()
        {
            if (availableDevices == null || availableDevices.Length <= 1) return;
            int next = (currentCameraIndex + 1) % availableDevices.Length;
            OpenWebcamIndex(next);
        }

        private void StopCurrentStream()
        {
            if (webcamTexture != null)
            {
                if (webcamTexture.isPlaying) webcamTexture.Stop();
                Destroy(webcamTexture);
                webcamTexture = null;
            }
        }

        private void Update()
        {
            if (!isOpen) return;

            // ตรวจสอบและผูก CenterEyeCamera กับ World Space Canvas ต่อเนื่อง (แก้ไขภาพไม่ขึ้นตาขวา)
            if (canvas != null && (canvas.worldCamera == null || !canvas.worldCamera.gameObject.activeInHierarchy))
            {
                FindReferences();
                if (centerEyeCamera != null)
                {
                    canvas.worldCamera = centerEyeCamera;
                }
            }

            // จัดตำแหน่งลอยหน้าผู้ใช้เมื่อ VR Camera เริ่มทำงาน
            if (!hasPositionedDefault)
            {
                FindReferences();
                if (centerEyeCamera != null || Camera.main != null)
                {
                    RepositionWindowDefault();
                    hasPositionedDefault = true;
                }
            }

            // Live Pulsing Green Dot
            if (liveDotImage != null && webcamTexture != null && webcamTexture.isPlaying)
            {
                float alpha = 0.5f + Mathf.PingPong(Time.time * 2f, 0.5f);
                liveDotImage.color = new Color(0.1f, 1.0f, 0.4f, alpha);
            }

            // Direct Hand Poke Interaction (ตรวจจับการใช้นิ้วจิ้มปุ่มโดยตรง)
            CheckDirectHandPoke();
        }

        private void CheckDirectHandPoke()
        {
            if (pinchDetector == null || pinchDetector.indexTipTransform == null) return;
            if (Time.time - lastPokeTime < 0.35f) return;

            Vector3 fingerPos = pinchDetector.indexTipTransform.position;

            foreach (var btn in interactiveButtons)
            {
                if (btn == null || !btn.gameObject.activeInHierarchy || !btn.interactable) continue;

                RectTransform rt = btn.GetComponent<RectTransform>();
                if (rt == null) continue;

                Vector3 localPos = rt.InverseTransformPoint(fingerPos);
                Rect r = rt.rect;

                // ตรวจว่าปลายนิ้วอยู่ในกรอบปุ่ม และระยะลึกใกล้ระนาบปุ่ม (< 2.5 ซม.)
                if (r.Contains(new Vector2(localPos.x, localPos.y)) && Mathf.Abs(localPos.z) <= 0.025f)
                {
                    btn.onClick.Invoke();
                    lastPokeTime = Time.time;

                    // Haptic feedback
                    try
                    {
                        OVRInput.SetControllerVibration(0.3f, 0.3f, OVRInput.Controller.RTouch);
                    }
                    catch { }

                    break;
                }
            }
        }

        public void SetWindowOpen(bool open)
        {
            isOpen = open;
            if (canvasObj != null) canvasObj.SetActive(open);
            if (open)
            {
                if (webcamTexture != null && !webcamTexture.isPlaying) webcamTexture.Play();
            }
            else
            {
                if (webcamTexture != null && webcamTexture.isPlaying) webcamTexture.Pause();
            }
        }

        public void ToggleWindow()
        {
            SetWindowOpen(!isOpen);
        }

        private void OnDestroy()
        {
            StopCurrentStream();
        }
    }
}
