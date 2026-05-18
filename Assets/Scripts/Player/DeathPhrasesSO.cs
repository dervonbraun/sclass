using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewDeathPhrases", menuName = "Game/Death Phrases")]
public class DeathPhrasesSO : ScriptableObject
{
    [TextArea(2, 5)]
    public List<string> phrases = new List<string>();

    public string GetRandomPhrase()
    {
        if (phrases == null || phrases.Count == 0) return "YOU DIED";
        return phrases[Random.Range(0, phrases.Count)];
    }
}
