using UnityEngine;
using Sclass.EffectsSystem;
using Sclass.EffectsSystem.Demo;

public class EffectTester : MonoBehaviour
{
    [Header("Effect Settings (ScriptableObjects)")]
    public EffectSettingsSO HypertrophySO;
    public EffectSettingsSO KineticMirrorSO;

    private EffectManager _manager;
    private HypertrophyEffect _hyper;
    private KineticMirrorEffect _mirror;

    void Start()
    {
        _manager = GetComponent<EffectManager>();
    }

    void Update()
    {
        // Нажимаем 'H' чтобы включить/выключить Гипертрофию
        if (Input.GetKeyDown(KeyCode.H))
        {
            if (_hyper != null && _hyper.IsActive)
            {
                _manager.RemoveEffect(_hyper);
                _hyper = null;
                Debug.Log("Гипертрофия снята!");
            }
            else
            {
                _hyper = new HypertrophyEffect { Settings = HypertrophySO };
                _manager.AddEffect(_hyper);
                Debug.Log("Гипертрофия наложена! (Размер и HP увеличены)");
            }
        }

        // Нажимаем 'M' чтобы включить/выключить Зеркало
        if (Input.GetKeyDown(KeyCode.M))
        {
            if (_mirror != null && _mirror.IsActive)
            {
                _manager.RemoveEffect(_mirror);
                _mirror = null;
                Debug.Log("Зеркало снято!");
            }
            else
            {
                _mirror = new KineticMirrorEffect { Settings = KineticMirrorSO };
                _manager.AddEffect(_mirror);
                Debug.Log("Зеркало наложено! (35% шанс отменить и отразить урон)");
            }
        }
    }
}
