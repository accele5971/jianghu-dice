namespace Jianghu
{
    public static class Block
    {
        public const byte Air = 0, Grass = 1, Dirt = 2, Stone = 3, Wood = 4,
                          Water = 5, Leaf = 6, Tile = 7, Pillar = 8, Sand = 9;
        public const int Count = 10;
    }

    /// <summary>
    /// xyz 體素世界。x、y 為水平，z 為高度。
    /// 「格子」指角色腳所在的空氣體素：下方實心、自身與上方皆為空。
    /// </summary>
    public sealed class VoxelWorld
    {
        public readonly int W, D, H, WD;
        public readonly byte[] v;    // 方塊種類
        public readonly short[] occ; // 佔位：單位 id 或 -1
        public readonly bool[] blocked; // 被地圖物件佔住（滾石、寶箱、路人、已發現的絆索）：不能走進去

        // 物件的碰撞體積
        public readonly bool[] wall;    // 擋視線與射擊的物件體素（滾石佔兩層）
        public readonly bool[] smoke;   // 濃煙：擋視線與射擊，但人可以走進去
        public readonly short[] propAt; // 物件本體佔到的體素 → 物件 id（滑鼠可以直接點到本體），沒有為 -1
        public readonly byte[] corner;  // 立在四格交界的擺設（竹叢、石燈籠、寨旗），見 Decor；不能斜著穿過

        /// <summary>格子角落 (cx, cy) 上的擺設；cx 為 0～W、cy 為 0～D。</summary>
        public byte CornerAt(int cx, int cy)
        {
            return cx < 0 || cy < 0 || cx > W || cy > D ? (byte)0 : corner[cx + cy * (W + 1)];
        }

        public void SetCorner(int cx, int cy, byte kind) { corner[cx + cy * (W + 1)] = kind; }

        // 遮蔽物（樹冠、屋頂）：每個體素所屬的結構編號（-1 表示不是遮蔽物），見 CoverMap
        public readonly short[] cover;
        public int coverCount;
        public bool[] coverHidden = new bool[0]; // 該結構目前是否被掀開（淡出且不可點選）

        public VoxelWorld(int w, int d, int h)
        {
            W = w; D = d; H = h; WD = w * d;
            v = new byte[w * d * h];
            occ = new short[w * d * h];
            blocked = new bool[w * d * h];
            wall = new bool[w * d * h];
            smoke = new bool[w * d * h];
            propAt = new short[w * d * h];
            corner = new byte[(w + 1) * (d + 1)];
            for (int i = 0; i < propAt.Length; i++) propAt[i] = -1;
            cover = new short[w * d * h];
            for (int i = 0; i < occ.Length; i++) { occ[i] = -1; cover[i] = -1; }
        }

        public int Idx(int x, int y, int z) { return x + y * W + z * WD; }
        public int X(int i) { return i % W; }
        public int Y(int i) { return (i / W) % D; }
        public int Z(int i) { return i / WD; }

        public bool InB(int x, int y, int z)
        {
            return x >= 0 && y >= 0 && z >= 0 && x < W && y < D && z < H;
        }

        /// <summary>界外視為空氣。</summary>
        public byte Get(int x, int y, int z)
        {
            return InB(x, y, z) ? v[x + y * W + z * WD] : Block.Air;
        }

        public void Set(int x, int y, int z, byte b)
        {
            if (InB(x, y, z)) v[x + y * W + z * WD] = b;
        }

        /// <summary>實心（可踩、擋視線）。水不算。</summary>
        public bool Solid(int x, int y, int z)
        {
            byte b = Get(x, y, z);
            return b != Block.Air && b != Block.Water;
        }

        public bool Standable(int x, int y, int z)
        {
            if (x < 0 || y < 0 || x >= W || y >= D || z < 1 || z > H - 2) return false;
            int i = x + y * W + z * WD;
            if (v[i] != 0 || v[i + WD] != 0) return false;
            byte below = v[i - WD];
            return below != Block.Air && below != Block.Water;
        }

        /// <summary>站在 (x,y,z) 的人頭頂上方最近的遮蔽結構編號，沒有則 -1。</summary>
        public int CoverAbove(int x, int y, int z)
        {
            if (x < 0 || y < 0 || x >= W || y >= D) return -1;
            for (int q = z < 0 ? 0 : z + 2; q < H; q++)
            {
                int c = cover[x + y * W + q * WD];
                if (c >= 0) return c;
            }
            return -1;
        }

        /// <summary>該直行最高的可站格 z，沒有則 -1。</summary>
        public int TopStand(int x, int y)
        {
            for (int z = H - 2; z >= 1; z--) if (Standable(x, y, z)) return z;
            return -1;
        }
    }
}
