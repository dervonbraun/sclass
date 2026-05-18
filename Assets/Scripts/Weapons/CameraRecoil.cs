using UnityEngine;

/// <summary>
/// Компонент отдачи камеры — аналог AdvancedCamRecoil из EXAMPLE, без DOTween.
/// Вешается на дочерний объект камеры (RecoilPivot).
/// Оружие вызывает Fire() / AimFire() — и больше ничего не знает о камере.
/// </summary>
public class CameraRecoil : MonoBehaviour
{
    // _currentRotation — «цель» которую мы плавно достигаем (_rot)
    // По аналогии с EXAMPLE: _currentRotation → 0 (return), _rot → _currentRotation (lag)
    private Vector3 _currentRotation;
    private Vector3 _rot;

    // Текущий профиль, заданный активным оружием
    private WeaponSettingsSO _profile;

    private void FixedUpdate()
    {
        if (_profile == null) return;

        // Плавный возврат к нулю (return speed)
        _currentRotation = Vector3.Lerp(
            _currentRotation,
            Vector3.zero,
            _profile.recoilReturnSpeed * Time.fixedDeltaTime
        );

        // Визуальная задержка (rotation speed/lag)
        _rot = Vector3.Lerp(
            _rot,
            _currentRotation,
            _profile.recoilRotationSpeed * Time.fixedDeltaTime
        );

        transform.localRotation = Quaternion.Euler(_rot);
    }

    // ── Публичный API ──────────────────────────────────────────────────

    /// <summary>Вызывается активным оружием при каждом выстреле (hip fire).</summary>
    public void Fire()
    {
        if (_profile == null) return;

        _currentRotation += new Vector3(
            -_profile.recoilCamRotation.x,
            Random.Range(-_profile.recoilCamRotation.y, _profile.recoilCamRotation.y),
            Random.Range(-_profile.recoilCamRotation.z, _profile.recoilCamRotation.z)
        );
    }

    /// <summary>Вызывается при выстреле в режиме ADS (прицеливание).</summary>
    public void AimFire()
    {
        if (_profile == null) return;

        _currentRotation += new Vector3(
            -_profile.recoilCamRotationADS.x,
            Random.Range(-_profile.recoilCamRotationADS.y, _profile.recoilCamRotationADS.y),
            Random.Range(-_profile.recoilCamRotationADS.z, _profile.recoilCamRotationADS.z)
        );
    }

    /// <summary>
    /// Устанавливает профиль нового оружия.
    /// Вызывается WeaponHolder при смене оружия.
    /// </summary>
    public void SetProfile(WeaponSettingsSO profile)
    {
        _profile = profile;
        // Сбрасываем накопленную отдачу предыдущего оружия
        _currentRotation = Vector3.zero;
        _rot = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }
}
