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
        
        [Tooltip("Задержка (КД) перед втеканием следующего эффекта при спаме (сек)")]
        public float EffectFlowCooldown = 0.3f;

        [Header("Shader Properties")]
        private Material _hydraMaterial;
        private Image _image;
        private CanvasGroup _canvasGroup;

        // Внутренние данные для интерполяции
        private class EffectBarData
        {
            public BaseEffect Effect;
            public float CurrentWeight;
            public float TargetWeight;
            public Color CurrentColor;
            public float BasePulseMultiplier = 1f;
        }

        private List<EffectBarData> _barData = new List<EffectBarData>();
        
        // Очередь для защиты от спама
        private Queue<BaseEffect> _pendingEffects = new Queue<BaseEffect>();
        private float _lastEffectTime = -999f;
        
        // Массивы для передачи в шейдер (до 16 эффектов)
        private Vector4[] _shaderColors = new Vector4[16];
        private float[] _shaderBorders = new float[16];

        private void Awake()
        {
            _image = GetComponent<Image>();
            
            // Получаем или добавляем CanvasGroup для плавного появления самой полоски
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            
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
                TargetManager.OnEffectAdded += QueueEffectAdded;
                TargetManager.OnEffectRemoved += HandleEffectRemoved;
                
                foreach (var effect in TargetManager.ActiveEffects)
                {
                    QueueEffectAdded(effect);
                }
            }
        }

        private void OnDisable()
        {
            if (TargetManager != null)
            {
                TargetManager.OnEffectAdded -= QueueEffectAdded;
                TargetManager.OnEffectRemoved -= HandleEffectRemoved;
            }
        }

        private void QueueEffectAdded(BaseEffect effect)
        {
            if (!_pendingEffects.Contains(effect))
            {
                _pendingEffects.Enqueue(effect);
            }
        }

        private void HandleEffectAdded(BaseEffect effect)
        {
            // Если массив близок к переполнению, агрессивно удаляем "мертвые" вытекающие эффекты
            while (_barData.Count >= 16)
            {
                int deadIndex = _barData.FindIndex(d => d.Effect == null);
                if (deadIndex != -1) 
                    _barData.RemoveAt(deadIndex);
                else 
                    break;
            }

            _barData.Add(new EffectBarData 
            { 
                Effect = effect, 
                CurrentWeight = 0f, 
                CurrentColor = effect.EffectColor 
            });
        }

        private void HandleEffectRemoved(BaseEffect effect)
        {
            var data = _barData.Find(d => d.Effect == effect);
            if (data != null)
            {
                data.Effect = null;
                data.TargetWeight = 0f;
            }
        }

        private void Update()
        {
            if (_hydraMaterial == null) return;

            ProcessPendingQueue();
            UpdateWeights();
            UpdateShaderArrays();
            UpdateBarVisibility();
        }

        private void ProcessPendingQueue()
        {
            if (_pendingEffects.Count > 0 && Time.time - _lastEffectTime > EffectFlowCooldown)
            {
                BaseEffect nextEffect = _pendingEffects.Dequeue();
                
                // Если эффект уже успели удалить, пока он был в очереди — пропускаем
                if (nextEffect != null && nextEffect.IsActive)
                {
                    HandleEffectAdded(nextEffect);
                    _lastEffectTime = Time.time;
                }
            }
        }

        private void UpdateBarVisibility()
        {
            // Плавно показываем полоску, если есть живые эффекты, и прячем, если пусто
            bool hasActiveEffects = _barData.Exists(d => d.Effect != null);
            float targetAlpha = hasActiveEffects ? 1f : 0f;
            _canvasGroup.alpha = Mathf.Lerp(_canvasGroup.alpha, targetAlpha, Time.deltaTime * LerpSpeed);
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
                if (data.Effect == null && data.CurrentWeight < 0.01f)
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
