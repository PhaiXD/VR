using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bhaptics.SDK2
{
    /// <summary>
    /// Bluetooth adapter state on iOS/visionOS, mirroring CoreBluetooth's CBManagerState raw
    /// values. Lets an app tell scan-failure causes apart (permission denied / Bluetooth off /
    /// BLE unsupported) instead of only seeing an empty device list. Off-device (including the
    /// Editor) the state is always <see cref="Unknown"/>.
    /// </summary>
    public enum AppleBluetoothState
    {
        Unknown = 0,
        Resetting = 1,
        Unsupported = 2,
        Unauthorized = 3,
        PoweredOff = 4,
        PoweredOn = 5,
    }

    /// <summary>
    /// Apple-only (iOS/visionOS) BLE device management — scan / pair / list. On Apple there is no
    /// external bHaptics Player, so devices are discovered and paired directly over CoreBluetooth.
    /// <para>Multi-user-safe: scanning and pairing are explicit (driven by a device picker). Only
    /// remembered (paired) devices auto-reconnect on <see cref="BhapticsLibrary.Initialize"/>;
    /// nothing in range is connected automatically.</para>
    /// <para>This surface is intentionally separate from the cross-platform <see cref="BhapticsLibrary"/>
    /// facade and is not gated by its availability check, so a picker can run before/independently
    /// of haptic playback. All platform/Editor guarding lives in <see cref="BhapticsPluginNative"/>,
    /// so these methods are safe no-ops off-device.</para>
    /// </summary>
    public static class BhapticsAppleDevices
    {
        [Serializable]
        private class AppleDevice
        {
            public string name;
            public string id;
            public string position;
            public bool connected;
            public bool paired;
        }

        [Serializable]
        private class AppleDeviceList
        {
            public AppleDevice[] devices;
        }

        private static readonly PositionType[] EmptyCandidates = new PositionType[0];

        /// <summary>True on iOS/visionOS device builds, where BLE scan/pair is available.</summary>
        public static bool IsSupported
        {
            get
            {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Start BLE scanning for nearby "Tact*" devices.</summary>
        public static void Scan() => BhapticsPluginNative.Scan();

        public static void StopScan() => BhapticsPluginNative.StopScan();

        public static bool IsScanning() => BhapticsPluginNative.IsScanning();

        /// <summary>
        /// Current Bluetooth adapter state. Transient — poll it rather than caching: it stays
        /// <see cref="AppleBluetoothState.Unknown"/> until CoreBluetooth first reports (shortly
        /// after startup), and changes whenever the user toggles Bluetooth or app permission.
        /// Note iOS never re-shows the permission popup once denied, so on
        /// <see cref="AppleBluetoothState.Unauthorized"/> the only remedy is guiding the user
        /// to the Settings app.
        /// </summary>
        public static AppleBluetoothState GetBluetoothState()
        {
            var raw = BhapticsPluginNative.GetBluetoothState();
            if (raw < (int)AppleBluetoothState.Unknown || raw > (int)AppleBluetoothState.PoweredOn)
            {
                // Future/unexpected CBManagerState values degrade to Unknown.
                return AppleBluetoothState.Unknown;
            }

            return (AppleBluetoothState)raw;
        }

        /// <summary>
        /// Opens this app's page in the iOS/visionOS Settings app, where the user can re-enable
        /// the Bluetooth permission — the remedy for <see cref="AppleBluetoothState.Unauthorized"/>,
        /// since iOS never re-shows a denied permission popup. Note this is the app's settings
        /// page, not the system Bluetooth panel (which has no public deep link); for
        /// <see cref="AppleBluetoothState.PoweredOff"/> guide the user to Control Center instead.
        /// </summary>
        public static void OpenAppSettings() => BhapticsPluginNative.OpenAppSettings();

        /// <summary>Pair (remember) and connect a scanned device by its id.</summary>
        public static void Pair(string deviceId) => BhapticsPluginNative.Pair(deviceId);

        /// <summary>Unpair (forget) and disconnect a device by its id.</summary>
        public static void Unpair(string deviceId) => BhapticsPluginNative.Unpair(deviceId);

        /// <summary>Current known devices (scanned + remembered), converted to <see cref="HapticDevice"/>.</summary>
        public static List<HapticDevice> GetDevices()
        {
            var res = new List<HapticDevice>();

            try
            {
                var json = BhapticsPluginNative.GetDevicesJson();
                if (string.IsNullOrEmpty(json))
                {
                    return res;
                }

                var list = JsonUtility.FromJson<AppleDeviceList>("{\"devices\":" + json + "}");
                if (list == null || list.devices == null)
                {
                    return res;
                }

                foreach (var d in list.devices)
                {
                    res.Add(new HapticDevice
                    {
                        DeviceName = d.name,
                        Address = d.id,
                        IsConnected = d.connected,
                        IsPaired = d.paired,
                        Position = ParsePosition(d.position),
                        Candidates = EmptyCandidates,
                        Battery = 0,
                        IsAudioJack = false,
                    });
                }
            }
            catch (Exception e)
            {
                BhapticsLogManager.LogErrorFormat("[bHaptics] BhapticsAppleDevices.GetDevices() {0}", e.Message);
            }

            return res;
        }

        // Swift BhapticsPosition.name yields exactly the C# PositionType names
        // (Vest/Head/HandL/HandR/FootL/FootR/ForearmL/ForearmR/GloveL/GloveR) or "Unknown".
        private static PositionType ParsePosition(string position)
        {
            if (!string.IsNullOrEmpty(position) && Enum.TryParse<PositionType>(position, true, out var parsed))
            {
                return parsed;
            }

            return PositionType.Vest;
        }
    }
}
