using System;

namespace Jianghu
{
    /// <summary>
    /// 敵方決策，只依「看得見與記得的事」行動（同陣營共享敵蹤，見 Battle.Observe）：
    /// 　交戰：有人正被己方看著 → 在走得到的格子中找期望傷害最高的出手位置，打不到就逼近。
    /// 　追查：只記得對方最後出現的位置 → 走過去看；到了沒人，或拖了太久，就放棄。
    /// 　駐守：毫無敵蹤 → 回到駐守位置，左右張望。
    /// 測試用的我方自動對戰也走同一套，只是毫無敵蹤時會主動往敵陣推進。
    /// </summary>
    public static class EnemyAI
    {
        const int Far = 120; // 追查與回防時的尋路上限

        static readonly int[] DirX = { 1, 1, 0, -1, -1, -1, 0, 1 };
        static readonly int[] DirY = { 0, 1, 1, 1, 0, -1, -1, -1 };
        static readonly int[] ScanStep = { 0, 1, 0, -1 };

        public static void Take(Battle b, Unit u)
        {
            GiveUpStaleTrails(b, u);

            // 追查或推進途中若撞見敵人，剩下的行動還可以拿來出手，所以最多判斷兩次
            for (int pass = 0; pass < 2; pass++)
            {
                if (!u.alive || b.over != 0) return; // 可能在半路上被借機攻擊砍倒
                bool sight = false, known = false;
                foreach (var t in b.units)
                {
                    if (!t.alive || t.team == u.team) continue;
                    if (t.inSight) sight = true;
                    if (t.knownCell >= 0) known = true;
                }

                if (sight) { Engage(b, u); return; }
                if (pass == 1) break;

                bool moved = known ? Investigate(b, u) : u.team == 0 ? Advance(b, u) : Guard(b, u);
                if (!moved) break;
            }
        }

        // 到了現場卻沒看到人，或行蹤不明太久：放棄這條線索
        static void GiveUpStaleTrails(Battle b, Unit u)
        {
            var w = b.world;
            foreach (var t in b.units)
            {
                if (!t.alive || t.team == u.team || t.knownCell < 0 || t.inSight) continue;
                int kx = w.X(t.knownCell), ky = w.Y(t.knownCell), kz = w.Z(t.knownCell);
                int d = Math.Max(Math.Abs(kx - u.x), Math.Abs(ky - u.y));
                bool arrived = d == 0 || (d <= 2 && VoxelRay.Los(w, u.x, u.y, u.z, kx, ky, kz));
                if (arrived || b.round - t.knownAt >= Battle.MemoryRounds) b.Forget(t);
            }
        }

        // ───────── 交戰 ─────────

        static void Engage(Battle b, Unit u)
        {
            var w = b.world;
            var pf = b.pf;
            int start = b.CellOf(u);
            pf.Flood(start, 60, u.Jump, u.team, b.units);

            float best = 0;
            int bestCell = -1;
            Skill bestSk = null;
            Unit bestT = null;
            int oaCell = -1, oaCount = 0;

            // 候選：原地 + 本回合走得到的格子
            for (int k = -1; k < pf.reach.Count; k++)
            {
                int cell = k < 0 ? start : pf.reach[k];
                int cost = k < 0 ? 0 : pf.CostOf(cell);
                if (cost > u.moveLeft) break; // reach 依成本遞增
                int cx = w.X(cell), cy = w.Y(cell), cz = w.Z(cell);

                foreach (var sk in u.skills)
                {
                    if (sk.kind != SkillKind.Attack || !b.CanUse(u, sk)) continue;
                    foreach (var t in b.units)
                    {
                        if (!t.alive || t.team == u.team || !t.inSight) continue;
                        if (!b.CanHit(sk, cx, cy, cz, t.x, t.y, t.z)) continue;
                        float p = b.ChanceFrom(u, sk, cx, cy, cz, t); // 含優劣勢與掩蔽
                        float dmg = sk.dmg.Avg + u.Mod(sk.ability);
                        float score = p * dmg * sk.hits;
                        if (dmg >= t.hp) score += 4;         // 可收頭
                        if (sk.push > 0) score += 1;
                        if (!sk.melee) score += 0.15f * NearestKnown(b, u, cx, cy) + 0.1f * cz;
                        score -= cost * 0.02f;
                        if (score <= best) continue;
                        // 走過去會挨借機攻擊的話，扣掉大約一刀的代價
                        if (cell != start)
                        {
                            if (cell != oaCell) { oaCell = cell; oaCount = b.Provokers(u, pf.Path(cell), null); }
                            score -= 2.5f * oaCount;
                        }
                        if (score > best) { best = score; bestCell = cell; bestSk = sk; bestT = t; }
                    }
                }
            }

            if (bestSk != null)
            {
                if (bestCell != start) b.Move(u, bestCell);
                // 走過去的途中對方可能已經脫離視線；UseSkill 自己會再檢查打不打得到
                b.UseSkill(u, bestSk, bestT, -1);
                return;
            }

            // 打不到：找最近的出手位置，沿路走到本回合能到的最遠處
            Skill main = null;
            foreach (var sk in u.skills) if (sk.kind == SkillKind.Attack && sk.cost == 0) { main = sk; break; }
            if (main == null) return;

            int goal = -1;
            for (int k = 0; k < pf.reach.Count && goal < 0; k++)
            {
                int cell = pf.reach[k];
                int cx = w.X(cell), cy = w.Y(cell), cz = w.Z(cell);
                foreach (var t in b.units)
                {
                    if (!t.alive || t.team == u.team || !t.inSight) continue;
                    if (b.CanHit(main, cx, cy, cz, t.x, t.y, t.z)) { goal = cell; break; }
                }
            }

            if (goal < 0 || !Walk(b, u, goal)) CloseIn(b, u);
            if (u.alive) LookAtNearestKnown(b, u);
        }

        // ───────── 追查 ─────────

        /// <summary>走向最近一處「敵人最後出現的位置」。回傳是否有移動。</summary>
        static bool Investigate(Battle b, Unit u)
        {
            var w = b.world;
            var pf = b.pf;
            pf.Flood(b.CellOf(u), Far, u.Jump, u.team, b.units);

            int goal = -1;
            for (int k = 0; k < pf.reach.Count && goal < 0; k++)
            {
                int cell = pf.reach[k];
                int cx = w.X(cell), cy = w.Y(cell), cz = w.Z(cell);
                foreach (var t in b.units)
                {
                    if (!t.alive || t.team == u.team || t.knownCell < 0) continue;
                    int kc = t.knownCell;
                    if (Math.Max(Math.Abs(w.X(kc) - cx), Math.Abs(w.Y(kc) - cy)) <= 1 && Math.Abs(w.Z(kc) - cz) <= 2) { goal = cell; break; }
                }
            }

            bool moved = goal >= 0 ? Walk(b, u, goal) : CloseIn(b, u);
            if (u.alive) LookAtNearestKnown(b, u);
            return moved;
        }

        // ───────── 駐守 ─────────

        /// <summary>沒有敵蹤：不在駐守位置就走回去，在的話左右張望。回傳是否有移動。</summary>
        static bool Guard(Battle b, Unit u)
        {
            var w = b.world;
            int here = b.CellOf(u);
            if (u.homeCell >= 0 && here != u.homeCell && w.occ[u.homeCell] < 0)
            {
                b.pf.Flood(here, Far, u.Jump, u.team, b.units);
                if (b.pf.Seen(u.homeCell) && Walk(b, u, u.homeCell))
                {
                    if (b.CellOf(u) == u.homeCell) b.LookAt(u, u.x + u.homeFx, u.y + u.homeFy);
                    return true;
                }
                // 跳下山崖之後爬不回去：就地駐守
                if (!b.pf.Seen(u.homeCell)) u.homeCell = here;
            }

            // 以駐守面向為中心，依序看：正面、偏左、正面、偏右
            int hi = 0;
            for (int i = 0; i < 8; i++) if (DirX[i] == u.homeFx && DirY[i] == u.homeFy) { hi = i; break; }
            int d = (hi + ScanStep[u.scan++ & 3] + 8) % 8;
            b.LookAt(u, u.x + DirX[d], u.y + DirY[d]);
            return false;
        }

        // ───────── 推進（僅測試用的我方自動對戰） ─────────

        /// <summary>毫無敵蹤時往敵陣推進，讓自動對戰能把整場打完。回傳是否有移動。</summary>
        static bool Advance(Battle b, Unit u)
        {
            var w = b.world;
            var pf = b.pf;
            pf.Flood(b.CellOf(u), Far, u.Jump, u.team, b.units);
            for (int k = 0; k < pf.reach.Count; k++)
            {
                int cell = pf.reach[k];
                int cx = w.X(cell), cy = w.Y(cell);
                foreach (var t in b.units)
                {
                    if (!t.alive || t.team == u.team) continue;
                    if (Math.Max(Math.Abs(t.x - cx), Math.Abs(t.y - cy)) <= 1) return Walk(b, u, cell);
                }
            }
            return false;
        }

        // ───────── 共用 ─────────

        // 沿最近一次 Flood 的路線往 goal 走，走到本回合能到的最遠處
        static bool Walk(Battle b, Unit u, int goal)
        {
            if (u.moveLeft <= 0) return false;
            var path = b.pf.Path(goal);
            for (int i = path.Length - 1; i > 0; i--)
                if (b.pf.CostOf(path[i]) <= u.moveLeft && b.world.occ[path[i]] < 0) return b.Move(u, path[i]);
            return false;
        }

        // 無路可達（例如被水隔開）：盡量縮短與已知敵蹤的直線距離
        static bool CloseIn(Battle b, Unit u)
        {
            var w = b.world;
            var pf = b.pf;
            int dest = -1;
            float bd = NearestKnown(b, u, u.x, u.y);
            for (int k = 0; k < pf.reach.Count; k++)
            {
                int cell = pf.reach[k];
                if (pf.CostOf(cell) > u.moveLeft) break;
                float d = NearestKnown(b, u, w.X(cell), w.Y(cell));
                if (d < bd) { bd = d; dest = cell; }
            }
            return dest >= 0 && b.Move(u, dest);
        }

        // 與最近一處已知敵蹤的距離
        static float NearestKnown(Battle b, Unit u, int x, int y)
        {
            var w = b.world;
            float best = 99;
            foreach (var t in b.units)
            {
                if (!t.alive || t.team == u.team || t.knownCell < 0) continue;
                float d = Math.Max(Math.Abs(w.X(t.knownCell) - x), Math.Abs(w.Y(t.knownCell) - y));
                if (d < best) best = d;
            }
            return best;
        }

        // 盯著最近一處已知敵蹤
        static void LookAtNearestKnown(Battle b, Unit u)
        {
            var w = b.world;
            int best = -1, bd = int.MaxValue;
            foreach (var t in b.units)
            {
                if (!t.alive || t.team == u.team || t.knownCell < 0) continue;
                int dx = w.X(t.knownCell) - u.x, dy = w.Y(t.knownCell) - u.y, d = dx * dx + dy * dy;
                if (d > 0 && d < bd) { bd = d; best = t.knownCell; }
            }
            if (best >= 0) b.LookAt(u, w.X(best), w.Y(best));
        }
    }
}
