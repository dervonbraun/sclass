using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class SecondFrameFader : MonoBehaviour
{
    public CanvasGroup group;
    public float duration;
    public float fadeTime;
    public System.Action onComplete;

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(duration);
        float t = 0;
        while (t < fadeTime)
        {
            t += Time.deltaTime;
            if (group != null) group.alpha = Mathf.Lerp(1f, 0f, t / fadeTime);
            yield return null;
        }
        if (group != null) group.alpha = 0f;

        onComplete?.Invoke();

        Destroy(gameObject);
    }
}

public class DeathScreenManager : MonoBehaviour
{
    [Header("UI Elements")]
    public GameObject DeathScreenContainer;
    public CanvasGroup FirstFrame;
    public CanvasGroup SecondFrame;
    public TMP_Text DeathText;

    [Header("First Frame Settings")]
    public float firstFrameDuration = 0.5f;
    public float firstFrameFadeTime = 1.5f;

    [Header("Second Frame Settings")]
    public float secondFrameDuration = 4f;
    public float secondFrameFadeTime = 1.5f;

    [Header("Phrases")]
    public DeathPhrasesSO phrasesData;

    private void Start()
    {
        if (DeathScreenContainer != null)
            DeathScreenContainer.SetActive(false);
    }

    public void TriggerDeath()
    {
        if (DeathScreenContainer == null) return;

        FindObjectOfType<PlayerInputBlocker>()?.SetInputBlocked(true);

        DeathScreenContainer.SetActive(true);

        if (FirstFrame != null)
        {
            FirstFrame.gameObject.SetActive(true);
            FirstFrame.alpha = 1f;
        }

        if (SecondFrame != null)
        {
            SecondFrame.gameObject.SetActive(true);
            SecondFrame.alpha = 1f;
        }

        if (phrasesData != null && DeathText != null)
            DeathText.text = phrasesData.GetRandomPhrase();

        StartCoroutine(DeathSequence());
    }

    private IEnumerator DeathSequence()
    {
        yield return new WaitForSeconds(firstFrameDuration);

        if (FirstFrame != null)
        {
            float t = 0;
            while (t < firstFrameFadeTime)
            {
                t += Time.deltaTime;
                FirstFrame.alpha = Mathf.Lerp(1f, 0f, t / firstFrameFadeTime);
                yield return null;
            }
            FirstFrame.alpha = 0f;
            FirstFrame.gameObject.SetActive(false);
        }

        Canvas parentCanvas = DeathScreenContainer.GetComponentInParent<Canvas>();
        CanvasScaler parentScaler = parentCanvas != null ? parentCanvas.GetComponent<CanvasScaler>() : null;

        DeathScreenContainer.transform.SetParent(null);
        DontDestroyOnLoad(DeathScreenContainer);

        Canvas canvas = DeathScreenContainer.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = DeathScreenContainer.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;

            if (parentScaler != null)
            {
                CanvasScaler scaler = DeathScreenContainer.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = parentScaler.uiScaleMode;
                scaler.referenceResolution = parentScaler.referenceResolution;
                scaler.matchWidthOrHeight = parentScaler.matchWidthOrHeight;
            }
        }
        else
        {
            canvas.sortingOrder = 999;
        }

        SecondFrameFader fader = DeathScreenContainer.AddComponent<SecondFrameFader>();
        fader.group = SecondFrame;
        fader.duration = secondFrameDuration;
        fader.fadeTime = secondFrameFadeTime;
        fader.onComplete = () =>
        {
            // После загрузки сцены старая ссылка недействительна — ищем в новой сцене
            PlayerInputBlocker blocker = FindObjectOfType<PlayerInputBlocker>();
            if (blocker != null) blocker.SetInputBlocked(false);
        };

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
