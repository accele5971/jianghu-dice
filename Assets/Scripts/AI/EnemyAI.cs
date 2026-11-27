using System;

namespace Jianghu
{
    /// <summary>
    /// 敵方決策，只依「看得見與記得的事」行動（同陣營共享敵蹤，見 Battle.Observe）：
    /// 　交戰：有人正被己方看著 → 在走得到的格子中找期望傷害最高的出手位置，打不到就逼近。
    /// 　追查：只記得對方最後出現的位置 → 走過去看；到了沒人，或拖了太久，就放棄。
    /// 　駐守：毫無敵蹤 → 回到駐守位置，左右張望。
    /// 測試用的我方自動對戰也走同一套，只是毫無敵蹤時會主動往敵陣推進。
    ///
    /// 敵人依性情（Unit.ai）而有不同打法：
    /// 　機警：挑傷重的打、顧忌借機攻擊；傷到一半會吃藥；會丟霹靂彈（不傷自己人）、撒石灰與鐵蒺藜、踢火油桶、推滾石。
    /// 　魯鈍：只打最近的人、不管借機攻擊；傷到三成才吃藥；霹靂彈要到慌了才亂丟（不管會不會炸到同伴）；不懂利用場景。
    /// 　兇悍：偏好重手、不管借機攻擊、不吃藥；丟霹靂彈與踢火油桶都不太顧同伴。
    /// 路被拒馬擋死時，誰都會動手拆。逃跑中的人（Unit.fleeing，見 Battle.Nerve）只顧往外跑。
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
            if (u.fleeing && Flee(b, u)) return;
            // 眼睛被石灰迷了：身邊有水就先洗（隨手互動，不算出手）
            if (u.team == 1 && u.blind > 0)
                foreach (var p in b.props)
                    if ((p.kind == PropKind.Bucket || p.kind == PropKind.Well) && !p.gone && b.WhyNot(u, p) == null) { b.Interact(u, p); break; }

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
            if (u.team == 1)
            {
                Supplies(b, u);
                if (!u.alive || b.over != 0) return;
                // 機警的人吃了藥就守在原地，不再往前送
                if (!u.actionLeft && u.ai == AiKind.Cunning) { LookAtNearestKnown(b, u); return; }
            }
            pf.Flood(start, 60, u.Jump, u.team, b.units, u.Climb);
            if (u.team == 1 && (Scene(b, u) || Throw(b, u)))
            {
                if (u.alive && b.over == 0) LookAtNearestKnown(b, u);
                return;
            }

            // 魯鈍：眼裡只有最近的那一個
            Unit only = null;
            if (u.team == 1 && u.ai == AiKind.Dull)
            {
                int nd = int.MaxValue;
                foreach (var t in b.units)
                {
                    if (!t.alive || t.team == u.team || !t.inSight) continue;
                    int d = Math.Max(Math.Abs(t.x - u.x), Math.Abs(t.y - u.y));
                    if (d < nd) { nd = d; only = t; }
                }
            }
            bool wary = u.team == 0 || u.ai == AiKind.Cunning;   // 顧忌借機攻擊
            bool fierce = u.team == 1 && u.ai == AiKind.Fierce;

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
                        if (only != null && t != only) continue;
                        if (!b.CanHit(sk, cx, cy, cz, t.x, t.y, t.z)) continue;
                        float p = b.ChanceFrom(u, sk, cx, cy, cz, t); // 含優劣勢與掩蔽
                        float dmg = sk.dmg.Avg + u.Mod(sk.ability);
                        float score = p * dmg * sk.hits;
                        if (only == null && dmg >= t.hp) score += 4;   // 可收頭
                        if (u.team == 1 && u.ai == AiKind.Cunning) score += 2f * (1f - t.hp / (float)t.maxHp); // 挑傷重的
                        if (fierce) score += 0.3f * p * dmg * sk.hits; // 偏好重手
                        if (sk.push > 0) score += 1;
                        if (!sk.melee && !fierce) score += 0.15f * NearestKnown(b, u, cx, cy) + 0.1f * cz;
                        score -= cost * 0.02f;
                        if (score <= best) continue;
                        // 走過去會挨借機攻擊的話，扣掉大約一刀的代價
                        if (wary && cell != start)
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

            if (goal < 0 || !Walk(b, u, goal)) { if (!Breach(b, u)) CloseIn(b, u); }
            if (u.alive) LookAtNearestKnown(b, u);
        }

        // ───────── 道具 ─────────

        static bool Has(Battle b, Unit u, Item it) { return b.BagCount(u, it) > 0; }

        // 吃藥：依性情決定傷到什麼程度才捨得用
        static void Supplies(Battle b, Unit u)
        {
            int healAt = u.ai == AiKind.Cunning ? 50 : u.ai == AiKind.Dull ? 30 : 0;
            if (u.hp * 100 <= healAt * u.maxHp && Has(b, u, Item.Salve) && b.WhyNotUse(u, Item.Salve, u) == null) b.UseItem(u, Item.Salve, u);
            if (u.ai == AiKind.Cunning && u.poison > 0 && Has(b, u, Item.Antidote) && b.WhyNotUse(u, Item.Antidote, u) == null) b.UseItem(u, Item.Antidote, u);
            if (u.ai != AiKind.Dull && Has(b, u, Item.QiPill))
            {
                int need = int.MaxValue;
                foreach (var sk in u.skills) if (sk.kind == SkillKind.Attack && sk.cost > 0 && sk.cost < need) need = sk.cost;
                if (need != int.MaxValue && u.qi < need && b.WhyNotUse(u, Item.QiPill, u) == null) b.UseItem(u, Item.QiPill, u);
            }
        }

        // 丟東西：霹靂彈、石灰、鐵蒺藜。回傳是否出手了
        static bool Throw(Battle b, Unit u)
        {
            if (!u.actionLeft) return false;
            var w = b.world;

            if (Has(b, u, Item.Thunder) && (u.ai != AiKind.Dull || u.hp * 2 <= u.maxHp))
            {
                int bestCell = -1, bestScore = 0;
                foreach (var t in b.units)
                {
                    if (!t.alive || t.team == u.team || !t.inSight) continue;
                    int cell = b.CellOf(t);
                    if (b.InBlast(cell, Item.Thunder.radius, u.x, u.y, u.z) || b.WhyNotUse(u, Item.Thunder, null, cell) != null) continue;
                    int foes = 0, friends = 0;
                    foreach (var x in b.units)
                    {
                        if (!x.alive || !b.InBlast(cell, Item.Thunder.radius, x.x, x.y, x.z)) continue;
                        if (x.team == u.team) friends++; else foes++;
                    }
                    // 機警：絕不炸到自己人，而且要划算（一次炸到兩個，或炸一個還很硬朗的）
                    if (u.ai == AiKind.Cunning && (friends > 0 || (foes < 2 && t.hp < 16))) continue;
                    int score = foes * 2 - friends;
                    if (score > bestScore) { bestScore = score; bestCell = cell; }
                }
                if (bestCell >= 0 && b.UseItem(u, Item.Thunder, null, bestCell)) return true;
            }

            if (u.ai != AiKind.Cunning) return false;

            if (Has(b, u, Item.Lime))
            {
                Unit pick = null;
                int nd = int.MaxValue;
                foreach (var t in b.units)
                {
                    if (!t.alive || t.team == u.team || !t.inSight || t.blind > 0 || b.WhyNotUse(u, Item.Lime, t) != null) continue;
                    int d = Math.Max(Math.Abs(t.x - u.x), Math.Abs(t.y - u.y));
                    if (d < nd) { nd = d; pick = t; }
                }
                if (pick != null && b.UseItem(u, Item.Lime, pick)) return true;
            }

            // 射手：有人逼近到兩三格，就在他來的方向撒一把鐵蒺藜
            if (Has(b, u, Item.Caltrops) && u.skills.Length > 0 && !u.skills[0].melee)
            {
                foreach (var t in b.units)
                {
                    if (!t.alive || t.team == u.team || !t.inSight) continue;
                    int d = Math.Max(Math.Abs(t.x - u.x), Math.Abs(t.y - u.y));
                    if (d < 2 || d > 3) continue;
                    int nx = u.x + Math.Sign(t.x - u.x), ny = u.y + Math.Sign(t.y - u.y);
                    for (int z = u.z + 1; z >= u.z - 1; z--)
                    {
                        if (!w.Standable(nx, ny, z)) continue;
                        int cell = w.Idx(nx, ny, z);
                        if (b.WhyNotUse(u, Item.Caltrops, null, cell) == null && b.UseItem(u, Item.Caltrops, null, cell)) return true;
                        break;
                    }
                }
            }
            return false;
        }

        // ───────── 場景 ─────────

        // 本回合能不能站到 (x, y)、高度與 z 相差 2 層以內的格子上；可以回傳那一格（就在原地也算），不行回傳 -1
        static int StandAt(Battle b, Unit u, int x, int y, int z)
        {
            var w = b.world;
            if (u.x == x && u.y == y && Math.Abs(u.z - z) <= 2) return b.CellOf(u);
            int best = -1, bc = int.MaxValue;
            foreach (int c in b.pf.reach)
            {
                int cost = b.pf.CostOf(c);
                if (cost > u.moveLeft) break;
                if (w.X(c) != x || w.Y(c) != y || Math.Abs(w.Z(c) - z) > 2) continue;
                if (cost < bc) { bc = cost; best = c; }
            }
            return best;
        }

        static bool GoAndUse(Battle b, Unit u, int cell, Prop p)
        {
            if (cell != b.CellOf(u) && !b.Move(u, cell)) return false;
            if (!u.alive || b.over != 0) return true;
            if (b.CellOf(u) == cell) b.Interact(u, p);
            return true;
        }

        /// <summary>利用場景：踢火油桶、推滾石。需要先 Flood。回傳是否做了（並用掉了動作或移動）。</summary>
        static bool Scene(Battle b, Unit u)
        {
            var w = b.world;
            if (u.ai == AiKind.Dull || !u.actionLeft) return false;

            Prop pick = null;
            int pickCell = -1, pickScore = 0;
            foreach (var p in b.props)
            {
                if (p.gone || (p.kind != PropKind.Barrel && !(p.kind == PropKind.Boulder && u.ai == AiKind.Cunning))) continue;
                int bx = w.X(p.cell), by = w.Y(p.cell), bz = w.Z(p.cell);
                for (int k = 0; k < 8; k++)
                {
                    int dx = DirX[k], dy = DirY[k], score;
                    if (p.kind == PropKind.Barrel)
                    {
                        int land = b.BarrelLanding(p, dx, dy);
                        if (land < 0 || b.InBlast(land, 1, bx - dx, by - dy, bz)) continue;
                        int foes = 0, friends = 0;
                        foreach (var x in b.units)
                        {
                            if (!x.alive || x == u || !b.InBlast(land, 1, x.x, x.y, x.z)) continue;
                            if (x.team == u.team) friends++; else if (x.inSight) foes++;
                        }
                        if (foes == 0 || (u.ai == AiKind.Cunning ? friends > 0 : friends > foes)) continue;
                        score = foes * 3 - friends;
                    }
                    else
                    {
                        var hit = b.BoulderVictim(p, dx, dy);
                        if (hit == null || hit.team == u.team || !hit.inSight) continue;
                        score = 2;
                    }
                    if (score <= pickScore) continue;
                    int cell = StandAt(b, u, bx - dx, by - dy, bz);
                    if (cell < 0) continue;
                    pickScore = score; pick = p; pickCell = cell;
                }
            }
            return pick != null && GoAndUse(b, u, pickCell, pick);
        }

        /// <summary>路被拒馬擋死：走到旁邊動手拆。需要先 Flood。回傳是否有動作。</summary>
        static bool Breach(Battle b, Unit u)
        {
            var w = b.world;
            float here = NearestKnown(b, u, u.x, u.y);
            foreach (var p in b.props)
            {
                if (p.kind != PropKind.Barricade || p.gone) continue;
                int px = w.X(p.cell), py = w.Y(p.cell), pz = w.Z(p.cell);
                if (NearestKnown(b, u, px, py) >= here) continue; // 要擋在去路上才拆
                if (b.Beside(u, p)) return u.actionLeft && b.Interact(u, p);
                int best = -1, bc = int.MaxValue;
                foreach (int c in b.pf.reach)
                {
                    int cost = b.pf.CostOf(c);
                    if (cost > u.moveLeft) break;
                    if (Math.Max(Math.Abs(w.X(c) - px), Math.Abs(w.Y(c) - py)) > 1 || Math.Abs(w.Z(c) - pz) > 2) continue;
                    if (cost < bc) { bc = cost; best = c; }
                }
                if (best < 0) continue;
                if (!b.Move(u, best)) continue;
                if (u.alive && b.over == 0 && u.actionLeft && b.Beside(u, p)) b.Interact(u, p);
                return true;
            }
            return false;
        }

        // ───────── 逃跑 ─────────

        /// <summary>
        /// 逃跑：把動作也拿來跑（兩倍移動），往離敵人最遠、最好是地圖邊緣的地方去；
        /// 跑到沒人看得見或到了邊緣就離開戰場。無路可逃就回頭拚命（回傳 false，照常行動）。
        /// </summary>
        static bool Flee(Battle b, Unit u)
        {
            if (b.Escape(u)) return true;
            var w = b.world;
            var pf = b.pf;
            int budget = u.move * 2;
            pf.Flood(b.CellOf(u), budget, u.Jump, u.team, b.units, u.Climb);
            float best = NearestKnown(b, u, u.x, u.y) + 0.5f;
            int dest = -1;
            foreach (int c in pf.reach)
            {
                int x = w.X(c), y = w.Y(c);
                float s = NearestKnown(b, u, x, y);
                if (x == 0 || y == 0 || x == w.W - 1 || y == w.D - 1) s += 4;
                if (s > best) { best = s; dest = c; }
            }
            if (dest < 0) { b.StandAndFight(u); return false; }
            u.moveLeft = budget;
            u.actionLeft = false;
            b.Move(u, dest);
            if (u.alive && b.over == 0) b.Escape(u);
            return true;
        }

        // ───────── 追查 ─────────

        /// <summary>走向最近一處「敵人最後出現的位置」。回傳是否有移動。</summary>
        static bool Investigate(Battle b, Unit u)
        {
            var w = b.world;
            var pf = b.pf;
            pf.Flood(b.CellOf(u), Far, u.Jump, u.team, b.units, u.Climb);

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

            bool moved = goal >= 0 ? Walk(b, u, goal) : Breach(b, u) || CloseIn(b, u);
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
                b.pf.Flood(here, Far, u.Jump, u.team, b.units, u.Climb);
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
            pf.Flood(b.CellOf(u), Far, u.Jump, u.team, b.units, u.Climb);
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
