using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Bhaptics.SDK2
{
    /// <summary>
    /// Apple-only (iOS/visionOS) connection UI. There is no bHaptics Player app on Apple platforms,
    /// so unlike <see cref="BhapticsUI"/> this panel drives BLE scan / pair directly through
    /// <see cref="BhapticsAppleDevices"/>: a Scan button (auto-stops after <see cref="scanDuration"/>
    /// seconds) plus a Connect / Disconnect button per discovered device.
    /// </summary>
    public class BhapticsAppleUI : MonoBehaviour
    {
        [SerializeField] private float intervalRefreshTime = 1f;
        [SerializeField] private float scanDuration = 30f;

        [Header("Scan UI")]
        [SerializeField] private Button scanButton;
        [SerializeField] private Text scanButtonText;
        [SerializeField] private Text statusText;

        [Header("Devices UI")]
        [SerializeField] private Transform devicesContainer;
        [SerializeField] private BhapticsAppleDeviceUI devicePrefab;

        private List<BhapticsAppleDeviceUI> rows = new List<BhapticsAppleDeviceUI>();
        private float scanRemaining;
        private int deviceCount;

        private void Start()
        {
            if (scanButton != null)
            {
                scanButton.onClick.AddListener(ToggleScan);
            }

            InvokeRepeating("Refresh", 0f, intervalRefreshTime);
        }

        private void OnDisable()
        {
            StopScanning();
        }

        private void OnDestroy()
        {
            CancelInvoke("Refresh");
        }

        private void Update()
        {
            if (scanRemaining <= 0f)
            {
                return;
            }

            scanRemaining -= Time.deltaTime;

            if (scanRemaining <= 0f)
            {
                StopScanning();
            }

            UpdateScanUi();
        }

        private void ToggleScan()
        {
            if (scanRemaining > 0f)
            {
                StopScanning();
            }
            else if (IsUnauthorized())
            {
                // Permission was denied and iOS won't re-show the popup — the only remedy is
                // the app's Settings page, so the button doubles as "Open Settings" here.
                BhapticsAppleDevices.OpenAppSettings();
            }
            else if (CanScan())
            {
                BhapticsAppleDevices.Scan();
                scanRemaining = scanDuration;
            }

            UpdateScanUi();
        }

        private bool IsUnauthorized()
        {
            return BhapticsAppleDevices.IsSupported
                && BhapticsAppleDevices.GetBluetoothState() == AppleBluetoothState.Unauthorized;
        }

        // On device, the native scan silently no-ops unless the adapter is poweredOn — don't run
        // a fake countdown in that case; UpdateScanUi() surfaces the cause instead. Off-device
        // (Editor) the state is always Unknown, so keep the countdown for UI preview.
        private bool CanScan()
        {
            return !BhapticsAppleDevices.IsSupported
                || BhapticsAppleDevices.GetBluetoothState() == AppleBluetoothState.PoweredOn;
        }

        private void StopScanning()
        {
            scanRemaining = 0f;

            if (BhapticsAppleDevices.IsScanning())
            {
                BhapticsAppleDevices.StopScan();
            }
        }

        private void Refresh()
        {
            var devices = BhapticsAppleDevices.GetDevices();
            deviceCount = devices.Count;

            while (rows.Count < devices.Count)
            {
                rows.Add(Instantiate(devicePrefab, devicesContainer));
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (i < devices.Count)
                {
                    rows[i].RefreshDevice(devices[i]);
                }
                else
                {
                    rows[i].gameObject.SetActive(false);
                }
            }

            UpdateScanUi();
        }

        private void UpdateScanUi()
        {
            if (scanButtonText != null)
            {
                if (scanRemaining > 0f)
                {
                    scanButtonText.text = string.Format("Stop ({0}s)", Mathf.CeilToInt(scanRemaining));
                }
                else
                {
                    scanButtonText.text = IsUnauthorized() ? "Settings" : "Scan";
                }
            }

            if (statusText != null)
            {
                if (scanRemaining > 0f)
                {
                    statusText.text = "Scanning for nearby bHaptics devices...";
                }
                else
                {
                    statusText.text = GetIdleStatusText();
                }
            }
        }

        private string GetIdleStatusText()
        {
            if (BhapticsAppleDevices.IsSupported)
            {
                switch (BhapticsAppleDevices.GetBluetoothState())
                {
                    case AppleBluetoothState.Unauthorized:
                        return "Bluetooth permission denied. Tap Settings to allow Bluetooth for this app.";
                    case AppleBluetoothState.PoweredOff:
                        return "Bluetooth is turned off. Turn it on to scan for devices.";
                    case AppleBluetoothState.Unsupported:
                        return "Bluetooth LE is not supported on this device.";
                }
            }

            return deviceCount == 0 ? "No devices found. Press Scan to search." : "";
        }
    }
}
