using UnityEngine;
using UnityEngine.Rendering;

namespace Jianghu
{
    /// <summary>
    /// 地形的畫面物件：提供地表高度查詢，並管理遮蔽結構（樹冠、屋頂）的漸變淡出。
    /// </summary>
    public sealed class TerrainView
    {
        const float GhostAlpha = 0.26f, FadeSpeed = 5f;

        readonly VoxelWorld w;
        readonly int[] ground;      // 每個直行的自然地面高度（層數）
        readonly MeshRenderer[] cover;
        readonly Material lit;
        readonly Material[] ghost;  // 每個結構自己的淡影材質，才能各自漸變
        readonly float[] alpha;
        readonly bool[] hidden;
        bool animating;

        public TerrainView(VoxelWorld world, int[] ground, MeshRenderer[] cover, Material lit)
        {
            w = world;
            this.ground = ground;
            this.cover = cover;
            this.lit = lit;
            ghost = new Material[cover.Length];
            alpha = new float[cover.Length];
            hidden = new bool[cover.Length];
            for (int s = 0; s < cover.Length; s++)
            {
                alpha[s] = 1;
                ghost[s] = Mats.Glow(Color.white);
            }
        }

        /// <summary>該格是否直接站在自然地面上（而不是橋面、屋頂、樹冠）。</summary>
        public bool IsGroundCell(int cell)
        {
            return w.Z(cell) == ground[w.X(cell) + w.Y(cell) * w.W];
        }

        /// <summary>世界座標 (x, z) 處的地表高度。</summary>
        public float HeightAt(float x, float z)
        {
            return TerrainMesher.GroundLevel(w, ground, x, z) * Vis.H;
        }

        /// <summary>掀開／還原第 s 個遮蔽結構；實際的透明度變化在 Tick 裡漸進。</summary>
        public void SetHidden(int s, bool hide)
        {
            if (cover[s] == null || hidden[s] == hide) return;
            hidden[s] = hide;
            animating = true;
        }

        /// <summary>推進漸變。回傳是否仍在變化（呼叫端據此維持全速渲染）。</summary>
        public bool Tick(float dt)
        {
            if (!animating) return false;
            animating = false;
            for (int s = 0; s < cover.Length; s++)
            {
                var r = cover[s];
                if (r == null) continue;
                float target = hidden[s] ? GhostAlpha : 1f;
                if (alpha[s] == target) continue;

                alpha[s] = Mathf.MoveTowards(alpha[s], target, dt * FadeSpeed);
                if (alpha[s] >= 1f)
                {
                    r.sharedMaterial = lit;
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.receiveShadows = true;
                }
                else
                {
                    if (r.sharedMaterial != ghost[s])
                    {
                        r.sharedMaterial = ghost[s];
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        r.receiveShadows = false;
                    }
                    ghost[s].color = new Color(1, 1, 1, alpha[s]);
                }
                if (alpha[s] != target) animating = true;
            }
            return true;
        }
    }

    /// <summary>
    /// 把體素世界畫成自然的地景，規則用的格子完全不變。
    /// 地面：高度差 1 層畫成緩坡、2 層以上畫成岩壁，平面著色的低面數風格。
    /// 物件：樹、木橋、涼亭各自生成造型，樹冠與屋頂依遮蔽結構分開以便淡出。
    /// </summary>
    public static class TerrainMesher
    {
        const int N = 4;               // 每格細分數
        const float Chunk = 12f;       // 地形與擺設每 12 格切成一塊，鏡頭外的不畫
        const float WaterLevel = 1.82f; // 水面高度（層）

        static Color32 C(float r, float g, float b) { return new Color(r, g, b, 1); }
        static Color32 Mix(Color32 a, Color32 b, float t) { return Color32.Lerp(a, b, Mathf.Clamp01(t)); }
        static Color32 Shade(Color32 c, float k)
        {
            return new Color32((byte)Mathf.Clamp(c.r * k, 0, 255), (byte)Mathf.Clamp(c.g * k, 0, 255), (byte)Mathf.Clamp(c.b * k, 0, 255), 255);
        }

        static readonly Color32 Grass1 = C(0.40f, 0.54f, 0.40f), Grass2 = C(0.50f, 0.60f, 0.42f), GrassHigh = C(0.62f, 0.63f, 0.46f);
        static readonly Color32 Earth = C(0.60f, 0.47f, 0.32f), Rock1 = C(0.42f, 0.46f, 0.49f), Rock2 = C(0.57f, 0.61f, 0.63f);
        static readonly Color32 Sand = C(0.83f, 0.78f, 0.64f), Paving1 = C(0.68f, 0.68f, 0.66f), Paving2 = C(0.60f, 0.60f, 0.59f);
        static readonly Color32 Riverbed = C(0.30f, 0.42f, 0.42f);
        static readonly Color32 Bark = C(0.27f, 0.20f, 0.16f), Plank1 = C(0.62f, 0.45f, 0.27f), Plank2 = C(0.54f, 0.39f, 0.23f), Beam = C(0.38f, 0.26f, 0.16f);
        static readonly Color32 Pillar = C(0.70f, 0.20f, 0.15f), Tile1 = C(0.25f, 0.34f, 0.40f), Tile2 = C(0.20f, 0.28f, 0.34f), Ridge = C(0.15f, 0.20f, 0.25f);

        static bool Natural(byte b) { return b == Block.Grass || b == Block.Dirt || b == Block.Stone || b == Block.Sand; }

        /// <summary>每個直行由底部往上連續的自然方塊數，也就是地面高度。</summary>
        static int[] GroundHeights(VoxelWorld w)
        {
            var g = new int[w.W * w.D];
            for (int y = 0; y < w.D; y++)
                for (int x = 0; x < w.W; x++)
                {
                    int h = 0;
                    while (h < w.H && Natural(w.v[w.Idx(x, y, h)])) h++;
                    g[x + y * w.W] = h;
                }
            return g;
        }

        static float Smooth(float a, float b, float t)
        {
            t = Mathf.Clamp01((t - a) / (b - a));
            return t * t * (3 - 2 * t);
        }

        // 兩格之間的過渡曲線：差 1 層是近乎線性的緩坡，差 2 層以上集中在交界處成為岩壁
        static float Blend(float t, int diff)
        {
            if (diff <= 1) return Mathf.Lerp(t, Smooth(0, 1, t), 0.5f);
            return Smooth(0.38f, 0.62f, t);
        }

        /// <summary>世界座標 (px, pz) 處的地面高度（單位：層）。格子中心恰為該格的高度。</summary>
        public static float GroundLevel(VoxelWorld w, int[] g, float px, float pz)
        {
            float fx = px - 0.5f, fz = pz - 0.5f;
            int i0 = Mathf.FloorToInt(fx), j0 = Mathf.FloorToInt(fz);
            float tx = fx - i0, tz = fz - j0;
            int x0 = Mathf.Clamp(i0, 0, w.W - 1), x1 = Mathf.Clamp(i0 + 1, 0, w.W - 1);
            int z0 = Mathf.Clamp(j0, 0, w.D - 1), z1 = Mathf.Clamp(j0 + 1, 0, w.D - 1);
            int h00 = g[x0 + z0 * w.W], h10 = g[x1 + z0 * w.W], h01 = g[x0 + z1 * w.W], h11 = g[x1 + z1 * w.W];
            float a = Mathf.Lerp(h00, h10, Blend(tx, Mathf.Abs(h00 - h10)));
            float b = Mathf.Lerp(h01, h11, Blend(tx, Mathf.Abs(h01 - h11)));
            return Mathf.Lerp(a, b, Blend(tz, Mathf.Max(Mathf.Abs(h00 - h01), Mathf.Abs(h10 - h11))));
        }

        public static TerrainView Build(VoxelWorld w, Transform parent, Color sky)
        {
            var g = GroundHeights(w);
            var lit = new Material(Mats.Terrain) { mainTexture = Texture2D.whiteTexture, color = Color.white };

            BuildGround(w, g).ToChunks("Terrain", parent, lit, Chunk);
            BuildWater(w, g, parent);

            var props = new MeshBuilder();
            var covers = new MeshBuilder[w.coverCount];
            for (int i = 0; i < covers.Length; i++) covers[i] = new MeshBuilder();

            BuildTrees(w, g, props, covers);
            BuildBridge(w, g, props);
            BuildPavilion(w, g, props, covers);
            BuildWalls(w, g, props);
            BuildFloors(w, covers);
            BuildDecor(w, g, props);
            BuildWuxiaProps(w, g, props);
            props.ToChunks("Props", parent, lit, Chunk);
            BuildBackdrop(w, parent, sky);

            var renderers = new MeshRenderer[covers.Length];
            for (int s = 0; s < covers.Length; s++)
                if (covers[s].VertexCount > 0) renderers[s] = covers[s].ToObject("Cover " + s, parent, lit);

            return new TerrainView(w, g, renderers, lit);
        }

        // ───────── 地面 ─────────

        static MeshBuilder BuildGround(VoxelWorld w, int[] g)
        {
            var mb = new MeshBuilder();
            int vx = w.W * N + 1, vz = w.D * N + 1;
            var hs = new float[vx * vz];
            for (int j = 0; j < vz; j++)
                for (int i = 0; i < vx; i++)
                {
                    float px = i / (float)N, pz = j / (float)N;
                    // 加一點起伏，避免平地像桌面
                    hs[i + j * vx] = GroundLevel(w, g, px, pz) * Vis.H + (Vis.Hash(i, j, 11) - 0.5f) * 0.035f;
                }

            for (int j = 0; j < vz - 1; j++)
            {
                for (int i = 0; i < vx - 1; i++)
                {
                    Vector3 a = P(i, j, hs, vx), b = P(i + 1, j, hs, vx), c = P(i + 1, j + 1, hs, vx), d = P(i, j + 1, hs, vx);
                    // 對角線交錯，塊面才不會全部朝同一個方向
                    if (((i + j) & 1) == 0)
                    {
                        GroundTri(mb, w, g, a, b, c);
                        GroundTri(mb, w, g, a, c, d);
                    }
                    else
                    {
                        GroundTri(mb, w, g, a, b, d);
                        GroundTri(mb, w, g, b, c, d);
                    }
                }
            }

            // 地圖邊緣往下封一圈土層，看起來像一塊切下來的地景
            const float Base = 0.26f; // 只比河床低一點：最低的地面在 0.5
            for (int i = 0; i < vx - 1; i++)
            {
                Skirt(mb, P(i, 0, hs, vx), P(i + 1, 0, hs, vx), Base, Vector3.back);
                Skirt(mb, P(i, vz - 1, hs, vx), P(i + 1, vz - 1, hs, vx), Base, Vector3.forward);
            }
            for (int j = 0; j < vz - 1; j++)
            {
                Skirt(mb, P(0, j, hs, vx), P(0, j + 1, hs, vx), Base, Vector3.left);
                Skirt(mb, P(vx - 1, j, hs, vx), P(vx - 1, j + 1, hs, vx), Base, Vector3.right);
            }
            return mb;
        }

        static Vector3 P(int i, int j, float[] hs, int vx)
        {
            return new Vector3(i / (float)N, hs[i + j * vx], j / (float)N);
        }

        static void Skirt(MeshBuilder mb, Vector3 a, Vector3 b, float baseY, Vector3 outward)
        {
            float mid = Mathf.Min(a.y, b.y) - 0.30f;
            Vector3 am = new Vector3(a.x, Mathf.Max(mid, baseY), a.z), bm = new Vector3(b.x, Mathf.Max(mid, baseY), b.z);
            mb.Quad(a, b, bm, am, Earth, outward);
            mb.Quad(am, bm, new Vector3(b.x, baseY, b.z), new Vector3(a.x, baseY, a.z), Mix(Rock2, C(0.875f, 0.865f, 0.820f), 0.45f), outward); // 底緣偏淡，融入霧色
        }

        static void GroundTri(MeshBuilder mb, VoxelWorld w, int[] g, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 m = (a + b + c) / 3f;
            float ny = Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y);
            int cx = Mathf.Clamp((int)m.x, 0, w.W - 1), cz = Mathf.Clamp((int)m.z, 0, w.D - 1);
            int h = g[cx + cz * w.W];
            byte top = h > 0 ? w.v[w.Idx(cx, cz, h - 1)] : Block.Dirt;
            float jit = Vis.Hash((int)(m.x * 8), (int)(m.z * 8), 5);

            Color32 baseCol;
            if (top == Block.Sand) baseCol = Shade(Sand, 0.94f + jit * 0.1f);
            else if (top == Block.Stone)
            {
                // 石面：每格深淺交錯，像鋪石
                baseCol = Shade(((cx + cz) & 1) == 0 ? Paving1 : Paving2, 0.96f + jit * 0.07f);
            }
            else
            {
                float patch = Vis.Hash(cx / 2, cz / 2, 9);
                baseCol = Mix(Mix(Grass1, Grass2, patch), GrassHigh, (h - 4) / 5f);
                baseCol = Shade(baseCol, 0.93f + jit * 0.12f);
            }

            Color32 col;
            if (ny < 0.55f) col = Mix(Rock1, Rock2, jit);                       // 陡：岩壁
            else if (ny < 0.80f) col = Mix(Earth, baseCol, (ny - 0.55f) / 0.25f); // 中：露土
            else col = baseCol;

            if (m.y < WaterLevel * Vis.H - 0.02f) col = Mix(col, Riverbed, 0.55f); // 水面下偏暗綠
            mb.Tri(a, b, c, col, Vector3.up);
        }

        // ───────── 水面 ─────────

        static void BuildWater(VoxelWorld w, int[] g, Transform parent)
        {
            var mb = new MeshBuilder();
            float y = WaterLevel * Vis.H;
            var done = new bool[w.W * w.D];
            for (int cy = 0; cy < w.D; cy++)
                for (int cx = 0; cx < w.W; cx++)
                {
                    if (w.Get(cx, cy, 1) != Block.Water) continue;
                    // 水面往岸邊多鋪一格，讓它自然地被斜坡切出岸線
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int x = cx + dx, z = cy + dy;
                            if (x < 0 || z < 0 || x >= w.W || z >= w.D || done[x + z * w.W]) continue;
                            done[x + z * w.W] = true;
                            mb.Quad(new Vector3(x, y, z), new Vector3(x, y, z + 1), new Vector3(x + 1, y, z + 1), new Vector3(x + 1, y, z),
                                Color.white, Vector3.up);
                        }
                }
            if (mb.VertexCount == 0) return;
            var r = mb.ToObject("Water", parent, Mats.Water(new Color(0.28f, 0.52f, 0.53f, 0.72f)), false);
            r.receiveShadows = false;
        }

        // ───────── 樹：松與桃花 ─────────

        static bool IsTreeBase(VoxelWorld w, int[] g, int x, int y)
        {
            int h = g[x + y * w.W];
            return w.Get(x, y, h) == Block.Wood && w.Get(x, y, h + 1) == Block.Wood && w.Get(x, y, h + 2) == Block.Leaf;
        }

        static void BuildTrees(VoxelWorld w, int[] g, MeshBuilder props, MeshBuilder[] covers)
        {
            for (int y = 0; y < w.D; y++)
                for (int x = 0; x < w.W; x++)
                {
                    if (!IsTreeBase(w, g, x, y)) continue;
                    int h = g[x + y * w.W], seed = x * 131 + y * 17;
                    var centre = new Vector3(x + 0.5f, 0, y + 0.5f);
                    bool blossom = Vis.Hash(seed, 9) < 0.30f;

                    // 樹幹：三段微微錯位，像盤曲的老樹
                    Color32 bark = Shade(Bark, 0.9f + Vis.Hash(seed, 1) * 0.3f);
                    Vector3 p = centre + Vector3.up * (h * Vis.H - 0.08f);
                    float lx = (Vis.Hash(seed, 2) - 0.5f) * 0.26f, lz = (Vis.Hash(seed, 4) - 0.5f) * 0.26f;
                    props.Lathe(p, new[] { 0.31f, 0.18f, 0.15f }, new[] { 0f, 0.14f, 0.48f }, 7, bark, false, seed);
                    props.Lathe(p + new Vector3(lx * 0.5f, 0.45f, lz * 0.5f), new[] { 0.15f, 0.13f }, new[] { 0f, 0.42f }, 7, bark, false, seed + 1);
                    props.Lathe(p + new Vector3(lx, 0.84f, lz), new[] { 0.13f, 0.11f, 0.16f }, new[] { 0f, 0.24f, 0.40f }, 7, bark, false, seed + 2);
                    // 一根橫出的枝
                    float ba = Vis.Hash(seed, 6) * 360f;
                    props.Box(p + new Vector3(lx, 0.98f, lz) + Quaternion.Euler(0, ba, 0) * new Vector3(0, 0.06f, 0.30f), new Vector3(0.07f, 0.07f, 0.62f), bark, ba, -14);

                    var mb = covers[w.cover[w.Idx(x, y, h + 2)]];
                    float cyMid = (h + 2.5f) * Vis.H;
                    if (blossom)
                    {
                        // 桃花：一團團粉白的花雲
                        Color32 petal = Mix(C(0.93f, 0.66f, 0.72f), C(0.97f, 0.86f, 0.86f), Vis.Hash(seed, 3));
                        mb.Blob(centre + Vector3.up * cyMid, new Vector3(1.40f, 0.30f, 1.40f), petal, seed, 0.12f, 4, 9, 0.10f);
                        mb.Blob(centre + Vector3.up * ((h + 3.42f) * Vis.H), new Vector3(0.60f, 0.26f, 0.60f), Shade(petal, 1.04f), seed + 1, 0.14f, 3, 7, 0.10f);
                        for (int k = 0; k < 6; k++)
                        {
                            float ang = (k + Vis.Hash(seed, 20 + k)) * Mathf.PI * 2 / 6, rad = 0.90f + Vis.Hash(seed, 30 + k) * 0.40f;
                            var off = new Vector3(Mathf.Cos(ang) * rad, cyMid - 0.03f + Vis.Hash(seed, 40 + k) * 0.10f, Mathf.Sin(ang) * rad);
                            mb.Blob(centre + off, new Vector3(0.50f, 0.26f, 0.50f), Shade(petal, 0.92f + Vis.Hash(seed, 50 + k) * 0.14f), seed + 5 + k, 0.16f, 3, 6, 0.10f);
                        }
                    }
                    else
                    {
                        // 松：橫向層疊的雲片，九片略為錯開，頂上再疊一片
                        Color32 pine = Mix(C(0.14f, 0.32f, 0.27f), C(0.24f, 0.42f, 0.30f), Vis.Hash(seed, 3));
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int k = (dx + 1) + (dy + 1) * 3;
                                var off = new Vector3(dx * 0.90f + (Vis.Hash(seed, 60 + k) - 0.5f) * 0.20f,
                                                      cyMid + (Vis.Hash(seed, 70 + k) - 0.5f) * 0.08f,
                                                      dy * 0.90f + (Vis.Hash(seed, 80 + k) - 0.5f) * 0.20f);
                                float r = 0.66f + Vis.Hash(seed, 90 + k) * 0.12f;
                                mb.Blob(centre + off, new Vector3(r, 0.20f, r), Shade(pine, 0.88f + Vis.Hash(seed, 100 + k) * 0.26f), seed + 20 + k, 0.13f, 3, 7, 0.10f);
                            }
                        mb.Blob(centre + Vector3.up * ((h + 3.44f) * Vis.H), new Vector3(0.62f, 0.19f, 0.62f), Shade(pine, 1.12f), seed + 1, 0.13f, 3, 7, 0.10f);
                    }
                }
        }

        // ───────── 木橋 ─────────

        static bool IsBridge(VoxelWorld w, int[] g, int x, int y, int z)
        {
            if (w.Get(x, y, z) != Block.Wood) return false;
            // 樹幹不算：樹幹上方兩層內會有樹葉
            return w.Get(x, y, z + 1) != Block.Wood && w.Get(x, y, z + 1) != Block.Leaf && w.Get(x, y, z + 2) != Block.Leaf && w.Get(x, y, z - 1) != Block.Wood;
        }

        static void BuildBridge(VoxelWorld w, int[] g, MeshBuilder mb)
        {
            for (int z = 1; z < w.H - 2; z++)
                for (int y = 0; y < w.D; y++)
                    for (int x = 0; x < w.W; x++)
                    {
                        if (!IsBridge(w, g, x, y, z)) continue;
                        float top = (z + 1) * Vis.H;
                        // 每格三片橋板，中間留縫
                        for (int k = 0; k < 3; k++)
                        {
                            float px = x + 0.17f + k * 0.33f;
                            mb.Box(new Vector3(px, top - 0.05f, y + 0.5f), new Vector3(0.30f, 0.10f, 1.0f),
                                Shade((x * 3 + k + y) % 2 == 0 ? Plank1 : Plank2, 0.94f + Vis.Hash(x * 3 + k, y, 4) * 0.12f));
                        }

                        // 兩側：下方是水的那一邊加橫樑與護欄
                        for (int side = -1; side <= 1; side += 2)
                        {
                            int ny = y + side;
                            if (IsBridge(w, g, x, ny, z)) continue;
                            float ez = y + 0.5f + side * 0.5f;
                            mb.Box(new Vector3(x + 0.5f, top - 0.17f, ez - side * 0.06f), new Vector3(1.0f, 0.14f, 0.12f), Beam);
                            if (w.Get(x, ny, 1) != Block.Water) continue;
                            mb.Box(new Vector3(x + 0.5f, top + 0.36f, ez - side * 0.05f), new Vector3(1.0f, 0.06f, 0.07f), Beam);
                            mb.Box(new Vector3(x + 0.5f, top + 0.20f, ez - side * 0.05f), new Vector3(1.0f, 0.04f, 0.05f), Plank2);
                            mb.Box(new Vector3(x + 0.02f, top + 0.19f, ez - side * 0.05f), new Vector3(0.09f, 0.42f, 0.09f), Beam);
                        }

                        // 橋墩：底下是水的格子每隔一格立一根樁
                        if ((x & 1) == 0 && w.Get(x, y, 1) == Block.Water && !IsBridge(w, g, x, y - 1, z))
                        {
                            mb.Lathe(new Vector3(x + 0.5f, 0.4f, y + 0.1f), new[] { 0.09f, 0.08f }, new[] { 0f, top - 0.5f }, 6, Beam, false);
                            mb.Lathe(new Vector3(x + 0.5f, 0.4f, y + 1.9f), new[] { 0.09f, 0.08f }, new[] { 0f, top - 0.5f }, 6, Beam, false);
                        }
                    }
        }

        // ───────── 涼亭 ─────────

        static void BuildPavilion(VoxelWorld w, int[] g, MeshBuilder props, MeshBuilder[] covers)
        {
            // 柱子：圓柱加石礎與柱頭
            for (int y = 0; y < w.D; y++)
                for (int x = 0; x < w.W; x++)
                {
                    int h = g[x + y * w.W];
                    if (w.Get(x, y, h) != Block.Pillar) continue;
                    int top = h;
                    while (w.Get(x, y, top) == Block.Pillar) top++;
                    var c = new Vector3(x + 0.5f, h * Vis.H, y + 0.5f);
                    float len = (top - h) * Vis.H;
                    props.Lathe(c, new[] { 0.27f, 0.25f, 0.19f }, new[] { 0f, 0.10f, 0.16f }, 8, Paving1, false);
                    props.Lathe(c, new[] { 0.16f, 0.15f, 0.15f, 0.21f }, new[] { 0.16f, 0.5f, len - 0.10f, len }, 10, Pillar, false);
                }

            // 屋頂：每一層屋瓦的外框畫成一層有出簷、四角上翹的斜頂；頂面保持平整可站立
            for (int s = 0; s < w.coverCount; s++)
            {
                for (int z = 0; z < w.H; z++)
                {
                    int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue;
                    for (int y = 0; y < w.D; y++)
                        for (int x = 0; x < w.W; x++)
                        {
                            int i = w.Idx(x, y, z);
                            if (w.cover[i] != s || w.v[i] != Block.Tile) continue;
                            if (x < x0) x0 = x; if (x > x1) x1 = x;
                            if (y < y0) y0 = y; if (y > y1) y1 = y;
                        }
                    if (x1 < x0) continue;
                    bool lowest = z == 0 || !HasTile(w, s, z - 1);
                    RoofTier(covers[s], x0, y0, x1 + 1, y1 + 1, z * Vis.H, (z + 1) * Vis.H, lowest);
                }
            }
        }

        // ───────── 房舍的牆與木柱 ─────────

        static readonly Color32 Plaster1 = C(0.86f, 0.82f, 0.73f), Plaster2 = C(0.80f, 0.76f, 0.67f), Footing = C(0.50f, 0.50f, 0.48f);

        static bool IsWall(VoxelWorld w, int x, int y, int z) { return w.Get(x, y, z) == Block.Wall; }

        // 牆：每一格一段粉牆，下有石基、上有木梁；門洞兩側立門框。木柱：一根細柱
        static void BuildWalls(VoxelWorld w, int[] g, MeshBuilder mb)
        {
            for (int y = 0; y < w.D; y++)
                for (int x = 0; x < w.W; x++)
                {
                    int h = g[x + y * w.W];
                    byte b = w.Get(x, y, h);
                    if (b != Block.Wall && b != Block.Post && b != Block.Stair) continue;
                    int top = h;
                    while (w.Get(x, y, top) == b) top++;
                    float y0 = h * Vis.H, len = (top - h) * Vis.H;
                    var c = new Vector3(x + 0.5f, y0, y + 0.5f);
                    if (b == Block.Stair)
                    {
                        // 樓梯的一階：實心木階，頂上一片踏板
                        mb.Box(c + Vector3.up * ((len - 0.06f) * 0.5f), new Vector3(0.96f, len - 0.06f, 1.0f), Shade(Plank2, 0.86f));
                        mb.Box(c + Vector3.up * (len - 0.03f), new Vector3(1.0f, 0.06f, 1.0f), Plank1);
                        continue;
                    }
                    if (b == Block.Post)
                    {
                        mb.Box(c + Vector3.up * 0.05f, new Vector3(0.34f, 0.10f, 0.34f), Footing);
                        mb.Box(c + Vector3.up * (len * 0.5f), new Vector3(0.20f, len, 0.20f), Beam);
                        continue;
                    }
                    // 牆身略薄於一格，相鄰的牆段之間補滿，轉角才不會有縫
                    bool wx0 = IsWall(w, x - 1, y, h), wx1 = IsWall(w, x + 1, y, h), wy0 = IsWall(w, x, y - 1, h), wy1 = IsWall(w, x, y + 1, h);
                    const float T = 0.36f; // 牆厚的一半
                    float ax0 = wx0 ? 0f : 0.5f - T, ax1 = wx1 ? 1f : 0.5f + T, az0 = wy0 ? 0f : 0.5f - T, az1 = wy1 ? 1f : 0.5f + T;
                    bool alongX = wx0 || wx1, alongY = wy0 || wy1;
                    Color32 col = Shade((x + y) % 2 == 0 ? Plaster1 : Plaster2, 0.96f + Vis.Hash(x, y, 61) * 0.08f);
                    if (alongX) Slab(mb, x + ax0, x + ax1, y + 0.5f - T, y + 0.5f + T, y0, len, col);
                    if (alongY) Slab(mb, x + 0.5f - T, x + 0.5f + T, y + az0, y + az1, y0, len, col);
                    if (!alongX && !alongY) Slab(mb, x + 0.5f - T, x + 0.5f + T, y + 0.5f - T, y + 0.5f + T, y0, len, col);
                    // 門框：這一段牆的旁邊是門洞（空的、但再過去又是牆）
                    for (int k = 0; k < 4; k++)
                    {
                        int dx = k == 0 ? 1 : k == 1 ? -1 : 0, dy = k == 2 ? 1 : k == 3 ? -1 : 0;
                        if (w.Get(x + dx, y + dy, h) != Block.Air || !IsWall(w, x + 2 * dx, y + 2 * dy, h)) continue;
                        mb.Box(new Vector3(x + 0.5f + dx * 0.5f, y0 + len * 0.5f, y + 0.5f + dy * 0.5f), new Vector3(dx != 0 ? 0.12f : 2 * T + 0.06f, len, dy != 0 ? 0.12f : 2 * T + 0.06f), Beam);
                        if (dx > 0 || dy > 0) // 門楣只畫一次
                            mb.Box(new Vector3(x + 0.5f + dx, y0 + len - 0.07f, y + 0.5f + dy), new Vector3(dx != 0 ? 1f : 2 * T + 0.06f, 0.14f, dy != 0 ? 1f : 2 * T + 0.06f), Beam);
                    }
                }
        }

        // 一段牆：石基、粉牆、頂上的木梁
        static void Slab(MeshBuilder mb, float x0, float x1, float z0, float z1, float y0, float len, Color32 col)
        {
            var c = new Vector3((x0 + x1) * 0.5f, y0, (z0 + z1) * 0.5f);
            var size = new Vector3(x1 - x0, 0, z1 - z0);
            mb.Box(c + Vector3.up * 0.09f, new Vector3(size.x + 0.05f, 0.18f, size.z + 0.05f), Footing);
            mb.Box(c + Vector3.up * (0.18f + (len - 0.30f) * 0.5f), new Vector3(size.x, len - 0.30f, size.z), col);
            mb.Box(c + Vector3.up * (len - 0.06f), new Vector3(size.x + 0.03f, 0.12f, size.z + 0.03f), Beam);
        }

        // 樓板：每格三片木板，底下一根橫梁。畫進所屬的遮蔽結構，人走到樓下時跟著淡出
        static void BuildFloors(VoxelWorld w, MeshBuilder[] covers)
        {
            for (int z = 1; z < w.H - 1; z++)
                for (int y = 0; y < w.D; y++)
                    for (int x = 0; x < w.W; x++)
                    {
                        int i = w.Idx(x, y, z);
                        if (w.v[i] != Block.Floor || w.cover[i] < 0) continue;
                        var mb = covers[w.cover[i]];
                        float top = (z + 1) * Vis.H;
                        for (int k = 0; k < 3; k++)
                            mb.Box(new Vector3(x + 0.17f + k * 0.33f, top - 0.05f, y + 0.5f), new Vector3(0.31f, 0.10f, 1.0f),
                                Shade((x * 3 + k + y) % 2 == 0 ? Plank1 : Plank2, 0.94f + Vis.Hash(x * 3 + k, y, 5) * 0.12f));
                        mb.Box(new Vector3(x + 0.5f, top - 0.17f, y + 0.5f), new Vector3(1.0f, 0.14f, 0.14f), Beam);
                        // 樓板的邊緣（旁邊沒有樓板也沒有牆）：加一道收邊
                        if (w.Get(x, y + 1, z) == Block.Air) mb.Box(new Vector3(x + 0.5f, top - 0.10f, y + 0.97f), new Vector3(1.0f, 0.20f, 0.06f), Beam);
                        if (w.Get(x, y - 1, z) == Block.Air) mb.Box(new Vector3(x + 0.5f, top - 0.10f, y + 0.03f), new Vector3(1.0f, 0.20f, 0.06f), Beam);
                    }
        }

        static bool HasTile(VoxelWorld w, int s, int z)
        {
            for (int y = 0; y < w.D; y++)
                for (int x = 0; x < w.W; x++)
                {
                    int i = w.Idx(x, y, z);
                    if (w.cover[i] == s && w.v[i] == Block.Tile) return true;
                }
            return false;
        }

        static void RoofTier(MeshBuilder mb, float x0, float z0, float x1, float z1, float yBottom, float yTop, bool lowest)
        {
            float over = lowest ? 0.40f : 0.26f; // 出簷
            float lift = lowest ? 0.20f : 0.13f; // 簷角上翹
            Vector3 centre = new Vector3((x0 + x1) * 0.5f, yBottom, (z0 + z1) * 0.5f);

            // 上緣四角（平頂）與下緣（出簷）的點；下緣每邊多一個中點，四角抬高形成翹角
            var top = new[] { new Vector3(x0, yTop, z0), new Vector3(x1, yTop, z0), new Vector3(x1, yTop, z1), new Vector3(x0, yTop, z1) };
            float ex0 = x0 - over, ex1 = x1 + over, ez0 = z0 - over, ez1 = z1 + over, eb = yBottom - 0.06f;
            var eave = new[] { new Vector3(ex0, eb + lift, ez0), new Vector3(ex1, eb + lift, ez0), new Vector3(ex1, eb + lift, ez1), new Vector3(ex0, eb + lift, ez1) };

            for (int k = 0; k < 4; k++)
            {
                int k1 = (k + 1) % 4;
                Vector3 ta = top[k], tb = top[k1], ea = eave[k], ebp = eave[k1];
                Vector3 em = (ea + ebp) * 0.5f; em.y = eb;                 // 簷邊中點較低
                Vector3 tm = (ta + tb) * 0.5f;
                Vector3 outward = (em - centre); outward.y = 0.8f;
                // 屋面：分成一壟一壟的瓦，深淺交錯；簷邊中段較低、兩端上翹
                int rows = Mathf.Max(4, Mathf.RoundToInt((tb - ta).magnitude * 2.4f));
                for (int i = 0; i < rows; i++)
                {
                    float t0 = i / (float)rows, t1 = (i + 1) / (float)rows;
                    Vector3 e0 = t0 < 0.5f ? Vector3.Lerp(ea, em, t0 * 2) : Vector3.Lerp(em, ebp, (t0 - 0.5f) * 2);
                    Vector3 e1 = t1 < 0.5f ? Vector3.Lerp(ea, em, t1 * 2) : Vector3.Lerp(em, ebp, (t1 - 0.5f) * 2);
                    mb.Quad(Vector3.Lerp(ta, tb, t0), Vector3.Lerp(ta, tb, t1), e1, e0,
                        Shade((i & 1) == 0 ? Tile1 : Tile2, 0.94f + Vis.Hash(i, k, 77) * 0.12f), outward);
                }
                // 簷底
                Vector3 ia = new Vector3(Mathf.Lerp(ta.x, centre.x, 0.05f), yBottom - 0.02f, Mathf.Lerp(ta.z, centre.z, 0.05f));
                Vector3 ib = new Vector3(Mathf.Lerp(tb.x, centre.x, 0.05f), yBottom - 0.02f, Mathf.Lerp(tb.z, centre.z, 0.05f));
                mb.Quad(ea, em, (ia + ib) * 0.5f, ia, Shade(Beam, 0.9f), Vector3.down);
                mb.Quad(em, ebp, ib, (ia + ib) * 0.5f, Shade(Beam, 0.9f), Vector3.down);
                // 垂脊：沿四條斜角的深色脊線
                Vector3 mid = (ta + ea) * 0.5f + Vector3.up * 0.05f;
                Vector3 dir = ea - ta;
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
                mb.Box(mid, new Vector3(0.10f, 0.08f, dir.magnitude), Ridge, yaw, pitch);
            }

            // 平頂與邊框
            mb.Quad(top[0], top[1], top[2], top[3], Shade(Tile1, 0.92f), Vector3.up);
            if (lowest)
            {
                // 簷角的紅燈籠
                for (int k = 0; k < 4; k++)
                {
                    Vector3 hang = Vector3.Lerp(eave[k], centre, 0.07f);
                    hang.y = eb + lift - 0.05f;
                    mb.Box(hang + Vector3.down * 0.08f, new Vector3(0.012f, 0.16f, 0.012f), Ridge);
                    mb.Blob(hang + Vector3.down * 0.25f, new Vector3(0.085f, 0.11f, 0.085f), C(0.86f, 0.17f, 0.12f), k + 3, 0.04f, 4, 7, 0.06f);
                    mb.Box(hang + Vector3.down * 0.145f, new Vector3(0.07f, 0.02f, 0.07f), C(0.84f, 0.67f, 0.30f));
                    mb.Box(hang + Vector3.down * 0.39f, new Vector3(0.016f, 0.08f, 0.016f), C(0.84f, 0.67f, 0.30f));
                }
                // 最下層：簷下一圈朱紅額枋，接在柱頭上
                float by = yBottom - 0.10f, t = 0.14f;
                mb.Box(new Vector3(centre.x, by, z0 + 0.5f), new Vector3(x1 - x0 - 0.6f, 0.16f, t), Pillar);
                mb.Box(new Vector3(centre.x, by, z1 - 0.5f), new Vector3(x1 - x0 - 0.6f, 0.16f, t), Pillar);
                mb.Box(new Vector3(x0 + 0.5f, by, centre.z), new Vector3(t, 0.16f, z1 - z0 - 0.6f), Pillar);
                mb.Box(new Vector3(x1 - 0.5f, by, centre.z), new Vector3(t, 0.16f, z1 - z0 - 0.6f), Pillar);
            }
        }

        // ───────── 武俠點綴：竹叢、石燈籠、寨旗 ─────────

        // 黑風寨的旗幟：插在敵方陣地的格子交界處（不佔站位）

        static void BuildWuxiaProps(VoxelWorld w, int[] g, MeshBuilder mb)
        {
            // 竹叢：長在四格交界的角落，四周必須都是平緩的草地
            for (int y = 1; y < w.D; y++)
                for (int x = 1; x < w.W; x++)
                {
                    // 位置由規則層決定（Decor）：竹叢有碰撞體積，不能斜著穿過
                    if (w.CornerAt(x, y) != Decor.Bamboo) continue;
                    int n = 3 + (int)(Vis.Hash(x, y, 201) * 3);
                    for (int k = 0; k < n; k++)
                    {
                        float px = x + (Vis.Hash(x, y, 210 + k) - 0.5f) * 0.36f, pz = y + (Vis.Hash(x, y, 220 + k) - 0.5f) * 0.36f;
                        Bamboo(mb, new Vector3(px, GroundLevel(w, g, px, pz) * Vis.H, pz), x * 97 + y * 13 + k);
                    }
                }

            // 石燈籠（涼亭四角外側）與寨旗：同樣照規則層給的位置畫
            for (int y = 1; y < w.D; y++)
                for (int x = 1; x < w.W; x++)
                    if (w.CornerAt(x, y) == Decor.Lantern) StoneLantern(mb, new Vector3(x, GroundLevel(w, g, x, y) * Vis.H, y));

            for (int i = 0; i < Decor.Banners.GetLength(0); i++)
            {
                int bx = Decor.Banners[i, 0], by = Decor.Banners[i, 1];
                if (w.CornerAt(bx, by) != Decor.Banner) continue;
                Banner(mb, new Vector3(bx, GroundLevel(w, g, bx, by) * Vis.H, by), 30 + i * 70);
            }
        }

        static void Bamboo(MeshBuilder mb, Vector3 p, int seed)
        {
            float len = 1.05f + Vis.Hash(seed, 1) * 0.75f;
            Color32 stalk = Shade(C(0.48f, 0.62f, 0.38f), 0.9f + Vis.Hash(seed, 2) * 0.22f);
            // 竹竿：一節一節，竹節處略粗
            int joints = Mathf.Max(3, (int)(len / 0.26f));
            var rad = new float[joints * 2 + 1];
            var hgt = new float[joints * 2 + 1];
            for (int j = 0; j <= joints * 2; j++)
            {
                hgt[j] = len * j / (joints * 2f);
                rad[j] = ((j & 1) == 0 ? 0.023f : 0.017f) * (1 - 0.35f * j / (joints * 2f));
            }
            mb.Lathe(p + Vector3.down * 0.04f, rad, hgt, 5, stalk, true, seed);

            // 竹葉：梢頭與上段各一簇下垂的細葉
            Color32 leaf = Shade(C(0.36f, 0.54f, 0.34f), 0.9f + Vis.Hash(seed, 3) * 0.25f);
            for (int tier = 0; tier < 2; tier++)
            {
                Vector3 top = p + Vector3.up * (len * (tier == 0 ? 0.98f : 0.76f));
                for (int k = 0; k < 5; k++)
                {
                    float ang = (k + Vis.Hash(seed, 10 + k + tier * 7)) * Mathf.PI * 2 / 5;
                    Vector3 dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)), side = new Vector3(-dir.z, 0, dir.x) * 0.030f;
                    Vector3 tip = top + dir * 0.30f + Vector3.down * 0.10f, mid = top + dir * 0.13f + Vector3.up * 0.03f;
                    mb.Tri(mid - side, mid + side, tip, leaf, Vector3.up);
                    mb.Tri(mid - side, mid + side, tip, leaf, Vector3.down);
                    mb.Tri(top, mid - side, mid + side, leaf, Vector3.up);
                    mb.Tri(top, mid - side, mid + side, leaf, Vector3.down);
                }
            }
        }

        static void StoneLantern(MeshBuilder mb, Vector3 p)
        {
            Color32 stone = Shade(Paving2, 0.92f), dark = Shade(Rock1, 0.85f);
            mb.Lathe(p + Vector3.down * 0.03f, new[] { 0.21f, 0.17f, 0.08f }, new[] { 0f, 0.09f, 0.14f }, 6, stone, false);
            mb.Lathe(p, new[] { 0.07f, 0.065f }, new[] { 0.14f, 0.46f }, 6, stone, false);
            mb.Box(p + Vector3.up * 0.49f, new Vector3(0.30f, 0.05f, 0.30f), stone);
            mb.Box(p + Vector3.up * 0.61f, new Vector3(0.19f, 0.19f, 0.19f), C(0.99f, 0.86f, 0.50f)); // 燈火
            for (int k = 0; k < 4; k++)
            {
                // 火窗四角的石柱
                var o = new Vector3((k & 1) == 0 ? -0.10f : 0.10f, 0.61f, (k & 2) == 0 ? -0.10f : 0.10f);
                mb.Box(p + o, new Vector3(0.04f, 0.20f, 0.04f), stone);
            }
            mb.Lathe(p, new[] { 0.27f, 0.07f }, new[] { 0.71f, 0.88f }, 4, dark, false, Mathf.PI / 4);
            mb.Blob(p + Vector3.up * 0.92f, Vector3.one * 0.045f, dark, 1, 0.05f, 3, 5);
        }

        static void Banner(MeshBuilder mb, Vector3 p, float yaw)
        {
            Color32 red = C(0.70f, 0.13f, 0.11f), edge = C(0.14f, 0.11f, 0.10f), gold = C(0.84f, 0.67f, 0.30f);
            var rot = Quaternion.Euler(0, yaw, 0);
            mb.Lathe(p + Vector3.down * 0.05f, new[] { 0.045f, 0.032f }, new[] { 0f, 2.25f }, 6, Beam, true);
            mb.Blob(p + Vector3.up * 2.24f, Vector3.one * 0.06f, gold, 2, 0.05f, 3, 6);
            mb.Box(p + Vector3.up * 2.08f + rot * new Vector3(0.24f, 0, 0), new Vector3(0.56f, 0.035f, 0.035f), Beam, yaw);
            // 旗面：紅底黑邊，下緣一條金線
            mb.Box(p + Vector3.up * 1.55f + rot * new Vector3(0.27f, 0, 0), new Vector3(0.44f, 1.00f, 0.014f), red, yaw);
            mb.Box(p + Vector3.up * 1.55f + rot * new Vector3(0.50f, 0, 0), new Vector3(0.03f, 1.00f, 0.018f), edge, yaw);
            mb.Box(p + Vector3.up * 1.07f + rot * new Vector3(0.27f, 0, 0), new Vector3(0.44f, 0.035f, 0.018f), gold, yaw);
            mb.Box(p + Vector3.up * 1.60f + rot * new Vector3(0.27f, 0, 0.002f), new Vector3(0.20f, 0.20f, 0.02f), edge, yaw, 0); // 寨徽
        }

        // ───────── 遠景：水墨遠山與霧 ─────────

        static void BuildBackdrop(VoxelWorld w, Transform parent, Color sky)
        {
            var mb = new MeshBuilder();
            Color32 bg = sky;
            var centre = new Vector3(w.W * 0.5f, 0, w.D * 0.5f);

            // 霧面：墊在地景下方，往外淡入天色，地景像是浮在雲霧上
            const int Seg = 40;
            Color32 mist = Mix(bg, C(0.62f, 0.66f, 0.66f), 0.40f);
            for (int i = 0; i < Seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / Seg, a1 = (i + 1) * Mathf.PI * 2 / Seg;
                Vector3 c = centre + Vector3.up * 0.28f;
                mb.TriGradient(c, c + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * 64, c + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * 64, mist, bg, bg);
            }

            // 遠山：三層，由遠而近；越遠越淡，山腳一律淡入霧色
            float[] radius = { 150f, 118f, 90f }, height = { 62f, 44f, 28f }, ink = { 0.24f, 0.40f, 0.58f };
            Color32 inkBlue = C(0.34f, 0.43f, 0.47f);
            for (int layer = 0; layer < 3; layer++)
            {
                const int n = 72;
                var ridge = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2 / n;
                    // 幾組不同頻率的起伏疊加出峰巒，再加一點隨機的尖峰
                    float f = 0.5f + 0.25f * Mathf.Sin(a * 3 + layer * 1.7f) + 0.16f * Mathf.Sin(a * 7 + layer * 4.1f) + 0.09f * Mathf.Sin(a * 13 + layer);
                    f = Mathf.Clamp01(f) * (0.55f + 0.45f * Vis.Hash(i, layer, 300));
                    if (Vis.Hash(i, layer, 301) > 0.82f) f = Mathf.Min(1.15f, f + 0.35f);
                    ridge[i] = centre + new Vector3(Mathf.Cos(a) * radius[layer], height[layer] * (0.18f + 0.82f * f) - 6, Mathf.Sin(a) * radius[layer]);
                }
                Color32 top = Mix(bg, inkBlue, ink[layer]);
                for (int i = 0; i < n; i++)
                {
                    Vector3 r0 = ridge[i], r1 = ridge[(i + 1) % n];
                    Vector3 b0 = new Vector3(r0.x, -8, r0.z), b1 = new Vector3(r1.x, -8, r1.z);
                    mb.TriGradient(b0, r0, r1, bg, top, top);
                    mb.TriGradient(b0, r1, b1, bg, top, bg);
                }
            }

            // 不受光、不受霧影響；排在不透明物件之後、其他半透明物件之前
            var mat = Mats.Glow(Color.white);
            mat.renderQueue = 2001;
            var r = mb.ToObject("Backdrop", parent, mat, false);
            r.receiveShadows = false;
        }

        // ───────── 地表點綴 ─────────

        static void BuildDecor(VoxelWorld w, int[] g, MeshBuilder mb)
        {
            for (int y = 0; y < w.D; y++)
            {
                for (int x = 0; x < w.W; x++)
                {
                    int h = g[x + y * w.W];
                    if (h == 0) continue;
                    byte top = w.v[w.Idx(x, y, h - 1)];
                    bool occupied = w.Get(x, y, h) != Block.Air; // 樹、柱、橋板所在
                    bool river = w.Get(x, y, 1) == Block.Water;
                    if (occupied || river) continue;

                    if (top == Block.Grass)
                    {
                        // 草叢：放在格子四周，不擋住中央站位
                        int n = (int)(Vis.Hash(x, y, 60) * 3.2f);
                        for (int k = 0; k < n; k++)
                        {
                            float ang = Vis.Hash(x, y, 61 + k) * Mathf.PI * 2, rad = 0.30f + Vis.Hash(x, y, 70 + k) * 0.16f;
                            float px = x + 0.5f + Mathf.Cos(ang) * rad, pz = y + 0.5f + Mathf.Sin(ang) * rad;
                            Tuft(mb, new Vector3(px, GroundLevel(w, g, px, pz) * Vis.H, pz), x * 7 + y * 13 + k,
                                Mix(C(0.34f, 0.50f, 0.34f), C(0.56f, 0.64f, 0.40f), Vis.Hash(x, y, 80 + k)), 0.13f + Vis.Hash(x, y, 90 + k) * 0.09f);
                        }
                        // 偶爾一朵小花
                        if (Vis.Hash(x, y, 100) > 0.90f)
                        {
                            float px = x + 0.2f + Vis.Hash(x, y, 101) * 0.6f, pz = y + 0.14f;
                            var p = new Vector3(px, GroundLevel(w, g, px, pz) * Vis.H, pz);
                            mb.Box(p + Vector3.up * 0.07f, new Vector3(0.015f, 0.14f, 0.015f), C(0.36f, 0.56f, 0.26f));
                            mb.Blob(p + Vector3.up * 0.15f, Vector3.one * 0.045f, Vis.Hash(x, y, 102) > 0.5f ? C(0.95f, 0.90f, 0.55f) : C(0.92f, 0.62f, 0.66f), x + y, 0.1f, 2, 5);
                        }
                    }

                    // 石塊：石地與高處較多，草地偶爾有
                    float rockChance = top == Block.Stone ? 0.14f : h >= 5 ? 0.18f : 0.05f;
                    if (Vis.Hash(x, y, 110) < rockChance)
                    {
                        // 放在格子角落，尺寸壓小，不和站在中央的角色搶位置
                        float px = x + 0.10f + Vis.Hash(x, y, 111) * 0.08f, pz = y + 0.90f - Vis.Hash(x, y, 112) * 0.06f;
                        float s = 0.05f + Vis.Hash(x, y, 113) * 0.06f;
                        mb.Blob(new Vector3(px, GroundLevel(w, g, px, pz) * Vis.H + s * 0.35f, pz), new Vector3(s * 1.3f, s * 0.8f, s),
                            Mix(Rock1, Rock2, Vis.Hash(x, y, 114)), x * 3 + y, 0.22f, 3, 5, 0.12f);
                    }

                    // 蘆葦：河岸沙地
                    if (top == Block.Sand && Vis.Hash(x, y, 120) < 0.55f)
                    {
                        for (int k = 0; k < 4; k++)
                        {
                            float px = x + 0.15f + Vis.Hash(x, y, 121 + k) * 0.7f, pz = y + 0.12f + Vis.Hash(x, y, 130 + k) * 0.2f;
                            var p = new Vector3(px, GroundLevel(w, g, px, pz) * Vis.H, pz);
                            float len = 0.34f + Vis.Hash(x, y, 140 + k) * 0.22f;
                            mb.Box(p + Vector3.up * len * 0.5f, new Vector3(0.022f, len, 0.022f), C(0.52f, 0.60f, 0.30f), 0, (Vis.Hash(x, y, 150 + k) - 0.5f) * 16);
                            mb.Box(p + Vector3.up * (len + 0.03f), new Vector3(0.04f, 0.10f, 0.04f), C(0.50f, 0.36f, 0.22f));
                        }
                    }
                }
            }
        }

        /// <summary>一叢草：三片從同一點往外張開的細長三角形。</summary>
        static void Tuft(MeshBuilder mb, Vector3 p, int seed, Color32 col, float height)
        {
            for (int k = 0; k < 3; k++)
            {
                float ang = (k + Vis.Hash(seed, k, 1)) * Mathf.PI * 2 / 3;
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)), side = new Vector3(-dir.z, 0, dir.x) * 0.035f;
                Vector3 tip = p + dir * 0.07f + Vector3.up * height;
                var shade = Shade(col, 0.9f + Vis.Hash(seed, k, 2) * 0.2f);
                // 草葉很薄，正反兩面都畫
                mb.Tri(p - side, p + side, tip, shade, dir + Vector3.up * 0.3f);
                mb.Tri(p - side, p + side, tip, shade, -dir - Vector3.up * 0.3f);
            }
        }
    }
}
