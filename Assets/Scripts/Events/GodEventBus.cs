using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GodEventBus : MonoBehaviour
{
    public static GodEventBus Instance { get; private set; }

    public event Action<GodEvent> OnGodEvent;

    [SerializeField] float eventRadius = 30f;
    [SerializeField] float smiteDamage = 0.5f;
    [SerializeField] float blessingAmount = 0.3f;
    [SerializeField] float feedAmount = 0.4f;

    List<GodVfx> activeVfx = new List<GodVfx>();

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (mouse == null || kb == null) return;
        if (MainMenu.Instance != null && MainMenu.Instance.IsOpen) return;

        if (mouse.leftButton.wasPressedThisFrame && !mouse.rightButton.isPressed)
        {
            bool hasAction = kb.digit1Key.isPressed || kb.digit2Key.isPressed || kb.digit3Key.isPressed;
            if (!hasAction) return;

            Ray ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (kb.digit1Key.isPressed)
                    PerformGodAction(GodActionType.Smite, hit.point);
                else if (kb.digit2Key.isPressed)
                    PerformGodAction(GodActionType.Bless, hit.point);
                else if (kb.digit3Key.isPressed)
                    PerformGodAction(GodActionType.Feed, hit.point);
            }
        }

        UpdateVfx();
    }

    public void PerformGodAction(GodActionType actionType, Vector3 position)
    {
        var creatures = FindObjectsByType<CreatureMind>(FindObjectsSortMode.None);
        int affected = 0;

        foreach (var creature in creatures)
        {
            float dist = Vector3.Distance(creature.transform.position, position);
            if (dist > eventRadius) continue;

            affected++;
            float intensity = 1f - (dist / eventRadius);
            string eventDescription = "";

            switch (actionType)
            {
                case GodActionType.Smite:
                    creature.FeelThreat(smiteDamage * intensity);
                    creature.FeelAwe(0.3f * intensity);
                    creature.GetComponent<CreatureBody>().FleeFrom(position);
                    eventDescription = $"The god struck near me with terrible force! (intensity: {intensity:F1})";
                    break;
                case GodActionType.Bless:
                    creature.FeelAwe(blessingAmount * intensity);
                    creature.FeelThreat(-0.2f * intensity);
                    eventDescription = $"A warm light from above washed over me. (intensity: {intensity:F1})";
                    break;
                case GodActionType.Feed:
                    creature.SatisfyHunger(feedAmount * intensity);
                    creature.FeelAwe(0.1f * intensity);
                    eventDescription = $"Food appeared from nowhere near me! (intensity: {intensity:F1})";
                    break;
            }

            creature.AddGodMemory(eventDescription);
        }

        Debug.Log($"[God] {actionType} at {position}, affected {affected} creatures");

        SpawnVfx(actionType, position);

        var godEvent = new GodEvent { type = actionType, position = position, radius = eventRadius };
        OnGodEvent?.Invoke(godEvent);
    }

    void SpawnVfx(GodActionType type, Vector3 position)
    {
        Color color = type switch
        {
            GodActionType.Smite => new Color(1f, 0.3f, 0.1f),
            GodActionType.Bless => new Color(1f, 0.95f, 0.4f),
            GodActionType.Feed => new Color(0.3f, 0.9f, 0.3f),
            _ => Color.white
        };

        var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.name = $"GodVfx_{type}";
        Destroy(obj.GetComponent<Collider>());
        obj.transform.position = position;
        obj.transform.localScale = Vector3.one * 2f;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.color = color;
        mat.SetFloat("_Surface", 1f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        obj.GetComponent<MeshRenderer>().material = mat;

        activeVfx.Add(new GodVfx { obj = obj, mat = mat, color = color, timer = 0f, duration = 1.5f });

        if (type == GodActionType.Smite)
        {
            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "SmitePillar";
            Destroy(pillar.GetComponent<Collider>());
            pillar.transform.position = position + (position - Vector3.zero).normalized * 15f;
            pillar.transform.localScale = new Vector3(1f, 15f, 1f);
            pillar.transform.up = (position - Vector3.zero).normalized;

            var pMat = new Material(mat);
            pMat.color = new Color(1f, 0.5f, 0.2f, 0.6f);
            pillar.GetComponent<MeshRenderer>().material = pMat;

            activeVfx.Add(new GodVfx { obj = pillar, mat = pMat, color = new Color(1f, 0.5f, 0.2f), timer = 0f, duration = 0.8f });
        }
    }

    void UpdateVfx()
    {
        for (int i = activeVfx.Count - 1; i >= 0; i--)
        {
            var vfx = activeVfx[i];
            vfx.timer += Time.deltaTime;
            float t = vfx.timer / vfx.duration;

            if (t >= 1f)
            {
                Destroy(vfx.obj);
                activeVfx.RemoveAt(i);
                continue;
            }

            float scale = 2f + t * 8f;
            vfx.obj.transform.localScale = Vector3.one * scale;
            vfx.mat.color = new Color(vfx.color.r, vfx.color.g, vfx.color.b, 1f - t);
        }
    }

    class GodVfx
    {
        public GameObject obj;
        public Material mat;
        public Color color;
        public float timer;
        public float duration;
    }
}

public enum GodActionType { Smite, Bless, Feed }

public struct GodEvent
{
    public GodActionType type;
    public Vector3 position;
    public float radius;
}
