using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Sclass.EffectsSystem;

namespace Sclass.UI
{
    [RequireComponent(typeof(Image))]
    public class EffectsHydraBar : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Менеджер эффектов игрока")]
        public EffectManager TargetManager;
        
        [Tooltip("Скорость 'втекания' и вытеснения эффектов")]
        public float LerpSpeed = 5f;

        [Header("Shader Properties")]
        private Material _hydraMaterial;
        private Image _image;

        // Внутренние данные для интерполяции
        private class EffectBarData
        {
            public BaseEffect Effect;
            public float CurrentWeight;
            public float TargetWeight;
            public Color CurrentColor;
            public float BasePulseMultiplier = 1f; // Для эффекта "Агонии"
        }

        private List<EffectBarData> _barData = new List<EffectBarData>();
        
        // Массивы для передачи в шейдер (до 16 эффектов)
        private Vector4[] _shaderColors = new Vector4[16];
        private float[] _shaderBorders = new float[16];

        private void Awake()
        {
            _image = GetComponent<Image>();
            
            // Клонируем материал, чтобы не менять ассет
            if (_image.material != null)
            {
                _hydraMaterial = new Material(_image.material);
                _image.material = _hydraMaterial;
            }
        }

        private void OnEnable()
        {
            if (TargetManager != null)
            {
                TargetManager.OnEffectAdded += HandleEffectAdded;
                TargetManager.OnEffectRemoved += HandleEffectRemoved;
                
                // Загружаем уже активные эффекты
                foreach (var effect in TargetManager.ActiveEffects)
                {
                    HandleEffectAdded(effect);
                }
            }
        }

        private void OnDisable()
        {
            if (TargetManager != null)
            {
                TargetManager.OnEffectAdded -= HandleEffectAdded;
                TargetManager.OnEffectRemoved -= HandleEffectRemoved;
            }
        }

        private void HandleEffectAdded(BaseEffect effect)
        {
            _barData.Add(new EffectBarData 
            { 
                Effect = effect, 
                CurrentWeight = 0f, // Начинаем с нуля (эффект втекания)
                CurrentColor = effect.EffectColor 
            });
        }

        private void HandleEffectRemoved(BaseEffect effect)
        {
            // Не удаляем сразу, а обнуляем TargetWeight, чтобы цвет плавно "вытек"
            var data = _barData.Find(d => d.Effect == effect);
            if (data != null)
            {
                data.Effect = null; // Помечаем как "мертвый"
                data.TargetWeight = 0f;
            }
        }

        private void Update()
        {
            if (_hydraMaterial == null) return;

            UpdateWeights();
            UpdateShaderArrays();
        }

        private void UpdateWeights()
        {
            float totalTargetWeight = 0f;

            // 1. Считаем целевые веса (только для живых эффектов)
            foreach (var data in _barData)
            {
                if (data.Effect != null)
                {
                    // Эффект "Агонии": можно добавить публичный метод Pulse(effect) 
                    // или читать какое-то свойство из эффекта. Здесь читаем HudWeight.
                    data.TargetWeight = data.Effect.HudWeight * data.BasePulseMultiplier;
                    data.CurrentColor = data.Effect.EffectColor;
                }
                else
                {
                    data.TargetWeight = 0f;
                }
                totalTargetWeight += data.TargetWeight;
            }

            // 2. Интерполируем CurrentWeight к нормализованному TargetWeight
            for (int i = _barData.Count - 1; i >= 0; i--)
            {
                var data = _barData[i];
                float normalizedTarget = totalTargetWeight > 0f ? (data.TargetWeight / totalTargetWeight) : 0f;
                
                data.CurrentWeight = Mathf.Lerp(data.CurrentWeight, normalizedTarget, Time.deltaTime * LerpSpeed);

                // Если вес стал почти нулем и эффект удален — полностью удаляем его из списка
                if (data.Effect == null && data.CurrentWeight < 0.001f)
                {
                    _barData.RemoveAt(i);
                }
            }
        }

        private void UpdateShaderArrays()
        {
            int count = Mathf.Min(_barData.Count, 16);
            _hydraMaterial.SetInt("_Count", count);

            if (count == 0) return;

            // Нормализуем текущие веса, так как их сумма может быть != 1 во время Lerp'а
            float totalCurrentWeight = 0f;
            for (int i = 0; i < count; i++) totalCurrentWeight += _barData[i].CurrentWeight;

            float currentAccumulatedBorder = 0f;

            for (int i = 0; i < count; i++)
            {
                var data = _barData[i];
                
                // Записываем цвет
                _shaderColors[i] = data.CurrentColor;

                // Записываем границу
                float normalizedWeight = totalCurrentWeight > 0f ? (data.CurrentWeight / totalCurrentWeight) : 0f;
                currentAccumulatedBorder += normalizedWeight;
                
                _shaderBorders[i] = currentAccumulatedBorder;
            }

            // Передаем в шейдер
            _hydraMaterial.SetVectorArray("_EffectColors", _shaderColors);
            _hydraMaterial.SetFloatArray("_EffectBorders", _shaderBorders);
        }

        /// <summary>
        /// Вызов эффекта "Агонии" из других скриптов.
        /// Заставляет цвет временно мигать и занимать больше места.
        /// </summary>
        public void TriggerAgonyPulse(BaseEffect effect, float intensityMult = 2f, float duration = 1f)
        {
            var data = _barData.Find(d => d.Effect == effect);
            if (data != null)
            {
                // Запускаем корутину или Tween для пульсации (здесь упрощенно для примера)
                StartCoroutine(PulseRoutine(data, intensityMult, duration));
            }
        }

        private System.Collections.IEnumerator PulseRoutine(EffectBarData data, float mult, float duration)
        {
            data.BasePulseMultiplier = mult;
            yield return new WaitForSeconds(duration);
            if (data != null) data.BasePulseMultiplier = 1f;
        }
    }
}
