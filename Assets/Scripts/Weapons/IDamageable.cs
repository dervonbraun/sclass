/// <summary>
/// Интерфейс для объектов, которые могут получать урон.
/// Реализуй его на персонажах, щитах, объектах окружения.
/// </summary>
public interface IDamageable
{
    void TakeDamage(float amount);
}
