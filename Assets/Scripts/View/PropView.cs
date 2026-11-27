using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jianghu
{
    /// <summary>
    /// 地圖物件的畫面：滾石、寶箱、芒草叢、絆鈴索、路人。
    /// 模型全部由程式生成，用和地形相同的材質，所以也會被迷霧蓋住。
    /// </summary>
    public sealed class PropViews
    {
        static Color32 C(float r, float g, float b) { return new Color(r, g, b, 1); }

        static readonly Color32 Rock = C(0.52f, 0.55f, 0.57f), Moss = C(0.42f, 0.52f, 0.38f);
        static readonly Color32 Wood = C(0.50f, 0.34f, 0.20f), WoodDark = C(0.36f, 0.24f, 0.14f), Brass = C(0.84f, 0.67f, 0.30f);
        static readonly Color32 Reed = C(0.76f, 0.78f, 0.46f), ReedDry = C(0.90f, 0.83f, 0.54f), Plume = C(1f, 0.97f, 0.88f);
        static readonly Color32 Stake = C(0.40f, 0.28f, 0.17f), Cord = C(0.80f, 0.72f, 0.52f), Bell = C(0.90f, 0.72f, 0.28f);

        readonly VoxelWorld w;
        readonly Transform root;
        readonly Material lit, glow;
        readonly Transform[] objs;                 // 每個物件一個（芒草合併成一個網格，沒有各自的物件）
        readonly Transform[] marks;                // 寶箱與路人頭上的金色標記
        readonly Dictionary<int, UnitView> people = new Dictionary<int, UnitView>();
        readonly List<Mesh> owned = new List<Mesh>();
        Mesh chestOpen;
        readonly Transform[] extras; // 物件上會消失的附屬品（兵器架上的兵器）
        readonly Material smokeMat;
        readonly Dictionary<int, GameObject> smokes = new Dictionary<int, GameObject>();

        /// <summary>濃煙出現或散去。cells 是被籠罩的格子。</summary>
        public void Smoke(int id, int[] cells, bool on)
        {
            GameObject go;
            if (smokes.TryGetValue(id, out go))
            {
                smokes.Remove(id);
                if (go != null) Object.Destroy(go);
            }
            if (!on || cells == null || cells.Length == 0) return;
            var mb = new MeshBuilder();
            foreach (int c in cells)
            {
                Vector3 p = Vis.Cell(w, c);
                // 每格兩三團大小不一的煙
                for (int k = 0; k < 3; k++)
                {
                    float ox = (Vis.Hash(c, k, 301) - 0.5f) * 0.5f, oz = (Vis.Hash(c, k, 302) - 0.5f) * 0.5f, r = 0.42f + Vis.Hash(c, k, 303) * 0.22f;
                    byte g = (byte)(205 + Vis.Hash(c, k, 304) * 40);
                    mb.Blob(p + new Vector3(ox, 0.35f + k * 0.28f, oz), new Vector3(r, r * 0.8f, r), new Color32(g, g, g, 255), c * 7 + k, 0.18f, 4, 7, 0.05f);
                }
            }
            var mr = mb.ToObject("Smoke " + id, root, smokeMat, false);
            mr.receiveShadows = false;
            owned.Add(mr.GetComponent<MeshFilter>().sharedMesh);
            smokes[id] = mr.gameObject;
        }

        readonly Dictionary<Item, Mesh> itemMeshes = new Dictionary<Item, Mesh>();

        /// <summary>道具的模型（丟出去的飛行物與掉在地上的實體共用）。</summary>
        public Mesh ItemMesh(Item it)
        {
            Mesh m;
            if (!itemMeshes.TryGetValue(it, out m) || m == null)
            {
                m = GearModels.Item(it).ToMesh(it.name);
                owned.Add(m);
                itemMeshes[it] = m;
            }
            return m;
        }

        public Material PropMaterial { get { return lit; } }

        // 掉在地上的道具：放大一點，頭上有金色標記
        Transform BuildLoot(Prop p)
        {
            var go = new GameObject("Loot " + p.name);
            go.transform.SetParent(root, false);
            go.transform.position = Vis.Cell(w, p.cell);
            go.transform.localScale = Vector3.one * 1.7f;
            go.transform.rotation = Quaternion.Euler(0, Vis.Hash(p.cell, 5, 321) * 360, 0);
            go.AddComponent<MeshFilter>().sharedMesh = p.gears.Count > 0 || p.item == null ? BundleMesh() : ItemMesh(p.item);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = lit;
            mr.shadowCastingMode = ShadowCastingMode.On;
            marks[p.id] = Mark(Vis.Cell(w, p.cell) + Vector3.up * 0.62f);
            return go.transform;
        }

        Mesh bundle;

        // 倒下的人留下的一包東西：一個紮口的布包，旁邊橫著一把刀
        Mesh BundleMesh()
        {
            if (bundle != null) return bundle;
            var mb = new MeshBuilder();
            Color32 cloth = C(0.44f, 0.36f, 0.26f);
            mb.Blob(new Vector3(0, 0.07f, 0), new Vector3(0.13f, 0.09f, 0.11f), cloth, 41, 0.12f, 4, 7);
            mb.Blob(new Vector3(0, 0.17f, 0), new Vector3(0.04f, 0.04f, 0.04f), C(0.60f, 0.50f, 0.36f), 42, 0.1f, 3, 6);
            mb.Box(new Vector3(0.10f, 0.02f, 0.10f), new Vector3(0.34f, 0.012f, 0.035f), C(0.80f, 0.82f, 0.86f), 35, 0);
            mb.Box(new Vector3(-0.03f, 0.02f, 0.01f), new Vector3(0.09f, 0.02f, 0.03f), C(0.30f, 0.20f, 0.12f), 35, 0);
            bundle = Own(mb.ToMesh("Bundle"));
            return bundle;
        }

        static readonly Color32[] ShopWall = { new Color32(214, 204, 184, 255), new Color32(120, 112, 104, 255), new Color32(196, 170, 120, 255), new Color32(168, 62, 48, 255), new Color32(222, 214, 206, 255) };
        static readonly Color32[] ShopSign = { new Color32(120, 92, 60, 255), new Color32(40, 40, 44, 255), new Color32(70, 120, 80, 255), new Color32(220, 170, 60, 255), new Color32(170, 80, 130, 255) };

        // 房舍（框架用的簡單模型）：一個方盒子、兩片斜屋頂、一扇門、一塊依店別上色的招牌
        MeshBuilder House(Prop p)
        {
            var mb = new MeshBuilder();
            float sx = p.sx - 0.16f, sz = p.sy - 0.16f, wallH = p.shop == ShopKind.Inn ? 1.9f : 1.45f;
            Color32 wall = ShopWall[(int)p.shop], sign = ShopSign[(int)p.shop], roof = C(0.24f, 0.28f, 0.33f), beam = C(0.30f, 0.20f, 0.13f);
            mb.Box(new Vector3(0, 0.06f, 0), new Vector3(sx + 0.12f, 0.12f, sz + 0.12f), C(0.52f, 0.52f, 0.50f));   // 石基
            if (p.shop == ShopKind.TeaStall)
            {
                // 茶攤：四根柱子撐著布棚，底下一張長桌
                for (int kx = -1; kx <= 1; kx += 2)
                    for (int kz = -1; kz <= 1; kz += 2) mb.Box(new Vector3(kx * (sx * 0.5f - 0.08f), 0.72f, kz * (sz * 0.5f - 0.08f)), new Vector3(0.09f, 1.3f, 0.09f), beam);
                mb.Box(new Vector3(0, 0.50f, 0), new Vector3(sx * 0.6f, 0.07f, 0.5f), beam);
                mb.Box(new Vector3(0, 0.30f, 0), new Vector3(sx * 0.5f, 0.36f, 0.08f), beam);
                mb.Lathe(new Vector3(0.3f, 0.535f, 0), new[] { 0.07f, 0.10f, 0.05f }, new[] { 0f, 0.07f, 0.16f }, 7, C(0.50f, 0.34f, 0.24f), true); // 茶壺
            }
            else
            {
                mb.Box(new Vector3(0, 0.12f + wallH * 0.5f, 0), new Vector3(sx, wallH, sz), wall);
                for (int kx = -1; kx <= 1; kx += 2)
                    for (int kz = -1; kz <= 1; kz += 2) mb.Box(new Vector3(kx * sx * 0.5f, 0.12f + wallH * 0.5f, kz * sz * 0.5f), new Vector3(0.10f, wallH, 0.10f), beam);
                mb.Box(new Vector3(0, 0.12f + 0.42f, sz * 0.5f + 0.01f), new Vector3(0.52f, 0.84f, 0.04f), beam);     // 門（朝南）
                mb.Box(new Vector3(-sx * 0.3f, 0.12f + 0.85f, sz * 0.5f + 0.01f), new Vector3(0.40f, 0.30f, 0.03f), C(0.20f, 0.16f, 0.13f)); // 窗
                mb.Box(new Vector3(sx * 0.3f, 0.12f + 0.85f, sz * 0.5f + 0.01f), new Vector3(0.40f, 0.30f, 0.03f), C(0.20f, 0.16f, 0.13f));
                if (p.shop == ShopKind.Smithy) mb.Box(new Vector3(sx * 0.3f, wallH + 0.75f, -sz * 0.2f), new Vector3(0.32f, 0.9f, 0.32f), C(0.34f, 0.33f, 0.33f)); // 煙囪
                if (p.shop == ShopKind.Weaver)
                    for (int k = -1; k <= 1; k++) mb.Box(new Vector3(k * 0.5f, 0.75f, sz * 0.5f + 0.22f), new Vector3(0.26f, 0.9f, 0.02f), k == 0 ? C(0.76f, 0.30f, 0.40f) : C(0.30f, 0.50f, 0.70f)); // 晾著的布
            }
            // 兩片斜屋頂，屋脊沿長邊
            float top = 0.12f + wallH, half = sz * 0.5f + 0.18f, run = half / Mathf.Cos(32 * Mathf.Deg2Rad);
            for (int k = -1; k <= 1; k += 2)
                mb.Box(new Vector3(0, top + Mathf.Tan(32 * Mathf.Deg2Rad) * half * 0.5f, k * half * 0.5f), new Vector3(sx + 0.36f, 0.07f, run), p.shop == ShopKind.TeaStall ? sign : roof, 0, k * 32);
            mb.Box(new Vector3(0, top + Mathf.Tan(32 * Mathf.Deg2Rad) * half + 0.02f, 0), new Vector3(sx + 0.40f, 0.08f, 0.10f), beam);
            // 招牌
            mb.Box(new Vector3(0, top - 0.16f, sz * 0.5f + 0.05f), new Vector3(0.80f, 0.26f, 0.04f), sign);
            mb.Box(new Vector3(0, top - 0.16f, sz * 0.5f + 0.075f), new Vector3(0.56f, 0.10f, 0.01f), C(0.95f, 0.92f, 0.82f));
            return mb;
        }

        // 鐵蒺藜：幾顆四面有刺的小鐵釘
        Transform BuildCaltrops(Prop p)
        {
            var mb = new MeshBuilder();
            Color32 iron = C(0.24f, 0.25f, 0.28f);
            for (int k = 0; k < 7; k++)
            {
                var o = new Vector3((Vis.Hash(p.cell, k, 311) - 0.5f) * 0.7f, 0.04f, (Vis.Hash(p.cell, k, 312) - 0.5f) * 0.7f);
                float yaw = Vis.Hash(p.cell, k, 313) * 180;
                mb.Box(o, new Vector3(0.11f, 0.018f, 0.018f), iron, yaw, 0);
                mb.Box(o, new Vector3(0.018f, 0.018f, 0.11f), iron, yaw, 0);
                mb.Box(o + Vector3.up * 0.03f, new Vector3(0.018f, 0.09f, 0.018f), iron, yaw, 0);
            }
            return Make(mb, "Caltrops", Vis.Cell(w, p.cell));
        }

        public PropViews(Battle b, Transform parent)
        {
            w = b.world;
            root = new GameObject("MapProps").transform;
            root.SetParent(parent, false);
            lit = new Material(Mats.Terrain) { mainTexture = Texture2D.whiteTexture, color = Color.white };
            glow = Mats.Glow(new Color(1f, 0.84f, 0.36f, 0.95f));
            // 戰鬥中還會多出東西（鐵蒺藜），所以多留一些位置
            objs = new Transform[b.props.Count + 128];
            marks = new Transform[b.props.Count + 128];
            extras = new Transform[b.props.Count + 128];
            smokeMat = Mats.Glow(new Color(0.86f, 0.86f, 0.84f, 0.62f));

            var reeds = new MeshBuilder();
            foreach (var p in b.props)
            {
                Vector3 pos = Vis.Cell(w, p.cell);
                switch (p.kind)
                {
                    case PropKind.Grass:
                        Reeds(reeds, pos, p.cell);
                        break;
                    case PropKind.Boulder:
                    {
                        var mb = new MeshBuilder();
                        mb.Blob(new Vector3(0, 0.36f, 0), new Vector3(0.41f, 0.37f, 0.39f), Rock, 31 + p.id, 0.16f, 5, 8, 0.10f);
                        mb.Blob(new Vector3(0.10f, 0.62f, -0.06f), new Vector3(0.20f, 0.07f, 0.18f), Moss, 77 + p.id, 0.2f, 3, 6);
                        objs[p.id] = Make(mb, "Boulder", pos);
                        break;
                    }
                    case PropKind.Chest:
                        objs[p.id] = Make(Chest(false), "Chest", pos);
                        objs[p.id].rotation = Quaternion.Euler(0, 35, 0);
                        chestOpen = Own(Chest(true).ToMesh("ChestOpen"));
                        marks[p.id] = Mark(pos + Vector3.up * 0.80f);
                        break;
                    case PropKind.Trap:
                    {
                        var mb = new MeshBuilder();
                        mb.Box(new Vector3(-0.42f, 0.12f, 0), new Vector3(0.05f, 0.26f, 0.05f), Stake);
                        mb.Box(new Vector3(0.42f, 0.12f, 0), new Vector3(0.05f, 0.26f, 0.05f), Stake);
                        mb.Box(new Vector3(0, 0.17f, 0), new Vector3(0.84f, 0.014f, 0.014f), Cord);
                        mb.Blob(new Vector3(-0.14f, 0.13f, 0), Vector3.one * 0.045f, Bell, 5, 0.05f, 3, 6);
                        mb.Blob(new Vector3(0.16f, 0.13f, 0), Vector3.one * 0.045f, Bell, 6, 0.05f, 3, 6);
                        // 幾根斜插的竹釘
                        for (int k = 0; k < 4; k++)
                            mb.Box(new Vector3(-0.24f + k * 0.16f, 0.07f, 0.16f), new Vector3(0.02f, 0.16f, 0.02f), Cord, 0, 25);
                        objs[p.id] = Make(mb, "Trap", pos);
                        objs[p.id].rotation = Quaternion.Euler(0, 90, 0);
                        objs[p.id].gameObject.SetActive(p.found && !p.gone);
                        break;
                    }
                    case PropKind.Barrel:
                    {
                        // 火油桶：鼓腹木桶，兩道鐵箍，桶身貼著紅紙
                        var mb = new MeshBuilder();
                        Color32 stave = C(0.46f, 0.31f, 0.19f), hoop = C(0.20f, 0.20f, 0.22f);
                        mb.Lathe(Vector3.zero, new[] { 0.21f, 0.27f, 0.27f, 0.21f }, new[] { 0f, 0.16f, 0.36f, 0.52f }, 10, stave, true);
                        mb.Lathe(Vector3.zero, new[] { 0.255f, 0.255f }, new[] { 0.10f, 0.14f }, 10, hoop, false);
                        mb.Lathe(Vector3.zero, new[] { 0.255f, 0.255f }, new[] { 0.38f, 0.42f }, 10, hoop, false);
                        mb.Box(new Vector3(0, 0.27f, 0.272f), new Vector3(0.16f, 0.16f, 0.012f), C(0.74f, 0.16f, 0.13f));
                        mb.Box(new Vector3(0.05f, 0.56f, 0), new Vector3(0.012f, 0.10f, 0.012f), C(0.86f, 0.80f, 0.62f)); // 引線
                        objs[p.id] = Make(mb, "Barrel", pos);
                        break;
                    }
                    case PropKind.Barricade:
                    {
                        // 拒馬：一根橫木，穿著幾組削尖的交叉木樁
                        var mb = new MeshBuilder();
                        Color32 log = C(0.42f, 0.29f, 0.18f), tip = C(0.78f, 0.70f, 0.52f);
                        mb.Box(new Vector3(0, 0.36f, 0), new Vector3(0.96f, 0.08f, 0.08f), log);
                        for (int k = -1; k <= 1; k++)
                            for (int side = -1; side <= 1; side += 2)
                            {
                                var c0 = new Vector3(k * 0.33f, 0.36f, 0);
                                mb.Box(c0, new Vector3(0.06f, 0.06f, 0.92f), log, 0, side * 42);
                                mb.Box(c0 + Quaternion.Euler(side * 42, 0, 0) * new Vector3(0, 0, 0.47f), new Vector3(0.045f, 0.045f, 0.10f), tip, 0, side * 42);
                            }
                        objs[p.id] = Make(mb, "Barricade", pos);
                        objs[p.id].rotation = Quaternion.Euler(0, 90, 0); // 橫擋在東西向的橋面上
                        break;
                    }
                    case PropKind.Rack:
                    {
                        // 兵器架：兩根立柱、兩道橫槓，架上斜靠著幾件兵器
                        var mb = new MeshBuilder();
                        for (int k = -1; k <= 1; k += 2) mb.Box(new Vector3(k * 0.36f, 0.42f, 0), new Vector3(0.06f, 0.84f, 0.06f), WoodDark);
                        mb.Box(new Vector3(0, 0.74f, 0), new Vector3(0.80f, 0.05f, 0.05f), Wood);
                        mb.Box(new Vector3(0, 0.30f, 0), new Vector3(0.80f, 0.05f, 0.05f), Wood);
                        mb.Box(new Vector3(0, 0.03f, 0), new Vector3(0.86f, 0.06f, 0.22f), WoodDark);
                        objs[p.id] = Make(mb, "Rack", pos);
                        // 架上的兵器另外做，拿走之後就收掉
                        var wb = new MeshBuilder();
                        Color32 steel = C(0.84f, 0.86f, 0.90f), haft = C(0.36f, 0.22f, 0.14f);
                        for (int k = 0; k < p.gears.Count && k < 3; k++)
                        {
                            float x = (k - 1) * 0.22f;
                            wb.Box(new Vector3(x, 0.50f, 0.06f), new Vector3(0.03f, 0.92f, 0.03f), haft, 0, 6);
                            wb.Box(new Vector3(x, 0.98f, 0.01f), new Vector3(k == 1 ? 0.10f : 0.045f, 0.20f, 0.02f), steel, 0, 6);
                        }
                        extras[p.id] = Make(wb, "RackWeapons", pos);
                        extras[p.id].gameObject.SetActive(!p.open);
                        marks[p.id] = Mark(pos + Vector3.up * 1.30f);
                        marks[p.id].gameObject.SetActive(!p.open);
                        break;
                    }
                    case PropKind.Herb:
                    {
                        // 藥草：一叢寬葉，中間抽出一枝小花
                        var mb = new MeshBuilder();
                        bool salve = p.item == Item.Salve;
                        Color32 leaf = salve ? C(0.30f, 0.56f, 0.30f) : C(0.26f, 0.48f, 0.42f), bloom = salve ? C(0.86f, 0.30f, 0.30f) : C(0.80f, 0.78f, 0.95f);
                        for (int k = 0; k < 6; k++)
                        {
                            float a = k * Mathf.PI / 3 + 0.3f;
                            Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)), side = new Vector3(-d.z, 0, d.x) * 0.06f;
                            Vector3 tip = d * 0.20f + Vector3.up * 0.10f, mid = d * 0.10f + Vector3.up * 0.09f;
                            mb.Tri(Vector3.zero, mid - side, mid + side, leaf, Vector3.up);
                            mb.Tri(mid - side, mid + side, tip, leaf, Vector3.up);
                            mb.Tri(Vector3.zero, mid - side, mid + side, leaf, Vector3.down);
                            mb.Tri(mid - side, mid + side, tip, leaf, Vector3.down);
                        }
                        mb.Box(new Vector3(0, 0.13f, 0), new Vector3(0.014f, 0.26f, 0.014f), leaf);
                        mb.Blob(new Vector3(0, 0.28f, 0), Vector3.one * 0.05f, bloom, p.id, 0.12f, 3, 6);
                        objs[p.id] = Make(mb, "Herb", pos);
                        marks[p.id] = Mark(pos + Vector3.up * 0.62f);
                        break;
                    }
                    case PropKind.Shrine:
                    {
                        // 山神龕：石座、紅漆小龕、瓦頂，龕前一只香爐
                        var mb = new MeshBuilder();
                        Color32 stone = C(0.58f, 0.58f, 0.56f), red = C(0.66f, 0.18f, 0.14f), roof = C(0.22f, 0.30f, 0.36f), gold = C(0.84f, 0.67f, 0.30f);
                        mb.Box(new Vector3(0, 0.12f, 0), new Vector3(0.56f, 0.24f, 0.44f), stone);
                        mb.Box(new Vector3(0, 0.44f, -0.02f), new Vector3(0.42f, 0.40f, 0.32f), red);
                        mb.Box(new Vector3(0, 0.42f, 0.145f), new Vector3(0.24f, 0.28f, 0.02f), C(0.14f, 0.10f, 0.09f)); // 龕口
                        mb.Box(new Vector3(0, 0.36f, 0.150f), new Vector3(0.08f, 0.14f, 0.03f), gold);                 // 神位
                        mb.Lathe(new Vector3(0, 0.64f, -0.02f), new[] { 0.40f, 0.10f }, new[] { 0f, 0.18f }, 4, roof, true, Mathf.PI / 4);
                        mb.Lathe(new Vector3(0, 0.24f, 0.30f), new[] { 0.06f, 0.08f }, new[] { 0f, 0.08f }, 6, C(0.34f, 0.30f, 0.26f), true);
                        for (int k = -1; k <= 1; k++) mb.Box(new Vector3(k * 0.02f, 0.38f, 0.30f), new Vector3(0.008f, 0.14f, 0.008f), C(0.70f, 0.30f, 0.24f));
                        objs[p.id] = Make(mb, "Shrine", pos);
                        objs[p.id].rotation = Quaternion.Euler(0, 110, 0);
                        marks[p.id] = Mark(pos + Vector3.up * 1.10f);
                        break;
                    }
                    case PropKind.Notice:
                    {
                        // 告示牌：兩根木樁撐著木板，板上貼著一張告示
                        var mb = new MeshBuilder();
                        for (int k = -1; k <= 1; k += 2) mb.Box(new Vector3(k * 0.30f, 0.42f, 0), new Vector3(0.05f, 0.84f, 0.05f), WoodDark);
                        mb.Box(new Vector3(0, 0.62f, 0), new Vector3(0.72f, 0.46f, 0.04f), Wood);
                        mb.Box(new Vector3(0, 0.62f, 0.024f), new Vector3(0.50f, 0.36f, 0.008f), C(0.93f, 0.90f, 0.80f));
                        for (int r = 0; r < 4; r++) mb.Box(new Vector3(0.14f - r * 0.09f, 0.62f, 0.030f), new Vector3(0.022f, 0.26f, 0.004f), C(0.16f, 0.14f, 0.14f));
                        mb.Box(new Vector3(0, 0.87f, 0), new Vector3(0.80f, 0.05f, 0.10f), WoodDark);
                        objs[p.id] = Make(mb, "Notice", pos);
                        objs[p.id].rotation = Quaternion.Euler(0, 200, 0);
                        marks[p.id] = Mark(pos + Vector3.up * 1.16f);
                        break;
                    }
                    case PropKind.Vine:
                    {
                        // 老藤：從崖頂垂到崖腳的幾股藤條
                        var mb = new MeshBuilder();
                        Vector3 top = p.link >= 0 ? Vis.Cell(w, p.link) : pos + Vector3.up * 2;
                        Vector3 flat = new Vector3(top.x - pos.x, 0, top.z - pos.z);
                        Vector3 wall = pos + flat * 0.52f; // 崖壁大約在兩格的交界
                        Color32 vine = C(0.30f, 0.42f, 0.22f), leafC = C(0.36f, 0.56f, 0.30f);
                        float h = top.y - pos.y;
                        for (int k = -1; k <= 1; k++)
                        {
                            Vector3 off = Vector3.Cross(Vector3.up, flat.normalized) * (k * 0.13f);
                            int segs = Mathf.Max(3, Mathf.CeilToInt(h / 0.3f));
                            for (int i = 0; i < segs; i++)
                            {
                                float y0 = h * i / segs, y1 = h * (i + 1) / segs;
                                float sway = Mathf.Sin(i * 1.3f + k) * 0.03f;
                                mb.Box(wall + off + new Vector3(sway, (y0 + y1) * 0.5f, 0) - flat.normalized * 0.04f, new Vector3(0.035f, y1 - y0 + 0.02f, 0.035f), vine);
                                if ((i + k) % 2 == 0) mb.Blob(wall + off + new Vector3(sway + 0.05f, y0 + 0.05f, 0) - flat.normalized * 0.07f, new Vector3(0.06f, 0.04f, 0.06f), leafC, i * 7 + k, 0.2f, 3, 5);
                            }
                        }
                        // Make 會把物件放到 pos，所以網格要用相對於 pos 的座標：整體平移回去
                        var holder = mb.ToObject("Vine", root, lit);
                        owned.Add(holder.GetComponent<MeshFilter>().sharedMesh);
                        objs[p.id] = holder.transform;
                        marks[p.id] = Mark(pos + Vector3.up * 0.9f);
                        break;
                    }
                    case PropKind.ArmorRack:
                    {
                        // 盔甲架：十字木架，架上掛著一副身甲、頂著一頂盔
                        var mb = new MeshBuilder();
                        mb.Box(new Vector3(0, 0.03f, 0), new Vector3(0.44f, 0.06f, 0.44f), WoodDark);
                        mb.Box(new Vector3(0, 0.50f, 0), new Vector3(0.06f, 0.94f, 0.06f), WoodDark);
                        mb.Box(new Vector3(0, 0.74f, 0), new Vector3(0.62f, 0.05f, 0.05f), Wood);
                        objs[p.id] = Make(mb, "ArmorRack", pos);
                        var ab = new MeshBuilder();
                        Color32 hide = C(0.50f, 0.36f, 0.24f), iron = C(0.50f, 0.52f, 0.56f);
                        ab.Box(new Vector3(0, 0.58f, 0), new Vector3(0.38f, 0.40f, 0.20f), hide);
                        ab.Box(new Vector3(0, 0.36f, 0), new Vector3(0.42f, 0.10f, 0.22f), iron);
                        for (int k = -1; k <= 1; k += 2) ab.Box(new Vector3(k * 0.25f, 0.72f, 0), new Vector3(0.14f, 0.10f, 0.20f), hide);
                        ab.Blob(new Vector3(0, 1.04f, 0), new Vector3(0.12f, 0.11f, 0.12f), iron, 9, 0.04f, 4, 7);
                        extras[p.id] = Make(ab, "RackArmor", pos);
                        extras[p.id].gameObject.SetActive(!p.open);
                        marks[p.id] = Mark(pos + Vector3.up * 1.36f);
                        marks[p.id].gameObject.SetActive(!p.open);
                        break;
                    }
                    case PropKind.Crate:
                    case PropKind.IronChest:
                    case PropKind.Box:
                    {
                        // 木箱（板條箱）、鐵箱（黑鐵包銅角）、收納盒（小木盒）：箱蓋另外做，打開後收掉
                        var mb = new MeshBuilder();
                        var lid = new MeshBuilder();
                        bool iron = p.kind == PropKind.IronChest, small = p.kind == PropKind.Box;
                        float bw = small ? 0.30f : iron ? 0.58f : 0.56f, bh = small ? 0.16f : iron ? 0.34f : 0.46f, bd = small ? 0.22f : iron ? 0.40f : 0.56f;
                        Color32 body = iron ? C(0.22f, 0.23f, 0.26f) : small ? C(0.62f, 0.44f, 0.28f) : C(0.56f, 0.42f, 0.26f);
                        Color32 trim = iron ? Brass : WoodDark;
                        mb.Box(new Vector3(0, bh * 0.5f, 0), new Vector3(bw, bh, bd), body);
                        for (int kx = -1; kx <= 1; kx += 2)
                            for (int kz = -1; kz <= 1; kz += 2) mb.Box(new Vector3(kx * bw * 0.5f, bh * 0.5f, kz * bd * 0.5f), new Vector3(0.05f, bh + 0.01f, 0.05f), trim);
                        if (!small && !iron) mb.Box(new Vector3(0, bh * 0.5f, bd * 0.5f + 0.005f), new Vector3(bw * 0.9f, 0.05f, 0.01f), WoodDark, 0, 0);
                        mb.Box(new Vector3(0, bh - 0.01f, 0), new Vector3(bw - 0.08f, 0.02f, bd - 0.08f), C(0.14f, 0.10f, 0.08f)); // 開了之後看得到的箱底
                        lid.Box(new Vector3(0, bh + 0.03f, 0), new Vector3(bw + 0.03f, 0.06f, bd + 0.03f), iron ? C(0.28f, 0.29f, 0.32f) : WoodDark);
                        if (iron) lid.Box(new Vector3(0, bh - 0.03f, bd * 0.5f + 0.02f), new Vector3(0.10f, 0.12f, 0.04f), Brass); // 重鎖
                        objs[p.id] = Make(mb, p.kind.ToString(), pos);
                        extras[p.id] = Make(lid, "Lid", pos);
                        float yaw = Vis.Hash(p.cell, 3, 331) * 50 - 25;
                        objs[p.id].rotation = extras[p.id].rotation = Quaternion.Euler(0, yaw, 0);
                        extras[p.id].gameObject.SetActive(!p.open);
                        marks[p.id] = Mark(pos + Vector3.up * (bh + 0.45f));
                        marks[p.id].gameObject.SetActive(!p.open);
                        break;
                    }
                    case PropKind.Bucket:
                    {
                        // 水桶：上寬下窄的木桶加一道提把，裡面的水另外做（倒空就收掉）
                        var mb = new MeshBuilder();
                        mb.Lathe(Vector3.zero, new[] { 0.13f, 0.17f }, new[] { 0f, 0.26f }, 9, Wood, false);
                        mb.Lathe(Vector3.zero, new[] { 0.135f, 0.135f }, new[] { 0.0f, 0.02f }, 9, WoodDark, true);
                        mb.Lathe(Vector3.zero, new[] { 0.165f, 0.165f }, new[] { 0.17f, 0.20f }, 9, C(0.22f, 0.22f, 0.24f), false);
                        mb.Box(new Vector3(0, 0.34f, 0), new Vector3(0.34f, 0.02f, 0.02f), WoodDark);
                        for (int k = -1; k <= 1; k += 2) mb.Box(new Vector3(k * 0.165f, 0.29f, 0), new Vector3(0.02f, 0.12f, 0.02f), WoodDark);
                        objs[p.id] = Make(mb, "Bucket", pos);
                        var wb = new MeshBuilder();
                        wb.Lathe(Vector3.zero, new[] { 0.155f, 0.155f }, new[] { 0.21f, 0.22f }, 9, C(0.36f, 0.62f, 0.74f), true);
                        extras[p.id] = Make(wb, "Water", pos);
                        extras[p.id].gameObject.SetActive(!p.open);
                        break;
                    }
                    case PropKind.Well:
                    {
                        // 水井：石砌井欄、兩根立柱、轆轤與小屋頂
                        var mb = new MeshBuilder();
                        Color32 stone = C(0.56f, 0.56f, 0.54f);
                        mb.Lathe(Vector3.zero, new[] { 0.40f, 0.40f, 0.33f, 0.33f }, new[] { 0f, 0.36f, 0.36f, 0.30f }, 10, stone, false);
                        mb.Lathe(Vector3.zero, new[] { 0.33f, 0.33f }, new[] { 0.12f, 0.13f }, 10, C(0.20f, 0.36f, 0.46f), true); // 井水
                        for (int k = -1; k <= 1; k += 2) mb.Box(new Vector3(k * 0.38f, 0.62f, 0), new Vector3(0.07f, 1.24f, 0.07f), WoodDark);
                        mb.Box(new Vector3(0, 0.92f, 0), new Vector3(0.80f, 0.07f, 0.07f), Wood);       // 轆轤
                        mb.Box(new Vector3(0.10f, 0.66f, 0), new Vector3(0.012f, 0.50f, 0.012f), Cord); // 井繩
                        for (int k = -1; k <= 1; k += 2) mb.Box(new Vector3(0, 1.30f, k * 0.17f), new Vector3(0.96f, 0.05f, 0.42f), C(0.24f, 0.28f, 0.33f), 0, k * 28);
                        objs[p.id] = Make(mb, "Well", pos);
                        marks[p.id] = Mark(pos + Vector3.up * 1.62f);
                        break;
                    }
                    case PropKind.Building:
                    {
                        Vector3 centre = pos + new Vector3((p.sx - 1) * 0.5f, 0, (p.sy - 1) * 0.5f);
                        objs[p.id] = Make(House(p), "Building " + p.shop, centre);
                        // 村北那一排門朝南（朝街），村南那一排轉過來門朝北
                        if (w.Y(p.cell) > MapGen.VillageY + 2) objs[p.id].rotation = Quaternion.Euler(0, 180, 0);
                        marks[p.id] = Mark(centre + Vector3.up * (p.shop == ShopKind.Inn ? 3.0f : 2.6f));
                        break;
                    }
                    case PropKind.Npc:
                    {
                        var u = new Unit
                        {
                            id = 0, name = p.name, team = 0, look = p.look, hp = 1, maxHp = 1,
                            x = w.X(p.cell), y = w.Y(p.cell), z = w.Z(p.cell), fx = p.fx, fy = p.fy,
                        };
                        var v = new UnitView(u, w, root);
                        // 中立：腳下的圈改成金色，不畫面向箭頭
                        var ring = v.root.Find("Ring");
                        if (ring != null) ring.GetComponent<MeshRenderer>().sharedMaterial = Mats.Solid(new Color(0.86f, 0.70f, 0.38f));
                        var arrow = v.root.Find("Facing");
                        if (arrow != null) arrow.gameObject.SetActive(false);
                        people[p.id] = v;
                        objs[p.id] = v.root;
                        marks[p.id] = Mark(pos + Vector3.up * 1.42f);
                        break;
                    }
                }
            }
            if (reeds.VertexCount > 0) reeds.ToObject("Reeds", root, lit, false);
        }

        Mesh Own(Mesh m) { owned.Add(m); return m; }

        Transform Make(MeshBuilder mb, string name, Vector3 pos)
        {
            var mr = mb.ToObject(name, root, lit);
            owned.Add(mr.GetComponent<MeshFilter>().sharedMesh);
            mr.transform.position = pos;
            return mr.transform;
        }

        // 頭上的金色小菱形：這裡可以互動
        Transform Mark(Vector3 pos)
        {
            var mb = new MeshBuilder();
            mb.Lathe(Vector3.zero, new[] { 0f, 0.075f, 0f }, new[] { -0.11f, 0f, 0.11f }, 4, Color.white, false);
            var mr = mb.ToObject("Mark", root, glow, false);
            owned.Add(mr.GetComponent<MeshFilter>().sharedMesh);
            mr.receiveShadows = false;
            mr.transform.position = pos;
            return mr.transform;
        }

        static MeshBuilder Chest(bool open)
        {
            var mb = new MeshBuilder();
            mb.Box(new Vector3(0, 0.17f, 0), new Vector3(0.56f, 0.30f, 0.36f), Wood);
            // 銅箍與底座
            mb.Box(new Vector3(-0.18f, 0.17f, 0), new Vector3(0.05f, 0.31f, 0.37f), Brass);
            mb.Box(new Vector3(0.18f, 0.17f, 0), new Vector3(0.05f, 0.31f, 0.37f), Brass);
            mb.Box(new Vector3(0, 0.02f, 0), new Vector3(0.60f, 0.04f, 0.40f), WoodDark);
            if (!open)
            {
                mb.Box(new Vector3(0, 0.37f, 0), new Vector3(0.58f, 0.10f, 0.38f), WoodDark);
                mb.Box(new Vector3(0, 0.30f, -0.19f), new Vector3(0.09f, 0.11f, 0.03f), Brass); // 鎖
            }
            else
            {
                // 箱蓋向後掀開
                mb.Box(new Vector3(0, 0.50f, 0.24f), new Vector3(0.58f, 0.10f, 0.38f), WoodDark, 0, -72);
                mb.Box(new Vector3(0, 0.30f, 0), new Vector3(0.48f, 0.02f, 0.28f), C(0.16f, 0.11f, 0.08f)); // 空的箱底
            }
            return mb;
        }

        // 一格芒草：十幾根高過膝的草葉，梢頭有白色的穗
        static void Reeds(MeshBuilder mb, Vector3 p, int seed)
        {
            for (int k = 0; k < 26; k++)
            {
                float ox = (Vis.Hash(seed, k, 1) - 0.5f) * 0.92f, oz = (Vis.Hash(seed, k, 2) - 0.5f) * 0.92f;
                float h = 0.55f + Vis.Hash(seed, k, 3) * 0.38f, ang = Vis.Hash(seed, k, 4) * Mathf.PI * 2;
                float lean = 0.10f + Vis.Hash(seed, k, 5) * 0.16f;
                Vector3 b0 = p + new Vector3(ox, -0.03f, oz);
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)), side = new Vector3(-dir.z, 0, dir.x) * 0.028f;
                Vector3 tip = b0 + Vector3.up * h + dir * lean;
                Color32 col = Color32.Lerp(Reed, ReedDry, Vis.Hash(seed, k, 6));
                // 葉面朝上下兩面，俯視時才看得到顏色
                mb.Tri(b0 - side, b0 + side, tip, col, Vector3.up);
                mb.Tri(b0 - side, b0 + side, tip, col, Vector3.down);
                if (k % 3 != 0) continue;
                // 穗：梢頭一縷下垂的淺色細絲
                Vector3 q = tip + dir * 0.17f + Vector3.down * 0.05f, wide = new Vector3(-dir.z, 0, dir.x) * 0.035f;
                mb.Tri(tip - wide, tip + wide, q, Plume, Vector3.up);
                mb.Tri(tip - wide, tip + wide, q, Plume, Vector3.down);
            }
        }

        /// <summary>依規則層的狀態更新外觀：開箱、絆索現形或消失、滾石沉沒。</summary>
        public void Sync(Prop p)
        {
            if (p.id >= objs.Length) return;
            if (objs[p.id] == null && p.kind == PropKind.Caltrops && !p.gone) objs[p.id] = BuildCaltrops(p);
            if (objs[p.id] == null && p.kind == PropKind.Loot && !p.gone) objs[p.id] = BuildLoot(p);
            var t = objs[p.id];
            if (t == null) return;
            switch (p.kind)
            {
                case PropKind.Caltrops:
                    t.gameObject.SetActive(!p.gone);
                    break;
                case PropKind.Loot:
                case PropKind.Herb:
                    t.gameObject.SetActive(!p.gone);
                    if (marks[p.id] != null) marks[p.id].gameObject.SetActive(!p.gone);
                    break;
                case PropKind.Barrel:
                case PropKind.Barricade:
                    if (p.gone) t.gameObject.SetActive(false);
                    else if (p.kind == PropKind.Barrel) t.position = Vis.Cell(w, p.cell);
                    break;
                case PropKind.Rack:
                case PropKind.ArmorRack:
                case PropKind.Crate:
                case PropKind.IronChest:
                case PropKind.Box:
                    if (extras[p.id] != null) extras[p.id].gameObject.SetActive(!p.open);
                    if (marks[p.id] != null) marks[p.id].gameObject.SetActive(!p.open);
                    break;
                case PropKind.Bucket:
                    if (extras[p.id] != null) extras[p.id].gameObject.SetActive(!p.open);
                    break;
                case PropKind.Chest:
                    if (p.open)
                    {
                        t.GetComponent<MeshFilter>().sharedMesh = chestOpen;
                        if (marks[p.id] != null) marks[p.id].gameObject.SetActive(false);
                    }
                    break;
                case PropKind.Trap:
                    t.gameObject.SetActive(p.found && !p.gone);
                    break;
                case PropKind.Boulder:
                    if (p.gone) t.gameObject.SetActive(false);
                    else t.position = Vis.Cell(w, p.cell);
                    break;
                case PropKind.Npc:
                    // 該說的都說完了，或不肯再說：收掉頭上的標記
                    if (marks[p.id] != null) marks[p.id].gameObject.SetActive(p.attitude > 0 && !(p.guided && p.gave));
                    break;
            }
        }

        /// <summary>路人轉身面向跟他說話的人。</summary>
        public void Face(Prop p, Vector3 worldPoint)
        {
            UnitView v;
            if (people.TryGetValue(p.id, out v)) v.FaceTo(worldPoint);
        }

        public Vector3 Position(Prop p) { return p.id < objs.Length && objs[p.id] != null ? objs[p.id].position : Vis.Cell(w, p.cell); }

        /// <summary>滾石沿著格子滾動。</summary>
        public IEnumerator Roll(Prop p, int[] path, float perCell)
        {
            var t = objs[p.id];
            if (t == null) yield break;
            for (int i = 0; i + 1 < path.Length; i++)
            {
                Vector3 a = Vis.Cell(w, path[i]), b = Vis.Cell(w, path[i + 1]);
                Vector3 flat = new Vector3(b.x - a.x, 0, b.z - a.z);
                Vector3 axis = flat.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
                var r0 = t.rotation;
                for (float k = 0; k < 1; k += Time.deltaTime / perCell)
                {
                    // 落差用拋物線帶過，看起來像是滾下去而不是瞬移
                    Vector3 pos = Vector3.Lerp(a, b, k);
                    if (b.y < a.y) pos.y = Mathf.Lerp(a.y, b.y, k * k);
                    t.position = pos;
                    t.rotation = Quaternion.AngleAxis(k * 150f, axis) * r0;
                    yield return null;
                }
                t.position = b;
            }
        }

        public void Dispose()
        {
            foreach (var v in people.Values) v.Dispose();
            foreach (var m in owned) if (m != null) Object.Destroy(m);
            Object.Destroy(lit);
        }
    }
}
