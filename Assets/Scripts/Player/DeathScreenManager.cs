using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

public class SecondFrameFader : MonoBehaviour
{
    public CanvasGroup group;
    public float duration;
    public float fadeTime;

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
        {
            DeathScreenContainer.SetActive(false);
        }
    }

    public void TriggerDeath()
    {
        if (DeathScreenContainer == null) return;
        
        DeathScreenContainer.SetActive(true);
        FirstFrame.gameObject.SetActive(true);
        SecondFrame.gameObject.SetActive(true);
        
        // Setup initial states
        FirstFrame.alpha = 1f;
        SecondFrame.alpha = 1f;
        
        if (phrasesData != null && DeathText != null)
        {
            DeathText.text = phrasesData.GetRandomPhrase();
        }

        StartCoroutine(DeathSequence());
    }

    private IEnumerator DeathSequence()
    {
        // Wait FirstFrame duration
        yield return new WaitForSeconds(firstFrameDuration);

        // Smoothly fade out FirstFrame
        float t = 0;
        while (t < firstFrameFadeTime)
        {
            t += Time.deltaTime;
            FirstFrame.alpha = Mathf.Lerp(1f, 0f, t / firstFrameFadeTime);
            yield return null;
        }
        FirstFrame.alpha = 0f;
        FirstFrame.gameObject.SetActive(false); // Hide it so it doesn't render behind SecondFrame

        // Extract Canvas scaler settings before unparenting
        Canvas parentCanvas = DeathScreenContainer.GetComponentInParent<Canvas>();
        CanvasScaler parentScaler = parentCanvas != null ? parentCanvas.GetComponent<CanvasScaler>() : null;

        // Detach DeathScreenContainer and make it survive the scene load
        DeathScreenContainer.transform.SetParent(null);
        DontDestroyOnLoad(DeathScreenContainer);

        // Ensure it has a Canvas to render as a root object
        Canvas canvas = DeathScreenContainer.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = DeathScreenContainer.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999; // Ensure it renders on top of the new scene

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

        // Add the fader component that will handle SecondFrame and destroy the container later
        SecondFrameFader fader = DeathScreenContainer.AddComponent<SecondFrameFader>();
        fader.group = SecondFrame;
        fader.duration = secondFrameDuration;
        fader.fadeTime = secondFrameFadeTime;

        // Restart the current scene exactly when FirstFrame disappears
#if UNITY_EDITOR
        UnityEditor.Selection.activeObject = null;
#endif
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
