using System;

namespace Jianghu
{
    public struct RayHit
    {
        public int x, y, z;    // 命中的體素
        public int nx, ny, nz; // 命中面的法線
        public float t;
    }

    /// <summary>體素射線（Amanatides–Woo）。視線判定與滑鼠拾取共用，逐格前進、零配置。</summary>
    public static class VoxelRay
    {
        /// <summary>
        /// 沿 (dx,dy,dz) 前進。pick 為 false：遇實心方塊停（視線用）；
        /// pick 為 true：遇任何方塊或有人站立的格子停（滑鼠拾取用；略過已掀開的遮蔽物）。
        /// </summary>
        public static bool Trace(VoxelWorld w, float ox, float oy, float oz,
                                 float dx, float dy, float dz, float maxT, bool pick, out RayHit hit)
        {
            hit = default(RayHit);
            int x = (int)Math.Floor(ox), y = (int)Math.Floor(oy), z = (int)Math.Floor(oz);
            int sx = dx > 0 ? 1 : -1, sy = dy > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
            float ax = dx != 0 ? Math.Abs(1 / dx) : float.PositiveInfinity;
            float ay = dy != 0 ? Math.Abs(1 / dy) : float.PositiveInfinity;
            float az = dz != 0 ? Math.Abs(1 / dz) : float.PositiveInfinity;
            float tx = dx != 0 ? (dx > 0 ? x + 1 - ox : ox - x) * ax : float.PositiveInfinity;
            float ty = dy != 0 ? (dy > 0 ? y + 1 - oy : oy - y) * ay : float.PositiveInfinity;
            float tz = dz != 0 ? (dz > 0 ? z + 1 - oz : oz - z) * az : float.PositiveInfinity;
            int face = 0;
            float t = 0;

            for (int n = 0; n < 600; n++)
            {
                if (w.InB(x, y, z))
                {
                    int i = x + y * w.W + z * w.WD;
                    byte b = w.v[i];
                    bool stop;
                    if (pick)
                    {
                        // 已掀開的樹冠、屋頂當作不存在，才能點到底下的格子
                        int cv = w.cover[i];
                        bool block = b != Block.Air && !(cv >= 0 && w.coverHidden[cv]);
                        // 人與物件都有體積：點到身體也算點到
                        stop = block || w.occ[i] >= 0 || w.propAt[i] >= 0 || (z > 0 && w.occ[i - w.WD] >= 0);
                    }
                    else stop = (b != Block.Air && b != Block.Water) || w.wall[i] || w.smoke[i]; // 滾石與濃煙也擋視線
                    if (stop)
                    {
                        hit.x = x; hit.y = y; hit.z = z; hit.t = t;
                        hit.nx = face == 1 ? -sx : 0;
                        hit.ny = face == 2 ? -sy : 0;
                        hit.nz = face == 3 ? -sz : 0;
                        return true;
                    }
                }
                if (tx <= ty && tx <= tz) { t = tx; tx += ax; x += sx; face = 1; }
                else if (ty <= tz) { t = ty; ty += ay; y += sy; face = 2; }
                else { t = tz; tz += az; z += sz; face = 3; }
                if (t > maxT) return false;
            }
            return false;
        }

        /// <summary>站在 a 格的人能否看見站在 b 格的人（眼睛 → 胸口）。</summary>
        public static bool Los(VoxelWorld w, int ax, int ay, int az, int bx, int by, int bz)
        {
            float ox = ax + 0.5f, oy = ay + 0.5f, oz = az + 1.4f;
            float dx = bx + 0.5f - ox, dy = by + 0.5f - oy, dz = bz + 1.0f - oz;
            float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 1e-6f) return true;
            RayHit h;
            return !Trace(w, ox, oy, oz, dx / len, dy / len, dz / len, len, false, out h);
        }

        /// <summary>兩點之間有沒有被實心方塊擋住。</summary>
        public static bool Clear(VoxelWorld w, float ox, float oy, float oz, float px, float py, float pz)
        {
            float dx = px - ox, dy = py - oy, dz = pz - oz;
            float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 1e-6f) return true;
            RayHit h;
            return !Trace(w, ox, oy, oz, dx / len, dy / len, dz / len, len, false, out h);
        }

        /// <summary>
        /// 掩蔽程度：從 a 格的眼睛看向站在 b 格的人上半身的五個點（頭頂、左右肩、左右耳側），
        /// 回傳被擋住的點數（0～5）。胸口那一點就是 Los，這裡不重複算。
        /// 只看上半身：一階的緩坡只到腰下，不算掩蔽；樹幹、亭柱、兩層以上的岩壁才算。
        /// </summary>
        public static int Blocked(VoxelWorld w, int ax, int ay, int az, int bx, int by, int bz)
        {
            float ox = ax + 0.5f, oy = ay + 0.5f, oz = az + 1.4f;
            float cx = bx + 0.5f, cy = by + 0.5f;
            float dx = cx - ox, dy = cy - oy;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6f) return 0;
            // 垂直於射擊方向的橫向位移：身體的左右兩側
            float sx = -dy / len * 0.35f, sy = dx / len * 0.35f;
            int n = 0;
            if (!Clear(w, ox, oy, oz, cx, cy, bz + 1.6f)) n++;
            if (!Clear(w, ox, oy, oz, cx + sx, cy + sy, bz + 1.2f)) n++;
            if (!Clear(w, ox, oy, oz, cx - sx, cy - sy, bz + 1.2f)) n++;
            if (!Clear(w, ox, oy, oz, cx + sx, cy + sy, bz + 1.6f)) n++;
            if (!Clear(w, ox, oy, oz, cx - sx, cy - sy, bz + 1.6f)) n++;
            return n;
        }

        /// <summary>把滑鼠射線轉成可站立的格子索引；沒有則 -1。</summary>
        public static int PickCell(VoxelWorld w, float ox, float oy, float oz, float dx, float dy, float dz)
        {
            RayHit h;
            if (!Trace(w, ox, oy, oz, dx, dy, dz, 400, true, out h)) return -1;
            int i = w.Idx(h.x, h.y, h.z);
            if (w.propAt[i] >= 0)                              // 點到物件本體：回傳它所在的格子
                return h.z > 0 && w.propAt[i - w.WD] == w.propAt[i] ? i - w.WD : i;
            if (w.occ[i] >= 0) return i;                       // 點到人的腳
            if (h.z > 0 && w.occ[i - w.WD] >= 0) return i - w.WD; // 點到人的上半身
            if (h.nz == 1) return w.Standable(h.x, h.y, h.z + 1) ? w.Idx(h.x, h.y, h.z + 1) : -1;
            if (h.nz == -1) return -1;
            // 點到側面：取旁邊那一行，往下找落腳處
            int x = h.x + h.nx, y = h.y + h.ny;
            for (int z = h.z, k = 0; k < 8 && z >= 1; k++, z--)
                if (w.Standable(x, y, z)) return w.Idx(x, y, z);
            return -1;
        }
    }
}
