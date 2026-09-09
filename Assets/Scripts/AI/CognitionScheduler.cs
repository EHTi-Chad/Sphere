using System.Collections.Generic;
using UnityEngine;

public class CognitionScheduler : MonoBehaviour
{
    public static CognitionScheduler Instance { get; private set; }

    // Slowed down across the board so a creature's decisions/speech actually linger long enough to
    // read and absorb, instead of the whole population re-deciding every couple of seconds.
    [SerializeField] float baseThinkInterval = 6f;
    [SerializeField] float stressedThinkInterval = 3f;
    [SerializeField] float calmThinkInterval = 18f;
    [SerializeField] int maxConcurrentThoughts = 3;

    List<CreatureThinkEntry> creatures = new List<CreatureThinkEntry>();
    int currentThinking;

    void Awake()
    {
        Instance = this;
    }

    public void Register(CreatureMind mind, CreatureBody body)
    {
        creatures.Add(new CreatureThinkEntry
        {
            mind = mind,
            body = body,
            nextThinkTime = Time.time + Random.Range(0f, baseThinkInterval)
        });
    }

    public void Unregister(CreatureMind mind)
    {
        creatures.RemoveAll(e => e.mind == mind);
    }

    void Update()
    {
        if (OllamaClient.Instance == null) return;

        // Wait quietly until the LLM backend has connected — no point requesting (or spamming errors) before then.
        // Creatures still run on the fast-layer AI in the meantime.
        if (!OllamaClient.Instance.IsReady) return;

        for (int i = 0; i < creatures.Count; i++)
        {
            var entry = creatures[i];
            if (entry.isThinking || Time.time < entry.nextThinkTime) continue;
            if (currentThinking >= maxConcurrentThoughts) break;

            RequestThought(entry);
        }
    }

    void RequestThought(CreatureThinkEntry entry)
    {
        entry.isThinking = true;
        currentThinking++;

        string prompt = entry.mind.BuildPrompt();

        OllamaClient.Instance.RequestThought(prompt,
            response =>
            {
                // The world may have been unloaded (Quit to Menu) while this thought was in flight —
                // drop it rather than touch a now-destroyed creature or scheduler.
                if (this == null || entry.mind == null) return;
                entry.mind.ApplyLLMResponse(
                    response.reasoning,
                    response.belief,
                    response.say,
                    response.ParseGoal(),
                    response.intensity
                );

                SpreadGossip(entry.mind, response.say);

                float interval = GetThinkInterval(entry);
                entry.nextThinkTime = Time.time + interval;
                entry.isThinking = false;
                currentThinking--;
            },
            error =>
            {
                if (this == null || entry.mind == null) return;
                Debug.LogWarning($"[{entry.mind.CreatureName}] LLM error: {error}");
                entry.nextThinkTime = Time.time + baseThinkInterval * 2f;
                entry.isThinking = false;
                currentThinking--;
            }
        );
    }

    float GetThinkInterval(CreatureThinkEntry entry)
    {
        float stress = Mathf.Max(entry.mind.Safety, entry.mind.Hunger);
        if (stress > 0.7f) return stressedThinkInterval;
        if (stress < 0.3f) return calmThinkInterval;
        return baseThinkInterval;
    }

    void SpreadGossip(CreatureMind speaker, string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        float gossipRange = 15f;
        foreach (var entry in creatures)
        {
            if (entry.mind == speaker) continue;
            float dist = Vector3.Distance(speaker.transform.position, entry.mind.transform.position);
            if (dist <= gossipRange)
                entry.mind.HearGossip(speaker.CreatureName, message);
        }
    }

    class CreatureThinkEntry
    {
        public CreatureMind mind;
        public CreatureBody body;
        public float nextThinkTime;
        public bool isThinking;
    }
}
