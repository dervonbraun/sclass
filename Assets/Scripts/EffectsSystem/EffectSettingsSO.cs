using UnityEngine;

namespace Sclass.EffectsSystem
{
    /// <summary>
    /// Конфигурация эффекта (ScriptableObject).
    /// Позволяет дизайнерам настраивать цвета, иконки и веса эффектов без изменения кода.
    /// </summary>
    [CreateAssetMenu(fileName = "NewEffectSettings", menuName = "Sclass/Effects/Effect Settings")]
    public class EffectSettingsSO : ScriptableObject
    {
        [Header("Отображение (UI)")]
        [Tooltip("Имя эффекта для локализации/UI")]
        public string EffectName = "Unknown Effect";
        
        [Tooltip("Цвет эффекта на полосе Hydra Bar")]
        public Color EffectColor = Color.white;
        
        [Tooltip("Базовый вес эффекта на полосе (чем больше, тем шире)")]
        [Range(0.1f, 10f)]
        public float HudWeight = 1f;
        
        [Tooltip("Опциональная иконка эффекта")]
        public Sprite Icon;
    }
}
