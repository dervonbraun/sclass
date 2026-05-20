using UnityEngine;
using TMPro;
using Sclass.EffectsSystem;

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
    public ModifiableStat MaxHealth = new ModifiableStat(100f);
    public ModifiableStat HitboxRadiusMultiplier = new ModifiableStat(1f);
    [Tooltip("Начальное базовое значение здоровья")]
    public float InitialBaseHealth = 100f;

    [Tooltip("Слой агентов роя.")]
    public LayerMask AgentLayer;

    [Header("UI — HP")]
    [Tooltip("TMP_Text с цифрами HP. Назначить в Inspector.")]
    public TMP_Text HealthText;

    [Header("UI — Screen Effects (Canvas Groups)")]
    public CanvasGroup DamageGlowGroup;
    public CanvasGroup HealGlowGroup;
    public CanvasGroup MediumHealthGroup;
    public CanvasGroup LowHealthGroup;

    [Header("Effect Settings — Damage & Heal")]
    [Tooltip("Скорость появления эффекта (резко).")]
    public float ActionFadeInSpeed = 10f;
    [Tooltip("Скорость затухания эффекта (плавно).")]
    public float ActionFadeOutSpeed = 2f;
    [Tooltip("Время до начала затухания после получения урона/хила (сек).")]
    public float ActionFadeDelay = 0.5f;

    [Header("Effect Settings — Health States")]
    [Tooltip("Порог среднего здоровья (доля от 0 до 1).")]
    [Range(0f, 1f)] public float MediumHealthThreshold = 0.5f;
    [Tooltip("Порог низкого здоровья (доля от 0 до 1).")]
    [Range(0f, 1f)] public float LowHealthThreshold = 0.25f;
    [Tooltip("Скорость плавного появления/исчезновения состояний здоровья.")]
    public float StateFadeSpeed = 3f;

    [Header("Dependencies")]
    [Tooltip("SwarmManager — для детекции агентов рядом.")]
    public SwarmManager Swarm;

    [Header("Death Screen")]
    public DeathScreenManager DeathScreen;
    private DamageContext _reusableDamageContext;

    // ── Состояние ──────────────────────────────────────────────────
    public float CurrentHealth { get; private set; }
    public bool  IsDead        { get; private set; }

    private CharacterController _cc;
    private float _lastDamageTime = -9999f;
    private float _lastHealTime = -9999f;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        MaxHealth.BaseValue = InitialBaseHealth;
        CurrentHealth = MaxHealth.GetValue();
        UpdateUI();
        
        // ИНИЦИАЛИЗИРУЙТЕ ЗДЕСЬ, а не в TakeDamage
        _reusableDamageContext = new DamageContext();
        
        // Сбрасываем все эффекты при старте
        if (DamageGlowGroup != null) DamageGlowGroup.alpha = 0f;
        if (HealGlowGroup != null) HealGlowGroup.alpha = 0f;
        if (MediumHealthGroup != null) MediumHealthGroup.alpha = 0f;
        if (LowHealthGroup != null) LowHealthGroup.alpha = 0f;
    }

    private void Update()
    {
        if (IsDead) return;

        // ── Детекция агентов через SwarmManager ──
        if (Swarm != null)
        {
            float actualRadius = Swarm.AgentAttackRadius * HitboxRadiusMultiplier.GetValue();
            if (Swarm.IsAgentNearPlayer(transform.position, actualRadius))
            {
                TakeDamage(Swarm.AgentDamagePerSecond * Time.deltaTime, Swarm.gameObject);
            }
        }

        UpdateScreenEffects();
    }

    // ── Публичный API ──────────────────────────────────────────────

    public void TakeDamage(float amount, GameObject sender = null)
    {
        if (IsDead) return;

        // Убираем проверку на null, так как _reusableDamageContext уже инициализирован
        _reusableDamageContext.Sender = sender;
        _reusableDamageContext.Target = gameObject;
        _reusableDamageContext.RawDamage = amount;
        _reusableDamageContext.FinalDamage = amount;
        _reusableDamageContext.IsCancelled = false;

        // 2. Прогоняем урон через шину
        GameplayEventBus.ProcessDamage(_reusableDamageContext);

        // Если эффекты отменили урон, выходим
        if (_reusableDamageContext.IsCancelled) return;

        // 3. Применяем итоговый урон
        CurrentHealth = Mathf.Max(0f, CurrentHealth - _reusableDamageContext.FinalDamage);
        UpdateUI();
        
        // Запоминаем время урона для эффекта на экране
        if (_reusableDamageContext.FinalDamage > 0)
        {
            _lastDamageTime = Time.time;
        }

        if (CurrentHealth <= 0f) Die();
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        
        CurrentHealth = Mathf.Min(MaxHealth.GetValue(), CurrentHealth + amount);
        UpdateUI();

        // Запоминаем время лечения для эффекта на экране
        if (amount > 0)
        {
            _lastHealTime = Time.time;
        }
    }

    // ── Внутренняя логика ──────────────────────────────────────────

    private void UpdateScreenEffects()
    {
        // 1. Эффект получения урона
        if (DamageGlowGroup != null)
        {
            float targetAlpha = (Time.time - _lastDamageTime < ActionFadeDelay) ? 1f : 0f;
            float speed = targetAlpha > DamageGlowGroup.alpha ? ActionFadeInSpeed : ActionFadeOutSpeed;
            DamageGlowGroup.alpha = Mathf.Lerp(DamageGlowGroup.alpha, targetAlpha, speed * Time.deltaTime);
            if (DamageGlowGroup.alpha < 0.01f) DamageGlowGroup.alpha = 0f;
        }

        // 2. Эффект получения лечения
        if (HealGlowGroup != null)
        {
            float targetAlpha = (Time.time - _lastHealTime < ActionFadeDelay) ? 1f : 0f;
            float speed = targetAlpha > HealGlowGroup.alpha ? ActionFadeInSpeed : ActionFadeOutSpeed;
            HealGlowGroup.alpha = Mathf.Lerp(HealGlowGroup.alpha, targetAlpha, speed * Time.deltaTime);
            if (HealGlowGroup.alpha < 0.01f) HealGlowGroup.alpha = 0f;
        }

        // 3. Эффекты состояний здоровья (Низкое / Среднее)
        float healthPercent = CurrentHealth / MaxHealth.GetValue();
        bool isLow = healthPercent <= LowHealthThreshold;
        // Среднее здоровье активно, только если здоровье ниже среднего порога, но ВЫШЕ низкого
        bool isMedium = !isLow && healthPercent <= MediumHealthThreshold;

        if (MediumHealthGroup != null)
        {
            float targetAlpha = isMedium ? 1f : 0f;
            MediumHealthGroup.alpha = Mathf.Lerp(MediumHealthGroup.alpha, targetAlpha, StateFadeSpeed * Time.deltaTime);
            if (MediumHealthGroup.alpha < 0.01f) MediumHealthGroup.alpha = 0f;
        }

        if (LowHealthGroup != null)
        {
            float targetAlpha = isLow ? 1f : 0f;
            LowHealthGroup.alpha = Mathf.Lerp(LowHealthGroup.alpha, targetAlpha, StateFadeSpeed * Time.deltaTime);
            if (LowHealthGroup.alpha < 0.01f) LowHealthGroup.alpha = 0f;
        }
    }

    private void Die()
    {
        IsDead = true;
        Debug.Log("[PlayerHealth] Игрок погиб.");
        if (DeathScreen != null)
        {
            DeathScreen.TriggerDeath();
        }
    }

    private void UpdateUI()
    {
        if (HealthText != null)
            HealthText.text = Mathf.CeilToInt(CurrentHealth).ToString();
    }

    // Запасная отрисовка убрана, так как OnGUI запрещен (вызывает сильные аллокации и является устаревшим API).

    // Визуализация радиуса детекции в Scene View
    private void OnDrawGizmosSelected()
    {
        if (!TryGetComponent(out CharacterController cc)) return;
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
        if (Swarm != null) // ДОБАВЬТЕ ЭТУ ПРОВЕРКУ
        {
            Gizmos.DrawWireSphere(transform.position + Vector3.up * (cc.height * 0.5f), Swarm.AgentAttackRadius);
        }

    }
}
