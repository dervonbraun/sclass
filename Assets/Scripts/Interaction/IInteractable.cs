/// <summary>
/// Реализуй на любом MonoBehaviour, чтобы PlayerInteractor мог с ним взаимодействовать.
/// </summary>
public interface IInteractable
{
    /// <summary>Текст подсказки (например «Купить»).</summary>
    string GetPrompt();

    /// <summary>Нажата кнопка взаимодействия (E) — подтверждение / действие.</summary>
    void Interact(PlayerInteractor interactor);

    /// <summary>Луч игрока вошёл в объект.</summary>
    void OnHoverEnter(PlayerInteractor interactor);

    /// <summary>Луч игрока покинул объект.</summary>
    void OnHoverExit(PlayerInteractor interactor);
}
