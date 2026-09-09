using UnityEngine;

public class ResourceNode : MonoBehaviour
{
    public enum ResourceType { Berry, Stone, Wood, Meat }

    [SerializeField] ResourceType type;
    [SerializeField] float maxAmount = 5f;
    [SerializeField] float respawnRate = 0.1f;

    float currentAmount;
    GameObject visual;

    public ResourceType Type => type;
    public bool IsEmpty => currentAmount <= 0f;

    void Start()
    {
        currentAmount = maxAmount;
    }

    void Update()
    {
        if (currentAmount < maxAmount)
            currentAmount = Mathf.Min(currentAmount + respawnRate * Time.deltaTime, maxAmount);

        if (visual != null)
        {
            float scale = Mathf.Lerp(0.3f, 1f, currentAmount / maxAmount);
            visual.transform.localScale = Vector3.one * scale;
        }
    }

    public float Harvest(float amount)
    {
        float taken = Mathf.Min(amount, currentAmount);
        currentAmount -= taken;
        return taken;
    }

    public void SetVisual(GameObject obj) { visual = obj; }

    public void Init(ResourceType t, float amount)
    {
        type = t;
        maxAmount = amount;
        currentAmount = amount;
    }
}
