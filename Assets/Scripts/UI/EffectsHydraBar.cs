using UnityEngine;
using UnityEngine.UI;
using Sclass.EffectsSystem;

namespace Sclass.UI
{
    [RequireComponent(typeof(Image))]
    public class EffectsHydraBar : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Скорость интерполяции весов сегментов.")]
        public float LerpSpeed = 5f;

        private Material    _hydraMaterial;
        private Image       _image;
        private CanvasGroup _canvasGroup;

        private readonly float[] _currentWeights = { 1f / 3f, 1f / 3f, 1f / 3f };
        private readonly float[] _targetWeights  = { 1f / 3f, 1f / 3f, 1f / 3f };
        private readonly Color[] _colors         = new Color[3];

        private readonly Vector4[] _shaderColors  = new Vector4[16];
        private readonly float[]   _shaderBorders = new float[16];

        private bool _hasData;

        // ── Lifecycle ────────────────────────────────────────────────────────────
        private void Awake()
        {
            _image = GetComponent<Image>();

            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;

            if (_image.material != null)
            {
                _hydraMaterial = new Material(_image.material);
                _image.material = _hydraMaterial;
            }
        }

        private void OnEnable()  => ElementalMutationManager.OnUIUpdate += HandleUIUpdate;
        private void OnDisable() => ElementalMutationManager.OnUIUpdate -= HandleUIUpdate;

        // ── Data input ───────────────────────────────────────────────────────────
        private void HandleUIUpdate(MutationUIData data)
        {
            _targetWeights[0] = data.KinesiaRatio;
            _targetWeights[1] = data.SmallionRatio;
            _targetWeights[2] = data.TransfiniteRatio;

            _colors[0] = data.KinesiaColor;
            _colors[1] = data.SmallionColor;
            _colors[2] = data.TransfiniteColor;

            _hasData = true;
        }

        // ── Update ───────────────────────────────────────────────────────────────
        private void Update()
        {
            if (_hydraMaterial == null) return;

            float targetAlpha = _hasData ? 1f : 0f;
            _canvasGroup.alpha = Mathf.Lerp(_canvasGroup.alpha, targetAlpha,
                LerpSpeed * Time.unscaledDeltaTime);

            if (!_hasData) return;

            LerpWeights();
            PushToShader();
        }

        private void LerpWeights()
        {
            float t = LerpSpeed * Time.unscaledDeltaTime;
            for (int i = 0; i < 3; i++)
                _currentWeights[i] = Mathf.Lerp(_currentWeights[i], _targetWeights[i], t);
        }

        private void PushToShader()
        {
            _hydraMaterial.SetInt("_Count", 3);

            float accumulated = 0f;
            for (int i = 0; i < 3; i++)
            {
                _shaderColors[i]  = (Vector4)_colors[i];
                accumulated      += _currentWeights[i];
                _shaderBorders[i] = accumulated;
            }

            _hydraMaterial.SetVectorArray("_EffectColors",  _shaderColors);
            _hydraMaterial.SetFloatArray("_EffectBorders", _shaderBorders);
            _hydraMaterial.SetFloat("_SingularityPulse", 0f);
            _hydraMaterial.SetInt("_SynergyType", 0);
        }
    }
}
