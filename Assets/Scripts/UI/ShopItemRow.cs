using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Вешается на префаб строки товара в терминале.
/// TerminalShopUI заполняет поля через Init().
/// </summary>
public class ShopItemRow : MonoBehaviour
{
    [Header("UI Elements")]
    public Image      IconImage;
    public TMP_Text   NameLabel;
    public TMP_Text   TypeLabel;
    public TMP_Text   CostLabel;
    public Image      SelectionHighlight;
    public Image      CannotAffordOverlay;

    [HideInInspector] public ShopItemSO Item;

    public void Init(ShopItemSO item)
    {
        Item = item;
        if (NameLabel  != null) NameLabel.text  = item.ItemName;
        if (TypeLabel  != null) TypeLabel.text   = item.Type.ToString().ToUpper();
        if (CostLabel  != null) CostLabel.text   = item.FormatCosts();
        if (IconImage  != null)
        {
            IconImage.sprite  = item.Icon;
            IconImage.enabled = item.Icon != null;
        }
        SetSelected(false);
        SetAffordable(true);
    }

    public void SetSelected(bool selected)
    {
        if (SelectionHighlight != null)
            SelectionHighlight.enabled = selected;
    }

    public void SetAffordable(bool affordable)
    {
        if (CannotAffordOverlay != null)
            CannotAffordOverlay.enabled = !affordable;
        if (CostLabel != null)
            CostLabel.color = affordable ? Color.white : new Color(1f, 0.3f, 0.3f);
    }
}
