using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives a real, visible construction process: a structure's parts are grouped into ordered stages
/// (e.g. frame → walls → roof) which reveal one at a time as real time passes, instead of a whole
/// building popping into existence at once. Each stage's parts start hidden and get a GrowIn pop when
/// their moment arrives. CreatureCamp exposes IsUnderConstruction (true until this finishes) so the
/// builder can stay put and "work" at the site for the whole duration rather than instantly finishing.
/// </summary>
public class ConstructionTimeline : MonoBehaviour
{
    class Stage
    {
        public List<GameObject> parts;
        public float revealAt; // normalized 0-1 fraction of the total duration
    }

    readonly List<Stage> stages = new List<Stage>();
    float duration;
    float timer;
    int nextStageIndex;

    public bool IsComplete { get; private set; }

    public void Init(float totalDuration) => duration = Mathf.Max(0.5f, totalDuration);

    /// <summary>Register a group of parts to reveal together once `revealAtNormalized` of the total
    /// duration has elapsed. Parts should already be created (as children) but are hidden until then.</summary>
    public void AddStage(List<GameObject> parts, float revealAtNormalized)
    {
        foreach (var p in parts)
            if (p != null) p.SetActive(false);
        stages.Add(new Stage { parts = parts, revealAt = Mathf.Clamp01(revealAtNormalized) });
    }

    void Update()
    {
        if (IsComplete) return;

        timer += Time.deltaTime;
        float t = timer / duration;

        while (nextStageIndex < stages.Count && t >= stages[nextStageIndex].revealAt)
        {
            var stage = stages[nextStageIndex];
            foreach (var part in stage.parts)
            {
                if (part == null) continue;
                part.SetActive(true);
                part.AddComponent<GrowIn>();
            }
            nextStageIndex++;
        }

        if (t >= 1f)
        {
            IsComplete = true;
            Destroy(this);
        }
    }
}
