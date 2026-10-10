using DG.Tweening;
using System;
using System.Collections;
using TurnBasedGame.Services;
using UnityEngine;
using UnityEngine.UI;

namespace TurnBasedGame.View
{
    /// <summary>
    /// 战斗视图，负责战斗场景的UI和动画表现
    /// 包含血条更新、角色动画和战斗特效
    /// </summary>
    public class BattleView : BaseView
    {
        [Header("Spawn Points")]
        public Transform PlayerSpawnPoint;
        public Transform EnemySpawnPoint;

        [Header("HUD")]
        public Slider PlayerHpBar;
        public Text PlayerHpText;
        public Slider EnemyHpBar;
        public Text EnemyHpText;

        [Header("UI Panels")]
        public GameObject ActionPanel;
        public Transform SkillContainer;
        public Button DefendButton;
        public Button ItemButton;
        public Text LogText;
        public Button SurrenderButton;

        [Header("Battle Bag")]
        public GameObject BattleBagPanel;
        public Transform BattleBagContainer;
        public Button CloseBagButton;
        public GameObject BagItemTemplate;

        [Header("Floating Text Setup")]
        public Transform WorldCanvas; // 专门用来放飘字的画布
        public GameObject FloatingTextTemplate;

        [Header("Prefabs")]
        public GameObject SkillButtonTemplate;

        private Transform _playerModel;
        private Transform _enemyModel;

        private Material _playerMat;
        private Material _enemyMat;

        [Header("Chat UI")]
        public GameObject ChatPanel;
        public Transform ChatContainer;
        public InputField ChatInput;
        public Button SendChatButton;
        public Button ToggleChatButton;
        public GameObject ChatMsgTemplate;

        [Header("End Game UI")]
        public GameObject ResultPanel;
        public Text ResultText;

        /// <summary>
        /// 初始化战斗场景，创建玩家和敌人模型
        /// 清理旧模型防止重连时模型叠加
        /// </summary>
        public void InitScene(string p1CharType, string p2CharType, bool amIP1)
        {
            // 清理旧模型
            if (_playerModel) Destroy(_playerModel.gameObject);
            if (_enemyModel) Destroy(_enemyModel.gameObject);

            string myCharType = amIP1 ? p1CharType : p2CharType;
            string enemyCharType = amIP1 ? p2CharType : p1CharType;

            // 尝试加载模型，失败则用胶囊体
            _playerModel = CreateModelOrCapsule(PlayerSpawnPoint, Color.blue, "Player", myCharType, out _playerMat);
            _enemyModel = CreateModelOrCapsule(EnemySpawnPoint, Color.red, "Enemy", enemyCharType, out _enemyMat);

            SetInteractable(false);
            if (BattleBagPanel) BattleBagPanel.SetActive(false);
        }

        /// <summary>
        /// 模型加载逻辑
        /// </summary>
        private Transform CreateModelOrCapsule(Transform parent, Color fallbackColor, string name, string charType, out Material mat)
        {
            mat = null;
            if (string.IsNullOrEmpty(charType)) charType = "warrior";

            // 1. 尝试从 Resources/models/ 文件夹加载 3D 预制体
            GameObject prefab = Resources.Load<GameObject>($"models/{charType.ToLower()}");
            if (prefab != null)
            {
                GameObject go = Instantiate(prefab, parent);
                go.name = name;
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                // 我们不随便改真实模型的材质，保持原样

                // 尝试获取一个材质用于后续的闪白/变色动画
                var renderer = go.GetComponentInChildren<Renderer>();
                if (renderer != null) mat = renderer.material;

                return go.transform;
            }

            // 2. 如果没找到模型，退回到胶囊体 (兜底逻辑)
            return CreateCapsule(parent, fallbackColor, name, out mat);
        }

        /// <summary>
        /// 创建胶囊体角色模型，包含眼睛等细节
        /// 使用共享材质解决材质问题
        /// </summary>
        private Transform CreateCapsule(Transform parent, Color color, string name, out Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one;

            var renderer = go.GetComponent<Renderer>();
            renderer.material = ResourceManager.Instance.GetSharedMaterial();
            renderer.material.color = color;
            mat = renderer.material; // 赋值给外部

            // 眼睛
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Cube);
            eye.transform.SetParent(go.transform);
            eye.transform.localPosition = new Vector3(0, 0.5f, 0.4f);
            eye.transform.localScale = new Vector3(0.6f, 0.2f, 0.2f);
            var eyeRenderer = eye.GetComponent<Renderer>();
            eyeRenderer.material = ResourceManager.Instance.GetSharedMaterial();
            eyeRenderer.material.color = Color.black;

            return go.transform;
        }

        /// <summary>
        /// 更新血条显示，使用DOTween实现平滑动画
        /// isPlayer=true更新玩家血条，false更新敌人血条
        /// </summary>
        public void UpdateHUD(int curHp, int maxHp, bool isPlayer)
        {
            float val = maxHp > 0 ? (float)curHp / maxHp : 0;
            if (isPlayer)
            {
                if (PlayerHpBar) PlayerHpBar.DOValue(val, 0.5f);
                if (PlayerHpText) PlayerHpText.text = $"{curHp}/{maxHp}";
            }
            else
            {
                if (EnemyHpBar) EnemyHpBar.DOValue(val, 0.5f);
                if (EnemyHpText) EnemyHpText.text = $"{curHp}/{maxHp}";
            }
        }

        /// <summary>
        /// 显示回合状态
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            if (ActionPanel) ActionPanel.SetActive(interactable);
            if (LogText) LogText.text = interactable ? "Your Turn" : "Opponent Turn";
        }

        // ================= 动画逻辑 =================
        /// <summary>
        /// 播放攻击动画：移动到目标位置 → 轻微冲撞 → 返回原位
        /// 用协程等待动画播完，保证节奏不错乱
        /// </summary>
        public IEnumerator PlayAttackAnim(bool isPlayer)
        {
            Transform attacker = isPlayer ? _playerModel : _enemyModel;
            Transform target = isPlayer ? _enemyModel : _playerModel;
            if (!attacker || !target) yield break;

            Vector3 originalPos = attacker.position;
            Vector3 targetPos = target.position + (isPlayer ? Vector3.back : Vector3.forward) * 1.5f;

            yield return attacker.DOMove(targetPos, 0.25f).SetEase(Ease.OutQuad).WaitForCompletion();
            yield return attacker.DOPunchPosition(target.position - attacker.position, 0.1f, 1, 0).WaitForCompletion();
            yield return attacker.DOMove(originalPos, 0.25f).SetEase(Ease.InQuad).WaitForCompletion();
        }

        /// <summary>
        /// 播放防御动画：通过缩放模拟防御姿态
        /// </summary>
        public IEnumerator PlayDefendAnim(bool isPlayer)
        {
            Transform actor = isPlayer ? _playerModel : _enemyModel;
            if (!actor) yield break;

            // 简单缩放示意
            yield return actor.DOScale(new Vector3(1.1f, 0.9f, 1.1f), 0.2f).SetLoops(2, LoopType.Yoyo).WaitForCompletion();
        }

        /// <summary>
        /// 播放使用道具动画：模型跳起
        /// </summary>
        public IEnumerator PlayItemAnim(bool isPlayer)
        {
            Transform actor = isPlayer ? _playerModel : _enemyModel;
            if (!actor) yield break;

            yield return actor.DOLocalJump(actor.localPosition, 1.0f, 1, 0.5f).WaitForCompletion();
        }

        /// <summary>
        /// 播放受击动画：屏幕抖动+伤害数字飘字
        /// </summary>
        public IEnumerator PlayHitAnim(bool isPlayer, int damage)
        {
            Transform victim = isPlayer ? _playerModel : _enemyModel;
            if (!victim) yield break;

            victim.DOShakePosition(0.3f, 0.5f, 20);
            ShowFloatingText(victim.position + Vector3.up * 2, $"-{damage}", Color.red);
            yield return new WaitForSeconds(0.4f);
        }

        /// <summary>
        /// 播放治疗动画：治疗数字飘字
        /// </summary>
        public IEnumerator PlayHealAnim(bool isPlayer, int heal)
        {
            Transform target = isPlayer ? _playerModel : _enemyModel;
            if (!target) yield break;

            ShowFloatingText(target.position + Vector3.up * 2, $"+{heal}", Color.green);
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>
        /// 播放闪避动画：“Miss”飘字
        /// </summary>
        public IEnumerator PlayDodgeAnim(bool isPlayer)
        {
            Transform target = isPlayer ? _playerModel : _enemyModel;
            if (!target) yield break;

            Vector3 originalPos = target.position;
            Vector3 dodgeOffset = (isPlayer ? Vector3.left : Vector3.right) * 1.0f; // 向旁边闪

            // 1. 侧闪 + 倾斜
            Sequence seq = DOTween.Sequence();
            seq.Append(target.DOMove(originalPos + dodgeOffset, 0.15f).SetEase(Ease.OutQuad));
            seq.Join(target.DORotate(new Vector3(0, 0, isPlayer ? -15 : 15), 0.15f)); // 稍微歪一下身子

            // 2. 飘字
            ShowFloatingText(target.position + Vector3.up * 2, "MISS", Color.gray);

            yield return seq.WaitForCompletion();

            // 3. 归位
            yield return target.DOMove(originalPos, 0.15f).SetEase(Ease.InQuad).WaitForCompletion();
            target.rotation = Quaternion.identity; // 复原旋转
        }

        /// <summary>
        /// 播放胜利动画：原地跳跃
        /// </summary>
        public IEnumerator PlayVictoryAnim(bool isPlayer)
        {
            Transform actor = isPlayer ? _playerModel : _enemyModel;
            if (!actor) yield break;

            // 连续跳两次
            yield return actor.DOLocalJump(actor.localPosition, 1.5f, 1, 0.4f).SetLoops(2).WaitForCompletion();
        }

        /// <summary>
        /// 播放死亡动画：旋转并缩小
        /// </summary>
        public IEnumerator PlayDeathAnim(bool isPlayer)
        {
            Transform victim = isPlayer ? _playerModel : _enemyModel;
            if (!victim) yield break;

            Sequence seq = DOTween.Sequence();
            seq.Append(victim.DORotate(new Vector3(90, 0, 0), 0.5f));
            seq.Join(victim.DOScale(Vector3.zero, 0.5f));
            yield return seq.WaitForCompletion();
        }

        /// <summary>
        /// 显示结算大字
        /// </summary>
        public void ShowResultPanel(bool isWin, string reason)
        {
            if (ResultPanel && ResultText)
            {
                ResultPanel.SetActive(true);

                // 显示格式：
                // VICTORY
                // (Opponent Surrendered)
                string title = isWin ? "VICTORY" : "DEFEAT";
                ResultText.text = $"{title}\n<size=40>({reason})</size>"; // 使用富文本缩小字体

                ResultText.color = isWin ? Color.yellow : Color.gray;

                ResultText.transform.localScale = Vector3.zero;
                ResultText.transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack);
            }
        }

        /// <summary>
        /// 飘字逻辑
        /// </summary>
        private void ShowFloatingText(Vector3 pos, string content, Color color)
        {
            if (!FloatingTextTemplate || !WorldCanvas) return;

            // 生成在 WorldCanvas 下
            GameObject go = Instantiate(FloatingTextTemplate, WorldCanvas);
            go.transform.position = pos; // 设置世界坐标
            // 确保朝向摄像机
            go.transform.rotation = Camera.main.transform.rotation;
            go.SetActive(true);

            Text txt = go.GetComponent<Text>();
            if (!txt) txt = go.GetComponentInChildren<Text>();

            txt.text = content;
            txt.color = color;

            // 向上飘一点然后消失
            go.transform.DOMoveY(pos.y + 2f, 1f);
            txt.DOFade(0, 1f).OnComplete(() => Destroy(go));
        }
    }
}