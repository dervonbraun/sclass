using UnityEngine;
using UnityEngine.UI;
using Sclass.EffectsSystem;

namespace Sclass.UI
{
    /// <summary>
    /// Gradient bar driven by ElementalMutationManager.OnUIUpdate.
    /// Always shows three segments: Kinesia | Smallion | Transfinite.
    /// In God Singularity mode weights lock to 1/3 each and a pulse value
    /// (_SingularityPulse 0→1) is sent to the shader for the "reactor boil" effect.
    ///
    /// Shader interface: _Count (int), _EffectColors (Vector4[16]),
    ///                   _EffectBorders (float[16]), _SingularityPulse (float).
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class EffectsHydraBar : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Скорость интерполяции весов сегментов.")]
        public float LerpSpeed = 5f;

        [Header("Singularity Pulse")]
        [Tooltip("Скорость пульсации в режиме Сингулярности (циклов в секунду).")]
        public float PulseFrequency = 3f;

        private Material    _hydraMaterial;
        private Image       _image;
        private CanvasGroup _canvasGroup;

        // Three fixed slots: [0] Kinesia, [1] Smallion, [2] Transfinite
        private readonly float[] _currentWeights = { 1f / 3f, 1f / 3f, 1f / 3f };
        private readonly float[] _targetWeights  = { 1f / 3f, 1f / 3f, 1f / 3f };
        private readonly Color[] _colors         = new Color[3];

        // Shader arrays — keep size 16 to match existing shader declaration
        private readonly Vector4[] _shaderColors  = new Vector4[16];
        private readonly float[]   _shaderBorders = new float[16];

        private bool _hasData;
        private bool _isSingularityActive;
        private Sclass.EffectsSystem.SynergyType _activeSynergy;

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

        private void OnEnable()
        {
            ElementalMutationManager.OnUIUpdate        += HandleUIUpdate;
            SingularityController.OnSingularityChanged += HandleSingularityChanged;
            SynergyManager.OnActiveSynergyChanged      += HandleSynergyChanged;
        }

        private void OnDisable()
        {
            ElementalMutationManager.OnUIUpdate        -= HandleUIUpdate;
            SingularityController.OnSingularityChanged -= HandleSingularityChanged;
            SynergyManager.OnActiveSynergyChanged      -= HandleSynergyChanged;
        }

        // ── Data input ───────────────────────────────────────────────────────────
        private void HandleUIUpdate(MutationUIData data)
        {
            // In singularity the weights are locked to 1/3 by SingularityController's
            // burn mechanic (all three drain equally), but we enforce it visually too.
            if (!_isSingularityActive)
            {
                _targetWeights[0] = data.KinesiaRatio;
                _targetWeights[1] = data.SmallionRatio;
                _targetWeights[2] = data.TransfiniteRatio;
            }

            // Colors are always updated so the transition looks right on exit
            _colors[0] = data.KinesiaColor;
            _colors[1] = data.SmallionColor;
            _colors[2] = data.TransfiniteColor;

            _hasData = true;
        }

        private void HandleSingularityChanged(bool active)
        {
            _isSingularityActive = active;

            if (active)
            {
                _targetWeights[0] = 1f / 3f;
                _targetWeights[1] = 1f / 3f;
                _targetWeights[2] = 1f / 3f;
            }
        }

        private void HandleSynergyChanged(SynergyType type)
        {
            _activeSynergy = type;
        }

        // ── Update ───────────────────────────────────────────────────────────────
        private void Update()
        {
            if (_hydraMaterial == null) return;

            // Use unscaledTime for UI — singularity drops timeScale to 0.2
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

            // _SingularityPulse: reactor-boil flicker for God Singularity mode.
            float pulse = _isSingularityActive
                ? Mathf.PingPong(Time.unscaledTime * PulseFrequency, 1f)
                : 0f;
            _hydraMaterial.SetFloat("_SingularityPulse", pulse);

            // _SynergyType: 0=None, 1=Wanderer(electric), 2=Darkness(lens), 3=Tax(fire).
            // Shader uses this int to select the junction effect between dominant segments.
            _hydraMaterial.SetInt("_SynergyType", (int)_activeSynergy);
        }
    }
}
