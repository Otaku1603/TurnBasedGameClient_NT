using UnityEngine;
using UnityEngine.UI;

namespace TurnBasedGame.View
{
    public class DevSettingsView : BaseView
    {
        [Header("Panel")]
        public GameObject SettingsPanel;
        public Button OpenSettingsButton;
        public Button CloseSettingsButton;
        public Button SaveButton;
        public Button ResetButton;

        [Header("Inputs")]
        public Toggle CustomModeToggle;
        public InputField IpInput;
        public Toggle SslToggle;

        [Header("Status")]
        public Text CurrentConfigText;
    }
}