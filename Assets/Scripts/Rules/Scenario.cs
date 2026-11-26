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

        public static Battle Create()
        {
            var w = MapGen.Generate();
            var b = new Battle(w);
            for (int i = 0; i < Db.Heroes.Length; i++)
                b.AddUnit(Db.Heroes[i], 0, MapGen.FindSpawn(w, HeroAt[i, 0], HeroAt[i, 1]));
            for (int i = 0; i < BanditAt.GetLength(0); i++)
                b.AddUnit(Db.Bandit, 1, MapGen.FindSpawn(w, BanditAt[i, 0], BanditAt[i, 1]), Db.Bandit.name + Ord[i]);
            for (int i = 0; i < ArcherAt.GetLength(0); i++)
                b.AddUnit(Db.Archer, 1, MapGen.FindSpawn(w, ArcherAt[i, 0], ArcherAt[i, 1]), Db.Archer.name + Ord[i]);
            b.AddUnit(Db.Boss, 1, MapGen.FindSpawn(w, 20, 12));
            PlaceProps(b);
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
