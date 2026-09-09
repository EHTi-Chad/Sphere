using System.Collections.Generic;
using UnityEngine;

public class SphericalTerrain : MonoBehaviour
{
    [Header("Generation")]
    [SerializeField] int seed = 42;
    [SerializeField] int subdivisions = 8; // 8 ≈ 4x the vertices of 7 → visibly sharper coastlines/relief
    [SerializeField] float radius = 50f;
    [SerializeField] float heightScale = 12f;
    [SerializeField] float waterLevel = 0.42f;

    [Header("Noise — Continental")]
    [SerializeField] int octaves = 8;
    [SerializeField] float baseFrequency = 1.2f;
    [SerializeField] float lacunarity = 2.05f;
    [SerializeField] float persistence = 0.52f; // rougher fractal — more varied hills, not just smooth swells

    [Header("Noise — Mountain Detail")]
    [SerializeField] float ridgeFrequency = 3f;
    [SerializeField] float ridgeStrength = 0.48f; // craggier, taller peaks

    [Header("Mountain Ranges")]
    [Tooltip("A handful of seeded locations that get boosted far past ordinary ridge noise, so the tallest peaks read as a few distinct named ranges instead of scattered anywhere land gets high.")]
    [SerializeField] int mountainRangeCount = 4;
    [Tooltip("Angular size of a range's influence, in radians — bigger = a wider stretch of terrain belongs to the range.")]
    [SerializeField] float mountainRangeAngularSize = 0.4f;
    [Tooltip("Extra ridge strength added on top of the base, at full influence — this is what makes a range's peaks dramatically taller than regular high ground.")]
    [SerializeField] float mountainRangeBoost = 0.9f;
    [Tooltip("Outside any range's influence, land height is scaled down by this much (1 = no cap) so ordinary hills stay well below true peak/snow altitude and the ranges stand out.")]
    [SerializeField] float nonRangeHeightCap = 0.72f;

    Vector3[] mountainCenters;

    [Header("Colors")]
    [SerializeField] Color deepWater = new Color(0.02f, 0.06f, 0.22f);
    [SerializeField] Color midWater = new Color(0.04f, 0.15f, 0.4f);
    [SerializeField] Color shallowWater = new Color(0.08f, 0.28f, 0.5f);
    [SerializeField] Color wetSand = new Color(0.6f, 0.55f, 0.4f);
    [SerializeField] Color drySand = new Color(0.78f, 0.72f, 0.52f);
    [SerializeField] Color lowGrass = new Color(0.3f, 0.58f, 0.18f);
    [SerializeField] Color highGrass = new Color(0.2f, 0.5f, 0.12f);
    [SerializeField] Color forest = new Color(0.08f, 0.32f, 0.06f);
    [SerializeField] Color denseForest = new Color(0.04f, 0.22f, 0.04f);
    [SerializeField] Color rock = new Color(0.42f, 0.38f, 0.33f);
    [SerializeField] Color highRock = new Color(0.52f, 0.48f, 0.42f);
    [SerializeField] Color snow = new Color(0.92f, 0.93f, 0.96f);

    [Header("Biome Colors")]
    [SerializeField] Color desertColor = new Color(0.82f, 0.72f, 0.45f);  // hot + dry
    [SerializeField] Color savannaColor = new Color(0.66f, 0.62f, 0.30f); // hot/temperate + dry-ish
    [SerializeField] Color taigaColor = new Color(0.16f, 0.34f, 0.24f);   // cold + wet (boreal)
    [SerializeField] Color tundraColor = new Color(0.46f, 0.45f, 0.36f);  // cold + dry

    [Header("Water")]
    [SerializeField] float waterAlpha = 0.85f; // more opaque = clearer water/land edge

    [Header("Rivers")]
    [SerializeField] float riverFlowThreshold = 120f; // upstream drainage needed to form a river (lower = more tributaries)
    [SerializeField] float riverCarveDepth = 0.04f;   // how deep rivers cut (normalized height)
    [SerializeField] Color riverColor = new Color(0.1f, 0.32f, 0.52f);

    [Header("Lakes")]
    [SerializeField] float lakeFlowThreshold = 280f;  // drainage pooling at a sink to form a lake (lower = more lakes)
    [SerializeField] float lakeFillRise = 0.012f;     // pond surface height above the sink
    [SerializeField] Color lakeColor = new Color(0.07f, 0.26f, 0.45f);

    // Water lookup (rivers + lakes + shoreline) for creature thirst / fording
    List<Vector3> waterPoints;
    Dictionary<Vector3Int, List<Vector3>> waterCells;
    float waterCellRes;
    bool hasWater;

    float[] heightMap;
    Vector3[] directions;
    Mesh terrainMesh;

    // Cache seed offsets so GetHeightAtDirection doesn't recalculate
    float seedOffsetX, seedOffsetY, seedOffsetZ;
    bool offsetsCached;

    public float WaterLevel => waterLevel;

    public void SetRadius(float r)
    {
        radius = r;
        heightScale = r * 0.34f; // taller, more dramatic relief relative to planet size

        // Mesh detail vs. planet size. Subdivision is EXPONENTIAL — each +1 is ~4x the vertices, not a
        // modest bump. Reverted back to 8/9: a brief attempt at 9/10 meant up to ~10.5M vertices on a
        // huge world, which stalled "Generate Planet" for many minutes (adjacency-graph construction,
        // the hydrology sort, and the GPU mesh upload all scale with vertex count, and at that size none
        // of them finish quickly). Shoreline smoothness has to come from the color-blend work
        // (BaseTerrainColor's widened bands, ComputeDisplacement, ComputeShoreHalo) instead — those cost
        // nothing extra at generation time, unlike raw subdivision.
        subdivisions = r > 500f ? 9 : 8;

        // More surface area gets a few more distinct ranges rather than the same handful stretched thin.
        mountainRangeCount = Mathf.Clamp(Mathf.RoundToInt(r / 35f), 2, 8);
    }

    void CacheSeedOffsets()
    {
        if (offsetsCached) return;
        Random.State saved = Random.state;
        Random.InitState(seed);
        seedOffsetX = Random.Range(-10000f, 10000f);
        seedOffsetY = Random.Range(-10000f, 10000f);
        seedOffsetZ = Random.Range(-10000f, 10000f);
        GenerateMountainCenters();
        Random.state = saved;
        offsetsCached = true;
    }

    // Cheap dot-product cutoff for MountainInfluence's early-out (cos of a slightly widened angular
    // size, to leave room for the edge-noise term below to still push influence above zero near the
    // boundary). Cached alongside the centers so it's one Cos() call per generation, not per query.
    float mountainCosCutoff;

    /// <summary>Picks a handful of seeded points on the sphere to anchor distinct mountain ranges.
    /// A separate Random stream (offset from the main seed) so it never perturbs the noise offsets.</summary>
    void GenerateMountainCenters()
    {
        Random.InitState(seed + 8241);
        int n = Mathf.Max(1, mountainRangeCount);
        mountainCenters = new Vector3[n];
        for (int i = 0; i < n; i++)
            mountainCenters[i] = Random.onUnitSphere;
        mountainCosCutoff = Mathf.Cos(mountainRangeAngularSize * 1.3f);
    }

    /// <summary>0-1 influence of the nearest mountain range at this direction — 1 at a range's center,
    /// fading to 0 past its angular size, with a bit of noise roughening the edge so ranges don't read
    /// as perfect circles.
    ///
    /// This runs on every terrain height query — not just once per vertex during generation, but every
    /// IsLand/SnapToSurface call every frame for every creature, critter, predator, flyer and swimmer.
    /// So the common case (a point far from every range, which is most of the planet) needs to be cheap:
    /// a plain dot-product cull skips the costly acos + noise sampling entirely for those points.</summary>
    float MountainInfluence(Vector3 dir)
    {
        if (mountainCenters == null || mountainCenters.Length == 0) return 0f;

        float best = 0f;
        for (int i = 0; i < mountainCenters.Length; i++)
        {
            float dot = Vector3.Dot(dir, mountainCenters[i]);
            if (dot < mountainCosCutoff) continue; // far outside this range's influence — skip cheaply

            float angularDist = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
            float t = 1f - Mathf.Clamp01(angularDist / mountainRangeAngularSize);
            if (t <= 0f) continue;

            float edgeNoise = SampleNoise3D(dir.x * 4f + seedOffsetX + 900f, dir.y * 4f + seedOffsetY + 900f, dir.z * 4f + seedOffsetZ + 900f);
            t = Mathf.Clamp01(t * Mathf.Lerp(0.65f, 1.2f, edgeNoise));

            float smooth = t * t * (3f - 2f * t); // smoothstep
            if (smooth > best) best = smooth;
        }
        return best;
    }

    public void Generate()
    {
        Random.InitState(seed);
        seedOffsetX = Random.Range(-10000f, 10000f);
        seedOffsetY = Random.Range(-10000f, 10000f);
        seedOffsetZ = Random.Range(-10000f, 10000f);
        GenerateMountainCenters();
        offsetsCached = true;

        terrainMesh = IcoSphereGenerator.Generate(subdivisions);
        var vertices = terrainMesh.vertices;
        int vcount = vertices.Length;
        var colors = new Color[vcount];
        heightMap = new float[vcount];
        directions = new Vector3[vcount];

        // Pass 1: base heights from noise
        for (int i = 0; i < vcount; i++)
        {
            Vector3 dir = vertices[i].normalized;
            directions[i] = dir;
            heightMap[i] = SampleHeight(dir, seedOffsetX, seedOffsetY, seedOffsetZ);
        }

        // Pass 2: hydrology — route water downhill, carve rivers, pool lakes, index water
        bool[] isRiver = new bool[vcount];
        bool[] isLake = new bool[vcount];
        float[] riverStrength = new float[vcount];
        float[] shoreHalo = new float[vcount];
        Color[] shoreHaloColor = new Color[vcount];
        ComputeHydrology(terrainMesh.triangles, isRiver, isLake, riverStrength, shoreHalo, shoreHaloColor);

        // Pass 3: displace vertices and assign colors
        for (int i = 0; i < vcount; i++)
        {
            float h = heightMap[i];
            float displacement = ComputeDisplacement(h);

            vertices[i] = directions[i] * (radius + displacement);
            if (isLake[i])
                colors[i] = lakeColor;
            else if (isRiver[i])
                colors[i] = Color.Lerp(riverColor, midWater, riverStrength[i]);
            else
            {
                Color c = GetTerrainColor(h, directions[i]);
                // A thin halo of land right next to a river/lake blends partway toward the water's
                // color instead of cutting hard at the vertex boundary — softens the bank.
                if (shoreHalo[i] > 0f) c = Color.Lerp(c, shoreHaloColor[i], shoreHalo[i]);
                colors[i] = c;
            }
        }

        terrainMesh.SetVertices(vertices);
        terrainMesh.colors = colors;
        terrainMesh.RecalculateNormals();
        terrainMesh.RecalculateBounds();

        var filter = GetComponent<MeshFilter>();
        if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = terrainMesh;

        var renderer = GetComponent<MeshRenderer>();
        if (renderer == null) renderer = gameObject.AddComponent<MeshRenderer>();
        var shader = Shader.Find("Custom/VertexColorLit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader);
        mat.SetFloat("_Smoothness", 0.15f);
        renderer.material = mat;

        var collider = GetComponent<MeshCollider>();
        if (collider == null) collider = gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = terrainMesh;

        CreateWaterSphere();
    }

    float SampleHeight(Vector3 dir, float offX, float offY, float offZ)
    {
        // Continental base shape
        float value = 0f;
        float amplitude = 1f;
        float frequency = baseFrequency;
        float maxValue = 0f;

        for (int o = 0; o < octaves; o++)
        {
            float nx = dir.x * frequency + offX;
            float ny = dir.y * frequency + offY;
            float nz = dir.z * frequency + offZ;

            float sample = SampleNoise3D(nx, ny, nz);
            value += sample * amplitude;
            maxValue += amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        float continental = value / maxValue;
        float mountainInfluence = MountainInfluence(dir);

        // Outside a mountain range's influence, soften how high land can climb — keeps ordinary hills
        // modest so the ranges read as a few distinct, dramatically taller features rather than the
        // tallest terrain being scattered anywhere continental noise happens to peak.
        if (continental > waterLevel)
        {
            float landH = (continental - waterLevel) / (1f - waterLevel);
            landH *= Mathf.Lerp(nonRangeHeightCap, 1f, mountainInfluence);
            continental = waterLevel + landH * (1f - waterLevel);
        }

        // Ridged noise for mountains — only on land; heavily boosted within a range's influence for
        // dramatic, craggy peaks distinct from the rest of the terrain.
        if (continental > waterLevel)
        {
            float landH = (continental - waterLevel) / (1f - waterLevel);
            float ridgeNoise = SampleRidgeNoise(dir, offX, offY, offZ);
            float effectiveRidgeStrength = ridgeStrength + mountainRangeBoost * mountainInfluence;
            continental += ridgeNoise * effectiveRidgeStrength * landH * landH; // stronger at higher elevations
        }

        return Mathf.Clamp01(continental);
    }

    float SampleRidgeNoise(Vector3 dir, float offX, float offY, float offZ)
    {
        float nx = dir.x * ridgeFrequency + offX + 500f;
        float ny = dir.y * ridgeFrequency + offY + 500f;
        float nz = dir.z * ridgeFrequency + offZ + 500f;
        float noise = SampleNoise3D(nx, ny, nz);
        // Ridged: invert and sharpen
        float ridge = 1f - Mathf.Abs(noise * 2f - 1f);
        return ridge * ridge;
    }

    float SampleNoise3D(float x, float y, float z)
    {
        float xy = Mathf.PerlinNoise(x, y);
        float xz = Mathf.PerlinNoise(x + 31.416f, z + 47.853f);
        float yz = Mathf.PerlinNoise(y + 67.291f, z + 83.124f);
        return (xy + xz + yz) / 3f;
    }

    Color GetTerrainColor(float height, Vector3 dir)
    {
        Color c = BaseTerrainColor(height, dir);
        if (height <= waterLevel) return c; // polar ocean ice is handled on the water mesh instead

        float lh = (height - waterLevel) / (1f - waterLevel);
        float polar = Mathf.Abs(dir.y);        // 0 = equator, 1 = pole (planet spin axis = world Y)
        float cold = polar + lh * 0.35f;        // colder toward the poles and at altitude
        if (cold > 0.62f)
            c = Color.Lerp(c, snow, Mathf.Clamp01((cold - 0.62f) / 0.28f));
        return c;
    }

    Color BaseTerrainColor(float height, Vector3 dir)
    {
        // Underwater bands widened (was a hard 0.5/0.85 split) — on a per-vertex-colored mesh, a
        // narrow color band only spans 1-2 triangles and reads as a jagged cut rather than a curve.
        // Spreading the same gradient across more of the depth range means it crosses far more
        // triangles, which is what actually reads as "smooth" at this mesh resolution.
        if (height < waterLevel * 0.4f) return deepWater;
        if (height < waterLevel * 0.72f) return Color.Lerp(deepWater, midWater, (height - waterLevel * 0.4f) / (waterLevel * 0.32f));
        if (height < waterLevel) return Color.Lerp(midWater, shallowWater, (height - waterLevel * 0.72f) / (waterLevel * 0.28f));

        float landHeight = (height - waterLevel) / (1f - waterLevel);

        // Micro-variation so colors aren't perfectly banded
        float variation = SampleNoise3D(dir.x * 8f + 200f, dir.y * 8f + 200f, dir.z * 8f + 200f) * 0.06f;
        landHeight += variation;

        // Biome ground colour from latitude (temperature) crossed with a low-frequency moisture map,
        // so deserts, savannas, grasslands, forests, jungles, taiga and tundra form as distinct
        // horizontal regions — not just altitude bands. Computed up front now (used by the beach
        // blend below too) rather than only after the old early-return.
        float polar = Mathf.Abs(dir.y);
        float temp = Mathf.Clamp01(1f - polar * 1.15f - landHeight * 0.4f);
        float moist = MoistureAt(dir);
        Color ground = BiomeGround(temp, moist);

        // Beach — was a razor-thin 0.015→0.06 band (a single triangle's worth of transition); now a
        // full wet-sand → dry-sand → ground gradient spanning ~10x the height range, so the shoreline
        // reads as a soft curve made of many blended triangles instead of one hard jagged edge.
        if (landHeight < 0.01f) return wetSand;
        if (landHeight < 0.07f) return Color.Lerp(wetSand, drySand, (landHeight - 0.01f) / 0.06f);
        if (landHeight < 0.16f) return Color.Lerp(drySand, ground, (landHeight - 0.07f) / 0.09f);
        if (landHeight < 0.5f) return ground;
        if (landHeight < 0.65f) return Color.Lerp(ground, rock, (landHeight - 0.5f) / 0.15f);
        if (landHeight < 0.8f) return Color.Lerp(rock, highRock, (landHeight - 0.65f) / 0.15f);
        return Color.Lerp(highRock, snow, Mathf.Clamp01((landHeight - 0.8f) / 0.2f));
    }

    /// <summary>Large-scale moisture 0-1 (low-frequency noise), used to vary biomes horizontally
    /// so two places at the same latitude can be desert vs jungle. Public so the foliage decorator
    /// can keep deserts bare and jungles lush.</summary>
    public float MoistureAt(Vector3 dir)
    {
        CacheSeedOffsets();
        const float f = 0.85f; // low frequency → big climatic regions
        float m = SampleNoise3D(dir.x * f + seedOffsetX + 1234.5f,
                                dir.y * f + seedOffsetY + 1234.5f,
                                dir.z * f + seedOffsetZ + 1234.5f);
        return Mathf.Clamp01((m - 0.5f) * 1.7f + 0.5f); // boost contrast so biomes read distinctly
    }

    /// <summary>Ground colour for a biome given temperature (0 cold … 1 hot) and moisture (0 dry … 1 wet).</summary>
    Color BiomeGround(float temp, float moist)
    {
        if (temp < 0.25f)                                    // cold belt: tundra (dry) → taiga (wet)
            return Color.Lerp(tundraColor, taigaColor, Mathf.SmoothStep(0f, 1f, moist));

        if (temp > 0.7f)                                     // hot belt: desert → savanna → jungle
        {
            if (moist < 0.4f) return Color.Lerp(desertColor, savannaColor, moist / 0.4f);
            return Color.Lerp(savannaColor, denseForest, (moist - 0.4f) / 0.6f);
        }

        // temperate belt: savanna (dry) → grassland → forest (wet)
        if (moist < 0.35f) return Color.Lerp(savannaColor, lowGrass, moist / 0.35f);
        if (moist < 0.7f) return Color.Lerp(lowGrass, highGrass, (moist - 0.35f) / 0.35f);
        return Color.Lerp(highGrass, forest, (moist - 0.7f) / 0.3f);
    }

    /// <summary>
    /// Routes rainfall downhill across the mesh, accumulates flow, and marks/carves
    /// vertices where enough water collects into rivers. Modifies heightMap in place.
    /// </summary>
    void ComputeHydrology(int[] tris, bool[] isRiver, bool[] isLake, float[] strength,
                           float[] shoreHalo, Color[] shoreHaloColor)
    {
        int n = heightMap.Length;

        // Build vertex adjacency from triangle edges.
        var adj = new List<int>[n];
        for (int i = 0; i < n; i++) adj[i] = new List<int>(6);
        for (int t = 0; t < tris.Length; t += 3)
        {
            int a = tris[t], b = tris[t + 1], c = tris[t + 2];
            AddNeighbor(adj, a, b); AddNeighbor(adj, b, a);
            AddNeighbor(adj, b, c); AddNeighbor(adj, c, b);
            AddNeighbor(adj, c, a); AddNeighbor(adj, a, c);
        }

        // Steepest-descent neighbor for each vertex (-1 = local pit / sink).
        int[] down = new int[n];
        for (int i = 0; i < n; i++)
        {
            int best = -1;
            float bestH = heightMap[i];
            var na = adj[i];
            for (int k = 0; k < na.Count; k++)
            {
                int nb = na[k];
                if (heightMap[nb] < bestH) { bestH = heightMap[nb]; best = nb; }
            }
            down[i] = best;
        }

        // Flow accumulation: push each vertex's water to its downhill neighbor,
        // processing highest vertices first so flow cascades to the sea.
        float[] flow = new float[n];
        for (int i = 0; i < n; i++) flow[i] = 1f; // uniform rainfall

        int[] order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        System.Array.Sort(order, (x, y) => heightMap[y].CompareTo(heightMap[x]));

        for (int k = 0; k < n; k++)
        {
            int i = order[k];
            int d = down[i];
            if (d >= 0) flow[d] += flow[i];
        }

        // Rivers: mark + carve where enough water has gathered.
        if (riverFlowThreshold > 0f)
        {
            float logMin = Mathf.Log(riverFlowThreshold);
            float logMax = Mathf.Log(riverFlowThreshold * 25f);
            for (int i = 0; i < n; i++)
            {
                if (heightMap[i] <= waterLevel) continue;     // already ocean
                if (flow[i] < riverFlowThreshold) continue;   // not enough drainage

                isRiver[i] = true;
                float s = Mathf.Clamp01((Mathf.Log(flow[i]) - logMin) / (logMax - logMin));
                strength[i] = s;

                // Carve a shallow channel but keep it just above sea level so it reads as a river, not a trench.
                heightMap[i] = Mathf.Max(waterLevel + 0.001f, heightMap[i] - riverCarveDepth * (0.4f + s));
            }

            DilateRivers(adj, isRiver, isLake, strength);
        }

        // Lakes: flood-fill basins at sinks that collect a lot of water.
        if (lakeFlowThreshold > 0f)
        {
            for (int i = 0; i < n; i++)
            {
                if (down[i] != -1) continue;                // only sinks (no downhill neighbor)
                if (heightMap[i] <= waterLevel) continue;
                if (flow[i] < lakeFlowThreshold) continue;
                FloodLake(i, adj, isLake, isRiver);
            }
        }

        BuildWaterIndex(adj, isRiver, isLake);
        ComputeShoreHalo(adj, isRiver, isLake, strength, shoreHalo, shoreHaloColor);
    }

    // A thin ring of dry land immediately next to a river/lake gets tagged with how strongly (and
    // toward what color) to blend in Pass 3 — softens the bank instead of a hard cut exactly at the
    // river/lake's vertex boundary. Reuses the adjacency list already built above; no new geometry.
    void ComputeShoreHalo(List<int>[] adj, bool[] isRiver, bool[] isLake, float[] strength,
                           float[] shoreHalo, Color[] shoreHaloColor)
    {
        int n = heightMap.Length;
        for (int i = 0; i < n; i++)
        {
            if (!isRiver[i] && !isLake[i]) continue;

            Color srcColor = isLake[i] ? lakeColor : Color.Lerp(riverColor, midWater, strength[i]);
            var na = adj[i];
            for (int k = 0; k < na.Count; k++)
            {
                int nb = na[k];
                if (isRiver[nb] || isLake[nb]) continue;   // only halo onto dry land
                if (heightMap[nb] <= waterLevel) continue; // true ocean is already blue, skip

                if (shoreHalo[nb] < 0.4f)
                {
                    shoreHalo[nb] = 0.4f;
                    shoreHaloColor[nb] = srcColor;
                }
            }
        }
    }

    // Thickens river lines so they render as solid channels with banks instead of blurry
    // single-vertex smears. Stronger (downstream) rivers widen more, giving natural taper.
    void DilateRivers(List<int>[] adj, bool[] isRiver, bool[] isLake, float[] strength)
    {
        int n = heightMap.Length;

        // Snapshot the original river cores so the dilation doesn't cascade.
        var core = new List<int>();
        for (int i = 0; i < n; i++)
            if (isRiver[i]) core.Add(i);

        // Ring 1: every river thickens by one vertex (gives a solid 2–3 wide core).
        var ring1 = new List<int>();
        for (int c = 0; c < core.Count; c++)
        {
            int v = core[c];
            float s = strength[v];
            var na = adj[v];
            for (int k = 0; k < na.Count; k++)
            {
                int nb = na[k];
                if (isRiver[nb] || isLake[nb] || heightMap[nb] <= waterLevel) continue;
                isRiver[nb] = true;
                strength[nb] = s * 0.85f;
                heightMap[nb] = Mathf.Max(waterLevel + 0.001f, heightMap[nb] - riverCarveDepth * 0.5f * (0.4f + s));
                ring1.Add(nb);
            }
        }

        // Ring 2: only strong (downstream) rivers widen once more.
        for (int c = 0; c < ring1.Count; c++)
        {
            int v = ring1[c];
            if (strength[v] < 0.45f) continue;
            var na = adj[v];
            for (int k = 0; k < na.Count; k++)
            {
                int nb = na[k];
                if (isRiver[nb] || isLake[nb] || heightMap[nb] <= waterLevel) continue;
                isRiver[nb] = true;
                strength[nb] = strength[v] * 0.85f;
                heightMap[nb] = Mathf.Max(waterLevel + 0.001f, heightMap[nb] - riverCarveDepth * 0.3f);
            }
        }
    }

    void FloodLake(int start, List<int>[] adj, bool[] isLake, bool[] isRiver)
    {
        float surface = heightMap[start] + lakeFillRise;
        var queue = new Queue<int>();
        queue.Enqueue(start);
        isLake[start] = true;

        int filled = 0;
        const int maxFill = 120; // allow larger lakes
        while (queue.Count > 0 && filled < maxFill)
        {
            int v = queue.Dequeue();
            filled++;
            heightMap[v] = surface; // flatten to the pond surface
            isRiver[v] = false;     // lake overrides any river marking here

            var na = adj[v];
            for (int k = 0; k < na.Count; k++)
            {
                int nb = na[k];
                if (isLake[nb]) continue;
                if (heightMap[nb] > waterLevel && heightMap[nb] < surface)
                {
                    isLake[nb] = true;
                    queue.Enqueue(nb);
                }
            }
        }
    }

    void BuildWaterIndex(List<int>[] adj, bool[] isRiver, bool[] isLake)
    {
        int n = heightMap.Length;
        waterPoints = new List<Vector3>();
        waterCells = new Dictionary<Vector3Int, List<Vector3>>();
        waterCellRes = Mathf.Max(8f, radius / 2.5f);

        for (int i = 0; i < n; i++)
        {
            bool flowing = isRiver[i] || isLake[i]; // rivers/lakes: drinkable AND forded
            bool water = flowing;

            // Shoreline: a land vertex bordering the ocean is a drinkable edge (but not "forded").
            if (!water && heightMap[i] > waterLevel)
            {
                var na = adj[i];
                for (int k = 0; k < na.Count; k++)
                    if (heightMap[na[k]] <= waterLevel) { water = true; break; }
            }

            if (!water) continue;

            float h = heightMap[i];
            float disp = h > waterLevel ? (h - waterLevel) * heightScale : 0f;
            Vector3 pos = directions[i] * (radius + disp);

            // Drinking list includes shorelines; the fording grid is rivers/lakes only.
            waterPoints.Add(pos);
            if (flowing)
            {
                var key = WaterKey(pos);
                if (!waterCells.TryGetValue(key, out var list)) { list = new List<Vector3>(); waterCells[key] = list; }
                list.Add(pos);
            }
        }

        hasWater = waterPoints.Count > 0;
    }

    Vector3Int WaterKey(Vector3 worldPos)
    {
        Vector3 d = worldPos.normalized;
        return new Vector3Int(
            Mathf.RoundToInt(d.x * waterCellRes),
            Mathf.RoundToInt(d.y * waterCellRes),
            Mathf.RoundToInt(d.z * waterCellRes));
    }

    /// <summary>Cheap per-frame check: is any water within `range` of this point? (for fording)</summary>
    public bool IsWaterNear(Vector3 worldPos, float range)
    {
        if (!hasWater || waterCells == null) return false;
        Vector3Int b = WaterKey(worldPos);
        float r2 = range * range;
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    if (waterCells.TryGetValue(new Vector3Int(b.x + x, b.y + y, b.z + z), out var list))
                        for (int i = 0; i < list.Count; i++)
                            if ((list[i] - worldPos).sqrMagnitude <= r2) return true;
                }
        return false;
    }

    /// <summary>Nearest water point within range (linear scan; call occasionally, e.g. when choosing a drink target).</summary>
    public bool FindNearestWater(Vector3 worldPos, float maxRange, out Vector3 result)
    {
        result = worldPos;
        if (!hasWater) return false;

        float best = maxRange * maxRange;
        bool found = false;
        for (int i = 0; i < waterPoints.Count; i++)
        {
            float d = (waterPoints[i] - worldPos).sqrMagnitude;
            if (d < best) { best = d; result = waterPoints[i]; found = true; }
        }
        return found;
    }

    static void AddNeighbor(List<int>[] adj, int a, int b)
    {
        var list = adj[a];
        for (int i = 0; i < list.Count; i++)
            if (list[i] == b) return;
        list.Add(b);
    }

    void CreateWaterSphere()
    {
        var waterObj = new GameObject("Water");
        waterObj.transform.SetParent(transform);
        waterObj.transform.localPosition = Vector3.zero;

        var waterMesh = IcoSphereGenerator.Generate(6);
        var waterVerts = waterMesh.vertices;
        var waterColors = new Color[waterVerts.Length];
        Color deepWaterCol = new Color(0.04f, 0.22f, 0.42f);
        Color iceCol = new Color(0.86f, 0.91f, 0.96f);
        for (int i = 0; i < waterVerts.Length; i++)
        {
            Vector3 n = waterVerts[i].normalized;
            waterVerts[i] = n * (radius - 0.05f);
            // Frozen sea ice creeps out from the poles to form the polar caps.
            float ice = Mathf.InverseLerp(0.74f, 0.9f, Mathf.Abs(n.y));
            waterColors[i] = Color.Lerp(deepWaterCol, iceCol, ice);
        }
        waterMesh.SetVertices(waterVerts);
        waterMesh.colors = waterColors;
        waterMesh.RecalculateNormals();

        var filter = waterObj.AddComponent<MeshFilter>();
        filter.sharedMesh = waterMesh;

        var renderer = waterObj.AddComponent<MeshRenderer>();
        // Vertex-color shader so the ocean body and the white polar ice render from mesh colors.
        var mat = new Material(Shader.Find("Custom/VertexColorLit") ?? Shader.Find("Universal Render Pipeline/Lit"));
        renderer.material = mat;
    }

    public float GetHeightAtDirection(Vector3 direction)
    {
        CacheSeedOffsets();
        return SampleHeight(direction.normalized, seedOffsetX, seedOffsetY, seedOffsetZ);
    }

    public bool IsLand(Vector3 worldPosition)
    {
        Vector3 dir = (worldPosition - transform.position).normalized;
        return GetHeightAtDirection(dir) > waterLevel;
    }

    public Vector3 GetSurfacePoint(Vector3 direction)
    {
        float h = GetHeightAtDirection(direction);
        return transform.position + direction.normalized * (radius + ComputeDisplacement(h));
    }

    /// <summary>Land rises at heightScale, the ocean floor dips at 0.4x that — but switching hard
    /// between the two exactly at waterLevel put a visible kink in the terrain's slope right at every
    /// shoreline. This blends the rate smoothly across a narrow band straddling the waterline instead,
    /// which softens the beach profile everywhere no extra mesh detail is spent. Shared by Generate()
    /// (mesh vertices) and GetSurfacePoint() (runtime queries) so they can never drift apart.</summary>
    float ComputeDisplacement(float h)
    {
        float diff = h - waterLevel;
        float t = Mathf.Clamp01((diff + 0.02f) / 0.04f); // blend zone: waterLevel ± 0.02
        float rate = Mathf.Lerp(heightScale * 0.4f, heightScale, t);
        return diff * rate;
    }

    /// <summary>Returns 0-1 land height above water level. Used by decorators for biome placement.</summary>
    public float GetLandHeight(Vector3 direction)
    {
        float h = GetHeightAtDirection(direction);
        if (h <= waterLevel) return -1f;
        return (h - waterLevel) / (1f - waterLevel);
    }

    /// <summary>
    /// Climate temperature at a world position: 1 = hot (equator, low ground),
    /// 0 = freezing (poles or high peaks). Latitude dominates; altitude cools further.
    /// </summary>
    public float TemperatureAt(Vector3 worldPos)
    {
        Vector3 d = (worldPos - transform.position).normalized;
        float polar = Mathf.Abs(d.y);                 // 0 = equator, 1 = pole (spin axis = world Y)
        float h = GetHeightAtDirection(d);
        float elev = h > waterLevel ? (h - waterLevel) / (1f - waterLevel) : 0f;
        return Mathf.Clamp01(1f - polar * 1.15f - elev * 0.4f);
    }
}
