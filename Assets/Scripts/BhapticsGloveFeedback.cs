using System;
using UnityEngine;
using Bhaptics.SDK2;

namespace UR3eVR
{
    /// <summary>
    /// BhapticsGloveFeedback: รับค่าแรงบีบ Gripper (Fz จาก UR3e TCP Force Sensor ผ่าน UKF) จาก GripperBridge
    /// แล้วสั่งให้ถุงมือ bHaptics Glove สั่นตอบสนองตามแรงจริงที่วัดได้แบบ Real-time
    /// พร้อมระบบ Hysteresis และ Smooth Attack/Release Envelope แก้ไขปัญหาการสั่นขาดช่วง (Intermittent Stutter)
    /// </summary>
    public class BhapticsGloveFeedback : MonoBehaviour
    {
        [Header("Target Device")]
        [Tooltip("เลือกถุงมือข้างที่จะให้สั่น (ค่าเริ่มต้นคือมือขวา GloveR)")]
        public PositionType glovePosition = PositionType.GloveR;

        [Header("References")]
        [Tooltip("GripperBridge reference (auto-detected if null)")]
        public GripperBridge gripperBridge;

        [Header("Hysteresis Thresholds (แก้ไขการสั่นขาดช่วง)")]
        [Tooltip("แรงขั้นต่ำ (N) ที่จะเริ่มให้ถุงมือสั่น (Threshold เริ่มสั่นเมื่อบีบ)")]
        [Range(0.2f, 4.0f)]
        public float startForceN = 0.8f;

        [Tooltip("แรงขั้นต่ำ (N) ที่จะหยุดสั่นเมื่อปล่อย (Hysteresis ป้องกันการสั่นติดๆ ดับๆ)")]
        [Range(0.05f, 2.0f)]
        public float stopForceN = 0.3f;

        [Tooltip("แรงสูงสุด (N) ที่จะให้ถุงมือสั่นเต็ม 100%")]
        [Range(5.0f, 50.0f)]
        public float maxForceN = 25.0f;

        [Tooltip("ความแรงการสั่นต่ำสุด (%) เมื่อเริ่มสัมผัสวัตถุ")]
        [Range(5, 50)]
        public int minIntensity = 15;

        [Tooltip("ความแรงการสั่นสูงสุด (%) เมื่อบีบเต็มที่")]
        [Range(50, 100)]
        public int maxIntensity = 100;

        [Header("Smoothing Envelope (ความเนียนต่อเนื่อง)")]
        [Tooltip("ความเร็วในการเพิ่มแรงสั่นเมื่อเริ่มสัมผัส (Attack speed)")]
        [Range(100f, 600f)]
        public float attackSpeed = 350f;

        [Tooltip("ความเร็วในการชะลอแรงสั่นเมื่อคลาย (Release speed ช่วยพยุงไม่ให้ขาดช่วง)")]
        [Range(50f, 300f)]
        public float releaseSpeed = 120f;

        [Header("Active Motors on Glove (เลือกนิ้วที่ต้องการให้สั่น)")]
        [Tooltip("นิ้วโป้ง (Thumb Tip) - นิ้วหลักในการบีบ")]
        public bool vibrateThumb = true;

        [Tooltip("นิ้วชี้ (Index Tip) - นิ้วหลักในการบีบ")]
        public bool vibrateIndex = true;

        [Tooltip("นิ้วกลาง (Middle Tip)")]
        public bool vibrateMiddle = false;

        [Tooltip("นิ้วนาง (Ring Tip)")]
        public bool vibrateRing = false;

        [Tooltip("นิ้วก้อย (Pinky Tip)")]
        public bool vibratePinky = false;

        [Tooltip("ฝ่ามือ (Palm)")]
        public bool vibratePalm = false;

        [Header("Haptic Timing (Continuous Burst)")]
        [Tooltip("ระยะเวลาการสั่นต่อรอบ (ms) ตั้งให้เหลื่อมกับ updateRateHz เพื่อให้มอเตอร์สั่นต่อเนื่อง ไร้รอยต่อ")]
        [Range(50, 250)]
        public int pulseDurationMs = 120;

        [Tooltip("ความถี่ในการส่งสัญญาณ haptic (Hz)")]
        [Range(15, 50)]
        public int updateRateHz = 25;

        [Header("Telemetry (Read Only)")]
        public float currentGripForceN = 0f;
        public int currentMotorIntensity = 0;
        public bool isVibrating = false;
        public bool isHapticActive = false;

        [Header("Testing")]
        [Tooltip("ติ๊กถูกเพื่อทดสอบสั่นถุงมือทันทีใน Editor")]
        public bool testVibration = false;
        [Range(0, 100)]
        public int testIntensity = 50;

        private float nextSendTime = 0f;
        private bool wasVibrating = false;
        private float currentSmoothedIntensity = 0f;
        private readonly int[] motorValues = new int[8];

        private void Awake()
        {
            if (gripperBridge == null)
            {
                gripperBridge = GetComponent<GripperBridge>();
                if (gripperBridge == null) gripperBridge = FindAnyObjectByType<GripperBridge>();
            }

            EnsureBhapticsInitialized();
        }

        private void EnsureBhapticsInitialized()
        {
            try
            {
                if (!BhapticsLibrary.IsBhapticsAvailable(true))
                {
                    BhapticsLibrary.Initialize("ur3e_vr_bridge", "ur3e_vr_bridge", "{}");
                    BhapticsLibrary.IsBhapticsAvailableForce(true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BhapticsGloveFeedback] Init note: " + ex.Message);
            }
        }

        private void Update()
        {
            EnsureBhapticsInitialized();

            // 1. อ่านค่าแรงจาก GripperBridge (ส่งมาจาก Python: Fz ผ่าน UKF กรองแล้ว)
            if (gripperBridge != null)
            {
                currentGripForceN = gripperBridge.actualGripForce;
            }

            // 2. ตรวจสอบการอ้านิ้ว: ถ้าอ้านิ้วระดับ <= 35 จาก 255 ให้ตัดการสั่นเป็น 0 ทันที ป้องกันสั่นค้าง
            bool isHandOpen = false;
            if (gripperBridge != null)
            {
                if (gripperBridge.actualGripperPos <= 35) isHandOpen = true;
                if (gripperBridge.pinchDetector != null && gripperBridge.pinchDetector.isTracked && gripperBridge.pinchDetector.gripperPos <= 35)
                {
                    isHandOpen = true;
                }
            }

            // คำนวณความแรงการสั่น (Intensity: 0 - 100) ด้วย Dual-Threshold Hysteresis
            int targetIntensity = 0;

            if (testVibration)
            {
                targetIntensity = testIntensity;
                isHapticActive = true;
            }
            else if (isHandOpen)
            {
                // อ้านิ้วแล้ว -> ปล่อยวัตถุแล้ว ดับการสั่นทันที ไร้การสั่นค้าง
                isHapticActive = false;
                targetIntensity = 0;
                currentSmoothedIntensity = 0f;
            }
            else
            {
                // Schmitt Trigger Hysteresis:
                // เมื่อยังไม่สั่น -> ต้องถึง startForceN ก่อนจึงจะเริ่ม
                // เมื่อกำลังสั่น -> แรงต้องลดลงต่ำกว่า stopForceN จึงจะหยุด
                if (!isHapticActive)
                {
                    if (currentGripForceN >= startForceN)
                    {
                        isHapticActive = true;
                    }
                }
                else
                {
                    if (currentGripForceN < stopForceN)
                    {
                        isHapticActive = false;
                    }
                }

                if (isHapticActive && currentGripForceN >= stopForceN)
                {
                    float t = Mathf.Clamp01((currentGripForceN - stopForceN) / Mathf.Max(0.1f, maxForceN - stopForceN));
                    targetIntensity = Mathf.RoundToInt(Mathf.Lerp(minIntensity, maxIntensity, t));
                }
            }

            // 3. Smooth Envelope (Attack / Release Filter):
            // ป้องกันการสั่นขาดช่วง (Intermittent Stutter) แม้สัญญาณเซนเซอร์จะกระเพื่อมชั่วคราว
            if (targetIntensity > currentSmoothedIntensity)
            {
                // Attack: ตอบสนองฉับไวทันทีเมื่อสัมผัส
                currentSmoothedIntensity = Mathf.MoveTowards(currentSmoothedIntensity, targetIntensity, attackSpeed * Time.deltaTime);
            }
            else
            {
                // Release: ชะลอลงอย่างนุ่มนวล พยุงการสั่นให้ต่อเนื่องไม่สะดุด
                currentSmoothedIntensity = Mathf.MoveTowards(currentSmoothedIntensity, targetIntensity, releaseSpeed * Time.deltaTime);
            }

            int finalIntensity = Mathf.RoundToInt(currentSmoothedIntensity);
            currentMotorIntensity = finalIntensity;
            isVibrating = finalIntensity > 0;

            // 4. ควบคุมจังหวะการส่งสัญญาณไปยัง bHaptics (Rate Limiting + Overlapping Pulse)
            float interval = 1f / Mathf.Max(1, updateRateHz);
            if (Time.time >= nextSendTime)
            {
                nextSendTime = Time.time + interval;

                if (isVibrating)
                {
                    SendGloveHaptic(finalIntensity);
                    wasVibrating = true;
                }
                else if (wasVibrating)
                {
                    StopGloveHaptic();
                    wasVibrating = false;
                }
            }
        }

        private void SendGloveHaptic(int intensity)
        {
            // เซ็ตค่ามอเตอร์ 8 ตัวของ bHaptics Glove:
            // 0: Thumb, 1: Index, 2: Middle, 3: Ring, 4: Pinky, 5: Wrist, 6: Palm Thumb, 7: Palm Pinky
            for (int i = 0; i < 8; i++) motorValues[i] = 0;

            if (vibrateThumb) motorValues[0] = intensity;
            if (vibrateIndex) motorValues[1] = intensity;
            if (vibrateMiddle) motorValues[2] = intensity;
            if (vibrateRing) motorValues[3] = intensity;
            if (vibratePinky) motorValues[4] = intensity;
            if (vibratePalm)
            {
                motorValues[6] = intensity;
                motorValues[7] = intensity;
            }

            try
            {
                BhapticsLibrary.PlayMotors((int)glovePosition, motorValues, pulseDurationMs);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BhapticsGloveFeedback] PlayMotors error: " + ex.Message);
            }
        }

        private void StopGloveHaptic()
        {
            for (int i = 0; i < 8; i++) motorValues[i] = 0;
            try
            {
                BhapticsLibrary.PlayMotors((int)glovePosition, motorValues, 20);
            }
            catch { }
        }

        private void OnDisable()
        {
            StopGloveHaptic();
        }

        private void OnDestroy()
        {
            StopGloveHaptic();
        }
    }
}
