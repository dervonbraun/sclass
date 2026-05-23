using Sclass.EffectsSystem;
using UnityEngine;

/// <summary>
/// Облако элементарного эффекта, остающееся на месте гибели агента.
/// Инициализируется через Init() сразу после Instantiate.
/// </summary>
public class AgentCloud : MonoBehaviour
{
    [Tooltip("Радиус облака — если игрок внутри, получает эффект.")]
    public float Radius = 3f;

    [Tooltip("Время жизни облака в секундах.")]
    public float Lifetime = 8f;

    private MutationType _mutationType;
    private float _amountPerSecond;
    private Transform _player;
    private ElementalMutationManager _mutationManager;
    private float _timer;
    private System.Action _onDestroyed;

    public void Init(Transform player, MutationType mutationType, float amountPerSecond, System.Action onDestroyed = null)
    {
        _player = player;
        _mutationType = mutationType;
        _amountPerSecond = amountPerSecond;
        _onDestroyed = onDestroyed;

        if (player != null)
            _mutationManager = player.GetComponentInChildren<ElementalMutationManager>();
    }

    private void Update()
    {
        _timer += Time.deltaTime;
        if (_timer >= Lifetime)
        {
            _onDestroyed?.Invoke();
            Destroy(gameObject);
            return;
        }

        if (_mutationManager == null || _player == null) return;

        if ((_player.position - transform.position).sqrMagnitude <= Radius * Radius)
            _mutationManager.ModifyStat(_mutationType, _amountPerSecond * Time.deltaTime);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0f, 1f, 0.3f);
        Gizmos.DrawSphere(transform.position, Radius);
    }
#endif
}
