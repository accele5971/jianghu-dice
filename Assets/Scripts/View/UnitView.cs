using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jianghu
{
    /// <summary>
    /// 角色的畫面物件。外觀完全由資料（Look）決定：體型的四個數值，加上髮型、頭飾、臉部、
    /// 服裝、袖型、肩部、腰間、背部、兵器各插槽的選項與配色。
    ///
    /// 模型來源有兩種：
    /// 1. 暫定模型（預設）：程式生成的低面數人形，關節由程式擺姿勢。
    /// 2. 外部模型：Look.model 有填、且 Resources/Units/ 下找得到同名的預製物時改用它；
    ///    若帶有 Animator，動作改由動畫控制（見 骨架.md 的「正式模型規格」）。
    ///
    /// 對外的動作介面兩者相同：PoseIdle / PoseWalk / PoseAttack / PoseHurt。
    /// </summary>
    public sealed partial class UnitView
    {
        public const float BarHeight = 1.34f;   // 血條離腳底的高度
        public const float ChestHeight = 0.66f; // 飛行物的起訖高度
        const float BaseScale = 0.77f;          // 身高 1.0 時的模型縮放

        public readonly Unit unit;
        public readonly Transform root;
        public int shownHp; // 畫面上目前顯示的氣血（隨事件播放更新）
        public bool dead;   // 已倒下（和「被迷霧藏起來」是兩回事）

        static readonly Color Straw = new Color(0.84f, 0.73f, 0.45f);
        static readonly Color Boot = new Color(0.17f, 0.15f, 0.14f);
        static readonly Color ArmorCol = new Color(0.33f, 0.35f, 0.39f);
        static readonly Color GoldCol = new Color(0.82f, 0.64f, 0.25f);
        static readonly Color TeamBlue = new Color(0.20f, 0.48f, 0.85f);
        static readonly Color TeamRed = new Color(0.82f, 0.22f, 0.18f);
        static readonly Color32 Steel = new Color32(214, 219, 228, 255), Grip = new Color32(96, 62, 38, 255), Gold = new Color32(214, 170, 78, 255);
        static readonly Color32 Red = new Color32(190, 40, 34, 255), Paper = new Color32(242, 237, 220, 255), Tan = new Color32(200, 158, 92, 255);
        static readonly int ColorId = Shader.PropertyToID("_Color"), FlashId = Shader.PropertyToID("_Flash");
        static Material mergedMat;
        readonly List<MeshRenderer> merged = new List<MeshRenderer>();
        readonly MaterialPropertyBlock flashBlock = new MaterialPropertyBlock();

        /// <summary>這個角色合併後剩幾個要畫的物件（效能量測用）。</summary>
        public int MergedCount { get { return merged.Count; } }

        /// <summary>
        /// 把同一個關節底下的所有零件併成一個網格（顏色寫進頂點），整個角色共用一個材質。
        /// 一個角色原本有三、四十個零件各畫一次（陰影再各畫一次），合併後只剩六塊：
        /// 腰、左右腿、上身（含頭與背上的東西）、左右臂（含手上拿的）。關節照常轉動，姿勢不受影響。
        /// </summary>
        void MergeParts()
        {
            if (hips == null || Shader.Find("Jianghu/UnitLit") == null) return; // 外部模型、或著色器不在時不合併
            if (mergedMat == null) mergedMat = Mats.Unit(Color.white);
            var joints = new[] { hips, hipL, hipR, upper, shoulderL, shoulderR_ };
            var groups = new List<MeshRenderer>[joints.Length];
            for (int i = 0; i < groups.Length; i++) groups[i] = new List<MeshRenderer>();

            var flat = new Dictionary<MeshRenderer, Color>();
            for (int i = 0; i < body.Count; i++) flat[body[i]] = bodyColor[i];

            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable || r.transform.childCount > 0) continue;
                // 歸給最近的那個關節
                for (Transform t = r.transform.parent; t != null && t != model; t = t.parent)
                {
                    int gi = System.Array.IndexOf(joints, t);
                    if (gi >= 0) { groups[gi].Add(r); break; }
                }
            }

            var old = new List<Mesh>(owned);
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color32>(); var tri = new List<int>();
            var tv = new List<Vector3>(); var tn = new List<Vector3>(); var tc = new List<Color32>(); var tt = new List<int>();
            for (int gi = 0; gi < joints.Length; gi++)
            {
                if (groups[gi].Count == 0) continue;
                v.Clear(); n.Clear(); c.Clear(); tri.Clear();
                bool silhouette = false;
                Matrix4x4 toJoint = joints[gi].worldToLocalMatrix;
                foreach (var r in groups[gi])
                {
                    var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    Matrix4x4 mx = toJoint * r.transform.localToWorldMatrix;
                    Matrix4x4 nmx = mx.inverse.transpose; // 零件常被壓扁拉長，法線要用逆轉置矩陣
                    mesh.GetVertices(tv); mesh.GetNormals(tn); mesh.GetColors(tc); mesh.GetTriangles(tt, 0);
                    Color fc;
                    bool part = flat.TryGetValue(r, out fc);
                    Color32 fc32 = part ? (Color32)fc : new Color32(255, 255, 255, 255);
                    if (part && r.sharedMaterials.Length > 1) silhouette = true;
                    int start = v.Count;
                    for (int i = 0; i < tv.Count; i++)
                    {
                        v.Add(mx.MultiplyPoint3x4(tv[i]));
                        n.Add(i < tn.Count ? nmx.MultiplyVector(tn[i]).normalized : Vector3.up);
                        c.Add(!part && i < tc.Count ? tc[i] : fc32);
                    }
                    for (int i = 0; i < tt.Count; i++) tri.Add(start + tt[i]);
                    body.Remove(r);
                    r.gameObject.SetActive(false); // Destroy 到這一幀結束才生效，先隱藏
                    Object.Destroy(r.gameObject);
                }

                var m = new Mesh { name = "Merged " + joints[gi].name };
                m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(tri, 0);
                m.RecalculateBounds();
                m.UploadMeshData(true);
                var go = new GameObject("Merged");
                go.transform.SetParent(joints[gi], false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>();
                if (silhouette && sil != null) mr.sharedMaterials = new[] { mergedMat, sil };
                else mr.sharedMaterial = mergedMat;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
                merged.Add(mr);
                owned.Add(m);
            }

            // 沒被合併的零件（理論上沒有）保留原本的閃白方式
            bodyColor.Clear();
            foreach (var r in body) { Color fc; bodyColor.Add(flat.TryGetValue(r, out fc) ? fc : Color.white); }
            // 各零件原本專屬的網格已經烘進合併後的網格，可以釋放
            foreach (var m in old) { if (m != null) Object.Destroy(m); owned.Remove(m); }
        }
        static readonly int WalkId = Animator.StringToHash("Walk"), AttackId = Animator.StringToHash("Attack"), HurtId = Animator.StringToHash("Hurt");

        // ── 共用網格（第一次用到時建立）──
        static Mesh torsoMesh, skirtMesh, hemMesh, sleeveMesh, narrowSleeveMesh, legMesh, coneHatMesh, flatHatMesh, veilMesh, bandMesh, sphereMesh, cubeMesh, teamRing;
        static Material propMat;
        static Mesh facingArrow, blobMesh;
        static Material blobMat;
        readonly GameObject blob;

        /// <summary>是否用腳下的圓影代替即時陰影（由畫質設定決定）。</summary>
        public static bool BlobShadows;

        public void SetBlob(bool on) { if (blob != null) blob.SetActive(on); }

        static void EnsureMeshes()
        {
            if (torsoMesh != null) return;
            // 軀幹：腰細、胸寬、往頸部收，前後略扁
            torsoMesh = Shapes.Lathe("Torso", new[] { 0.165f, 0.185f, 0.215f, 0.205f, 0.10f }, new[] { 0.60f, 0.72f, 0.86f, 0.95f, 1.03f }, 10, false, true, 0.76f);
            // 長袍下襬：從腰往下散開到腳踝
            skirtMesh = Shapes.Lathe("Skirt", new[] { 0.295f, 0.26f, 0.205f, 0.170f }, new[] { 0.13f, 0.30f, 0.48f, 0.63f }, 10, true, false, 0.86f);
            // 短衣下襬／裙甲：只到大腿
            hemMesh = Shapes.Lathe("Hem", new[] { 0.262f, 0.222f, 0.172f }, new[] { 0.39f, 0.51f, 0.63f }, 10, true, false, 0.86f);
            // 寬袖：袖口寬；箭袖：窄而直
            sleeveMesh = Shapes.Lathe("Sleeve", new[] { 0.060f, 0.072f, 0.108f }, new[] { 0.02f, -0.20f, -0.40f }, 8, true, true);
            narrowSleeveMesh = Shapes.Lathe("NarrowSleeve", new[] { 0.058f, 0.054f, 0.049f }, new[] { 0.02f, -0.20f, -0.40f }, 8, true, true);
            legMesh = Shapes.Lathe("Leg", new[] { 0.078f, 0.062f, 0.066f }, new[] { 0f, -0.30f, -0.45f }, 7, true, true);
            // 斗笠：寬簷尖頂；帷帽：寬而平
            coneHatMesh = Shapes.Lathe("ConeHat", new[] { 0.37f, 0.21f, 0.012f }, new[] { 0f, 0.085f, 0.185f }, 12, true, false);
            flatHatMesh = Shapes.Lathe("FlatHat", new[] { 0.36f, 0.31f, 0.13f, 0.11f }, new[] { 0f, 0.025f, 0.05f, 0.11f }, 12, true, true);
            // 腰帶、頭帶、額箍：一圈短筒
            bandMesh = Shapes.Lathe("Band", new[] { 1f, 1.04f, 1f }, new[] { -0.5f, 0f, 0.5f }, 10, false, false);
            sphereMesh = Primitive(PrimitiveType.Sphere);
            cubeMesh = Primitive(PrimitiveType.Cube);
            teamRing = RingMesh(0.40f, 0.07f);
            propMat = new Material(Mats.Terrain) { mainTexture = Texture2D.whiteTexture, color = Color.white };

            // 帷帽的薄紗：只圍住兩側與後方，正面留空露出臉
            var veil = new MeshBuilder();
            const int n = 14;
            for (int i = 0; i < n; i++)
            {
                float a0 = Mathf.Deg2Rad * (145 + i * 250f / n), a1 = Mathf.Deg2Rad * (145 + (i + 1) * 250f / n);
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 t0 = d0 * 0.30f, t1 = d1 * 0.30f, b0 = d0 * 0.32f + Vector3.down * 0.27f, b1 = d1 * 0.32f + Vector3.down * 0.27f;
                var c = (i & 1) == 0 ? new Color32(240, 238, 230, 255) : new Color32(226, 224, 216, 255);
                veil.Quad(t0, t1, b1, b0, c, d0 + d1);
                veil.Quad(t0, t1, b1, b0, c, -(d0 + d1));
            }
            veilMesh = veil.ToMesh("Veil");
        }

        /// <summary>平放的圓環（腳下的陣營圈、行動者的金圈）。</summary>
        public static Mesh RingMesh(float outer, float width)
        {
            var mb = new MeshBuilder();
            const int n = 32;
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2 / n, a1 = (i + 1) * Mathf.PI * 2 / n;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                mb.Quad(d0 * (outer - width), d0 * outer, d1 * outer, d1 * (outer - width), Color.white, Vector3.up);
            }
            return mb.ToMesh("Ring");
        }

        static Mesh Primitive(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return m;
        }

        readonly Material sil;
        readonly List<MeshRenderer> body = new List<MeshRenderer>();
        readonly List<Color> bodyColor = new List<Color>();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        // 關節與掛點（暫定模型才有；外部模型時為 null）
        readonly Transform model;
        Transform hips, upper, shoulderL, shoulderR_, hipL, hipR;
        Transform socketHead, socketBack, socketHandL, socketHandR;
        bool twoHanded, bowInLeftHand, poleWeapon;
        readonly Animator animator; // 外部模型帶動畫時使用

        // 本角色的配色與比例
        Color skin, hair, cloth, trim, accent;
        float g = 1, sw = 1; // 身圍、肩寬
        float dL, chestZ = 0.16f, waistZ = 0.15f, shoulderX = 0.22f; // 腿長增減、胸前與腰前的表面位置、肩的位置

        // 這個角色專屬的網格（軀幹、下襬、兵器…），重建或移除角色時一併釋放
        readonly List<Mesh> owned = new List<Mesh>();
        Mesh Own(Mesh m) { owned.Add(m); return m; }

        /// <summary>移除這個角色的畫面物件與專屬網格（創角時每次修改都會重建）。</summary>
        public void Dispose()
        {
            foreach (var m in owned) if (m != null) Object.Destroy(m);
            owned.Clear();
            if (root == null) return;
            root.gameObject.SetActive(false); // Destroy 要到這一幀結束才生效，先隱藏以免新舊模型重疊
            Object.Destroy(root.gameObject);
        }

        public UnitView(Unit u, VoxelWorld w, Transform parent)
        {
            EnsureMeshes();
            unit = u;
            shownHp = u.hp;
            sil = Mats.Silhouette(u.team);
            var look = u.look ?? new Look();

            root = new GameObject(u.name).transform;
            root.SetParent(parent, false);
            root.position = Vis.Cell(w, w.Idx(u.x, u.y, u.z));
            root.rotation = Quaternion.LookRotation(new Vector3(u.fx, 0, u.fy == 0 && u.fx == 0 ? 1 : u.fy));

            // 腳下的陣營圈（不隨模型縮放，也不參與剪影）
            var ring = MakeMesh("Ring", root, teamRing, new Vector3(0, 0.045f, 0), Vector3.one, Vector3.zero);
            ring.sharedMaterial = Mats.Solid(u.team == 0 ? TeamBlue : TeamRed);
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            // 圈的前方加一個箭頭，標出面向（視野朝這個方向展開）
            if (facingArrow == null)
            {
                var mb = new MeshBuilder();
                mb.Tri(new Vector3(-0.11f, 0, 0.40f), new Vector3(0.11f, 0, 0.40f), new Vector3(0, 0, 0.60f), Color.white, Vector3.up);
                facingArrow = mb.ToMesh("FacingArrow");
            }
            var arrow = MakeMesh("Facing", root, facingArrow, new Vector3(0, 0.045f, 0), Vector3.one, Vector3.zero);
            arrow.sharedMaterial = ring.sharedMaterial;
            arrow.shadowCastingMode = ShadowCastingMode.Off;
            arrow.receiveShadows = false;

            // 腳下的圓影：關掉即時陰影（省電畫質）時用它代替
            if (blobMesh == null) { blobMesh = RingMesh(0.34f, 0.34f); blobMat = Mats.Glow(new Color(0, 0, 0, 0.30f)); }
            var blobR = MakeMesh("Blob", root, blobMesh, new Vector3(0, 0.03f, 0), Vector3.one, Vector3.zero);
            blobR.sharedMaterial = blobMat;
            blobR.shadowCastingMode = ShadowCastingMode.Off;
            blobR.receiveShadows = false;
            blob = blobR.gameObject;
            blob.SetActive(BlobShadows);

            // 外部模型：找得到就用，找不到退回暫定模型
            if (!string.IsNullOrEmpty(look.model))
            {
                var prefab = Resources.Load<GameObject>("Units/" + look.model);
                if (prefab != null)
                {
                    var inst = Object.Instantiate(prefab, root, false);
                    model = inst.transform;
                    animator = inst.GetComponentInChildren<Animator>();
                    return;
                }
            }

            skin = Mats.FromHex(look.skin);
            hair = Mats.FromHex(look.hairColor);
            cloth = Mats.FromHex(look.cloth);
            trim = look.trim != 0 ? Mats.FromHex(look.trim) : Color.Lerp(cloth, Color.black, 0.5f);
            // 襟口、髮帶的顏色：淺色衣服用滾邊色，深色衣服用淺色
            accent = cloth.grayscale > 0.7f ? trim : Color.Lerp(cloth, Color.white, 0.6f);
            GearColors(u); // 夜行衣等裝備會改掉衣服的顏色
            g = Mathf.Clamp(look.girth, 0.7f, 1.5f);
            sw = Mathf.Clamp(look.shoulder, 0.7f, 1.5f) * (look.gender == Gender.Female ? 0.92f : 1f);

            model = Node("Model", root, Vector3.zero);
            model.localScale = Vector3.one * (BaseScale * Mathf.Clamp(look.height, 0.7f, 1.3f));
            BuildBody(look);
            BuildHead(look);
            BuildOutfit(look);
            BuildWaist(look);
            BuildBack(look);
            // 有裝備資料的人：手上拿的、身上穿的都照裝備各自的模型；沒有的（路人、展示用人偶）照外觀資料
            if (u.Weapon != null) BuildGearWeapon(u.Weapon); else BuildWeapon(look.weapon);
            BuildGear(u);
            MergeParts();
            PoseIdle();
        }

        static readonly Vector3 Up = new Vector3(0, -0.62f, 0); // 上身零件以腳底為原點建立，掛到腰部節點時往下補回

        // ───────── 體型、下身、軀幹、手臂 ─────────

        void BuildBody(Look look)
        {
            bool female = look.gender == Gender.Female;
            bool armor = look.outfit == Outfit.Armor;
            bool longRobe = look.outfit == Outfit.Robe || look.outfit == Outfit.MonkRobe;
            float waist = Mathf.Clamp(look.waist, 0.75f, 1.35f), chest = Mathf.Clamp(look.chest, 0.75f, 1.35f);
            float hipW = Mathf.Clamp(look.hips, 0.75f, 1.35f) * (female ? 1.04f : 1f);
            dL = (Mathf.Clamp(look.legs, 0.85f, 1.18f) - 1) * 0.45f; // 腿長的增減量；腰以上整體跟著升降
            var wide = new Vector3(g, 1, g);

            // 軀幹輪廓：男性肩寬身直，女性肩窄腰細、較圓潤
            float waistR = (female ? 0.148f : 0.168f) * waist;
            float chestR = (female ? 0.200f : 0.215f) * chest;
            float shoulderR = female ? 0.176f : 0.205f, neckR = female ? 0.084f : 0.10f;
            var torso = Own(Shapes.Lathe("Torso",
                new[] { waistR, Mathf.Lerp(waistR, chestR, female ? 0.35f : 0.55f), chestR, shoulderR, neckR },
                new[] { 0.60f, 0.72f, 0.86f, 0.95f, 1.03f }, 10, false, true, female ? 0.83f : 0.76f));

            // 下襬：上緣接腰，往下依臀寬散開。長袍及踝，短衣與裙甲只到大腿
            float top = 0.63f + dL;
            Mesh lower = longRobe
                ? Own(Shapes.Lathe("Skirt", new[] { 0.295f * hipW, 0.262f * hipW, 0.208f * hipW, waistR + 0.004f },
                                   new[] { 0.13f, 0.30f + dL * 0.3f, 0.48f + dL * 0.7f, top }, 10, true, false, 0.86f))
                : Own(Shapes.Lathe("Hem", new[] { 0.262f * hipW, 0.222f * hipW, waistR + 0.004f },
                                   new[] { 0.39f + dL, 0.51f + dL, top }, 10, true, false, 0.86f));

            hips = Node("Hips", model, Vector3.zero);
            Part(lower, hips, Vector3.zero, armor ? wide * 1.05f : wide, armor && !longRobe ? ArmorCol : cloth, true);
            Part(bandMesh, hips, new Vector3(0, 0.615f + dL, 0), new Vector3((waistR + 0.012f) * g, 0.07f, (waistR + 0.012f) * 0.86f * g), armor ? GoldCol : trim, false);

            hipL = Node("HipL", hips, new Vector3(-0.085f * g * hipW, 0.50f + dL, 0));
            hipR = Node("HipR", hips, new Vector3(0.085f * g * hipW, 0.50f + dL, 0));
            float legScale = (0.45f + dL) / 0.45f;
            foreach (var hip in new[] { hipL, hipR })
            {
                Part(legMesh, hip, Vector3.zero, new Vector3(1, legScale, 1), trim, !longRobe);
                Part(cubeMesh, hip, new Vector3(0, -0.465f - dL, 0.035f), new Vector3(0.12f, 0.075f, 0.21f), Boot, false);
                // 短打與鎧甲露出小腿：綁腿
                if (!longRobe) Part(bandMesh, hip, new Vector3(0, -0.33f - dL * 0.7f, 0), new Vector3(0.068f, 0.17f, 0.068f), armor ? ArmorCol : accent, false);
            }

            // 上半身（以腰為軸，可前傾後仰）
            upper = Node("Upper", hips, new Vector3(0, 0.62f + dL, 0));
            float chestScale = g * (sw > 1 ? 1 + (sw - 1) * 0.25f : 1f);
            if (armor)
            {
                // 胸甲：罩在軀幹外，胸口一面護心鏡
                Part(torso, upper, Up, new Vector3(chestScale * 1.07f, 1, g * 1.10f), ArmorCol, true);
                Part(sphereMesh, upper, Up + new Vector3(0, 0.84f, chestR * 0.80f * g), new Vector3(0.13f, 0.13f, 0.04f), GoldCol, false);
            }
            else Part(torso, upper, Up, new Vector3(chestScale, 1, g), cloth, true);
            chestZ = chestR * (female ? 0.83f : 0.76f) * g; // 胸前表面的位置，襟口與配件貼在這裡
            waistZ = (waistR + 0.012f) * 0.86f * g;

            float sx = (shoulderR + 0.010f) * sw * g;
            shoulderL = Node("ShoulderL", upper, Up + new Vector3(-sx, 0.935f, 0));
            shoulderR_ = Node("ShoulderR", upper, Up + new Vector3(sx, 0.935f, 0));
            shoulderX = sx;
            float arm = (1 + (sw - 1) * 0.6f) * (female ? 0.90f : 1f);
            var thick = new Vector3(arm, 1, arm);
            foreach (var sh in new[] { shoulderL, shoulderR_ })
            {
                switch (look.sleeve)
                {
                    case Sleeve.Wide:
                        Part(sleeveMesh, sh, Vector3.zero, thick, cloth, false);
                        break;
                    case Sleeve.Narrow:
                        Part(narrowSleeveMesh, sh, Vector3.zero, thick, cloth, false);
                        Part(bandMesh, sh, new Vector3(0, -0.35f, 0), new Vector3(0.056f * arm, 0.08f, 0.056f * arm), trim, false); // 束袖
                        break;
                    case Sleeve.Bare:
                        Part(narrowSleeveMesh, sh, Vector3.zero, thick * 1.08f, skin, false);
                        Part(sphereMesh, sh, new Vector3(0, -0.01f, 0), Vector3.one * 0.15f * arm, armor ? ArmorCol : cloth, false); // 肩頭的衣緣
                        break;
                }
                Part(sphereMesh, sh, new Vector3(0, -0.435f, 0), Vector3.one * 0.105f * arm, skin, false);
            }

            // 掛點：配件一律掛在這幾個位置，體型怎麼調都不必改配件
            socketHead = Node("Socket_Head", upper, Up + new Vector3(0, 1.14f, 0.005f));
            socketHead.localScale = Vector3.one * Mathf.Clamp(look.headSize, 0.8f, 1.25f);
            socketBack = Node("Socket_Back", upper, Up + new Vector3(0, 0.84f, -chestZ - 0.01f));
            socketHandL = Node("Socket_HandL", shoulderL, new Vector3(0, -0.44f, 0));
            socketHandR = Node("Socket_HandR", shoulderR_, new Vector3(0, -0.44f, 0));
        }

        // ───────── 頭：臉、髮型、頭飾 ─────────
        // 以頭的中心為原點，頭的半徑約 0.13

        void BuildHead(Look look)
        {
            var h = socketHead;
            // 臉型：寬與長各自縮放
            float fw = Mathf.Clamp(look.faceWidth, 0.8f, 1.25f), fl = Mathf.Clamp(look.faceLength, 0.8f, 1.25f);
            Part(sphereMesh, h, Vector3.zero, new Vector3(0.265f * fw, 0.265f * fl, 0.265f), skin, true);
            const float Shift = 0.022f; // 位移滑桿 ±1 對應的距離
            Color dark = new Color(0.10f, 0.09f, 0.09f);

            // 眼睛：款式、大小、眼距、高度
            float es = Mathf.Clamp(look.eyeSize, 0.6f, 1.4f);
            float ex = (0.048f + Mathf.Clamp(look.eyeSpacing, -1, 1) * 0.014f) * fw;
            float ey = 0.010f + Mathf.Clamp(look.eyeHeight, -1, 1) * Shift;
            Vector3 eyeScale = new Vector3(0.034f, 0.040f, 0.022f);
            float eyeTilt = 0;
            switch (look.eyes)
            {
                case EyeStyle.Phoenix: eyeScale = new Vector3(0.050f, 0.021f, 0.020f); eyeTilt = 14; break; // 鳳眼：細長、眼尾上挑
                case EyeStyle.Narrow: eyeScale = new Vector3(0.046f, 0.012f, 0.020f); break;
                case EyeStyle.Big: eyeScale = new Vector3(0.043f, 0.054f, 0.022f); break;
            }

            // 眉毛：款式決定角度，另可調大小與高度
            float bs = Mathf.Clamp(look.browSize, 0.6f, 1.6f);
            float by = ey + 0.036f + Mathf.Clamp(look.browHeight, -1, 1) * Shift * 0.7f + eyeScale.y * es * 0.25f;
            by = Mathf.Min(by, 0.062f); // 不要高到被髮際蓋住
            float browAngle = look.brow == Brow.Fierce ? 16 : look.brow == Brow.Stern ? 6 : -6;
            float browThick = (look.brow == Brow.Fierce ? 0.014f : 0.010f) * bs;

            for (int e = -1; e <= 1; e += 2)
            {
                if (look.face == FaceItem.Eyepatch && e == 1) continue; // 這隻眼睛被眼罩蓋住
                Part(sphereMesh, h, new Vector3(e * ex, ey, 0.117f), eyeScale * es, dark, false, new Vector3(0, 0, e * eyeTilt));
                Part(cubeMesh, h, new Vector3(e * (ex + 0.002f), by, 0.115f), new Vector3(0.050f * bs, browThick, 0.012f), hair, false, new Vector3(0, 0, e * browAngle));
            }

            // 鼻與嘴（蒙面時看不到）
            if (look.face != FaceItem.Mask)
            {
                float ns = Mathf.Clamp(look.noseSize, 0.6f, 1.6f);
                float ny = -0.018f + Mathf.Clamp(look.noseHeight, -1, 1) * Shift * 0.7f;
                Color noseCol = Color.Lerp(skin, new Color(0.45f, 0.28f, 0.20f), 0.22f);
                switch (look.nose)
                {
                    case NoseStyle.Small: Part(sphereMesh, h, new Vector3(0, ny, 0.128f), new Vector3(0.024f, 0.028f, 0.030f) * ns, noseCol, false); break;
                    case NoseStyle.High: Part(sphereMesh, h, new Vector3(0, ny + 0.008f, 0.128f), new Vector3(0.022f, 0.056f, 0.046f) * ns, noseCol, false); break;
                    case NoseStyle.Wide: Part(sphereMesh, h, new Vector3(0, ny, 0.127f), new Vector3(0.046f, 0.028f, 0.030f) * ns, noseCol, false); break;
                }

                float ms = Mathf.Clamp(look.mouthSize, 0.6f, 1.6f);
                float my = -0.068f + Mathf.Clamp(look.mouthHeight, -1, 1) * Shift * 0.7f;
                Color lip = new Color(0.56f, 0.26f, 0.23f);
                switch (look.mouth)
                {
                    case MouthStyle.Flat:
                        Part(cubeMesh, h, new Vector3(0, my, 0.121f), new Vector3(0.046f * ms, 0.009f, 0.010f), lip, false);
                        break;
                    case MouthStyle.Small:
                        Part(cubeMesh, h, new Vector3(0, my, 0.122f), new Vector3(0.024f * ms, 0.013f, 0.010f), lip, false);
                        break;
                    default:
                        // 微笑：兩側嘴角上揚；下撇：兩側嘴角下垂
                        float bend = look.mouth == MouthStyle.Smile ? 17 : -17;
                        for (int e = -1; e <= 1; e += 2)
                            Part(cubeMesh, h, new Vector3(e * 0.0115f * ms, my, 0.121f), new Vector3(0.027f * ms, 0.009f, 0.010f), lip, false, new Vector3(0, 0, e * bend));
                        break;
                }
            }

            // 鬍鬚
            if (look.beard != Beard.None && look.face != FaceItem.Mask)
            {
                if (look.beard == Beard.Moustache || look.beard == Beard.LongBeard)
                    for (int e = -1; e <= 1; e += 2)
                        Part(cubeMesh, h, new Vector3(e * 0.030f, -0.036f, 0.124f), new Vector3(0.052f, 0.013f, 0.013f), hair, false, new Vector3(0, 0, e * -20));
                if (look.beard == Beard.Goatee)
                    Part(sphereMesh, h, new Vector3(0, -0.118f, 0.092f), new Vector3(0.05f, 0.09f, 0.04f), hair, false);
                if (look.beard == Beard.LongBeard)
                    Part(sphereMesh, h, new Vector3(0, -0.175f, 0.085f), new Vector3(0.12f, 0.27f, 0.07f), hair, false);
            }

            // 臉部配件
            if (look.face == FaceItem.Mask)
                Part(sphereMesh, h, new Vector3(0, -0.050f, 0.030f), new Vector3(0.275f, 0.14f, 0.24f), trim, false); // 蒙面巾遮住口鼻
            else if (look.face == FaceItem.Eyepatch)
            {
                Part(sphereMesh, h, new Vector3(0.048f, 0.012f, 0.120f), new Vector3(0.060f, 0.060f, 0.022f), Boot, false);
                Part(bandMesh, h, new Vector3(0, 0.030f, 0), new Vector3(0.136f, 0.014f, 0.138f), Boot, false, new Vector3(0, 0, 18));
            }

            // 髮型。帽子會蓋住頭頂時，髮髻收起；完全包住頭的帽子連髮際也不畫
            var hw = look.headwear;
            bool wrapped = hw == Headwear.Headwrap || hw == Headwear.Helmet || hw == Headwear.Hood;
            bool covered = wrapped || hw == Headwear.ConeHat || hw == Headwear.VeilHat || hw == Headwear.ScholarCap;
            float knotTop = 0.13f; // 頭頂最高點，給玉冠定位用
            if (look.hair != HairStyle.Bald)
            {
                if (!wrapped) Part(sphereMesh, h, new Vector3(0, 0.088f, -0.040f), new Vector3(0.285f, 0.19f, 0.29f), hair, false);
                switch (look.hair)
                {
                    case HairStyle.Topknot:
                        if (!covered) { Part(sphereMesh, h, new Vector3(0, 0.180f, -0.035f), Vector3.one * 0.105f, hair, false); knotTop = 0.235f; }
                        break;
                    case HairStyle.HighKnot:
                        if (!covered) { Part(sphereMesh, h, new Vector3(0, 0.195f, -0.035f), new Vector3(0.11f, 0.16f, 0.11f), hair, false); knotTop = 0.275f; }
                        break;
                    case HairStyle.TwinBuns:
                        if (!covered)
                            for (int e = -1; e <= 1; e += 2)
                            {
                                Part(sphereMesh, h, new Vector3(e * 0.095f, 0.165f, -0.02f), Vector3.one * 0.11f, hair, false);
                                Part(bandMesh, h, new Vector3(e * 0.095f, 0.125f, -0.02f), new Vector3(0.045f, 0.02f, 0.045f), accent, false);
                            }
                        break;
                    case HairStyle.LongLoose:
                        // 披在背後的長髮與兩鬢
                        Part(sphereMesh, h, new Vector3(0, -0.15f, -0.125f), new Vector3(0.25f, 0.52f, 0.09f), hair, false, new Vector3(-6, 0, 0));
                        for (int e = -1; e <= 1; e += 2)
                            Part(sphereMesh, h, new Vector3(e * 0.125f, -0.09f, -0.01f), new Vector3(0.05f, 0.26f, 0.08f), hair, false);
                        break;
                    case HairStyle.Ponytail:
                        Part(sphereMesh, h, new Vector3(0, -0.15f, -0.135f), new Vector3(0.07f, 0.30f, 0.07f), hair, false, new Vector3(-12, 0, 0));
                        if (!covered) { Part(sphereMesh, h, new Vector3(0, 0.150f, -0.075f), Vector3.one * 0.08f, hair, false); knotTop = 0.19f; }
                        break;
                }
            }

            // 頭飾
            Color hatCol = look.hat != 0 ? Mats.FromHex(look.hat) : Straw;
            switch (hw)
            {
                case Headwear.ConeHat:
                    Part(coneHatMesh, h, new Vector3(0, 0.075f, 0), Vector3.one, hatCol, true);
                    break;
                case Headwear.VeilHat:
                    Part(flatHatMesh, h, new Vector3(0, 0.085f, 0), Vector3.one, hatCol, true);
                    Prop(h, veilMesh).transform.localPosition = new Vector3(0, 0.090f, 0);
                    break;
                case Headwear.ScholarCap:
                    Part(sphereMesh, h, new Vector3(0, 0.085f, -0.02f), new Vector3(0.292f, 0.20f, 0.297f), Boot, false);
                    Part(cubeMesh, h, new Vector3(0, 0.195f, -0.02f), new Vector3(0.20f, 0.075f, 0.17f), Boot, false);
                    for (int e = -1; e <= 1; e += 2)
                        Part(cubeMesh, h, new Vector3(e * 0.04f, -0.10f, -0.168f), new Vector3(0.032f, 0.30f, 0.010f), Boot, false, new Vector3(12, 0, e * 6));
                    break;
                case Headwear.Headwrap:
                    Part(sphereMesh, h, new Vector3(0, 0.075f, -0.025f), new Vector3(0.29f, 0.20f, 0.295f), trim, false);
                    Part(bandMesh, h, new Vector3(0, 0.065f, -0.005f), new Vector3(0.141f, 0.05f, 0.144f), Color.Lerp(trim, Color.black, 0.3f), false);
                    Part(sphereMesh, h, new Vector3(0.05f, 0.03f, -0.150f), Vector3.one * 0.07f, trim, false);
                    break;
                case Headwear.Helmet:
                    Part(sphereMesh, h, new Vector3(0, 0.068f, -0.008f), new Vector3(0.30f, 0.245f, 0.305f), ArmorCol, true);
                    Part(bandMesh, h, new Vector3(0, 0.040f, -0.004f), new Vector3(0.150f, 0.034f, 0.153f), GoldCol, false);
                    Part(cubeMesh, h, new Vector3(0, -0.055f, -0.135f), new Vector3(0.25f, 0.15f, 0.03f), ArmorCol, false, new Vector3(-10, 0, 0)); // 護頸
                    Part(sphereMesh, h, new Vector3(0, 0.215f, -0.01f), new Vector3(0.05f, 0.07f, 0.05f), GoldCol, false);
                    Part(sphereMesh, h, new Vector3(0, 0.290f, -0.03f), new Vector3(0.075f, 0.15f, 0.075f), new Color(0.75f, 0.16f, 0.13f), false, new Vector3(-14, 0, 0)); // 紅纓
                    break;
                case Headwear.Hood:
                    Part(sphereMesh, h, new Vector3(0, 0.030f, -0.070f), new Vector3(0.335f, 0.345f, 0.33f), cloth, true);
                    Part(sphereMesh, h, new Vector3(0, 0.135f, -0.150f), new Vector3(0.13f, 0.13f, 0.16f), cloth, false); // 帽尖
                    break;
                case Headwear.Crown:
                    Part(bandMesh, h, new Vector3(0, knotTop + 0.015f, -0.035f), new Vector3(0.052f, 0.07f, 0.052f), new Color(0.80f, 0.90f, 0.84f), false);
                    Part(cubeMesh, h, new Vector3(0, knotTop + 0.005f, -0.035f), new Vector3(0.24f, 0.012f, 0.012f), GoldCol, false); // 髮簪
                    break;
                case Headwear.Headband:
                    Part(bandMesh, h, new Vector3(0, 0.068f, -0.005f), new Vector3(0.140f, 0.038f, 0.143f), accent, false);
                    break;
                case Headwear.GoldCirclet:
                    Part(bandMesh, h, new Vector3(0, 0.068f, -0.005f), new Vector3(0.140f, 0.045f, 0.143f), GoldCol, false);
                    Part(sphereMesh, h, new Vector3(0, 0.072f, 0.138f), new Vector3(0.035f, 0.045f, 0.02f), new Color(0.75f, 0.16f, 0.13f), false); // 額前紅寶
                    break;
            }

            if (look.ribbons)
            {
                // 髮帶：兩條垂在腦後的飄帶
                for (int e = -1; e <= 1; e += 2)
                    Part(cubeMesh, h, new Vector3(e * 0.035f, -0.17f, -0.160f), new Vector3(0.028f, 0.36f, 0.010f), accent, false, new Vector3(15, 0, e * 7));
            }
        }

        // ───────── 服裝細節與肩部 ─────────

        void BuildOutfit(Look look)
        {
            switch (look.outfit)
            {
                case Outfit.Robe:
                    // 交領與蔽膝
                    Part(cubeMesh, upper, Up + new Vector3(0.012f, 0.86f, chestZ - 0.012f), new Vector3(0.042f, 0.27f, 0.026f), accent, false, new Vector3(0, 0, 24));
                    Part(cubeMesh, hips, new Vector3(0, 0.395f + dL * 0.5f, waistZ + 0.058f), new Vector3(0.115f, 0.37f + dL, 0.016f), trim, false, new Vector3(-9, 0, 0));
                    break;

                case Outfit.ShortJacket:
                    // 交領、衣襟下緣的滾邊
                    Part(cubeMesh, upper, Up + new Vector3(0.012f, 0.86f, chestZ - 0.012f), new Vector3(0.042f, 0.27f, 0.026f), accent, false, new Vector3(0, 0, 24));
                    Part(bandMesh, hips, new Vector3(0, 0.405f + dL, 0), new Vector3(0.262f * g * look.hips, 0.035f, 0.226f * g * look.hips), trim, false);
                    break;

                case Outfit.Armor:
                    // 裙甲的金邊與腹部甲片
                    Part(bandMesh, hips, new Vector3(0, 0.405f + dL, 0), new Vector3(0.277f * g * look.hips, 0.035f, 0.239f * g * look.hips), GoldCol, false);
                    Part(cubeMesh, hips, new Vector3(0, 0.50f + dL, waistZ + 0.055f), new Vector3(0.15f, 0.20f, 0.02f), cloth, false, new Vector3(-9, 0, 0));
                    break;

                case Outfit.MonkRobe:
                    // 袈裟：斜斜圍過胸口的一圈；念珠掛在頸上
                    Part(bandMesh, upper, Up + new Vector3(0, 0.85f, 0), new Vector3(chestZ * 1.36f, 0.11f, chestZ * 1.10f), trim, false, new Vector3(0, 0, 30));
                    Part(bandMesh, upper, Up + new Vector3(0, 0.985f, 0.02f), new Vector3(0.135f, 0.035f, 0.125f), new Color(0.25f, 0.16f, 0.11f), false, new Vector3(18, 0, 0));
                    break;
            }
            // 腰帶結
            Part(cubeMesh, hips, new Vector3(0.07f, 0.585f + dL, waistZ + 0.004f), new Vector3(0.05f, 0.09f, 0.03f), look.outfit == Outfit.Armor ? GoldCol : trim, false, new Vector3(0, 0, -20));

            bool fur = look.shoulders == Shoulder.FurCollar || look.shoulders == Shoulder.FurAndPads;
            bool pads = look.shoulders == Shoulder.Pads || look.shoulders == Shoulder.FurAndPads || look.outfit == Outfit.Armor;
            if (fur) Part(bandMesh, upper, Up + new Vector3(0, 1.00f, 0), new Vector3(0.175f * g, 0.08f, 0.150f * g), new Color(0.30f, 0.22f, 0.17f), false);
            if (pads)
            {
                float sx = shoulderX + 0.012f;
                Color pc = look.outfit == Outfit.Armor ? ArmorCol : trim;
                for (int e = -1; e <= 1; e += 2)
                {
                    Part(sphereMesh, upper, Up + new Vector3(e * sx, 0.955f, 0), new Vector3(0.21f, 0.13f, 0.23f), pc, false);
                    if (look.outfit == Outfit.Armor) Part(bandMesh, upper, Up + new Vector3(e * sx, 0.915f, 0), new Vector3(0.108f, 0.02f, 0.118f), GoldCol, false);
                }
            }
        }

        // ───────── 腰間掛件 ─────────

        void BuildWaist(Look look)
        {
            if (look.pendant)
            {
                // 玉佩：左腰前，絲繩、玉環、紅穗
                var mb = new MeshBuilder();
                mb.Box(new Vector3(0, 0.07f, 0), new Vector3(0.010f, 0.14f, 0.010f), Red);
                mb.Blob(Vector3.zero, new Vector3(0.034f, 0.034f, 0.012f), new Color32(120, 196, 160, 255), 3, 0.03f, 4, 8, 0.05f);
                mb.Box(new Vector3(0, -0.075f, 0), new Vector3(0.016f, 0.09f, 0.016f), Red);
                Prop(hips, mb.ToMesh("Pendant")).transform.localPosition = new Vector3(-0.105f * g, 0.43f + dL, waistZ + 0.055f);
            }
            if (look.gourd)
            {
                // 酒葫蘆：右腰側
                var mb = new MeshBuilder();
                mb.Blob(Vector3.zero, new Vector3(0.062f, 0.062f, 0.062f), Tan, 5, 0.04f, 4, 7, 0.06f);
                mb.Blob(new Vector3(0, 0.082f, 0), new Vector3(0.040f, 0.040f, 0.040f), Tan, 6, 0.04f, 3, 7, 0.06f);
                mb.Box(new Vector3(0, 0.130f, 0), new Vector3(0.018f, 0.03f, 0.018f), Red);
                Prop(hips, mb.ToMesh("Gourd")).transform.localPosition = new Vector3(waistZ + 0.07f, 0.47f + dL, 0.07f);
            }
            if (look.pouch)
            {
                // 暗器囊：右後腰的小皮囊
                var mb = new MeshBuilder();
                mb.Box(Vector3.zero, new Vector3(0.10f, 0.09f, 0.055f), new Color32(72, 50, 36, 255));
                mb.Box(new Vector3(0, 0.03f, 0.03f), new Vector3(0.105f, 0.035f, 0.012f), new Color32(50, 34, 24, 255));
                mb.Box(new Vector3(0, 0.005f, 0.036f), new Vector3(0.018f, 0.018f, 0.008f), Gold);
                Prop(hips, mb.ToMesh("Pouch")).transform.localPosition = new Vector3(0.11f * g, 0.545f + dL, -waistZ - 0.02f);
            }
        }

        // ───────── 背部 ─────────

        void BuildBack(Look look)
        {
            var item = look.back;
            if (item == BackItem.Auto)
                item = look.weapon == WeaponKind.Sword ? BackItem.Sheath : look.weapon == WeaponKind.Bow ? BackItem.Quiver : BackItem.None;

            switch (item)
            {
                case BackItem.Sheath:
                    Prop(socketBack, SheathMesh()).transform.localRotation = Quaternion.Euler(0, 0, 32);
                    break;

                case BackItem.TwinSheaths:
                    Prop(socketBack, SheathMesh()).transform.localRotation = Quaternion.Euler(0, 0, 32);
                    var second = Prop(socketBack, SheathMesh()).transform;
                    second.localRotation = Quaternion.Euler(0, 0, -32);
                    second.localPosition = new Vector3(0, 0, -0.045f);
                    break;

                case BackItem.Quiver:
                {
                    var mb = new MeshBuilder();
                    mb.Lathe(new Vector3(0, -0.20f, 0), new[] { 0.045f, 0.055f }, new[] { 0f, 0.40f }, 6, Grip, false);
                    for (int i = -1; i <= 1; i++) mb.Box(new Vector3(i * 0.02f, 0.27f, 0), new Vector3(0.012f, 0.16f, 0.012f), Paper);
                    Prop(socketBack, mb.ToMesh("Quiver")).transform.localRotation = Quaternion.Euler(0, 0, -24);
                    break;
                }

                case BackItem.Cape:
                {
                    // 披風：從肩頭垂到小腿，往下漸寬並略往後飄
                    var mb = new MeshBuilder();
                    Color32 c = trim, lining = Color.Lerp(trim, cloth, 0.5f);
                    const int rows = 4;
                    for (int r = 0; r < rows; r++)
                    {
                        float t0 = r / (float)rows, t1 = (r + 1) / (float)rows;
                        float w0 = Mathf.Lerp(0.23f, 0.33f, t0) * g, w1 = Mathf.Lerp(0.23f, 0.33f, t1) * g;
                        float y0 = Mathf.Lerp(0.12f, -0.76f, t0), y1 = Mathf.Lerp(0.12f, -0.76f, t1);
                        float z0 = -0.015f - 0.15f * t0 * t0, z1 = -0.015f - 0.15f * t1 * t1;
                        Vector3 a = new Vector3(-w0, y0, z0), b = new Vector3(w0, y0, z0), cc = new Vector3(w1, y1, z1), d = new Vector3(-w1, y1, z1);
                        mb.Quad(a, b, cc, d, c, Vector3.back);
                        mb.Quad(a, b, cc, d, lining, Vector3.forward);
                    }
                    Prop(socketBack, mb.ToMesh("Cape"));
                    // 領口的扣環
                    Part(sphereMesh, upper, Up + new Vector3(0, 0.99f, chestZ * 0.62f), new Vector3(0.05f, 0.05f, 0.03f), GoldCol, false);
                    break;
                }

                case BackItem.HatOnBack:
                    // 背在身後的斗笠
                    Part(coneHatMesh, socketBack, new Vector3(0, -0.02f, -0.07f), Vector3.one * 0.92f, look.hat != 0 ? Mats.FromHex(look.hat) : Straw, false, new Vector3(-78, 0, 0));
                    break;

                case BackItem.Pack:
                {
                    // 書箱：木箱、頂上捲著畫軸、兩條背帶
                    var mb = new MeshBuilder();
                    mb.Box(new Vector3(0, -0.06f, -0.09f), new Vector3(0.27f, 0.36f, 0.15f), new Color32(140, 100, 62, 255));
                    mb.Box(new Vector3(0, -0.06f, -0.168f), new Vector3(0.20f, 0.28f, 0.006f), new Color32(112, 78, 46, 255));
                    mb.Box(new Vector3(0, 0.155f, -0.09f), new Vector3(0.32f, 0.06f, 0.06f), Paper);
                    mb.Box(new Vector3(0, 0.155f, -0.09f), new Vector3(0.05f, 0.064f, 0.064f), Red);
                    for (int e = -1; e <= 1; e += 2) mb.Box(new Vector3(e * 0.10f, 0.0f, 0.0f), new Vector3(0.03f, 0.30f, 0.012f), Grip);
                    Prop(socketBack, mb.ToMesh("Pack"));
                    break;
                }

                case BackItem.Gourd:
                {
                    // 背上的大葫蘆，紅繩繫著
                    var mb = new MeshBuilder();
                    mb.Blob(new Vector3(0, -0.14f, -0.11f), new Vector3(0.125f, 0.135f, 0.125f), Tan, 8, 0.04f, 4, 8, 0.06f);
                    mb.Blob(new Vector3(0, 0.03f, -0.11f), new Vector3(0.082f, 0.085f, 0.082f), Tan, 9, 0.04f, 4, 8, 0.06f);
                    mb.Box(new Vector3(0, -0.045f, -0.11f), new Vector3(0.11f, 0.02f, 0.11f), Red);
                    mb.Box(new Vector3(0, 0.125f, -0.11f), new Vector3(0.03f, 0.05f, 0.03f), Grip);
                    Prop(socketBack, mb.ToMesh("BigGourd"));
                    break;
                }
            }
        }

        static Mesh SheathMesh()
        {
            var mb = new MeshBuilder();
            mb.Box(Vector3.zero, new Vector3(0.05f, 0.62f, 0.04f), new Color32(40, 44, 58, 255));
            mb.Box(new Vector3(0, -0.30f, 0), new Vector3(0.056f, 0.06f, 0.046f), Gold);
            mb.Box(new Vector3(0, 0.30f, 0), new Vector3(0.056f, 0.04f, 0.046f), Gold);
            return mb.ToMesh("Sheath");
        }

        // ───────── 兵器 ─────────
        // 手的掛點：手臂自然下垂時 +Z 朝前、+Y 朝上

        void BuildWeapon(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Sword:
                    Prop(socketHandR, BladeMesh(0.70f, 0.034f, 0.012f, true));
                    break;
                case WeaponKind.Saber:
                    Prop(socketHandR, BladeMesh(0.58f, 0.014f, 0.075f, false));
                    break;
                case WeaponKind.Cleaver:
                    Prop(socketHandR, BladeMesh(0.66f, 0.018f, 0.105f, false));
                    twoHanded = true;
                    break;
                case WeaponKind.Fists:
                    Prop(socketHandL, BracerMesh());
                    Prop(socketHandR, BracerMesh());
                    break;
                case WeaponKind.Bow:
                    Prop(socketHandL, BowMesh());
                    bowInLeftHand = true;
                    break;
                case WeaponKind.Knives:
                {
                    Prop(socketHandR, BladeMesh(0.17f, 0.022f, 0.008f, false));
                    var belt = new MeshBuilder(); // 腰間一排飛刀
                    for (int i = -1; i <= 1; i++) belt.Box(new Vector3(i * 0.075f, 0.56f + dL, waistZ + 0.014f), new Vector3(0.022f, 0.12f, 0.010f), Steel);
                    Prop(hips, belt.ToMesh("Knives"));
                    break;
                }
                case WeaponKind.TwinBlades:
                    Prop(socketHandL, BladeMesh(0.36f, 0.030f, 0.010f, false));
                    Prop(socketHandR, BladeMesh(0.36f, 0.030f, 0.010f, false));
                    break;

                case WeaponKind.Spear:
                {
                    // 長槍：直立握持，槍尖下繫紅纓
                    var mb = new MeshBuilder();
                    mb.Box(new Vector3(0, 0.30f, 0.02f), new Vector3(0.028f, 1.55f, 0.028f), Grip);
                    mb.Lathe(new Vector3(0, 1.075f, 0.02f), new[] { 0.040f, 0.0f }, new[] { 0f, 0.20f }, 4, Steel, false);
                    mb.Blob(new Vector3(0, 1.03f, 0.02f), new Vector3(0.05f, 0.06f, 0.05f), Red, 4, 0.10f, 3, 6, 0.08f);
                    Prop(socketHandR, mb.ToMesh("Spear"));
                    poleWeapon = true;
                    break;
                }
                case WeaponKind.Staff:
                {
                    // 齊眉棍：兩端包鐵
                    var mb = new MeshBuilder();
                    mb.Box(new Vector3(0, 0.25f, 0.02f), new Vector3(0.034f, 1.45f, 0.034f), new Color32(150, 108, 62, 255));
                    mb.Box(new Vector3(0, 0.93f, 0.02f), new Vector3(0.040f, 0.09f, 0.040f), Steel);
                    mb.Box(new Vector3(0, -0.43f, 0.02f), new Vector3(0.040f, 0.09f, 0.040f), Steel);
                    Prop(socketHandR, mb.ToMesh("Staff"));
                    poleWeapon = true;
                    break;
                }
                case WeaponKind.Fan:
                {
                    // 摺扇：攤開的扇面，扇骨由握把放射
                    var mb = new MeshBuilder();
                    const int ribs = 8;
                    for (int i = 0; i < ribs; i++)
                    {
                        float a0 = Mathf.Deg2Rad * (-52 + i * 104f / ribs), a1 = Mathf.Deg2Rad * (-52 + (i + 1) * 104f / ribs);
                        Vector3 d0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)), d1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1));
                        Vector3 i0 = d0 * 0.09f, i1 = d1 * 0.09f, o0 = d0 * 0.29f, o1 = d1 * 0.29f;
                        var c = (i & 1) == 0 ? Paper : new Color32(228, 222, 204, 255);
                        mb.Quad(i0, o0, o1, i1, c, Vector3.up);
                        mb.Quad(i0, o0, o1, i1, c, Vector3.down);
                        mb.Box((Vector3.zero + o0) * 0.5f, new Vector3(0.008f, 0.006f, 0.29f), Grip, a0 * Mathf.Rad2Deg, 0);
                    }
                    Prop(socketHandR, mb.ToMesh("Fan"));
                    break;
                }
                case WeaponKind.Mace:
                {
                    // 銅錘：短柄、瓜形錘頭
                    var mb = new MeshBuilder();
                    mb.Box(new Vector3(0, 0, 0.16f), new Vector3(0.030f, 0.030f, 0.36f), Grip);
                    mb.Blob(new Vector3(0, 0, 0.42f), new Vector3(0.105f, 0.105f, 0.125f), new Color32(186, 142, 62, 255), 7, 0.03f, 4, 8, 0.08f);
                    mb.Box(new Vector3(0, 0, 0.30f), new Vector3(0.05f, 0.05f, 0.03f), Gold);
                    Prop(socketHandR, mb.ToMesh("Mace"));
                    break;
                }
                case WeaponKind.Flute:
                {
                    // 洞簫：竹管、尾端紅穗
                    var mb = new MeshBuilder();
                    mb.Box(new Vector3(0, 0, 0.20f), new Vector3(0.022f, 0.022f, 0.52f), new Color32(104, 78, 46, 255));
                    for (int i = 0; i < 3; i++) mb.Box(new Vector3(0, 0, 0.06f + i * 0.16f), new Vector3(0.026f, 0.026f, 0.012f), new Color32(70, 52, 30, 255));
                    mb.Box(new Vector3(0, -0.07f, -0.05f), new Vector3(0.014f, 0.14f, 0.014f), Red);
                    Prop(socketHandR, mb.ToMesh("Flute"));
                    break;
                }
            }
        }

        // 刀劍：握柄在原點，刃朝 +Z
        static Mesh BladeMesh(float length, float width, float thick, bool guard)
        {
            var mb = new MeshBuilder();
            mb.Box(new Vector3(0, 0, 0.06f + length * 0.5f), new Vector3(width, thick, length), Steel);
            mb.Box(new Vector3(0, 0, -0.03f), new Vector3(0.032f, 0.032f, 0.16f), Grip);
            if (guard)
            {
                mb.Box(new Vector3(0, 0, 0.055f), new Vector3(0.15f, 0.035f, 0.028f), Gold);
                mb.Box(new Vector3(0, -0.07f, -0.12f), new Vector3(0.016f, 0.16f, 0.016f), Red); // 劍穗
            }
            else mb.Box(new Vector3(0, 0, 0.055f), new Vector3(0.05f, thick + 0.03f, 0.03f), Grip);
            return mb.ToMesh("Blade");
        }

        static Mesh BracerMesh()
        {
            var mb = new MeshBuilder();
            mb.Lathe(Vector3.zero, new[] { 0.085f, 0.095f, 0.085f }, new[] { 0f, 0.07f, 0.14f }, 7, Steel, false);
            return mb.ToMesh("Bracer");
        }

        // 弓：握把在原點，弓身沿 Y，弓弦在後
        static Mesh BowMesh()
        {
            var mb = new MeshBuilder();
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Box(new Vector3(0, s * 0.13f, 0.055f), new Vector3(0.030f, 0.27f, 0.030f), Grip, 0, s * 10);
                mb.Box(new Vector3(0, s * 0.34f, 0.015f), new Vector3(0.026f, 0.20f, 0.026f), Grip, 0, s * 26);
                mb.Box(new Vector3(0, s * 0.47f, -0.050f), new Vector3(0.022f, 0.13f, 0.022f), Grip, 0, s * 46);
            }
            mb.Box(new Vector3(0, 0, -0.075f), new Vector3(0.008f, 1.02f, 0.008f), Paper);
            return mb.ToMesh("Bow");
        }

        // ───────── 建構小工具 ─────────

        static Transform Node(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        static MeshRenderer MakeMesh(string name, Transform parent, Mesh mesh, Vector3 pos, Vector3 scale, Vector3 euler)
        {
            var t = Node(name, parent, pos);
            t.localScale = scale;
            t.localRotation = Quaternion.Euler(euler);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            return t.gameObject.AddComponent<MeshRenderer>();
        }

        /// <summary>身體部位：用角色材質（可閃白）；silhouette 為 true 的大塊部位會多畫一次剪影。</summary>
        void Part(Mesh mesh, Transform parent, Vector3 pos, Vector3 scale, Color color, bool silhouette, Vector3 euler = default(Vector3))
        {
            var r = MakeMesh(mesh.name, parent, mesh, pos, scale, euler);
            var mat = Mats.Unit(color);
            if (silhouette && sil != null) r.sharedMaterials = new[] { mat, sil };
            else r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            body.Add(r);
            bodyColor.Add(color);
        }

        /// <summary>兵器與配件：顏色寫在頂點上，所有角色共用同一個材質。</summary>
        MeshRenderer Prop(Transform socket, Mesh mesh)
        {
            if (mesh != veilMesh) owned.Add(mesh); // 薄紗是共用的，其餘都是這個角色專屬
            var r = MakeMesh(mesh.name, socket, mesh, Vector3.zero, Vector3.one, Vector3.zero);
            r.sharedMaterial = propMat;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            return r;
        }

        // ───────── 動作介面 ─────────
        // 暫定模型：直接設定關節角度。繞 X 軸的負角度是往前抬（手臂、腿自然下垂時指向 -Y）。
        // 外部模型：有 Animator 就設定對應的參數，沒有就維持原樣。

        bool Procedural { get { return hips != null; } }

        void SetArms(float leftX, float rightX, float spread)
        {
            shoulderL.localRotation = Quaternion.Euler(leftX, 0, -spread);
            shoulderR_.localRotation = Quaternion.Euler(rightX, 0, spread);
        }

        public void PoseIdle()
        {
            if (!Procedural)
            {
                if (animator != null) animator.SetBool(WalkId, false);
                return;
            }
            hips.localPosition = Vector3.zero;
            upper.localRotation = Quaternion.identity;
            hipL.localRotation = hipR.localRotation = Quaternion.identity;
            // 持兵器的手略往前；雙手兵器兩手都往前；長兵器直立在身側
            SetArms(bowInLeftHand || twoHanded ? -38 : -4, bowInLeftHand ? -6 : twoHanded ? -44 : poleWeapon ? -9 : -24, 7);
        }

        /// <summary>走路：phase 持續累加，雙腿交替邁步、雙臂反向擺動、身體微微起伏並前傾。</summary>
        public void PoseWalk(float phase)
        {
            if (!Procedural)
            {
                if (animator != null) animator.SetBool(WalkId, true);
                return;
            }
            float s = Mathf.Sin(phase);
            hipL.localRotation = Quaternion.Euler(-s * 34, 0, 0);
            hipR.localRotation = Quaternion.Euler(s * 34, 0, 0);
            hips.localPosition = new Vector3(0, Mathf.Abs(Mathf.Cos(phase)) * 0.035f - 0.02f, 0);
            upper.localRotation = Quaternion.Euler(7, s * 5, 0);
            SetArms(s * 28 - 6, poleWeapon ? -12 : -s * 22 - (twoHanded ? 40 : 20), 8);
        }

        float attackK = 1, hurtK;

        /// <summary>出招：k 由 0 到 1。先高舉蓄力，再揮下，最後收回。遠程則是引弓或甩手。</summary>
        public void PoseAttack(float k, bool melee)
        {
            if (!Procedural)
            {
                // 外部模型：只在動作開始時觸發一次
                if (animator != null && k < attackK) animator.SetTrigger(AttackId);
                attackK = k;
                return;
            }
            float raise = Mathf.Clamp01(k / 0.38f), swing = Mathf.Clamp01((k - 0.38f) / 0.22f), back = Mathf.Clamp01((k - 0.60f) / 0.40f);
            float rest = poleWeapon ? -9 : -24;
            float arm = Mathf.Lerp(rest, poleWeapon ? -70 : -165, raise);
            arm = Mathf.Lerp(arm, poleWeapon ? -108 : 38, swing * swing); // 長兵器是往前刺，其餘是往下劈
            arm = Mathf.Lerp(arm, rest, back);
            float lean = Mathf.Lerp(Mathf.Lerp(-9, 16, swing), 0, back) * (raise > 0 ? 1 : 0);

            if (bowInLeftHand)
            {
                // 弓：左手平舉持弓，右手往後拉弦再放開
                float draw = Mathf.Sin(Mathf.Clamp01(k / 0.6f) * Mathf.PI * 0.5f) * (1 - back);
                shoulderL.localRotation = Quaternion.Euler(-88, 0, -4);
                shoulderR_.localRotation = Quaternion.Euler(Mathf.Lerp(-80, -50, draw), 0, Mathf.Lerp(4, 30, draw));
                upper.localRotation = Quaternion.Euler(0, -18 * (1 - back), 0);
                return;
            }

            upper.localRotation = Quaternion.Euler(lean, melee ? Mathf.Lerp(-16, 20, swing) * (1 - back) : 0, 0);
            SetArms(twoHanded ? arm + 10 : Mathf.Lerp(-4, 22, swing) * (1 - back), arm, 7);
            hipL.localRotation = Quaternion.Euler(Mathf.Lerp(0, 18, swing) * (1 - back), 0, 0);
            hipR.localRotation = Quaternion.Euler(Mathf.Lerp(0, -22, swing) * (1 - back), 0, 0);
        }

        /// <summary>受擊：上身後仰、雙臂張開。k 為 0～1 的強度。</summary>
        public void PoseHurt(float k)
        {
            if (!Procedural)
            {
                if (animator != null && k > 0 && hurtK <= 0) animator.SetTrigger(HurtId);
                hurtK = k;
                return;
            }
            upper.localRotation = Quaternion.Euler(-24 * k, 0, 0);
            SetArms(Mathf.Lerp(-4, 20, k), Mathf.Lerp(poleWeapon ? -9 : -24, 16, k), Mathf.Lerp(7, 34, k));
        }

        /// <summary>受擊閃白：k 為 0～1。只覆寫本體材質（索引 0），不影響剪影。外部模型不處理。</summary>
        public void SetFlash(float k)
        {
            // 合併過的零件：顏色在頂點上，閃白交給著色器的 _Flash
            for (int i = 0; i < merged.Count; i++)
            {
                if (k <= 0) { merged[i].SetPropertyBlock(null, 0); continue; }
                flashBlock.SetFloat(FlashId, k);
                merged[i].SetPropertyBlock(flashBlock, 0);
            }
            for (int i = 0; i < body.Count; i++)
            {
                if (k <= 0) { body[i].SetPropertyBlock(null, 0); continue; }
                mpb.SetColor(ColorId, Color.Lerp(bodyColor[i], Color.white, k));
                body[i].SetPropertyBlock(mpb, 0);
            }
        }

        public void FaceTo(Vector3 worldPoint)
        {
            var d = worldPoint - root.position;
            d.y = 0;
            if (d.sqrMagnitude > 1e-4f) root.rotation = Quaternion.LookRotation(d);
        }
    }
}
