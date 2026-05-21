using UnityEngine;

/// <summary>
/// Здоровье агента роя. Вешать на корневой объект AgentPrefab.
/// Реализует IDamageable — Projectile.ApplyDamage() вызывает его автоматически.
///
/// ВАЖНО: не деактивирует GameObject (это сломает Job TransformAccessArray).
/// При смерти: скрывает визуал, отключает все коллайдеры, телепортирует под карту.
/// </summary>
public class AgentHealth : MonoBehaviour, IDamageable
{
    /// <summary>Fires on every agent death. SingularityController listens to this for kill restoration.</summary>
    public static event System.Action OnAgentKilled;

    [Header("Настройки")]
    [Tooltip("Максимальное HP агента.")]
    public float MaxHealth = 30f;

    [Tooltip("Эффект смерти (опционально).")]
    public GameObject DeathEffectPrefab;

    // Индекс в массиве SwarmManager._agents — назначается при спавне
    [HideInInspector] public int AgentIndex = -1;

    private float _currentHealth;
    private bool  _isDead;
    private SwarmManager _swarmManager;

    // Компоненты для скрытия при смерти (без деактивации GO)
    private Renderer[]  _renderers;
    private Collider[]  _colliders;

    private void Awake()
    {
        _currentHealth = MaxHealth;
        _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        _colliders = GetComponentsInChildren<Collider>(includeInactive: true);
    }

    /// <summary>Вызывается SwarmManager сразу после Instantiate.</summary>
    public void Init(int index, SwarmManager manager)
    {
        AgentIndex    = index;
        _swarmManager = manager;
    }

    public void TakeDamage(float amount)
    {
        if (_isDead) return;

        _currentHealth -= amount;

        if (_currentHealth <= 0f)
            Die();
    }

    private void Die()
    {
        if (_isDead) return;
        _isDead = true;
        _currentHealth = 0f;

        // Скрываем визуал
        foreach (var r in _renderers) r.enabled = false;

        // Отключаем ВСЕ коллайдеры
        foreach (var c in _colliders) c.enabled = false;

        // Переносим на слой Ignore Raycast — пули и OverlapSphere его не видят
        SetLayerRecursive(gameObject, 2); // 2 = Ignore Raycast

        // Запоминаем позицию ДО телепорта, чтобы эффект появился в правильном месте
        UnityEngine.Vector3 deathPosition = transform.position;

        // Телепортируем глубоко под карту — агент невидим и коллизий нет,
        // но Job System по-прежнему может двигать его (transform остаётся активным)
        transform.position = new UnityEngine.Vector3(deathPosition.x, -1000f, deathPosition.z);

        // Эффект смерти
        if (DeathEffectPrefab != null)
            Instantiate(DeathEffectPrefab, deathPosition, UnityEngine.Quaternion.identity);

        // Сообщаем SwarmManager: пометить агента как Dead в NativeArray
        _swarmManager?.KillAgent(AgentIndex);
        OnAgentKilled?.Invoke();
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
