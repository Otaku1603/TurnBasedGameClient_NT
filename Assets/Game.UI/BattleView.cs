using DG.Tweening;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 战斗视图：血条 HUD、技能/防御/道具/投降面板、飘字与 DOTween 动画、结算面板。
    /// 字段名与旧客户端一致；模型加载兜底胶囊体（模型经 AssetService 加载）。
    /// 动画分派由 Controller 按 ActionType 枚举驱动（禁止中文字符串判断）。
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
        public Transform WorldCanvas;
        public GameObject FloatingTextTemplate;

        [Header("Prefabs")]
        public GameObject SkillButtonTemplate;

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

        private Transform _playerModel;
        private Transform _enemyModel;
        private Material _playerMat;
        private Material _enemyMat;

        public void InitScene(string p1CharType, string p2CharType, bool amIP1)
        {
            if (_playerModel) Destroy(_playerModel.gameObject);
            if (_enemyModel) Destroy(_enemyModel.gameObject);

            string myCharType = amIP1 ? p1CharType : p2CharType;
            string enemyCharType = amIP1 ? p2CharType : p1CharType;

            _playerModel = CreateModelOrCapsule(PlayerSpawnPoint, Color.blue, "Player", myCharType, out _playerMat);
            _enemyModel = CreateModelOrCapsule(EnemySpawnPoint, Color.red, "Enemy", enemyCharType, out _enemyMat);

            SetInteractable(false);
            if (BattleBagPanel) BattleBagPanel.SetActive(false);
        }

        private Transform CreateModelOrCapsule(Transform parent, Color fallbackColor, string name, string charType, out Material mat)
        {
            mat = null;
            if (string.IsNullOrEmpty(charType)) charType = "warrior";

            GameObject prefab = Game.Presentation.AssetService.GetModelPrefab(charType);
            if (prefab != null)
            {
                GameObject go = Instantiate(prefab, parent);
                go.name = name;
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;

                var renderer = go.GetComponentInChildren<Renderer>();
                if (renderer != null) mat = renderer.material;

                return go.transform;
            }

            return CreateCapsule(parent, fallbackColor, name, out mat);
        }

        private Transform CreateCapsule(Transform parent, Color color, string name, out Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one;

            var renderer = go.GetComponent<Renderer>();
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = color;
            mat = renderer.material;

            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Cube);
            eye.transform.SetParent(go.transform);
            eye.transform.localPosition = new Vector3(0, 0.5f, 0.4f);
            eye.transform.localScale = new Vector3(0.6f, 0.2f, 0.2f);
            eye.GetComponent<Renderer>().material.color = Color.black;

            return go.transform;
        }

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

        /// <summary>回合指示：本回合可操作与否（由 currentActorUserId 决定）。</summary>
        public void SetInteractable(bool interactable)
        {
            if (ActionPanel) ActionPanel.SetActive(interactable);
            if (LogText) LogText.text = interactable ? "Your Turn" : "Opponent Turn";
        }

        public void AppendLog(string line)
        {
            if (LogText) LogText.text += "\n" + line;
        }

        // ================= 动画（由 ActionType 驱动） =================

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

        public IEnumerator PlayDefendAnim(bool isPlayer)
        {
            Transform actor = isPlayer ? _playerModel : _enemyModel;
            if (!actor) yield break;

            yield return actor.DOScale(new Vector3(1.1f, 0.9f, 1.1f), 0.2f).SetLoops(2, LoopType.Yoyo).WaitForCompletion();
        }

        public IEnumerator PlayItemAnim(bool isPlayer)
        {
            Transform actor = isPlayer ? _playerModel : _enemyModel;
            if (!actor) yield break;

            yield return actor.DOLocalJump(actor.localPosition, 1.0f, 1, 0.5f).WaitForCompletion();
        }

        public IEnumerator PlayHitAnim(bool isPlayer, int damage)
        {
            Transform victim = isPlayer ? _playerModel : _enemyModel;
            if (!victim) yield break;

            victim.DOShakePosition(0.3f, 0.5f, 20);
            ShowFloatingText(victim.position + Vector3.up * 2, $"-{damage}", Color.red);
            yield return new WaitForSeconds(0.4f);
        }

        public IEnumerator PlayHealAnim(bool isPlayer, int heal)
        {
            Transform target = isPlayer ? _playerModel : _enemyModel;
            if (!target) yield break;

            ShowFloatingText(target.position + Vector3.up * 2, $"+{heal}", Color.green);
            yield return new WaitForSeconds(0.5f);
        }

        public IEnumerator PlayDodgeAnim(bool isPlayer)
        {
            Transform target = isPlayer ? _playerModel : _enemyModel;
            if (!target) yield break;

            Vector3 originalPos = target.position;
            Vector3 dodgeOffset = (isPlayer ? Vector3.left : Vector3.right) * 1.0f;

            Sequence seq = DOTween.Sequence();
            seq.Append(target.DOMove(originalPos + dodgeOffset, 0.15f).SetEase(Ease.OutQuad));
            seq.Join(target.DORotate(new Vector3(0, 0, isPlayer ? -15 : 15), 0.15f));

            ShowFloatingText(target.position + Vector3.up * 2, "MISS", Color.gray);

            yield return seq.WaitForCompletion();

            yield return target.DOMove(originalPos, 0.15f).SetEase(Ease.InQuad).WaitForCompletion();
            target.rotation = Quaternion.identity;
        }

        public IEnumerator PlayVictoryAnim(bool isPlayer)
        {
            Transform actor = isPlayer ? _playerModel : _enemyModel;
            if (!actor) yield break;

            yield return actor.DOLocalJump(actor.localPosition, 1.5f, 1, 0.4f).SetLoops(2).WaitForCompletion();
        }

        public IEnumerator PlayDeathAnim(bool isPlayer)
        {
            Transform victim = isPlayer ? _playerModel : _enemyModel;
            if (!victim) yield break;

            Sequence seq = DOTween.Sequence();
            seq.Append(victim.DORotate(new Vector3(90, 0, 0), 0.5f));
            seq.Join(victim.DOScale(Vector3.zero, 0.5f));
            yield return seq.WaitForCompletion();
        }

        public void ShowResultPanel(bool isWin, string reason)
        {
            if (ResultPanel && ResultText)
            {
                ResultPanel.SetActive(true);
                string title = isWin ? "VICTORY" : "DEFEAT";
                ResultText.text = $"{title}\n<size=40>({reason})</size>";
                ResultText.color = isWin ? Color.yellow : Color.gray;

                ResultText.transform.localScale = Vector3.zero;
                ResultText.transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack);
            }
        }

        private void ShowFloatingText(Vector3 pos, string content, Color color)
        {
            if (!FloatingTextTemplate || !WorldCanvas) return;

            GameObject go = Instantiate(FloatingTextTemplate, WorldCanvas);
            go.transform.position = pos;
            go.transform.rotation = Camera.main.transform.rotation;
            go.SetActive(true);

            Text txt = go.GetComponent<Text>();
            if (!txt) txt = go.GetComponentInChildren<Text>();

            txt.text = content;
            txt.color = color;

            go.transform.DOMoveY(pos.y + 2f, 1f);
            txt.DOFade(0, 1f).OnComplete(() => Destroy(go));
        }
    }
}
