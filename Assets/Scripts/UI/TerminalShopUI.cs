using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sclass.EffectsSystem;

namespace Sclass.UI
{
    /// <summary>
    /// World-space терминал с магазином.
    /// Idle-панель всегда видна; Shop-панель открывается через PlayerInteractor.
    /// Клавиши: ↑↓ — навигация, E — купить, Esc — закрыть.
    /// </summary>
    public class TerminalShopUI : MonoBehaviour, IInteractable
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Billboard")]
        public bool  FacePlayer = true;
        public float FacePlayerY = 1.7f; // высота точки взгляда на игрока

        [Header("Idle Panel (всегда видна)")]
        public GameObject IdlePanel;
        public TMP_Text   IdleCoinsLabel;
        public string     IdleCoinsFormat = "◈ {0}";

        [Header("Shop Panel (открывается по E)")]
        public GameObject ShopPanel;

        [Header("Shop — Баланс")]
        public TMP_Text CreditsLabel;
        public TMP_Text KinesiaLabel;
        public TMP_Text SmallionLabel;
        public TMP_Text TransfiniteLabel;

        [Header("Shop — Список товаров")]
        [Tooltip("Префаб строки товара (ShopItemRow).")]
        public ShopItemRow ItemRowPrefab;
        [Tooltip("Родитель для строк (Vertical Layout Group).")]
        public Transform   ItemListContainer;

        [Header("Shop — Обратная связь")]
        public TMP_Text    FeedbackLabel;
        [Tooltip("Сколько секунд показывать сообщение об успехе/ошибке.")]
        public float       FeedbackDuration = 1.5f;

        [Header("Товары")]
        public ShopItemSO[] Items;

        // ── Runtime ───────────────────────────────────────────────────────────

        private PlayerInteractor   _interactor;
        private ShopItemRow[]      _rows;
        private int                _selectedIndex;
        private float              _feedbackTimer;
        private bool               _isOpen;
        private Transform          _playerTransform;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void OnEnable()
        {
            PlayerWallet.OnCoinsChanged += OnCoinsChanged;
        }

        private void OnDisable()
        {
            PlayerWallet.OnCoinsChanged -= OnCoinsChanged;
        }

        private void Start()
        {
            BuildItemList();

            if (ShopPanel != null) ShopPanel.SetActive(false);

            // Начальный idle
            if (PlayerWallet.Instance != null)
                SetIdleCoins(PlayerWallet.Instance.Coins);
        }

        private void LateUpdate()
        {
            if (FacePlayer && _playerTransform != null)
            {
                Vector3 target = _playerTransform.position + Vector3.up * FacePlayerY;
                Vector3 dir    = target - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.LookRotation(-dir);
            }

            if (_feedbackTimer > 0f)
            {
                _feedbackTimer -= Time.deltaTime;
                if (_feedbackTimer <= 0f && FeedbackLabel != null)
                    FeedbackLabel.text = string.Empty;
            }
        }

        // ── IInteractable ─────────────────────────────────────────────────────

        public string GetPrompt() => "Терминал";

        /// <summary>E = подтвердить покупку выбранного товара.</summary>
        public void Interact(PlayerInteractor interactor) => Confirm();

        public void OnHoverEnter(PlayerInteractor interactor)
        {
            _interactor      = interactor;
            _playerTransform = interactor.CameraTransform != null
                ? interactor.CameraTransform
                : interactor.transform;

            interactor.WeaponHolder?.SetFiringEnabled(false);
            Open();
        }

        public void OnHoverExit(PlayerInteractor interactor)
        {
            interactor.WeaponHolder?.SetFiringEnabled(true);
            Close();
            _interactor      = null;
            _playerTransform = null;
        }

        // ── Open / Close ──────────────────────────────────────────────────────

        public void Open()
        {
            _isOpen = true;
            if (ShopPanel != null) ShopPanel.SetActive(true);

            RefreshBalances();
            RefreshAffordability();
            SetSelection(0);
        }

        public void Close()
        {
            _isOpen = false;
            if (ShopPanel != null) ShopPanel.SetActive(false);
            if (FeedbackLabel != null) FeedbackLabel.text = string.Empty;
        }

        // ── Navigation (called by PlayerInteractor) ───────────────────────────

        /// <param name="direction">+1 = вниз, -1 = вверх</param>
        public void Navigate(int direction)
        {
            if (_rows == null || _rows.Length == 0) return;

            int next = _selectedIndex + direction;
            next = Mathf.Clamp(next, 0, _rows.Length - 1);
            SetSelection(next);
        }

        public void Confirm()
        {
            if (_rows == null || _selectedIndex >= _rows.Length) return;

            ShopItemSO item = _rows[_selectedIndex].Item;
            if (item == null) return;

            bool bought = item.TryPurchase(
                _interactor.Wallet,
                _interactor.Mutations,
                _interactor.WeaponHolder);

            if (bought)
            {
                ShowFeedback($"✓ {item.ItemName}", new Color(0.2f, 1f, 0.4f));
                RefreshBalances();
                RefreshAffordability();
            }
            else
            {
                ShowFeedback("✗ Недостаточно средств", new Color(1f, 0.3f, 0.3f));
            }
        }

        // ── Build list ────────────────────────────────────────────────────────

        private void BuildItemList()
        {
            if (ItemRowPrefab == null || ItemListContainer == null || Items == null) return;

            // Чистим старые строки
            foreach (Transform child in ItemListContainer)
                Destroy(child.gameObject);

            _rows = new ShopItemRow[Items.Length];

            for (int i = 0; i < Items.Length; i++)
            {
                if (Items[i] == null) continue;

                ShopItemRow row = Instantiate(ItemRowPrefab, ItemListContainer);
                row.Init(Items[i]);
                _rows[i] = row;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SetSelection(int index)
        {
            if (_rows == null) return;

            if (_selectedIndex >= 0 && _selectedIndex < _rows.Length && _rows[_selectedIndex] != null)
                _rows[_selectedIndex].SetSelected(false);

            _selectedIndex = index;

            if (_selectedIndex >= 0 && _selectedIndex < _rows.Length && _rows[_selectedIndex] != null)
                _rows[_selectedIndex].SetSelected(true);
        }

        private void RefreshBalances()
        {
            if (_interactor == null) return;

            PlayerWallet            w = _interactor.Wallet;
            ElementalMutationManager m = _interactor.Mutations;

            if (CreditsLabel     != null) CreditsLabel.text     = w != null ? $"◈ {w.Coins}"              : "—";
            if (KinesiaLabel     != null) KinesiaLabel.text     = m != null ? $"K {m.Kinesia:F0}"          : "—";
            if (SmallionLabel    != null) SmallionLabel.text    = m != null ? $"S {m.Smallion:F0}"         : "—";
            if (TransfiniteLabel != null) TransfiniteLabel.text = m != null ? $"T {m.Transfinite:F0}"      : "—";
        }

        private void RefreshAffordability()
        {
            if (_rows == null || _interactor == null) return;

            foreach (var row in _rows)
            {
                if (row == null || row.Item == null) continue;
                bool can = row.Item.CanAfford(_interactor.Wallet, _interactor.Mutations);
                row.SetAffordable(can);
            }
        }

        private void ShowFeedback(string message, Color color)
        {
            if (FeedbackLabel == null) return;
            FeedbackLabel.text  = message;
            FeedbackLabel.color = color;
            _feedbackTimer = FeedbackDuration;
        }

        private void SetIdleCoins(int coins)
        {
            if (IdleCoinsLabel != null)
                IdleCoinsLabel.text = string.Format(IdleCoinsFormat, coins);
        }

        private void OnCoinsChanged(int coins)
        {
            SetIdleCoins(coins);
            if (_isOpen) RefreshBalances();
            if (_isOpen) RefreshAffordability();
        }
    }
}
