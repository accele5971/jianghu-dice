using System;

namespace Jianghu
{
    /// <summary>穩定的雜湊亂數（0～1）。規則層與畫面層共用，結果每次都一樣。</summary>
    public static class Hashing
    {
        public static float Hash(int a, int b, int c = 0)
        {
            unchecked
            {
                uint h = (uint)(a * 374761393 + b * 668265263 + c * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }

    /// <summary>
    /// 立在四格交界（格子角落）的擺設：竹叢、石燈籠、寨旗。
    /// 它們有碰撞體積：不能斜著穿過那個角落。位置由這裡決定，畫面層照著畫。
    /// </summary>
    public static class Decor
    {
        public const byte None = 0, Bamboo = 1, Lantern = 2, Banner = 3;

        /// <summary>寨旗所在的角落（x, y）。</summary>
        public static readonly int[,] Banners = { { 18, 13 }, { 21, 16 }, { 18, 3 } };

        static bool Natural(byte b) { return b == Block.Grass || b == Block.Dirt || b == Block.Stone || b == Block.Sand; }

        /// <summary>每個直行由底部往上連續的自然方塊數，也就是地面高度。</summary>
        public static int[] GroundHeights(VoxelWorld w)
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

        public static void Build(VoxelWorld w)
        {
            var g = GroundHeights(w);

            // 竹叢：四周必須都是平緩的草地
            for (int y = 1; y < w.D; y++)
                for (int x = 1; x < w.W; x++)
                {
                    if (Hashing.Hash(x, y, 200) > 0.085f) continue;
                    if (x < 8 && y > 15) continue; // 我方出生區留空
                    if (y >= MapGen.VillageY - 1 && x <= MapGen.VillageX + 1) continue; // 村子裡不長竹子
                    int h0 = g[x + y * w.W];
                    bool ok = true;
                    for (int dy = -1; dy <= 0 && ok; dy++)
                        for (int dx = -1; dx <= 0; dx++)
                        {
                            int cx = x + dx, cy = y + dy, h = g[cx + cy * w.W];
                            if (h == 0 || w.v[w.Idx(cx, cy, h - 1)] != Block.Grass || w.Get(cx, cy, h) != Block.Air || Math.Abs(h - h0) > 1) { ok = false; break; }
                        }
                    if (ok) w.SetCorner(x, y, Bamboo);
                }

            // 石燈籠：涼亭四角外側
            int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue;
            for (int y = 0; y < w.D; y++)
                for (int x = 0; x < w.W; x++)
                {
                    if (w.Get(x, y, g[x + y * w.W]) != Block.Pillar) continue;
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
            if (x1 >= x0)
            {
                int[,] c = { { x0 - 1, y0 - 1 }, { x1 + 2, y0 - 1 }, { x0 - 1, y1 + 2 }, { x1 + 2, y1 + 2 } };
                for (int i = 0; i < 4; i++)
                    if (c[i, 0] >= 1 && c[i, 1] >= 1 && c[i, 0] <= w.W - 1 && c[i, 1] <= w.D - 1) w.SetCorner(c[i, 0], c[i, 1], Lantern);
            }

            for (int i = 0; i < Banners.GetLength(0); i++)
            {
                int bx = Banners[i, 0], by = Banners[i, 1];
                if (bx >= 1 && by >= 1 && bx <= w.W - 1 && by <= w.D - 1) w.SetCorner(bx, by, Banner);
            }
        }
    }
}
