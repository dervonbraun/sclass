using UnityEngine;
using TMPro;

/// <summary>
/// Здоровье игрока + HUD (HP).
/// Вешать на GameObject с CharacterController.
///
/// Детекция контакта с агентами через SwarmManager.IsAgentNearPlayer.
/// Счётчик агентов перенесён в SwarmManager.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerHealth : MonoBehaviour
{
    [Header("HP")]
    public float MaxHealth = 100f;

    [Tooltip("Урон в секунду пока хоть один агент находится рядом с игроком.")]
    public float DamagePerSecondFromAgent = 15f;

    [Tooltip("Радиус нанесения урона игроку от агентов. " +
             "Должен быть >= PlayerRadius + AgentRadius (сумма радиусов отталкивания в SwarmManager). " +
             "По умолчанию 1.2 при PlayerRadius=AgentRadius=0.5.")]
    public float DamageRadius = 1.2f;

    [Tooltip("Слой агентов роя.")]
    public LayerMask AgentLayer;

    [Header("UI — HP")]
    [Tooltip("TMP_Text с цифрами HP. Назначить в Inspector.")]
    public TMP_Text HealthText;

    [Tooltip("SwarmManager — для детекции агентов рядом.")]
    public SwarmManager Swarm;

    // ── Состояние ──────────────────────────────────────────────────
    public float CurrentHealth { get; private set; }
    public bool  IsDead        { get; private set; }

    private CharacterController _cc;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        CurrentHealth = MaxHealth;
        UpdateUI();
    }

    private void Update()
    {
        if (IsDead) return;

        // ── Детекция агентов через SwarmManager (не зависит от коллайдеров/слоёв) ──
        if (Swarm != null && Swarm.IsAgentNearPlayer(transform.position, DamageRadius))
        {
            TakeDamage(DamagePerSecondFromAgent * Time.deltaTime);
        }
    }

    // ── Публичный API ──────────────────────────────────────────────

    public void TakeDamage(float amount)
    {
        if (IsDead) return;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        UpdateUI();
        if (CurrentHealth <= 0f) Die();
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
        UpdateUI();
    }

    // ── Внутренняя логика ──────────────────────────────────────────

    private void Die()
    {
        IsDead = true;
        Debug.Log("[PlayerHealth] Игрок погиб.");
        // GameManager.Instance?.OnPlayerDied();
    }

    private void UpdateUI()
    {
        if (HealthText != null)
            HealthText.text = Mathf.CeilToInt(CurrentHealth).ToString();
    }

    // Запасная отрисовка если TMP_Text не назначен
    private void OnGUI()
    {
        if (HealthText == null)
        {
            GUI.contentColor = Color.red;
            GUI.Label(new Rect(20, 20, 300, 30),
                      $"HP: {Mathf.CeilToInt(CurrentHealth)} / {Mathf.CeilToInt(MaxHealth)}");
        }
    }

    // Визуализация радиуса детекции в Scene View
    private void OnDrawGizmosSelected()
    {
        if (!TryGetComponent(out CharacterController cc)) return;
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * (cc.height * 0.5f), DamageRadius);
    }
}
