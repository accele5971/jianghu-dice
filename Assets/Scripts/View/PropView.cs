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
            var go = new GameObject("Loot " + p.item.name);
            go.transform.SetParent(root, false);
            go.transform.position = Vis.Cell(w, p.cell);
            go.transform.localScale = Vector3.one * 1.7f;
            go.transform.rotation = Quaternion.Euler(0, Vis.Hash(p.cell, 5, 321) * 360, 0);
            go.AddComponent<MeshFilter>().sharedMesh = ItemMesh(p.item);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = lit;
            mr.shadowCastingMode = ShadowCastingMode.On;
            marks[p.id] = Mark(Vis.Cell(w, p.cell) + Vector3.up * 0.62f);
            return go.transform;
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
                    t.gameObject.SetActive(!p.gone);
                    if (marks[p.id] != null) marks[p.id].gameObject.SetActive(!p.gone);
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
