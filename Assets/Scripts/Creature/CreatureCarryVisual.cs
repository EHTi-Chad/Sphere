using UnityEngine;

/// <summary>
/// Shows what a creature is hauling. Reads CreatureBody's carried amounts and displays a held
/// item in front of the chest — a log for wood, a rock for stone, a berry cluster, a meat chunk.
/// The item pops in when picked up and disappears the instant it's deposited onto the camp pile,
/// so the whole gather → carry → build loop reads at a glance. Pure visual; touches no game state.
/// </summary>
[RequireComponent(typeof(CreatureBody))]
public class CreatureCarryVisual : MonoBehaviour
{
    [SerializeField] Vector3 handLocalPos = new Vector3(0f, 0.45f, 0.5f); // in front of the chest
    [SerializeField] float popSpeed = 10f;

    CreatureBody body;
    Transform anchor;
    GameObject woodItem, stoneItem, berryItem, meatItem;
    GameObject shown;
    float shownScale;     // eased 0..1 for the pop-in
    Vector3 lastPos;

    static Material woodMat, stoneMat, berryMat, meatMat;

    void Start()
    {
        body = GetComponent<CreatureBody>();
        if (body == null) { enabled = false; return; }

        EnsureMaterials();

        anchor = new GameObject("CarryAnchor").transform;
        anchor.SetParent(transform, false);
        anchor.localPosition = handLocalPos;

        woodItem = BuildLog();
        stoneItem = BuildRock();
        berryItem = BuildBerries();
        meatItem = BuildMeat();
        SetActiveSafe(woodItem, false);
        SetActiveSafe(stoneItem, false);
        SetActiveSafe(berryItem, false);
        SetActiveSafe(meatItem, false);

        lastPos = transform.position;
    }

    void EnsureMaterials()
    {
        if (woodMat != null) return;
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        woodMat  = new Material(sh) { color = new Color(0.35f, 0.22f, 0.10f) };
        stoneMat = new Material(sh) { color = new Color(0.50f, 0.47f, 0.42f) };
        berryMat = new Material(sh) { color = new Color(0.70f, 0.15f, 0.10f) };
        meatMat  = new Material(sh) { color = new Color(0.45f, 0.12f, 0.10f) };
    }

    GameObject Prim(PrimitiveType t, Transform parent, Material mat)
    {
        var go = GameObject.CreatePrimitive(t);
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.transform.SetParent(parent, false);
        return go;
    }

    GameObject BuildLog()
    {
        var root = new GameObject("Carry_Wood");
        root.transform.SetParent(anchor, false);
        var log = Prim(PrimitiveType.Cylinder, root.transform, woodMat);
        log.transform.localScale = new Vector3(0.09f, 0.22f, 0.09f);
        log.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // lie horizontally across the arms
        return root;
    }

    GameObject BuildRock()
    {
        var root = new GameObject("Carry_Stone");
        root.transform.SetParent(anchor, false);
        var rock = Prim(PrimitiveType.Sphere, root.transform, stoneMat);
        rock.transform.localScale = new Vector3(0.2f, 0.16f, 0.2f);
        return root;
    }

    GameObject BuildBerries()
    {
        var root = new GameObject("Carry_Berries");
        root.transform.SetParent(anchor, false);
        Vector3[] offs = { new Vector3(-0.07f, 0f, 0f), new Vector3(0.07f, 0f, 0f), new Vector3(0f, 0.08f, 0.02f) };
        foreach (var o in offs)
        {
            var b = Prim(PrimitiveType.Sphere, root.transform, berryMat);
            b.transform.localPosition = o;
            b.transform.localScale = Vector3.one * 0.09f;
        }
        return root;
    }

    GameObject BuildMeat()
    {
        var root = new GameObject("Carry_Meat");
        root.transform.SetParent(anchor, false);
        var chunk = Prim(PrimitiveType.Cube, root.transform, meatMat);
        chunk.transform.localScale = new Vector3(0.16f, 0.1f, 0.14f);
        return root;
    }

    void SetActiveSafe(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on) go.SetActive(on);
    }

    void LateUpdate()
    {
        if (body == null) return;

        // Which resource dominates the load? That's what they're hauling home.
        float w = body.CarriedWood, s = body.CarriedStone, f = body.CarriedFood, m = body.CarriedMeat;
        float total = w + s + f + m;

        GameObject want = null;
        if (total > 0.01f)
        {
            want = woodItem; float max = w;
            if (s > max) { max = s; want = stoneItem; }
            if (f > max) { max = f; want = berryItem; }
            if (m > max) { max = m; want = meatItem; }
        }

        if (want != shown)
        {
            SetActiveSafe(shown, false);
            shown = want;
            SetActiveSafe(shown, true);
            if (shown != null) shownScale = 0f; // pop the new item in
        }

        // Pop-in / fade-out scale.
        float targetScale = shown != null ? 1f : 0f;
        shownScale = Mathf.MoveTowards(shownScale, targetScale, popSpeed * Time.deltaTime);

        if (shown != null)
        {
            // Gentle carry bob while moving, so the load feels held rather than glued on.
            float speed = (transform.position - lastPos).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            float walk = Mathf.Clamp01(speed / 4f);
            float bob = Mathf.Sin(Time.time * 9f) * 0.025f * walk;
            anchor.localPosition = handLocalPos + new Vector3(0f, bob, 0f);
            shown.transform.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, shownScale);
        }

        lastPos = transform.position;
    }
}
