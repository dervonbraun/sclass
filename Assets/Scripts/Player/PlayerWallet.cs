using UnityEngine;

/// <summary>
/// Кошелёк игрока. Монеты автоматически начисляются при смерти агентов роя.
/// </summary>
public class PlayerWallet : MonoBehaviour
{
    public static PlayerWallet Instance { get; private set; }

    /// <summary>Срабатывает при каждом изменении баланса. Передаёт новый баланс.</summary>
    public static event System.Action<int> OnCoinsChanged;

    [Header("Настройки")]
    [Tooltip("Начальный баланс монет.")]
    public int StartingCoins = 0;

    public int Coins { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Coins = StartingCoins;
    }

    private void OnEnable()
    {
        AgentHealth.OnAgentKilledForCoins += HandleAgentKilled;
    }

    private void OnDisable()
    {
        AgentHealth.OnAgentKilledForCoins -= HandleAgentKilled;
    }

    private void HandleAgentKilled(int amount)
    {
        AddCoins(amount);
    }

    public void AddCoins(int amount)
    {
        if (amount <= 0) return;
        Coins += amount;
        OnCoinsChanged?.Invoke(Coins);
    }

    public bool SpendCoins(int amount)
    {
        if (amount > Coins) return false;
        Coins -= amount;
        OnCoinsChanged?.Invoke(Coins);
        return true;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
