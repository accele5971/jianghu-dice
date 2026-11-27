using System;

namespace Jianghu
{
    /// <summary>生成一張有山崖、河流、木橋、淺灘、涼亭與樹的地圖。</summary>
    public static class MapGen
    {
        public const int W = 24, D = 34, H = 16;
        const int D0 = 24; // 原本的山谷範圍；再往南是山腳下的小村
        /// <summary>山腳小村：這一排以南、VillageX 以西整平成空地，給房舍用。</summary>
        public const int VillageY = 26, VillageX = 13, VillageH = 3;
        const int PX = 4, PY = 7, PS = 5, PH = 3; // 涼亭位置、邊長、地基高

        static float RiverX(int y) { return 9 + y * 0.3f - (float)Math.Sin(y * 0.5) * 1.5f; }

        public static VoxelWorld Generate(uint seed = 11)
        {
            var w = new VoxelWorld(W, D, H);
            var rnd = new Rng(seed);

            // 低頻雜訊：粗格點雙線性內插
            const int L = 7;
            var lat = new float[L * L];
            for (int i = 0; i < lat.Length; i++) lat[i] = (float)rnd.Next();

            var river = new byte[W * D]; // 1 河道、2 河岸
            var hgt = new byte[W * D];

            for (int y = 0; y < D; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float fx = x / (float)(W - 1) * (L - 1), fy = Math.Min(y, D0 - 1) / (float)(D0 - 1) * (L - 1);
                    int x0 = Math.Min(L - 2, (int)fx), y0 = Math.Min(L - 2, (int)fy);
                    float tx = fx - x0, ty = fy - y0;
                    float a = lat[x0 + y0 * L], b = lat[x0 + 1 + y0 * L];
                    float c = lat[x0 + (y0 + 1) * L], d = lat[x0 + 1 + (y0 + 1) * L];
                    float n = (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;

                    int h = 2 + (int)(n * 2.6f);
                    // 東北角山崖
                    double dist = Math.Sqrt((x - 19.5) * (x - 19.5) + (y - 4.5) * (y - 4.5));
                    h += Math.Max(0, Math.Min(5, (int)Math.Round(6.2 - dist * 0.95)));

                    float rd = Math.Abs(x - RiverX(y));
                    if (rd < 1.4f) { river[x + y * W] = 1; h = 1; }
                    else if (rd < 2.4f) { river[x + y * W] = 2; h = Math.Min(h, 2); }
                    else if (y >= VillageY && x <= VillageX) h = VillageH;
                    hgt[x + y * W] = (byte)h;
                }
            }

            // 涼亭地基：整平
            for (int y = PY; y < PY + PS; y++)
                for (int x = PX; x < PX + PS; x++) hgt[x + y * W] = PH;

            // 填入地形柱
            for (int y = 0; y < D; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int h = hgt[x + y * W], r = river[x + y * W];
                    bool rocky = h >= 6;
                    for (int z = 0; z < h; z++)
                    {
                        byte b;
                        if (z == h - 1) b = r != 0 ? Block.Sand : h >= 8 ? Block.Stone : Block.Grass;
                        else if (!rocky && z >= h - 3) b = Block.Dirt;
                        else b = Block.Stone;
                        w.Set(x, y, z, b);
                    }
                    if (r == 1) w.Set(x, y, 1, Block.Water);
                }
            }

            // 涼亭：石地、朱柱、兩層瓦頂、外側石階
            for (int y = PY; y < PY + PS; y++)
            {
                for (int x = PX; x < PX + PS; x++)
                {
                    w.Set(x, y, PH - 1, Block.Stone);
                    w.Set(x, y, PH + 3, Block.Tile);
                    bool corner = (x == PX || x == PX + PS - 1) && (y == PY || y == PY + PS - 1);
                    if (corner) for (int z = PH; z < PH + 3; z++) w.Set(x, y, z, Block.Pillar);
                }
            }
            for (int y = PY + 1; y < PY + PS - 1; y++)
                for (int x = PX + 1; x < PX + PS - 1; x++) w.Set(x, y, PH + 4, Block.Tile);
            for (int s = 0; s < 3; s++)
            {
                int x = PX + PS, y = PY + 1 + s;
                for (int z = 0; z < PH + 1 + s; z++) w.Set(x, y, z, Block.Stone);
            }

            // 山腳小村北排：兩間走得進去的屋子與一座茶棚（南排的食樓、繡坊是另一個場景，只有門面）
            House(w, 0, VillageY, 4, 4, 2);
            House(w, 5, VillageY, 4, 4, 6);
            Stall(w, 10, VillageY, 4, 3);

            // 木橋（橫跨河面）與下游淺灘
            for (int y = 11; y <= 12; y++)
                for (int x = 0; x < W; x++)
                    if (Math.Abs(x - RiverX(y)) < 3.6f && w.Get(x, y, 2) == Block.Air) w.Set(x, y, 2, Block.Wood);
            for (int y = 19; y <= 20; y++)
                for (int x = 0; x < W; x++)
                    if (river[x + y * W] == 1) w.Set(x, y, 1, Block.Stone);

            // 樹：樹幹兩格、樹冠可站
            int trees = 0;
            for (int n = 0; n < 300 && trees < 11; n++)
            {
                int x = 1 + rnd.Range(W - 2), y = 1 + rnd.Range(D0 - 2);
                int h = hgt[x + y * W];
                if (river[x + y * W] != 0 || h > 4) continue;
                if (x >= PX - 1 && x <= PX + PS + 1 && y >= PY - 1 && y <= PY + PS) continue; // 涼亭
                if (x < 7 && y > 15) continue;               // 我方出生區
                if (x > 15 && y >= 10 && y <= 18) continue;  // 敵方出生區
                if (y >= 10 && y <= 13) continue;            // 橋的動線
                if (w.Get(x, y, h) != Block.Air) continue;
                bool crowded = false;
                for (int dy = -2; dy <= 2 && !crowded; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        if (w.Get(x + dx, y + dy, h + 2) == Block.Leaf) { crowded = true; break; }
                if (crowded) continue;

                w.Set(x, y, h, Block.Wood);
                w.Set(x, y, h + 1, Block.Wood);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (w.Get(x + dx, y + dy, h + 2) == Block.Air) w.Set(x + dx, y + dy, h + 2, Block.Leaf);
                w.Set(x, y, h + 3, Block.Leaf);
                trees++;
            }

            CoverMap.Build(w);
            Decor.Build(w);
            return w;
        }

        /// <summary>屋內地面比屋外高幾層（0：與村子的地面齊平）。牆高 3 層，其上是瓦頂。</summary>
        public const int WallH = 3;

        // 一間屋子：四面牆（南牆留一個門洞）、石板地、兩層瓦頂。西北角在 (x0, y0)，佔 sx × sy 格
        static void House(VoxelWorld w, int x0, int y0, int sx, int sy, int doorX)
        {
            int z = VillageH;
            for (int y = y0; y < y0 + sy; y++)
                for (int x = x0; x < x0 + sx; x++)
                {
                    bool edge = x == x0 || x == x0 + sx - 1 || y == y0 || y == y0 + sy - 1;
                    bool door = x == doorX && y == y0 + sy - 1;
                    w.Set(x, y, z - 1, Block.Stone);
                    if (edge && !door) for (int dz = 0; dz < WallH; dz++) w.Set(x, y, z + dz, Block.Wall);
                    w.Set(x, y, z + WallH, Block.Tile);
                    if (!edge) w.Set(x, y, z + WallH + 1, Block.Tile);
                }
        }

        // 一座棚子：四角木柱撐著瓦頂，四面敞開
        static void Stall(VoxelWorld w, int x0, int y0, int sx, int sy)
        {
            int z = VillageH;
            for (int y = y0; y < y0 + sy; y++)
                for (int x = x0; x < x0 + sx; x++)
                {
                    bool corner = (x == x0 || x == x0 + sx - 1) && (y == y0 || y == y0 + sy - 1);
                    w.Set(x, y, z - 1, Block.Stone);
                    if (corner) for (int dz = 0; dz < WallH; dz++) w.Set(x, y, z + dz, Block.Post);
                    w.Set(x, y, z + WallH, Block.Tile);
                }
        }

        /// <summary>室內場景的地面高度（層）。</summary>
        public const int FloorH = 2;
        /// <summary>食樓的二樓佔北邊幾排；二樓的站立高度是 UpperZ。</summary>
        public const int LoftRows = 3, UpperZ = FloorH + WallH + 1;

        /// <summary>
        /// 另一個場景用的室內地圖（食樓、繡坊）：一間四面有牆、沒有屋頂的大屋，南牆正中是門口。
        /// 整張地圖就是這間屋子。
        /// </summary>
        public static VoxelWorld Interior(ShopKind kind)
        {
            bool inn = kind == ShopKind.Inn;
            int W = inn ? 11 : 9, D = inn ? 9 : 8;
            var w = new VoxelWorld(W, D, 12);
            for (int y = 0; y < D; y++)
                for (int x = 0; x < W; x++)
                {
                    for (int z = 0; z < FloorH; z++) w.Set(x, y, z, Block.Stone);
                    bool edge = x == 0 || x == W - 1 || y == 0 || y == D - 1;
                    bool door = x == W / 2 && y == D - 1;
                    // 食樓北半邊有二樓：那一段的外牆砌高，高出樓板一層當作女兒牆
                    int high = inn && y <= LoftRows ? WallH + 2 : WallH;
                    if (edge && !door) for (int dz = 0; dz < high; dz++) w.Set(x, y, FloorH + dz, Block.Wall);
                }
            if (inn)
            {
                // 二樓：北邊三排鋪樓板（樓板在牆頂的高度，人站在它上面）
                for (int y = 1; y <= LoftRows; y++)
                    for (int x = 1; x < W - 1; x++) w.Set(x, y, FloorH + WallH, Block.Floor);
                // 樓梯：貼著東牆由南往北，一階高一層，最後一階與樓板齊平
                for (int k = 0; k < WallH + 1; k++)
                    for (int dz = 0; dz <= k; dz++) w.Set(W - 2, LoftRows + WallH + 1 - k, FloorH + dz, Block.Stair);
                // 樓板南緣底下立兩根柱子
                foreach (int x in new[] { 2, W - 4 })
                    for (int dz = 0; dz < WallH; dz++) w.Set(x, LoftRows, FloorH + dz, Block.Post);
            }
            CoverMap.Build(w);
            Decor.Build(w);
            return w;
        }

        /// <summary>找離 (x,y) 最近、未被佔用的可站格；回傳格子索引，找不到為 -1。</summary>
        public static int FindSpawn(VoxelWorld w, int x, int y)
        {
            for (int r = 0; r < 6; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        int z = w.TopStand(x + dx, y + dy);
                        if (z > 0 && w.occ[w.Idx(x + dx, y + dy, z)] < 0 && !w.blocked[w.Idx(x + dx, y + dy, z)]) return w.Idx(x + dx, y + dy, z);
                    }
                }
            }
            return -1;
        }
    }
}
