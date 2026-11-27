using System;

namespace Jianghu
{
    /// <summary>開局佈陣：三俠對黑風寨。</summary>
    public static class Scenario
    {
        static readonly int[,] HeroAt = { { 3, 18 }, { 2, 20 }, { 4, 20 } };
        static readonly int[,] BanditAt = { { 17, 13 }, { 18, 16 }, { 20, 15 } };
        static readonly int[,] ArcherAt = { { 19, 5 }, { 20, 3 } };
        static readonly string[] Ord = { "甲", "乙", "丙", "丁" };

        /// <summary>房舍門面通往的場景名稱。</summary>
        public static string PlaceOf(ShopKind shop) { return shop == ShopKind.Inn ? "inn" : shop == ShopKind.Weaver ? "weaver" : ""; }

        /// <summary>建立一場戲。place 是空字串時是山谷（主地圖），否則是室內場景（"inn"、"weaver"）。</summary>
        public static Battle Create(string place)
        {
            return string.IsNullOrEmpty(place) ? Create() : Interior(place);
        }

        // 室內場景：只有三位俠客、店家、出口與幾件擺設，沒有敵人
        static Battle Interior(string place)
        {
            var kind = place == "inn" ? ShopKind.Inn : ShopKind.Weaver;
            var w = MapGen.Interior(kind);
            var b = new Battle(w) { place = place };
            int z = MapGen.FloorH, mx = w.W / 2;
            int[] hx = { mx, mx - 1, mx + 1 };
            for (int i = 0; i < Db.Heroes.Length; i++)
            {
                var u = b.AddUnit(Db.Heroes[i], 0, w.Idx(hx[i], w.D - 2, z));
                u.fx = 0; u.fy = -1; u.homeFx = 0; u.homeFy = -1;
            }
            b.AddProp(PropKind.Door, w.Idx(mx, w.D - 1, z), "門口");
            // 食樓的掌櫃站在大堂（北邊是二樓底下）；二樓放著一只匣子
            b.AddProp(new Prop { kind = PropKind.Building, keeper = true, shop = kind, cell = kind == ShopKind.Inn ? w.Idx(mx - 2, 5, z) : w.Idx(mx, 1, z), name = kind == ShopKind.Inn ? "食樓掌櫃" : "繡坊繡娘",
                                 look = Looks.Hermit.Clone(), fx = 0, fy = 1 });
            if (kind == ShopKind.Inn)
            {
                var jar = b.AddProp(PropKind.Crate, w.Idx(w.W - 2, 1, z), "酒罈木箱");
                jar.items.Add(Item.Salve); jar.silver = 3;
                b.AddProp(PropKind.Bucket, w.Idx(1, 1, z), "水桶");
                var chest = b.AddProp(PropKind.Box, w.Idx(1, 1, MapGen.UpperZ), "客房的匣子");
                chest.items.Add(Item.QiPill); chest.silver = 12;
            }
            else
            {
                var box = b.AddProp(PropKind.Box, w.Idx(1, 1, z), "針線盒");
                box.items.Add(Item.Antidote);
                var stand = b.AddProp(PropKind.ArmorRack, w.Idx(w.W - 2, 1, z), "衣架");
                stand.gears.Add(Gears.Robe); stand.gears.Add(Gears.Headwrap);
            }
            return b;
        }

        /// <summary>
        /// 壓力測試：在一張現成的大地圖上放三位俠客（西南角）與 foes 個敵人（平均散佈，兩個山賊配一個弓手）。
        /// 沒有物件、沒有任務，只用來量效能。
        /// </summary>
        public static Battle Stress(VoxelWorld w, int foes, uint seed = 3)
        {
            var b = new Battle(w);
            for (int i = 0; i < Db.Heroes.Length; i++) b.AddUnit(Db.Heroes[i], 0, MapGen.FindSpawn(w, 3 + i * 2, 3));
            var rnd = new Rng(seed);
            int side = (int)Math.Ceiling(Math.Sqrt(foes)), n = 0;
            for (int gy = 0; gy < side && n < foes; gy++)
                for (int gx = 0; gx < side && n < foes; gx++)
                {
                    // 每個人落在自己那一格區域裡的隨機位置；離出發點太近的往後挪
                    int x = (int)((gx + 0.2f + (float)rnd.Next() * 0.6f) * w.W / side), y = (int)((gy + 0.2f + (float)rnd.Next() * 0.6f) * w.D / side);
                    if (x < 14 && y < 14) { x += 14; y += 14; }
                    int c = MapGen.FindSpawn(w, Math.Min(x, w.W - 1), Math.Min(y, w.D - 1));
                    if (c < 0) continue;
                    var t = n % 3 == 2 ? Db.Archer : Db.Bandit;
                    var u = b.AddUnit(t, 1, c, t.name + (n + 1));
                    u.ai = (AiKind)(n % 3);
                    n++;
                }
            return b;
        }

        public static Battle Create()
        {
            var w = MapGen.Generate();
            var b = new Battle(w);
            for (int i = 0; i < Db.Heroes.Length; i++)
                b.AddUnit(Db.Heroes[i], 0, MapGen.FindSpawn(w, HeroAt[i, 0], HeroAt[i, 1]));
            for (int i = 0; i < BanditAt.GetLength(0); i++)
            {
                var u = b.AddUnit(Db.Bandit, 1, MapGen.FindSpawn(w, BanditAt[i, 0], BanditAt[i, 1]), Db.Bandit.name + Ord[i]);
                // 三個山賊性情各異：甲兇悍；乙魯鈍，懷裡卻揣著一顆霹靂彈；丙機警，帶著石灰
                u.ai = i == 0 ? AiKind.Fierce : i == 1 ? AiKind.Dull : AiKind.Cunning;
                if (i == 1) b.Give(u, Item.Thunder);
                if (i == 2) b.Give(u, Item.Lime);
            }
            for (int i = 0; i < ArcherAt.GetLength(0); i++)
                b.AddUnit(Db.Archer, 1, MapGen.FindSpawn(w, ArcherAt[i, 0], ArcherAt[i, 1]), Db.Archer.name + Ord[i]);
            b.AddUnit(Db.Boss, 1, MapGen.FindSpawn(w, 20, 12));
            PlaceProps(b);
            PlaceMore(b);
            PlaceThird(b);
            return b;
        }

        // 芒草叢：過河之後、進寨之前的兩片藏身處
        static readonly int[,] GrassAt = { { 14, 15 }, { 15, 15 }, { 14, 16 }, { 15, 16 }, { 16, 15 }, { 16, 16 }, { 16, 17 },
                                           { 15, 9 }, { 16, 9 }, { 15, 8 }, { 16, 8 }, { 17, 8 }, { 17, 9 } };
        // 絆鈴索：木橋東口與淺灘上岸處
        static readonly int[,] TrapAt = { { 16, 12 }, { 17, 19 } };

        static void PlaceProps(Battle b)
        {
            var w = b.world;

            // 涼亭裡的寶箱
            int chest = -1;
            for (int z = 1; z < w.H - 1 && chest < 0; z++) if (w.Standable(5, 8, z)) chest = w.Idx(5, 8, z); // 屋頂下的亭內地面
            if (chest >= 0 && w.occ[chest] < 0) b.AddProp(PropKind.Chest, chest, "舊木箱", 12);

            // 路人：河西岸、木橋頭的老樵夫
            int npc = MapGen.FindSpawn(w, 8, 14);
            if (npc >= 0)
            {
                var p = b.AddProp(PropKind.Npc, npc, "老樵夫");
                p.look = Looks.Hermit.Clone();
                p.fx = -1; p.fy = 1;
            }

            for (int i = 0; i < GrassAt.GetLength(0); i++)
            {
                // 只長在草地上；樹下也長（取樹冠底下的地面，而不是樹冠頂）
                int c = Ground(w, GrassAt[i, 0], GrassAt[i, 1]);
                if (c >= 0 && w.v[c - w.WD] == Block.Grass && w.occ[c] < 0 && b.PropAt(c) == null) b.AddProp(PropKind.Grass, c, "芒草叢");
            }

            for (int i = 0; i < TrapAt.GetLength(0); i++)
            {
                int c = Free(w, TrapAt[i, 0], TrapAt[i, 1]);
                if (c >= 0) b.AddProp(PropKind.Trap, c, "絆鈴索", 12);
            }

            // 滾石：山崖南緣，找一處往南有落差的崖邊（推下去會滾向寨子）
            for (int x = 20; x >= 18; x--)
            {
                bool placed = false;
                for (int y = 10; y >= 6 && !placed; y--)
                {
                    int z = w.TopStand(x, y), zs = w.TopStand(x, y + 1), zn = w.TopStand(x, y - 1);
                    if (z < 0 || zs < 0 || zn < 0 || z - zs < 1 || Math.Abs(zn - z) > 1) continue;
                    int c = w.Idx(x, y, z);
                    if (w.occ[c] >= 0 || w.blocked[c] || w.occ[w.Idx(x, y - 1, zn)] >= 0) continue;
                    b.AddProp(PropKind.Boulder, c, "山岩", 10);
                    placed = true;
                }
                if (placed) break;
            }
        }

        // 第二批物件：火油桶、兵器架、藥草、神龕、拒馬、告示牌、藤蔓
        static void PlaceMore(Battle b)
        {
            var w = b.world;

            // 告示牌：我方出發處的山道口
            int c = Free(w, 7, 19);
            if (c >= 0 && b.PropAt(c) == null) b.AddProp(PropKind.Notice, c, "懸賞告示");

            // 神龕：涼亭西側
            c = Free(w, 2, 11);
            if (c >= 0 && b.PropAt(c) == null) b.AddProp(PropKind.Shrine, c, "山神龕");

            // 藥草：西岸三處，採下各得一份成藥
            int[,] herbs = { { 1, 14 }, { 8, 17 }, { 6, 21 }, { 2, 8 } };
            for (int i = 0; i < herbs.GetLength(0); i++)
            {
                c = Ground(w, herbs[i, 0], herbs[i, 1]);
                if (c < 0 || w.v[c - w.WD] != Block.Grass || w.occ[c] >= 0 || w.blocked[c] || b.PropAt(c) != null) continue;
                var h = b.AddProp(PropKind.Herb, c, (i & 1) == 0 ? "止血草" : "解毒藤");
                h.item = (i & 1) == 0 ? Item.Salve : Item.Antidote;
            }

            // 拒馬：把木橋的東口堵死（橋面兩排各一座）
            for (int y = 11; y <= 12; y++)
            {
                c = Free(w, 15, y);
                if (c >= 0 && b.PropAt(c) == null) b.AddProp(PropKind.Barricade, c, "拒馬", 13);
            }

            // 火油桶：寨子裡兩桶
            int[,] barrels = { { 19, 13 }, { 21, 14 } };
            for (int i = 0; i < barrels.GetLength(0); i++)
            {
                c = Free(w, barrels[i, 0], barrels[i, 1]);
                if (c >= 0 && b.PropAt(c) == null) b.AddProp(PropKind.Barrel, c, "火油桶");
            }

            // 兵器架：寨子深處一座（兩件名器）、涼亭裡一座
            c = Free(w, 21, 17);
            if (c >= 0 && b.PropAt(c) == null)
            {
                var r = b.AddProp(PropKind.Rack, c, "寨中兵器架");
                r.gears.Add(Gears.DarkIron); r.gears.Add(Gears.Halberd); r.gears.Add(Gears.WolfClub);
            }
            c = -1;
            for (int z = 1; z < w.H - 1 && c < 0; z++) if (w.Standable(7, 10, z)) c = w.Idx(7, 10, z);
            if (c >= 0 && w.occ[c] < 0 && !w.blocked[c] && b.PropAt(c) == null)
            {
                var r = b.AddProp(PropKind.Rack, c, "亭中兵器架");
                r.gears.Add(Gears.MeteorHammer); r.gears.Add(Gears.IronShield); r.gears.Add(Gears.TideFlute);
            }

            // 藤蔓：山崖下找一處緊鄰高出 3～6 層的崖壁，爬上去就是崖頂
            int[] dx = { 0, 1, 0, -1 }, dy = { -1, 0, 1, 0 };
            for (int y = 10; y >= 2; y--)
                for (int x = 14; x <= 22; x++)
                {
                    int z = w.TopStand(x, y);
                    if (z < 0) continue;
                    int at = w.Idx(x, y, z);
                    if (w.occ[at] >= 0 || w.blocked[at] || b.PropAt(at) != null) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + dx[k], ny = y + dy[k];
                        if (nx < 0 || ny < 0 || nx >= w.W || ny >= w.D) continue;
                        int zn = w.TopStand(nx, ny);
                        if (zn - z < 3 || zn - z > 6) continue;
                        int top = w.Idx(nx, ny, zn);
                        if (w.occ[top] >= 0 || w.blocked[top] || b.PropAt(top) != null) continue;
                        var v = b.AddProp(PropKind.Vine, at, "老藤");
                        v.link = top;
                        return;
                    }
                }
        }

        // 第三批物件：寨子裡的盔甲架、鐵箱、木箱；山腳小村的房舍、水井、水桶、木箱、收納盒
        static void PlaceThird(Battle b)
        {
            var w = b.world;

            int c = Near(b, 22, 13);
            if (c >= 0)
            {
                var r = b.AddProp(PropKind.ArmorRack, c, "寨中盔甲架");
                r.gears.Add(Gears.Leather); r.gears.Add(Gears.Tassets); r.gears.Add(Gears.Headwrap);
            }
            c = Near(b, 22, 18);
            if (c >= 0)
            {
                var r = b.AddProp(PropKind.IronChest, c, "寨主的鐵箱", 15);
                r.gears.Add(Gears.TwinBlades);
                r.items.Add(Item.Thunder); r.items.Add(Item.Venom); r.items.Add(Item.QiPill);
                r.silver = 40;
            }
            c = Near(b, 18, 18);
            if (c >= 0)
            {
                var r = b.AddProp(PropKind.Crate, c, "糧草木箱");
                r.items.Add(Item.Salve); r.items.Add(Item.Lime);
                r.silver = 4;
            }

            // 斷魂崖頂的石匣：爬得上去的人才拿得到
            c = Near(b, (int)MapGen.MesaX, (int)MapGen.MesaY);
            if (c >= 0)
            {
                var r = b.AddProp(PropKind.Box, c, "崖頂的石匣");
                r.items.Add(Item.Grapnel); r.items.Add(Item.Thunder);
                r.silver = 50;
            }

            // 山腳小村。北排是蓋在地圖上的屋子（MapGen.House／Stall），主人站在屋裡；
            // 南排的食樓、繡坊只有門面，進門會換到另一個場景
            int vz = MapGen.VillageH, vy = MapGen.VillageY;
            Keeper(b, 1, vy + 1, ShopKind.Home, "村婦");
            Keeper(b, 6, vy + 1, ShopKind.Smithy, "鐵匠");
            Keeper(b, 11, vy, ShopKind.TeaStall, "茶攤老闆娘");
            Building(b, 2, vy + 6, ShopKind.Inn, "食樓");
            Building(b, 7, vy + 6, ShopKind.Weaver, "繡坊");

            c = w.Idx(9, vy + 4, vz);
            if (w.Standable(9, vy + 4, vz) && b.PropAt(c) == null) b.AddProp(PropKind.Well, c, "水井");
            c = w.Idx(10, vy + 4, vz);
            if (w.Standable(10, vy + 4, vz) && b.PropAt(c) == null) b.AddProp(PropKind.Bucket, c, "水桶");
            c = w.Idx(4, vy + 3, vz);
            if (w.Standable(4, vy + 3, vz) && b.PropAt(c) == null) b.AddProp(PropKind.Bucket, c, "水桶");
            c = Free(w, 12, MapGen.VillageY + 4);
            if (c >= 0 && b.PropAt(c) == null)
            {
                var r = b.AddProp(PropKind.Crate, c, "貨箱");
                r.items.Add(Item.Salve); r.items.Add(Item.Caltrops);
                r.silver = 5;
            }
            c = w.Idx(2, vy + 1, vz); // 村舍屋裡
            if (w.Standable(2, vy + 1, vz) && b.PropAt(c) == null)
            {
                var r = b.AddProp(PropKind.Box, c, "收納盒");
                r.items.Add(Item.QiPill); r.items.Add(Item.Antidote);
            }
        }

        // 站在屋裡（或棚下）的店家
        static void Keeper(Battle b, int x, int y, ShopKind shop, string name)
        {
            var w = b.world;
            int z = MapGen.VillageH;
            if (!w.Standable(x, y, z)) return;
            b.AddProp(new Prop { kind = PropKind.Building, keeper = true, shop = shop, cell = w.Idx(x, y, z), name = name, look = Looks.Hermit.Clone(), fx = 0, fy = 1 });
        }

        // 一棟佔 3×2 格的房舍，西北角在 (x, y)；地面要是整平的空地
        static void Building(Battle b, int x, int y, ShopKind shop, string name)
        {
            var w = b.world;
            int z = w.TopStand(x, y);
            if (z < 0) return;
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 3; dx++)
                {
                    if (!w.Standable(x + dx, y + dy, z)) return;
                    int c = w.Idx(x + dx, y + dy, z);
                    if (w.occ[c] >= 0 || w.blocked[c] || b.PropAt(c) != null) return;
                }
            var p = new Prop { id = b.props.Count, kind = PropKind.Building, cell = w.Idx(x, y, z), name = name, shop = shop, sx = 3, sy = 2 };
            b.AddProp(p);
        }

        // 離 (x,y) 最近、沒人也沒有物件的可站格
        static int Near(Battle b, int x, int y)
        {
            var w = b.world;
            for (int r = 0; r < 5; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        int z = w.TopStand(x + dx, y + dy);
                        if (z <= 0) continue;
                        int c = w.Idx(x + dx, y + dy, z);
                        if (Ground(w, x + dx, y + dy) != c) continue; // 樹冠、屋頂上不算
                        if (w.occ[c] < 0 && !w.blocked[c] && b.PropAt(c) == null) return c;
                    }
            return -1;
        }

        // (x,y) 這一直行最低的可站格（樹冠、屋頂底下的地面）；不存在回傳 -1
        static int Ground(VoxelWorld w, int x, int y)
        {
            for (int z = 1; z < w.H - 1; z++) if (w.Standable(x, y, z)) return w.Idx(x, y, z);
            return -1;
        }

        // (x,y) 這一直行最高的可站格；被佔用或不存在回傳 -1
        static int Free(VoxelWorld w, int x, int y)
        {
            int z = w.TopStand(x, y);
            if (z < 0) return -1;
            int c = w.Idx(x, y, z);
            return w.occ[c] < 0 && !w.blocked[c] ? c : -1;
        }
    }
}
