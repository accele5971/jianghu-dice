using System;

namespace Jianghu
{
    /// <summary>
    /// 視野與迷霧。以直行（x, y）為單位記錄兩件事：
    /// seen：曾經看過（看過就永久揭開）；lit：此刻正在視野內。
    ///
    /// 每個觀察者的視野是面向前方的扇形（左右各 60 度），另有周身 2 格的餘光；
    /// 還要通過 3D 視線判定，所以山壁、樹冠、屋頂都會擋住視線。
    /// 站得高看得遠：每高 2 層視距加 1，最多加 3。
    /// </summary>
    public sealed class Vision
    {
        public const int Range = 8;       // 正前方的視距
        public const int Peripheral = 2;  // 不論面向都看得到的距離

        public readonly byte[] seen, lit;
        readonly VoxelWorld w;

        public Vision(VoxelWorld world)
        {
            w = world;
            seen = new byte[world.W * world.D];
            lit = new byte[world.W * world.D];
        }

        /// <summary>重新計算「此刻看得到」之前先清空；seen 不清，保留探索紀錄。</summary>
        public void BeginUpdate() { Array.Clear(lit, 0, lit.Length); }

        public void RevealAll()
        {
            for (int i = 0; i < seen.Length; i++) { seen[i] = 1; lit[i] = 1; }
        }

        /// <summary>目標是否落在觀察者的扇形視野或餘光內（不含視線遮擋）。</summary>
        public static bool InCone(int vx, int vy, int fx, int fy, int tx, int ty, int range)
        {
            int dx = tx - vx, dy = ty - vy, d2 = dx * dx + dy * dy;
            if (d2 <= Peripheral * Peripheral) return true;
            if (d2 > range * range) return false;
            int f2 = fx * fx + fy * fy;
            if (f2 == 0) return true;
            int dot = dx * fx + dy * fy;
            // 夾角 60 度以內：cos ≥ 0.5，也就是 dot² ≥ 0.25·|d|²·|f|²（且 dot 為正）
            return dot > 0 && 4L * dot * dot >= (long)d2 * f2;
        }

        /// <summary>目標是否在觀察者正前方的扇形內（不看距離，也不含餘光）。</summary>
        public static bool InFront(int vx, int vy, int fx, int fy, int tx, int ty)
        {
            int dx = tx - vx, dy = ty - vy, d2 = dx * dx + dy * dy, f2 = fx * fx + fy * fy;
            if (f2 == 0 || d2 == 0) return true;
            int dot = dx * fx + dy * fy;
            return dot > 0 && 4L * dot * dot >= (long)d2 * f2;
        }

        static int RangeFrom(int vz, int tz)
        {
            return Range + Math.Max(-2, Math.Min(3, (vz - tz) / 2));
        }

        /// <summary>站在 (vx,vy,vz)、面向 (fx,fy) 的人，能否看見站在 (tx,ty,tz) 的人。</summary>
        public bool CanSeeCell(int vx, int vy, int vz, int fx, int fy, int tx, int ty, int tz)
        {
            return InCone(vx, vy, fx, fy, tx, ty, RangeFrom(vz, tz)) && VoxelRay.Los(w, vx, vy, vz, tx, ty, tz);
        }

        // 該直行地表的高度：最高的可站格；沒有可站格（例如水面）就取最高方塊的上方
        int SurfaceZ(int x, int y)
        {
            int z = w.TopStand(x, y);
            if (z >= 0) return z;
            for (z = w.H - 1; z > 0; z--) if (w.v[w.Idx(x, y, z - 1)] != Block.Air) return z;
            return 1;
        }

        // 該直行最低的可站格（屋頂下的亭內、樹冠下的地面）
        int LowestStand(int x, int y)
        {
            for (int z = 1; z < w.H - 1; z++) if (w.Standable(x, y, z)) return z;
            return -1;
        }

        /// <summary>加入一個觀察者，把他看得到的直行標成 lit 與 seen。</summary>
        public void AddViewer(int vx, int vy, int vz, int fx, int fy)
        {
            int r = Range + 3;
            for (int y = Math.Max(0, vy - r); y <= Math.Min(w.D - 1, vy + r); y++)
            {
                for (int x = Math.Max(0, vx - r); x <= Math.Min(w.W - 1, vx + r); x++)
                {
                    int i = x + y * w.W;
                    if (lit[i] != 0) continue;
                    int top = SurfaceZ(x, y);
                    if (!InCone(vx, vy, fx, fy, x, y, RangeFrom(vz, top))) continue;
                    bool visible = (x == vx && y == vy) || VoxelRay.Los(w, vx, vy, vz, x, y, top);
                    if (!visible)
                    {
                        // 屋頂或樹冠擋住了上面，但底下的地面可能看得到
                        int low = LowestStand(x, y);
                        visible = low >= 0 && low != top && VoxelRay.Los(w, vx, vy, vz, x, y, low);
                    }
                    if (visible) { lit[i] = 1; seen[i] = 1; }
                }
            }
        }
    }
}
