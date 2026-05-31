using UnityEngine;

public class CreatureSpawner : MonoBehaviour
{
    [SerializeField] int creatureCount = 10;
    [SerializeField] float creatureScale = 1f;
    [SerializeField] Color[] creatureColors;

    static readonly string[] Names = {
        "Sena", "Mira", "Tomas", "Liora", "Bohl",
        "Kael", "Yuna", "Drex", "Vala", "Fen",
        "Nira", "Orik", "Zaya", "Pim", "Asha",
        "Rhen", "Dova", "Luk", "Thessa", "Grin"
    };

    void Start()
    {
        var world = SphericalWorld.Instance;
        if (world == null)
        {
            Debug.LogError("SphericalWorld not found!");
            return;
        }

        if (creatureColors == null || creatureColors.Length == 0)
            creatureColors = new[] {
                new Color(0.9f, 0.4f, 0.3f),
                new Color(0.3f, 0.7f, 0.9f),
                new Color(0.4f, 0.9f, 0.4f),
                new Color(0.9f, 0.8f, 0.3f),
                new Color(0.7f, 0.4f, 0.9f)
            };

        for (int i = 0; i < creatureCount; i++)
        {
            Vector3 surfacePoint = world.GetRandomLandPoint();
            Vector3 up = world.GetSurfaceNormal(surfacePoint);

            GameObject creature = CreateCreatureMesh();
            creature.name = Names[i % Names.Length];
            creature.transform.position = surfacePoint;
            creature.transform.up = up;
            creature.transform.localScale = Vector3.one * creatureScale;

            var renderer = creature.GetComponentInChildren<MeshRenderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = creatureColors[i % creatureColors.Length];
                renderer.material = mat;
            }

            var mind = creature.AddComponent<CreatureMind>();
            var personality = (PersonalityType)(i % System.Enum.GetValues(typeof(PersonalityType)).Length);
            mind.SetIdentity(creature.name, personality);

            var body = creature.AddComponent<CreatureBody>();

            if (CognitionScheduler.Instance != null)
                CognitionScheduler.Instance.Register(mind, body);
        }
    }

    GameObject CreateCreatureMesh()
    {
        var root = new GameObject();

        var bodyObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        bodyObj.transform.SetParent(root.transform);
        bodyObj.transform.localPosition = Vector3.zero;
        bodyObj.transform.localScale = new Vector3(0.6f, 0.8f, 0.6f);

        var headObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        headObj.transform.SetParent(root.transform);
        headObj.transform.localPosition = new Vector3(0f, 1f, 0.2f);
        headObj.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

        return root;
    }
}
