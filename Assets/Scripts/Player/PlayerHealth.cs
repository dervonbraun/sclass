using UnityEngine;
using UnityEngine.UI;
using Sclass.EffectsSystem;

[System.Serializable]
public struct MutationGlowColors
{
    public MutationType Type;
    [Tooltip("Цвет A (базовый акцент эффекта).")]
    public Color ColorA;
    [Tooltip("Цвет B.")]
    public Color ColorB;
    [Tooltip("Цвет C.")]
    public Color ColorC;
}

/// <summary>
/// Детекция контакта с роем → применение элементарных эффектов.
/// Смерть обрабатывается в ElementalMutationManager.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerHealth : MonoBehaviour
{
    [Header("Swarm Contact")]
    public ModifiableStat HitboxRadiusMultiplier = new ModifiableStat(1f);

    [Header("UI — Glow Border")]
    [Tooltip("CanvasGroup на объекте с Image — управляет видимостью рамки.")]
    public CanvasGroup GlowBorderGroup;
    [Tooltip("Материал — игрок получает эффект (стат растёт).")]
    public Material GainMaterial;
    [Tooltip("Материал — игрок теряет эффект (стат убывает).")]
    public Material LoseMaterial;
    [Tooltip("Цвет D: заменяет Color A в материале потери.")]
    public Color ColorD = Color.red;
    [Tooltip("Цвета A/B/C для каждого типа мутации.")]
    public MutationGlowColors[] MutationColors = new MutationGlowColors[]
    {
        new MutationGlowColors { Type = MutationType.Kinesia,     ColorA = new Color(0.20f, 0.90f, 0.20f), ColorB = new Color(0.10f, 0.60f, 0.10f), ColorC = new Color(0.50f, 1.00f, 0.50f) },
        new MutationGlowColors { Type = MutationType.Smallion,    ColorA = new Color(0.20f, 0.50f, 1.00f), ColorB = new Color(0.10f, 0.30f, 0.80f), ColorC = new Color(0.60f, 0.80f, 1.00f) },
        new MutationGlowColors { Type = MutationType.Transfinite, ColorA = new Color(1.00f, 0.20f, 0.20f), ColorB = new Color(0.80f, 0.10f, 0.10f), ColorC = new Color(1.00f, 0.60f, 0.60f) },
    };

    [Header("Glow Border — Fade")]
    public float GlowFadeInSpeed  = 10f;
    public float GlowFadeOutSpeed = 2f;
    [Tooltip("Сколько секунд рамка держится после последнего изменения мутации.")]
    public float GlowFadeDelay = 0.5f;

    [Header("Dependencies")]
    public SwarmManager Swarm;
    public ElementalMutationManager MutationManager;

    [Header("Death Screen")]
    public DeathScreenManager DeathScreen;

    public bool IsDead { get; private set; }

    /// <summary>
    /// Set by FlickeringWanderer while the player is sprinting.
    /// Prevents swarm contact from draining mutation stats (invisibility).
    /// </summary>
    [HideInInspector] public bool IsInvisible = false;

    private Image    _glowImage;
    private Material _gainInstance;
    private Material _lossInstance;
    private float    _lastGlowTime = -9999f;

    private float _prevKinesia;
    private float _prevSmallion;
    private float _prevTransfinite;
    private bool  _mutationValuesInitialized;

    private void Awake()
    {
        if (GlowBorderGroup != null)
        {
            _glowImage = GlowBorderGroup.GetComponent<Image>();
            GlowBorderGroup.alpha = 0f;
        }

        if (GainMaterial != null) _gainInstance = new Material(GainMaterial);
        if (LoseMaterial != null) _lossInstance = new Material(LoseMaterial);
    }

    private void Update()
    {
        if (IsDead) return;

        if (!_mutationValuesInitialized && MutationManager != null)
        {
            _prevKinesia     = MutationManager.Kinesia;
            _prevSmallion    = MutationManager.Smallion;
            _prevTransfinite = MutationManager.Transfinite;
            _mutationValuesInitialized = true;
        }

        if (!IsInvisible && Swarm != null && MutationManager != null)
        {
            float actualRadius = Swarm.AgentAttackRadius * HitboxRadiusMultiplier.GetValue();
            if (Swarm.IsAgentNearPlayer(transform.position, actualRadius))
            {
                foreach (var effect in Swarm.MutationEffects)
                    MutationManager.ModifyStat(effect.Type, effect.AmountPerSecond * Time.deltaTime);
            }
        }

        if (MutationManager != null && _mutationValuesInitialized)
        {
            DetectMutationDelta(MutationType.Kinesia,     MutationManager.Kinesia,     _prevKinesia);
            DetectMutationDelta(MutationType.Smallion,    MutationManager.Smallion,    _prevSmallion);
            DetectMutationDelta(MutationType.Transfinite, MutationManager.Transfinite, _prevTransfinite);

            _prevKinesia     = MutationManager.Kinesia;
            _prevSmallion    = MutationManager.Smallion;
            _prevTransfinite = MutationManager.Transfinite;
        }

        UpdateGlowBorder();
    }

    public void Die()
    {
        IsDead = true;
        if (DeathScreen != null)
            DeathScreen.TriggerDeath();
    }

    private void DetectMutationDelta(MutationType type, float current, float prev)
    {
        float delta = current - prev;
        if (Mathf.Abs(delta) < 0.0001f) return;
        ShowGlowBorder(type, gaining: delta > 0f);
    }

    private void ShowGlowBorder(MutationType type, bool gaining)
    {
        if (_glowImage == null) return;

        Material instance = gaining ? _gainInstance : _lossInstance;
        if (instance == null) return;

        MutationGlowColors colors = default;
        bool found = false;
        foreach (var entry in MutationColors)
        {
            if (entry.Type == type) { colors = entry; found = true; break; }
        }
        if (!found) return;

        instance.SetColor("_ColorA", gaining ? colors.ColorA : ColorD);
        instance.SetColor("_ColorB", colors.ColorB);
        instance.SetColor("_ColorC", colors.ColorC);

        _glowImage.material = instance;
        _lastGlowTime = Time.time;
    }

    private void UpdateGlowBorder()
    {
        if (GlowBorderGroup == null) return;

        float target = (Time.time - _lastGlowTime < GlowFadeDelay) ? 1f : 0f;
        float speed  = target > GlowBorderGroup.alpha ? GlowFadeInSpeed : GlowFadeOutSpeed;
        GlowBorderGroup.alpha = Mathf.Lerp(GlowBorderGroup.alpha, target, speed * Time.deltaTime);
        if (GlowBorderGroup.alpha < 0.01f) GlowBorderGroup.alpha = 0f;
    }

    private void OnDrawGizmosSelected()
    {
        if (!TryGetComponent(out CharacterController cc)) return;
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
        if (Swarm != null)
            Gizmos.DrawWireSphere(transform.position + Vector3.up * (cc.height * 0.5f), Swarm.AgentAttackRadius);
    }
}
