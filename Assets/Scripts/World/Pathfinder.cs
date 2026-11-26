using System;
using System.Collections.Generic;

namespace Jianghu
{
    /// <summary>
    /// 含高度的移動範圍搜尋。成本皆為小整數，用桶佇列（Dial 演算法）；
    /// 緩衝區建構時配置一次，之後以世代戳記重複使用，搜尋過程不產生垃圾。
    ///
    /// 移動方式：
    /// 　走：八個方向，平地與下坡每步 1 點、上坡 2 點。斜走不能切過牆角，也不能穿過立在角落的擺設。
    /// 　縱躍：輕功（可攀高度）2 以上的人可以直線躍過障礙或缺口，落在 2 格外（輕功 4 以上可達 3 格），
    /// 　　　　成本為格數 + 1（往上跳再 +1）。不能從敵人頭上或滾石上躍過。
    /// 路徑中相鄰兩格若不相鄰，就是一次縱躍。
    /// </summary>
    public sealed class Pathfinder
    {
        // 前四個是正向，後四個是斜向
        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        readonly VoxelWorld w;
        readonly ushort[] cost;
        readonly int[] prev;
        readonly uint[] mark;
        uint gen;
        readonly List<List<int>> buckets = new List<List<int>>();

        /// <summary>可停留的格子（不含起點、不含被佔用格），依成本由小到大。</summary>
        public readonly List<int> reach = new List<int>();

        public Pathfinder(VoxelWorld world)
        {
            w = world;
            int n = world.v.Length;
            cost = new ushort[n];
            prev = new int[n];
            mark = new uint[n];
        }

        /// <summary>輕功多高可以躍多遠（格）。1 表示不會縱躍。</summary>
        public static int LeapRange(int jump) { return jump >= 4 ? 3 : jump >= 2 ? 2 : 1; }

        /// <summary>兩格是否相鄰（含斜向）。路徑上不相鄰的兩格之間是一次縱躍。</summary>
        public bool Adjacent(int a, int b)
        {
            return Math.Abs(w.X(a) - w.X(b)) <= 1 && Math.Abs(w.Y(a) - w.Y(b)) <= 1;
        }

        // (x,y) 這一直行在高度 z、z+1 是否淨空（身體過得去）；滾石也算擋住
        bool Open(int x, int y, int z)
        {
            if (x < 0 || y < 0 || x >= w.W || y >= w.D) return false;
            if (z < 0 || z + 1 >= w.H) return false;
            int i = x + y * w.W + z * w.WD;
            return w.v[i] == Block.Air && w.v[i + w.WD] == Block.Air && !w.wall[i];
        }

        // 這一直行在高度 lo～hi 之間有沒有敵人站著
        bool FoeIn(int x, int y, int lo, int hi, int team, List<Unit> units)
        {
            for (int z = Math.Max(1, lo); z <= hi && z < w.H; z++)
            {
                int o = w.occ[x + y * w.W + z * w.WD];
                if (o >= 0 && units[o].team != team) return true;
            }
            return false;
        }

        /// <summary>
        /// 從 start 擴散。jump：可攀高度；可安全落下 jump+2 層。友軍可穿越不可停留，敵軍阻擋。
        /// </summary>
        public void Flood(int start, int budget, int jump, int team, List<Unit> units)
        {
            int W = w.W, D = w.D, H = w.H, WD = w.WD;
            gen++;
            while (buckets.Count <= budget) buckets.Add(new List<int>());
            for (int i = 0; i <= budget; i++) buckets[i].Clear();
            reach.Clear();

            cost[start] = 0; prev[start] = -1; mark[start] = gen;
            buckets[0].Add(start);
            int drop = jump + 2, leap = LeapRange(jump);

            for (int c = 0; c <= budget; c++)
            {
                var b = buckets[c];
                for (int k = 0; k < b.Count; k++)
                {
                    int i = b[k];
                    if (cost[i] != c) continue; // 已被更短路徑取代
                    if (i != start && w.occ[i] < 0) reach.Add(i);
                    int x = i % W, y = (i / W) % D, z = i / WD;

                    for (int d = 0; d < 8; d++)
                    {
                        int dx = DX[d], dy = DY[d];
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= W || ny >= D) continue;
                        // 斜走：角落上立著竹叢、石燈籠、寨旗就過不去
                        bool diag = d >= 4;
                        if (diag && w.CornerAt(x + (dx > 0 ? 1 : 0), y + (dy > 0 ? 1 : 0)) != 0) continue;

                        // ── 走一步 ──
                        int hi = Math.Min(z + jump, H - 2), lo = Math.Max(1, z - drop);
                        for (int tz = hi; tz >= lo; tz--)
                        {
                            if (!w.Standable(nx, ny, tz)) continue;
                            // 淨空：往上先在原地升起，往下先平移再落下
                            bool ok = true;
                            if (tz > z)
                            {
                                for (int q = z + 2; q <= tz + 1; q++)
                                    if (w.Get(x, y, q) != Block.Air) { ok = false; break; }
                            }
                            else if (tz < z)
                            {
                                for (int q = tz + 2; q <= z + 1; q++)
                                    if (w.Get(nx, ny, q) != Block.Air) { ok = false; break; }
                            }
                            if (!ok) continue;
                            // 斜走不能切過牆角：兩側的直行在較高的那一層都要過得去
                            if (diag)
                            {
                                int top = Math.Max(z, tz);
                                if (!Open(nx, y, top) || !Open(x, ny, top)) continue;
                            }

                            int j = nx + ny * W + tz * WD;
                            if (w.blocked[j]) continue; // 滾石、寶箱等地圖物件擋路
                            int o = w.occ[j];
                            if (o >= 0 && units[o].team != team) continue;
                            Relax(i, j, c + (tz > z ? 2 : 1), budget);
                        }

                        // ── 縱躍 ──
                        for (int dist = 2; dist <= leap; dist++)
                        {
                            int mx = x + dx * (dist - 1), my = y + dy * (dist - 1);
                            int tx = x + dx * dist, ty = y + dy * dist;
                            if (tx < 0 || ty < 0 || tx >= W || ty >= D) break;
                            // 躍過的那一格：頭頂要有空間、不能有滾石或敵人；斜向還要避開角落的擺設
                            if (!Open(mx, my, z + 1) || FoeIn(mx, my, z - 2, z + 1, team, units)) break;
                            if (diag && w.CornerAt(mx + (dx > 0 ? 1 : 0), my + (dy > 0 ? 1 : 0)) != 0) break;
                            if (w.Get(x, y, z + 2) != Block.Air) break; // 起跳處頭頂被擋

                            for (int tz = Math.Min(z + 1, H - 2); tz >= lo; tz--)
                            {
                                if (!w.Standable(tx, ty, tz)) continue;
                                if (!Open(tx, ty, Math.Max(z, tz) + 1)) continue; // 落點上方要有弧線的空間
                                int j = tx + ty * W + tz * WD;
                                if (w.blocked[j]) break;
                                int o = w.occ[j];
                                if (o >= 0 && units[o].team != team) break;
                                Relax(i, j, c + dist + 1 + (tz > z ? 1 : 0), budget);
                                break; // 只落在最高的那一個落腳處
                            }
                        }
                    }
                }
            }
        }

        void Relax(int from, int j, int nc, int budget)
        {
            if (nc > budget) return;
            if (mark[j] == gen && cost[j] <= nc) return;
            mark[j] = gen; cost[j] = (ushort)nc; prev[j] = from;
            buckets[nc].Add(j);
        }

        public bool Seen(int i) { return mark[i] == gen; }
        public int CostOf(int i) { return mark[i] == gen ? cost[i] : -1; }

        /// <summary>起點到 i 的格子序列（含兩端）。</summary>
        public int[] Path(int i)
        {
            int n = 0;
            for (int c = i; c != -1; c = prev[c]) n++;
            var path = new int[n];
            for (int c = i; c != -1; c = prev[c]) path[--n] = c;
            return path;
        }
    }
}
