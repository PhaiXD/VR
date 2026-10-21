using UnityEngine;
using UnityEngine.UI;

namespace Bhaptics.SDK2
{
    /// <summary>
    /// A single device row inside <see cref="BhapticsAppleUI"/>: device name / state, and a
    /// Connect (<see cref="BhapticsAppleDevices.Pair"/>) or Disconnect
    /// (<see cref="BhapticsAppleDevices.Unpair"/>) button.
    /// </summary>
    public class BhapticsAppleDeviceUI : MonoBehaviour
    {
        private static readonly Color ConnectColor = new Color32(0x52, 0x67, 0xF9, 0xFF);
        private static readonly Color ConnectHoverColor = new Color32(0x69, 0x7C, 0xFF, 0xFF);
        private static readonly Color DisconnectColor = new Color32(0x52, 0x54, 0x66, 0xFF);
        private static readonly Color DisconnectHoverColor = new Color32(0x63, 0x64, 0x6F, 0xFF);
        private static readonly Color ConnectedStateColor = new Color32(0x4C, 0xD9, 0x64, 0xFF);
        private static readonly Color DefaultStateColor = new Color32(0x9A, 0x9D, 0xB2, 0xFF);

        [SerializeField] private Text nameText;
        [SerializeField] private Text statusText;
        [SerializeField] private Button connectButton;
        [SerializeField] private Text connectButtonText;

        private HapticDevice device;

        private void Awake()
        {
            if (connectButton != null)
            {
                connectButton.onClick.AddListener(OnConnectButton);
            }
        }

        public void RefreshDevice(HapticDevice d)
        {
            device = d;

            if (device == null)
            {
                gameObject.SetActive(false);
                return;
            }

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            nameText.text = device.DeviceName;

            if (device.IsConnected)
            {
                statusText.text = device.Position + " - Connected";
                statusText.color = ConnectedStateColor;
            }
            else if (device.IsPaired)
            {
                statusText.text = device.Position + " - Paired";
                statusText.color = DefaultStateColor;
            }
            else
            {
                statusText.text = device.Position.ToString();
                statusText.color = DefaultStateColor;
            }

            bool paired = device.IsPaired || device.IsConnected;
            connectButtonText.text = paired ? "Disconnect" : "Connect";
            ChangeButtonColor(connectButton, !paired);
        }

        private void OnConnectButton()
        {
            if (device == null)
            {
                return;
            }

            if (device.IsPaired || device.IsConnected)
            {
                BhapticsAppleDevices.Unpair(device.Address);
            }
            else
            {
                BhapticsAppleDevices.Pair(device.Address);
            }
        }

        private void ChangeButtonColor(Button targetButton, bool isSelect)
        {
            var defaultColor = isSelect ? ConnectColor : DisconnectColor;
            var hoverColor = isSelect ? ConnectHoverColor : DisconnectHoverColor;

            var buttonColors = targetButton.colors;
            buttonColors.normalColor = defaultColor;
            buttonColors.highlightedColor = hoverColor;
            buttonColors.pressedColor = defaultColor;
            targetButton.colors = buttonColors;
        }
    }
}
