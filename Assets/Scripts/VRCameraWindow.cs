using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UR3eVR
{
    /// <summary>
    /// VRCameraWindow: Apple Vision Pro OS Style Floating Webcam Monitor for Meta Quest VR
    /// - Styled using JetXR UI Kit For Vision Pro OS (frosted glass, pill buttons, rounded glass window)
    /// - Perfectly centered 16:9 RawImage viewport (eliminates "จอกล้องเบี้ยว" skew / overflow)
    /// - Dual-eye stereo rendering in XR Single Pass Instanced (eliminates single-left-eye UI bug)
    /// - Accurate 3D Quad raycast hitboxes for Switch Cam, Close Button, and Pill Grab Bar
    /// </summary>
    public class VRCameraWindow : MonoBehaviour
    {
        public static VRCameraWindow Instance { get; private set; }

        [Header("Window Configuration")]
        [Tooltip("Width of window in meters in VR")]
        public float windowWidthMeters = 0.68f;

        [Tooltip("Height of window in meters in VR")]
        public float windowHeightMeters = 0.44f;

        [Tooltip("Distance in front of user on spawn")]
        public float defaultDistance = 1.05f;

        [Header("State")]
        public bool isOpen = true;
        public int currentCameraIndex = 0;

        // UI References
        private GameObject canvasObj;
        private Canvas canvas;
        private RawImage videoRawImage;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI subtitleText;
        private Image liveDotImage;
        private GameObject grabBarObj;
        private VRWindowDragger dragger;

        // Vision Pro OS UI Kit Assets
        [Header("Vision Pro OS Asset References")]
        public Material visionWindowMaterial;
        public Material visionElementMaterial;
        public Sprite windowGlassSprite;
        public Sprite roundedRectSprite;
        public Sprite roundedRectHighlightSprite;
        public Sprite crossIconSprite;
        public TMP_FontAsset visionFont;

        // Webcam runtime
        private WebCamTexture webcamTexture;
        public WebCamDevice[] availableDevices { get; private set; }

        // VR References
        private Camera centerEyeCamera;
        private Transform leftControllerAnchor;
        private Transform rightControllerAnchor;

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

            LoadVisionProAssets();
        }

        private void Start()
        {
            FindReferences();
            BuildCameraWindowUI();
            InitializeWebcam();
            SetWindowOpen(true);
        }

        private void LoadVisionProAssets()
        {
            // 1. Vision Window Frosted Glass Material
            if (visionWindowMaterial == null)
            {
#if UNITY_EDITOR
                visionWindowMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/WindowBlurredBackground.mat")
                                    ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/WindowOverlayBackground.mat");
#endif
                if (visionWindowMaterial == null)
                {
                    visionWindowMaterial = Resources.Load<Material>("VisionUI/WindowBlurredBackground")
                                        ?? Resources.Load<Material>("VisionUI/WindowOverlayBackground");
                }
            }

            // 2. Vision Element Platter Material
            if (visionElementMaterial == null)
            {
#if UNITY_EDITOR
                visionElementMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/LightElementBackground.mat")
                                     ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/DarkElementBackground.mat");
#endif
                if (visionElementMaterial == null)
                {
                    visionElementMaterial = Resources.Load<Material>("VisionUI/LightElementBackground")
                                         ?? Resources.Load<Material>("VisionUI/DarkElementBackground");
                }
            }

            // 3. Vision Sprites
            if (windowGlassSprite == null)
            {
#if UNITY_EDITOR
                windowGlassSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Windows/WindowGlass.png")
                                 ?? AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Windows/WindowGlassNoAlpha.png");
#endif
                if (windowGlassSprite == null)
                    windowGlassSprite = Resources.Load<Sprite>("VisionUI/WindowGlass");
            }

            if (roundedRectSprite == null)
            {
#if UNITY_EDITOR
                roundedRectSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Buttons/RoundedRectBackground.png");
#endif
                if (roundedRectSprite == null)
                    roundedRectSprite = Resources.Load<Sprite>("VisionUI/RoundedRectBackground");
            }

            if (roundedRectHighlightSprite == null)
            {
#if UNITY_EDITOR
                roundedRectHighlightSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Buttons/RoundedRectHighlight.png");
#endif
                if (roundedRectHighlightSprite == null)
                    roundedRectHighlightSprite = Resources.Load<Sprite>("VisionUI/RoundedRectHighlight");
            }

            if (crossIconSprite == null)
            {
#if UNITY_EDITOR
                crossIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Icons/Cross Icon.png");
#endif
                if (crossIconSprite == null)
                    crossIconSprite = Resources.Load<Sprite>("VisionUI/Cross Icon");
            }

            // 4. Vision Font
            if (visionFont == null)
            {
#if UNITY_EDITOR
                visionFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/JetXR/UI Kit For Vision Pro OS/Fonts/Resources/Inter-SemiBold SDF.asset")
                          ?? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/JetXR/UI Kit For Vision Pro OS/Fonts/Resources/Inter-Bold SDF.asset");
#endif
                if (visionFont == null)
                    visionFont = Resources.Load<TMP_FontAsset>("Inter-SemiBold SDF") ?? Resources.Load<TMP_FontAsset>("Inter-Bold SDF");
            }
        }

        private void FindReferences()
        {
            if (centerEyeCamera == null)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) centerEyeCamera = cec.GetComponent<Camera>();
                if (centerEyeCamera == null && Camera.main != null) centerEyeCamera = Camera.main;
            }

            if (leftControllerAnchor == null)
            {
                var lca = GameObject.Find("LeftControllerAnchor");
                if (lca != null) leftControllerAnchor = lca.transform;
            }

            if (rightControllerAnchor == null)
            {
                var rca = GameObject.Find("RightControllerAnchor");
                if (rca != null) rightControllerAnchor = rca.transform;
            }
        }

        private void BuildCameraWindowUI()
        {
            // Root WorldSpace Canvas
            canvasObj = new GameObject("VR_Camera_Window_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObj.transform.SetParent(transform, false);

            canvas = canvasObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (centerEyeCamera != null) canvas.worldCamera = centerEyeCamera;

            var scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            RectTransform canvasRT = canvasObj.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(680f, 440f);
            canvasRT.localScale = Vector3.one * (windowWidthMeters / 680f);

            // 1. Vision Pro OS Frosted Glass Window Frame
            Image bgImg = canvasObj.AddComponent<Image>();
            if (windowGlassSprite != null)
            {
                bgImg.sprite = windowGlassSprite;
                bgImg.type = Image.Type.Sliced;
                bgImg.pixelsPerUnitMultiplier = 2f;
                bgImg.material = visionWindowMaterial;
                bgImg.color = new Color(1f, 1f, 1f, 0.92f);
            }
            else
            {
                bgImg.material = null;
                bgImg.color = new Color(0.08f, 0.10f, 0.16f, 0.94f);
            }

            Outline bgOutline = canvasObj.AddComponent<Outline>();
            bgOutline.effectColor = new Color(1.0f, 1.0f, 1.0f, 0.22f);
            bgOutline.effectDistance = new Vector2(1.5f, -1.5f);

            // 2. Vision Pro OS Navigation Bar (Header)
            GameObject header = new GameObject("HeaderBar", typeof(RectTransform));
            header.transform.SetParent(canvasObj.transform, false);
            RectTransform hRT = header.GetComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0f, 0.84f);
            hRT.anchorMax = new Vector2(1f, 1f);
            hRT.offsetMin = Vector2.zero;
            hRT.offsetMax = Vector2.zero;

            // 2.1 Live Indicator Dot (Apple Vision Green Dot)
            GameObject liveDot = new GameObject("LiveDot", typeof(RectTransform), typeof(Image));
            liveDot.transform.SetParent(header.transform, false);
            RectTransform dotRT = liveDot.GetComponent<RectTransform>();
            dotRT.anchorMin = new Vector2(0.035f, 0.50f);
            dotRT.anchorMax = new Vector2(0.035f, 0.50f);
            dotRT.pivot = new Vector2(0.5f, 0.5f);
            dotRT.sizeDelta = new Vector2(10f, 10f);
            liveDotImage = liveDot.GetComponent<Image>();
            liveDotImage.material = null; // Standard UI renderer for both eyes
            liveDotImage.color = new Color(0.19f, 0.82f, 0.35f, 1.0f);

            // 2.2 Title & Subtitle Texts (TMP with Vision font)
            GameObject titleObj = new GameObject("TitleText", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleObj.transform.SetParent(header.transform, false);
            RectTransform titleRT = titleObj.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0.06f, 0.45f);
            titleRT.anchorMax = new Vector2(0.55f, 0.92f);
            titleRT.offsetMin = Vector2.zero;
            titleRT.offsetMax = Vector2.zero;
            titleText = titleObj.GetComponent<TextMeshProUGUI>();
            if (visionFont != null) titleText.font = visionFont;
            titleText.fontSize = 17;
            titleText.fontStyle = FontStyles.Bold;
            titleText.color = Color.white;
            titleText.text = "Camera Feed [Live]";

            GameObject subObj = new GameObject("SubtitleText", typeof(RectTransform), typeof(TextMeshProUGUI));
            subObj.transform.SetParent(header.transform, false);
            RectTransform subRT = subObj.GetComponent<RectTransform>();
            subRT.anchorMin = new Vector2(0.06f, 0.08f);
            subRT.anchorMax = new Vector2(0.55f, 0.46f);
            subRT.offsetMin = Vector2.zero;
            subRT.offsetMax = Vector2.zero;
            subtitleText = subObj.GetComponent<TextMeshProUGUI>();
            if (visionFont != null) subtitleText.font = visionFont;
            subtitleText.fontSize = 12;
            subtitleText.color = new Color(0.6f, 0.75f, 0.9f, 0.85f);
            subtitleText.text = "Detecting video devices...";

            // 2.3 Action Buttons on Header (VisionOS Platter Pill Buttons)
            // Switch Camera Button
            CreateVisionPillButton(header.transform, "SwitchCam", "↻ Switch Cam", 
                new Vector2(0.56f, 0.16f), new Vector2(0.84f, 0.84f), () => CycleNextCamera());

            // Close Button (Circular Vision Pro style button)
            CreateVisionCloseButton(header.transform, "CloseBtn", 
                new Vector2(0.88f, 0.16f), new Vector2(0.965f, 0.84f), () => SetWindowOpen(false));

            // 3. Center Screen: Video Container with RectMask2D (PERFECTLY CENTERED, NO SKEW)
            GameObject videoContainer = new GameObject("VideoContainer", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            videoContainer.transform.SetParent(canvasObj.transform, false);
            RectTransform contRT = videoContainer.GetComponent<RectTransform>();
            contRT.anchorMin = new Vector2(0.035f, 0.13f);
            contRT.anchorMax = new Vector2(0.965f, 0.83f);
            contRT.offsetMin = Vector2.zero;
            contRT.offsetMax = Vector2.zero;
            contRT.pivot = new Vector2(0.5f, 0.5f);

            Image contBg = videoContainer.GetComponent<Image>();
            contBg.material = null;
            contBg.color = new Color(0.03f, 0.04f, 0.06f, 0.98f);

            // Video Feed (RawImage) strictly centered and fitted to 16:9
            GameObject videoObj = new GameObject("VideoDisplay", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            videoObj.transform.SetParent(videoContainer.transform, false);
            RectTransform vidRT = videoObj.GetComponent<RectTransform>();
            vidRT.anchorMin = Vector2.zero;
            vidRT.anchorMax = Vector2.one;
            vidRT.offsetMin = Vector2.zero;
            vidRT.offsetMax = Vector2.zero;
            vidRT.pivot = new Vector2(0.5f, 0.5f);

            videoRawImage = videoObj.GetComponent<RawImage>();
            videoRawImage.color = Color.white;
            // Native CanvasRenderer default material renders to BOTH EYES with 0 vertex displacement
            videoRawImage.material = null;

            var arf = videoObj.GetComponent<AspectRatioFitter>();
            arf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            arf.aspectRatio = 16f / 9f;

            // Video Border Frame
            Outline vidOutline = videoObj.AddComponent<Outline>();
            vidOutline.effectColor = new Color(1f, 1f, 1f, 0.15f);
            vidOutline.effectDistance = new Vector2(1f, -1f);

            // 4. Apple Vision Pro OS Pill Grab Handle Bar
            grabBarObj = new GameObject("GrabHandleBar", typeof(RectTransform), typeof(Image), typeof(VRWindowDragger));
            grabBarObj.transform.SetParent(canvasObj.transform, false);
            RectTransform grabRT = grabBarObj.GetComponent<RectTransform>();
            grabRT.anchorMin = new Vector2(0.32f, 0.025f);
            grabRT.anchorMax = new Vector2(0.68f, 0.095f);
            grabRT.offsetMin = Vector2.zero;
            grabRT.offsetMax = Vector2.zero;
            grabRT.pivot = new Vector2(0.5f, 0.5f);

            Image grabImg = grabBarObj.GetComponent<Image>();
            if (roundedRectSprite != null)
            {
                grabImg.sprite = roundedRectSprite;
                grabImg.type = Image.Type.Sliced;
                grabImg.material = visionElementMaterial;
                grabImg.color = new Color(0.25f, 0.32f, 0.44f, 0.92f);
            }
            else
            {
                grabImg.material = null;
                grabImg.color = new Color(0.18f, 0.22f, 0.32f, 0.90f);
            }

            Outline grabOutline = grabBarObj.AddComponent<Outline>();
            grabOutline.effectColor = new Color(1.0f, 1.0f, 1.0f, 0.25f);
            grabOutline.effectDistance = new Vector2(1f, -1f);

            // Grab Bar Label
            GameObject grabLabelObj = new GameObject("GrabLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            grabLabelObj.transform.SetParent(grabBarObj.transform, false);
            RectTransform glRT = grabLabelObj.GetComponent<RectTransform>();
            glRT.anchorMin = Vector2.zero;
            glRT.anchorMax = Vector2.one;
            glRT.offsetMin = Vector2.zero;
            glRT.offsetMax = Vector2.zero;
            var grabLabel = grabLabelObj.GetComponent<TextMeshProUGUI>();
            if (visionFont != null) grabLabel.font = visionFont;
            grabLabel.fontSize = 12;
            grabLabel.alignment = TextAlignmentOptions.Center;
            grabLabel.color = new Color(0.9f, 0.95f, 1.0f, 0.90f);
            grabLabel.text = "═══  GRAB TO MOVE  ═══";

            // Attach VRWindowDragger
            dragger = grabBarObj.GetComponent<VRWindowDragger>();
            dragger.windowTransform = canvasObj.transform;
            dragger.handleImage = grabImg;

            // Initial Position in Front of User
            RepositionWindowDefault();
        }

        private void CreateVisionPillButton(Transform parent, string name, string label, Vector2 min, Vector2 max, Action onClick)
        {
            GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(parent, false);

            RectTransform rt = btnObj.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);

            Image img = btnObj.GetComponent<Image>();
            if (roundedRectSprite != null)
            {
                img.sprite = roundedRectSprite;
                img.type = Image.Type.Sliced;
                img.material = visionElementMaterial;
                img.color = new Color(0.20f, 0.32f, 0.52f, 0.95f);
            }
            else
            {
                img.material = null;
                img.color = new Color(0.14f, 0.24f, 0.40f, 0.95f);
            }
            img.raycastTarget = true;

            Button btn = btnObj.GetComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            cb.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick());

            Outline ol = btnObj.AddComponent<Outline>();
            ol.effectColor = new Color(1f, 1f, 1f, 0.25f);
            ol.effectDistance = new Vector2(1f, -1f);

            GameObject txtObj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtObj.transform.SetParent(btnObj.transform, false);
            RectTransform tRT = txtObj.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.offsetMin = Vector2.zero;
            tRT.offsetMax = Vector2.zero;
            TextMeshProUGUI txt = txtObj.GetComponent<TextMeshProUGUI>();
            if (visionFont != null) txt.font = visionFont;
            txt.fontSize = 13;
            txt.fontStyle = FontStyles.Bold;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = Color.white;
            txt.raycastTarget = false;
            txt.text = label;

            interactiveButtons.Add(btn);
        }

        private void CreateVisionCloseButton(Transform parent, string name, Vector2 min, Vector2 max, Action onClick)
        {
            GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(parent, false);

            RectTransform rt = btnObj.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);

            Image img = btnObj.GetComponent<Image>();
            if (roundedRectSprite != null)
            {
                img.sprite = roundedRectSprite;
                img.type = Image.Type.Sliced;
                img.material = visionElementMaterial;
                img.color = new Color(0.50f, 0.18f, 0.22f, 0.95f);
            }
            else
            {
                img.material = null;
                img.color = new Color(0.40f, 0.15f, 0.18f, 0.95f);
            }
            img.raycastTarget = true;

            Button btn = btnObj.GetComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            cb.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick());

            Outline ol = btnObj.AddComponent<Outline>();
            ol.effectColor = new Color(1f, 1f, 1f, 0.25f);
            ol.effectDistance = new Vector2(1f, -1f);

            if (crossIconSprite != null)
            {
                GameObject iconObj = new GameObject("CrossIcon", typeof(RectTransform), typeof(Image));
                iconObj.transform.SetParent(btnObj.transform, false);
                RectTransform iRT = iconObj.GetComponent<RectTransform>();
                iRT.anchorMin = new Vector2(0.2f, 0.2f);
                iRT.anchorMax = new Vector2(0.8f, 0.8f);
                iRT.offsetMin = Vector2.zero;
                iRT.offsetMax = Vector2.zero;
                Image iImg = iconObj.GetComponent<Image>();
                iImg.sprite = crossIconSprite;
                iImg.material = null;
                iImg.color = Color.white;
                iImg.raycastTarget = false;
            }
            else
            {
                GameObject txtObj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                txtObj.transform.SetParent(btnObj.transform, false);
                RectTransform tRT = txtObj.GetComponent<RectTransform>();
                tRT.anchorMin = Vector2.zero;
                tRT.anchorMax = Vector2.one;
                tRT.offsetMin = Vector2.zero;
                tRT.offsetMax = Vector2.zero;
                TextMeshProUGUI txt = txtObj.GetComponent<TextMeshProUGUI>();
                if (visionFont != null) txt.font = visionFont;
                txt.fontSize = 15;
                txt.fontStyle = FontStyles.Bold;
                txt.alignment = TextAlignmentOptions.Center;
                txt.color = Color.white;
                txt.raycastTarget = false;
                txt.text = "✕";
            }

            interactiveButtons.Add(btn);
        }

        public void RepositionWindowDefault()
        {
            if (centerEyeCamera == null) FindReferences();
            if (centerEyeCamera == null && Camera.main != null) centerEyeCamera = Camera.main;

            if (canvasObj == null) return;

            if (centerEyeCamera != null)
            {
                Vector3 headPos = centerEyeCamera.transform.position;
                Vector3 headFwd = centerEyeCamera.transform.forward;
                headFwd.y = 0;
                if (headFwd.sqrMagnitude < 0.01f) headFwd = Vector3.forward;
                headFwd.Normalize();

                Vector3 targetPos = headPos + headFwd * defaultDistance + Vector3.up * 0.12f;
                canvasObj.transform.position = targetPos;
                canvasObj.transform.rotation = Quaternion.LookRotation(headFwd, Vector3.up);
            }
            else
            {
                canvasObj.transform.localPosition = new Vector3(0f, 1.25f, defaultDistance);
                canvasObj.transform.localRotation = Quaternion.identity;
            }

            if (canvas != null && centerEyeCamera != null)
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
                if (liveDotImage != null) liveDotImage.color = new Color(1f, 0.7f, 0.1f, 1f);
                return;
            }

            int selected = 0;
            // Prefer real USB webcams over Quest virtual cameras
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
                }

                // Clean defaultGraphicMaterial to prevent contamination of other UI buttons
                if (Graphic.defaultGraphicMaterial.mainTexture != null)
                {
                    Graphic.defaultGraphicMaterial.mainTexture = null;
                }

                webcamTexture.Play();

                if (titleText != null) titleText.text = $"Camera [{currentCameraIndex + 1}/{availableDevices.Length}]";
                if (subtitleText != null) subtitleText.text = $"{devName} (Streaming)";
                if (liveDotImage != null) liveDotImage.color = new Color(0.19f, 0.82f, 0.35f, 1.0f);
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

            // Single Pass Instanced: Continuously guarantee CenterEyeCamera on World Space Canvas
            if (canvas != null && centerEyeCamera != null)
            {
                if (canvas.worldCamera != centerEyeCamera)
                {
                    canvas.worldCamera = centerEyeCamera;
                }
            }
            else if (canvas != null && canvas.worldCamera == null)
            {
                FindReferences();
                if (centerEyeCamera != null) canvas.worldCamera = centerEyeCamera;
            }

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
                liveDotImage.color = new Color(0.19f, 0.82f, 0.35f, alpha);
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
