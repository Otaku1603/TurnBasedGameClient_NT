using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TurnBasedGame.View
{
    /// <summary>
    /// 主场景视图，管理大厅所有功能面板
    /// 包括商店、背包、排行榜、好友、留言板等
    /// </summary>
    public class MainSceneView : BaseView
    {
        [Header("Top Bar")]
        public GameObject TopBar;
        public Button TopBarButton; // 点击顶部打开个人资料
        public Text NicknameText;
        public Text GoldText;
        public Text EloText;

        [Header("Main Menu")]
        public GameObject MainMenu;
        public Button MatchButton;
        public Button ShopButton;
        public Button BagButton;
        public Button RankButton;
        public Button FriendButton;
        public Button MessageButton;

        [Header("Profile Panel")]
        public GameObject ProfilePanel;
        public Button CloseProfileButton;
        public Text ProfileNameText;
        public Text ProfileIDText;
        public Text ProfileStatsText; // 显示胜率、场次等
        public Image ProfileAvatarIcon; // 头像框
        public Text ProfileCharStatsText; //角色数值文本

        [Header("Profile Panel Addons")]
        public Button ProfileHistoryButton;

        [Header("History Panel")]
        public GameObject HistoryPanel;
        public Button CloseHistoryButton;
        public Transform HistoryContainer;

        [Header("Friend Panel")]
        public GameObject FriendPanel;
        public Button CloseFriendButton;
        public Transform FriendContainer;
        public InputField FriendIdInput;
        public Button AddFriendButton;

        [Header("Message Panel")]
        public GameObject MessagePanel;
        public Button CloseMessageButton;
        public Transform MessageContainer;
        public InputField MsgTargetIdInput; // 发给谁
        public InputField MsgContentInput;  // 内容
        public Button SendMsgButton;

        [Header("Existing Panels")]
        public GameObject ShopPanel;
        public GameObject BagPanel;
        public GameObject RankPanel;
        public Button CloseShopButton;
        public Button CloseBagButton;
        public Button CloseRankButton;
        public Transform ShopContainer;
        public Transform BagContainer;
        public Transform RankContainer;

        [Header("Match Popup")]
        public GameObject MatchReadyPanel;

        [Header("Templates")]
        public GameObject ItemTemplate;
        public GameObject RankTemplate;
        public GameObject FriendTemplate;
        public GameObject MessageTemplate;
        public GameObject HistoryTemplate;

        private List<GameObject> _spawnedItems = new List<GameObject>();

        protected override void Awake()
        {
            base.Awake();
            ForceShowMenu();
        }

        /// <summary>
        /// 强制显示主菜单，隐藏所有功能面板
        /// 用于返回大厅时的界面重置
        /// </summary>
        public void ForceShowMenu()
        {
            if (TopBar) TopBar.SetActive(true);
            if (MainMenu) MainMenu.SetActive(true);

            // 隐藏所有面板
            if (ShopPanel) ShopPanel.SetActive(false);
            if (BagPanel) BagPanel.SetActive(false);
            if (RankPanel) RankPanel.SetActive(false);
            if (ProfilePanel) ProfilePanel.SetActive(false);
            if (FriendPanel) FriendPanel.SetActive(false);
            if (MessagePanel) MessagePanel.SetActive(false);
            if (HistoryPanel) HistoryPanel.SetActive(false);

            // 隐藏预制体
            if (ItemTemplate) ItemTemplate.SetActive(false);
            if (RankTemplate) RankTemplate.SetActive(false);
            if (FriendTemplate) FriendTemplate.SetActive(false);
            if (MessageTemplate) MessageTemplate.SetActive(false);
            if (HistoryTemplate) HistoryTemplate.SetActive(false);
        }

        public void UpdateTopBar(string nickname, int gold, int elo)
        {
            if (NicknameText) NicknameText.text = nickname;
            if (GoldText) GoldText.text = $"Gold: {gold}";
            if (EloText) EloText.text = $"ELO: {elo}";
        }

        public void ShowPanel(string panelName)
        {
            if (ShopPanel) ShopPanel.SetActive(panelName == "Shop");
            if (BagPanel) BagPanel.SetActive(panelName == "Bag");
            if (RankPanel) RankPanel.SetActive(panelName == "Rank");
            if (ProfilePanel) ProfilePanel.SetActive(panelName == "Profile");
            if (FriendPanel) FriendPanel.SetActive(panelName == "Friend");
            if (MessagePanel) MessagePanel.SetActive(panelName == "Message");
            if (HistoryPanel) HistoryPanel.SetActive(panelName == "History");
        }

        public void ClearList()
        {
            foreach (var item in _spawnedItems) if (item) Destroy(item);
            _spawnedItems.Clear();
        }

        /// <summary>
        /// 统一生成列表项（比如好友列表、商品列表）。
        /// 把它加到列表里，切换面板的时候方便一键清空，防止列表重复堆叠。
        /// </summary>
        public GameObject SpawnItem(Transform container, GameObject template)
        {
            if (!container || !template) return null;
            GameObject go = Instantiate(template, container);
            go.SetActive(true);
            _spawnedItems.Add(go);
            return go;
        }
    }
}