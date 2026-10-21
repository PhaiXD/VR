using System;
using UnityEngine;
using UnityEngine.UI;

namespace UR3eVR
{
    /// <summary>
    /// VRWindowDragger: ระบบหยิบจับลากย้ายหน้าต่าง UI ในพื้นที่ 3D (Meta Horizon OS Style)
    /// - รองรับการหยิบด้วย Remote: เล็ง Laser หรือนำรีโมทมาใกล้บาร์ แล้วกดปุ่ม Grip / Index Trigger ค้างเพื่อลาก
    /// - รองรับการหยิบด้วยมือเปล่า (Hand Tracking): เอามือมาใกล้บาร์แล้วจีบมือ (Pinch) ค้างเพื่อลาก
    /// - ปล่อยปุ่มหรือปล่อยมือเพื่อวางหน้าต่างไว้ในตำแหน่ง 3D นั้นทันที
    /// </summary>
    public class VRWindowDragger : MonoBehaviour
    {
        [Header("Targets")]
        [Tooltip("Transform ของหน้าต่างที่ต้องการให้เคลื่อนที่ (ถ้าไม่ระบุ จะใช้ Parent)")]
        public Transform windowTransform;

        [Tooltip("Image ของ Grab Bar สำหรับเปลี่ยนสีสถานะ (Normal, Hover, Grabbed)")]
        public Image handleImage;

        [Tooltip("ระยะตรวจจับการจับด้วยมือหรือรีโมท (เมตร)")]
        public float grabDistance = 0.22f;

        [Header("Styling Colors")]
        public Color normalColor = new Color(0.20f, 0.24f, 0.35f, 0.85f);
        public Color hoverColor = new Color(0.0f, 0.85f, 1.0f, 0.95f);
        public Color grabbedColor = new Color(0.05f, 0.95f, 0.55f, 1.0f);

        [Header("State (Read Only)")]
        public bool isGrabbed = false;
        public bool isHovered = false;

        private Transform grabberTransform = null;
        private Vector3 grabOffset;
        private Quaternion grabRotOffset;
        private bool isHandGrab = false;

        private PinchDetector pinchDetector;
        private OVRHand rightHand;
        private Transform rightControllerAnchor;
        private Transform headTransform;

        private void Start()
        {
            if (windowTransform == null)
            {
                windowTransform = transform.parent != null ? transform.parent : transform;
            }
            FindReferences();
        }

        private void FindReferences()
        {
            if (pinchDetector == null) pinchDetector = FindAnyObjectByType<PinchDetector>();

            var hands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
            foreach (var h in hands)
            {
                if (h.transform.name.ToLower().Contains("right")) rightHand = h;
            }

            if (rightControllerAnchor == null)
            {
                var rca = GameObject.Find("RightControllerAnchor");
                if (rca != null) rightControllerAnchor = rca.transform;
            }

            if (headTransform == null)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) headTransform = cec.transform;
                else if (Camera.main != null) headTransform = Camera.main.transform;
            }
        }

        private void Update()
        {
            if (rightControllerAnchor == null || pinchDetector == null) FindReferences();

            if (isGrabbed)
            {
                UpdateDragging();
            }
            else
            {
                CheckGrabInitiation();
            }

            UpdateHandleVisual();
        }

        private void CheckGrabInitiation()
        {
            Vector3 handlePos = transform.position;
            bool hovering = false;

            // 1. ตรวจสอบการจับจาก Remote Controller ขวา
            bool controllerTracked = rightControllerAnchor != null && OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch);
            if (controllerTracked)
            {
                float distToRemote = Vector3.Distance(handlePos, rightControllerAnchor.position);
                bool remoteNear = distToRemote <= grabDistance;

                if (remoteNear || CheckLaserPointingHandle())
                {
                    hovering = true;

                    bool gripPressed = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch) ||
                                      OVRInput.Get(OVRInput.RawButton.RHandTrigger) ||
                                      OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);

                    if (gripPressed)
                    {
                        StartGrab(rightControllerAnchor, false);
                        return;
                    }
                }
            }

            // 2. ตรวจสอบการจับจากมือเปล่า (Hand Tracking)
            if (rightHand != null && rightHand.IsTracked)
            {
                Vector3 handPos = pinchDetector != null && pinchDetector.indexTipTransform != null
                    ? pinchDetector.indexTipTransform.position
                    : rightHand.transform.position;

                float distToHand = Vector3.Distance(handlePos, handPos);
                if (distToHand <= grabDistance)
                {
                    hovering = true;

                    bool isPinching = (pinchDetector != null && pinchDetector.normalizedPinch > 0.65f) ||
                                      rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);

                    if (isPinching)
                    {
                        Transform handT = pinchDetector != null && pinchDetector.indexTipTransform != null
                            ? pinchDetector.indexTipTransform
                            : rightHand.transform;
                        StartGrab(handT, true);
                        return;
                    }
                }
            }

            isHovered = hovering;
        }

        private bool CheckLaserPointingHandle()
        {
            if (rightControllerAnchor == null) return false;

            Ray ray = new Ray(rightControllerAnchor.position, rightControllerAnchor.forward);
            Plane p = new Plane(-transform.forward, transform.position);

            if (p.Raycast(ray, out float enter) && enter <= 4.0f && enter > 0f)
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                Vector3 localHit = transform.InverseTransformPoint(hitPoint);
                RectTransform rt = GetComponent<RectTransform>();
                if (rt != null)
                {
                    Rect r = rt.rect;
                    if (Mathf.Abs(localHit.x) <= r.width * 0.5f && Mathf.Abs(localHit.y) <= r.height * 0.5f)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void StartGrab(Transform grabber, bool byHand)
        {
            isGrabbed = true;
            isHandGrab = byHand;
            grabberTransform = grabber;

            grabOffset = windowTransform.position - grabber.position;
            grabRotOffset = Quaternion.Inverse(grabber.rotation) * windowTransform.rotation;

            TriggerHaptic(0.25f, 0.08f);
        }

        private void UpdateDragging()
        {
            if (grabberTransform == null)
            {
                EndGrab();
                return;
            }

            if (isHandGrab)
            {
                bool stillPinching = (pinchDetector != null && pinchDetector.normalizedPinch > 0.35f) ||
                                     (rightHand != null && rightHand.IsTracked && rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index));
                if (!stillPinching)
                {
                    EndGrab();
                    return;
                }
            }
            else
            {
                bool stillHolding = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch) ||
                                    OVRInput.Get(OVRInput.RawButton.RHandTrigger) ||
                                    OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
                                    Input.GetMouseButton(0);
                if (!stillHolding)
                {
                    EndGrab();
                    return;
                }
            }

            Vector3 targetPos = grabberTransform.position + grabberTransform.rotation * (Quaternion.Inverse(grabberTransform.rotation) * grabOffset);
            Quaternion targetRot = grabberTransform.rotation * grabRotOffset;

            // หันหน้าต่างเข้าหาผู้ใช้ในแนวแกนราบ (Billboard Yaw)
            if (headTransform != null)
            {
                Vector3 toHead = headTransform.position - windowTransform.position;
                toHead.y = 0;
                if (toHead.sqrMagnitude > 0.01f)
                {
                    targetRot = Quaternion.LookRotation(-toHead.normalized, Vector3.up);
                }
            }

            windowTransform.position = Vector3.Lerp(windowTransform.position, targetPos, Time.deltaTime * 20f);
            windowTransform.rotation = Quaternion.Slerp(windowTransform.rotation, targetRot, Time.deltaTime * 20f);
        }

        private void EndGrab()
        {
            isGrabbed = false;
            grabberTransform = null;
            TriggerHaptic(0.15f, 0.05f);
        }

        private void UpdateHandleVisual()
        {
            if (handleImage == null) handleImage = GetComponent<Image>();
            if (handleImage == null) return;

            Color targetColor = normalColor;
            if (isGrabbed) targetColor = grabbedColor;
            else if (isHovered) targetColor = hoverColor;

            handleImage.color = Color.Lerp(handleImage.color, targetColor, Time.deltaTime * 15f);
        }

        private void TriggerHaptic(float amp, float dur)
        {
            try
            {
                OVRInput.SetControllerVibration(amp, amp, OVRInput.Controller.RTouch);
                OVRInput.SetControllerVibration(amp, amp, OVRInput.Controller.LTouch);
            }
            catch { }
        }
    }
}
