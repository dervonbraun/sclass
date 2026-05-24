using UnityEngine;
using Sclass.EffectsSystem;

public enum ShopItemType { Consumable, Ammo, Weapon }

[System.Serializable]
public struct ItemCost
{
    public CurrencyType Currency;
    [Tooltip("Стоимость. Credits — целое число, мутации — float.")]
    public float Amount;
}

/// <summary>
/// ScriptableObject товара терминала.
/// Создать: ПКМ в Project → Sclass/Shop/Item
/// </summary>
[CreateAssetMenu(menuName = "Sclass/Shop/Item", fileName = "NewShopItem")]
public class ShopItemSO : ScriptableObject
{
    [Header("Описание")]
    public string ItemName = "Item";
    [TextArea(1, 3)] public string Description;
    public Sprite Icon;
    public ShopItemType Type;

    [Header("Цена")]
    public ItemCost[] Costs;

    [Header("Weapon")]
    [Tooltip("Только для Type = Weapon.")]
    public GameObject WeaponPrefab;
    [Tooltip("Слот в WeaponHolder (0 или 1).")]
    public int WeaponSlotIndex = 0;

    [Header("Ammo")]
    [Tooltip("Только для Type = Ammo. Количество патронов для восстановления.")]
    public int AmmoAmount = 30;
    [Tooltip("Слот оружия, которому пополняются патроны.")]
    public int AmmoWeaponSlot = 0;

    [Header("Consumable")]
    [Tooltip("Только для Type = Consumable.")]
    public MutationType ConsumableMutation;
    [Tooltip("Значение мутации (может быть отрицательным).")]
    public float ConsumableAmount = 10f;

    // ── Purchase logic ───────────────────────────────────────────────────────

    public bool CanAfford(PlayerWallet wallet, ElementalMutationManager mutations)
    {
        foreach (var cost in Costs)
        {
            float balance = GetBalance(cost.Currency, wallet, mutations);
            if (balance < cost.Amount) return false;
        }
        return true;
    }

    /// <summary>
    /// Списывает стоимость и применяет эффект покупки.
    /// Возвращает false если не хватает валюты.
    /// </summary>
    public bool TryPurchase(PlayerWallet wallet, ElementalMutationManager mutations, WeaponHolder weaponHolder)
    {
        if (!CanAfford(wallet, mutations)) return false;

        // Списываем
        foreach (var cost in Costs)
        {
            switch (cost.Currency)
            {
                case CurrencyType.Credits:
                    wallet.SpendCoins(Mathf.RoundToInt(cost.Amount));
                    break;
                case CurrencyType.Kinesia:
                    mutations.ModifyStat(MutationType.Kinesia, -cost.Amount);
                    break;
                case CurrencyType.Smallion:
                    mutations.ModifyStat(MutationType.Smallion, -cost.Amount);
                    break;
                case CurrencyType.Transfinite:
                    mutations.ModifyStat(MutationType.Transfinite, -cost.Amount);
                    break;
            }
        }

        // Применяем эффект
        switch (Type)
        {
            case ShopItemType.Weapon:
                weaponHolder?.SetSlot(WeaponSlotIndex, WeaponPrefab);
                break;

            case ShopItemType.Ammo:
                WeaponBase target = null;
                if (weaponHolder != null)
                {
                    // Если нужный слот активен — берём активное оружие, иначе null
                    if (weaponHolder.ActiveSlotIndex == AmmoWeaponSlot)
                        target = weaponHolder.ActiveWeapon;
                }
                target?.RestoreAmmo(AmmoAmount);
                break;

            case ShopItemType.Consumable:
                mutations?.ModifyStat(ConsumableMutation, ConsumableAmount);
                break;
        }

        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static float GetBalance(CurrencyType currency, PlayerWallet wallet, ElementalMutationManager mutations)
    {
        return currency switch
        {
            CurrencyType.Credits     => wallet != null ? wallet.Coins : 0f,
            CurrencyType.Kinesia     => mutations != null ? mutations.Kinesia     : 0f,
            CurrencyType.Smallion    => mutations != null ? mutations.Smallion    : 0f,
            CurrencyType.Transfinite => mutations != null ? mutations.Transfinite : 0f,
            _ => 0f
        };
    }

    public string FormatCosts()
    {
        if (Costs == null || Costs.Length == 0) return "Бесплатно";

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Costs.Length; i++)
        {
            if (i > 0) sb.Append("  ");
            sb.Append(CurrencyShortName(Costs[i].Currency));
            sb.Append(' ');
            sb.Append(Costs[i].Currency == CurrencyType.Credits
                ? Mathf.RoundToInt(Costs[i].Amount).ToString()
                : Costs[i].Amount.ToString("F0"));
        }
        return sb.ToString();
    }

    private static string CurrencyShortName(CurrencyType c) => c switch
    {
        CurrencyType.Credits     => "₵",
        CurrencyType.Kinesia     => "K",
        CurrencyType.Smallion    => "S",
        CurrencyType.Transfinite => "T",
        _ => "?"
    };
}
