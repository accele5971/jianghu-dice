using System;
using System.Collections.Generic;

namespace Jianghu
{
    public enum EvType { Log, TurnStart, Move, Attack, Miss, Damage, Heal, Push, Death, Aoe, Buff, Wait, GameOver, Face, Alert,
                         Prop,    // 地圖物件有變化：prop；path 非空表示滾石沿這些格子滾動
                         Flee,    // b 投降或潰逃，離開戰場
                         Reveal,  // 揭開迷霧：path 是直行索引（x + y × W）
                         Gear,    // a 換了裝備：重建他的模型
                         Throw,   // a 把東西擲向 cell（text 是道具名）
                         Blast,   // 爆炸：cell 是中心，n 是半徑
                         Combat,  // 自由行動結束，擲先攻進入回合制
                         Smoke }  // 濃煙出現或散去：n 是編號，path 是籠罩的格子，flag 為 true 表示出現

    /// <summary>規則層輸出給畫面層播放的事件。</summary>
    public sealed class Ev
    {
        public EvType type;
        public Unit a, b;
        public Skill skill;
        public Prop prop;
        public Item item;    // Throw、Aoe：丟出去的是哪一種道具（畫面用它的模型當飛行物）
        public int[] path;   // Move / Push：格子序列（含起點）
        public int n;        // 數值：傷害、回復量、等待毫秒、回合數、半徑
        public int hp;       // 事件後的氣血（畫面顯示用）
        public int cell;     // Aoe 中心
        public bool flag;    // Damage：是否會心；GameOver：是否勝利
        public string text;
    }

    /// <summary>
    /// 戰鬥規則。所有方法同步結算並把過程寫入事件串，
    /// 由畫面層取走（Drain）後依序播放。不引用 UnityEngine。
    /// </summary>
    public sealed partial class Battle
    {
        public readonly VoxelWorld world;
        public readonly Pathfinder pf;
        public readonly List<Unit> units = new List<Unit>();
        public readonly List<Unit> order = new List<Unit>();

        public Unit active;
        public int round;
        public int over;        // 0 進行中、1 勝、-1 敗、-2 逾時（僅測試）
        public bool autoHeroes; // 測試用：我方也交給 AI
        public int maxRounds = 0;

        int turn = -1;
        List<Ev> evs = new List<Ev>();

        public Battle(VoxelWorld w)
        {
            world = w;
            pf = new Pathfinder(w);
            vision = new Vision(w);
        }

        // ───────── 事件 ─────────

        public List<Ev> Drain()
        {
            var e = evs;
            evs = new List<Ev>();
            return e;
        }

        void Emit(Ev e) { evs.Add(e); }
        void Log(string s) { evs.Add(new Ev { type = EvType.Log, text = s }); }

        public static string N(Unit u)
        {
            return (u.team == 0 ? "<color=#8FC1EE>" : "<color=#F0958A>") + u.name + "</color>";
        }

        // ───────── 佈陣與回合 ─────────

        public Unit AddUnit(UnitTemplate t, int team, int cell, string name = null)
        {
            var u = new Unit
            {
                id = units.Count, name = name ?? t.name, cls = t.cls, team = team, look = t.look != null ? t.look.Clone() : null,
                stats = (int[])t.stats.Clone(), hp = t.hp, maxHp = t.hp, qi = t.qi, maxQi = t.qi,
                baseMove = t.move, baseJump = t.jump, arts = t.skills ?? new Skill[0],
                x = world.X(cell), y = world.Y(cell), z = world.Z(cell),
                // 開局雙方面對面：我方在西南、敵方在東北
                fx = team == 0 ? 1 : -1, fy = team == 0 ? -1 : 1,
                homeCell = cell, sneak = t.sneak, talents = t.talents, boss = t.boss,
            };
            u.homeFx = u.fx; u.homeFy = u.fy;
            if (u.look != null) u.baseOutfit = u.look.outfit;
            if (t.gear != null)
                foreach (var g in t.gear)
                {
                    u.gear[(int)g.slot] = g;
                    // 開場的樣子就照身上的裝備：手上的兵器、頭盔、鎧甲
                    if (g.slot == Slot.MainHand || g.slot == Slot.Head || (g.slot == Slot.Upper && g.outfit >= 0)) ApplyLook(u, g.slot, g);
                }
            u.RefreshGear();
            if (t.items != null) foreach (var it in t.items) Give(u, it);
            units.Add(u);
            world.occ[cell] = (short)u.id;
            return u;
        }

        public int CellOf(Unit u) { return world.Idx(u.x, u.y, u.z); }

        public void Start()
        {
            started = true;
            foesAtStart = 0;
            foreach (var u in units) if (u.team == 1) foesAtStart++;
            foreach (var u in units) RollStealth(u);
            UpdateVision();
            // 自由行動：還沒被發現就不擲先攻（測試用的全自動對戰一律直接開打）
            if (explore && !autoHeroes) { BeginExplore(); return; }
            RollInitiative();
            Observe();
            Advance();
        }

        void RollInitiative()
        {
            foreach (var u in units) u.init = Dice.Die(20) + u.Mod(Stat.Dex);
            order.Clear();
            order.AddRange(units);
            order.Sort((p, q) => q.init != p.init ? q.init.CompareTo(p.init) : p.id.CompareTo(q.id));
            var sb = new System.Text.StringBuilder("先攻：");
            for (int i = 0; i < order.Count; i++) sb.Append(i > 0 ? "、" : "").Append(N(order[i])).Append(' ').Append(order[i].init);
            Log(sb.ToString());
        }

        // 潛行擲骰：每個人在自己的回合開始時擲一次，用到下個回合；重甲有劣勢
        void RollStealth(Unit u)
        {
            int r1, r2;
            u.stealth = Dice.D20(u.Clanky ? -1 : 0, out r1, out r2) + u.StealthBonus;
            u.stealthTold = false;
        }

        /// <summary>結束回合。自由行動時是「原地等候片刻」：時間過一輪。</summary>
        public void EndTurn()
        {
            if (over != 0) return;
            if (exploring) { exploreClock += TickCost; AfterAction(); return; }
            Advance();
        }

        /// <summary>推進到下一個需要玩家操作的回合；途中的敵方回合直接結算。</summary>
        void Advance()
        {
            for (int safety = 0; over == 0 && safety < 100000; safety++)
            {
                turn = (turn + 1) % order.Count;
                if (turn == 0)
                {
                    round++;
                    clock++;
                    if (maxRounds > 0 && round > maxRounds) { over = -2; return; }
                }
                var u = order[turn];
                if (!u.alive) continue;

                active = u;
                u.moveLeft = u.move;
                u.actionLeft = u.bonusLeft = true;
                u.guard = false;
                u.light = false;
                u.reactionLeft = true;
                u.freeLeft = true;
                if (u.fright > 0) u.fright--;
                if (u.blind > 0) u.blind--;
                if (u.tangle > 0) u.tangle--;
                if (u.bless > 0) u.bless--;
                ExpireSmoke();
                RollStealth(u);
                Emit(new Ev { type = EvType.TurnStart, a = u, n = round, text = StatusText(u) });
                if (!TickWounds(u)) continue; // 流血或毒發身亡

                if (u.surprised)
                {
                    u.surprised = false;
                    if (u.stun) u.stun = false; // 同時被定身也只耽誤這一回合
                    Log(N(u) + " 措手不及，這一回合來不及反應。");
                    Emit(new Ev { type = EvType.Wait, n = 600 });
                    continue;
                }

                if (u.stun)
                {
                    u.stun = false;
                    Log(N(u) + " 穴道受制，動彈不得。");
                    Emit(new Ev { type = EvType.Wait, n = 600 });
                    continue;
                }
                if (u.team == 0 && !autoHeroes) return;

                Emit(new Ev { type = EvType.Wait, n = 300 });
                Observe();
                EnemyAI.Take(this, u);
                Emit(new Ev { type = EvType.Wait, n = 250 });
            }
        }

        // ───────── 面向與視野 ─────────

        /// <summary>我方目前的視野與已探索範圍（迷霧）。</summary>
        public readonly Vision vision;

        // 把任意方向收成八方位
        static void SetFacing(Unit u, int dx, int dy)
        {
            if (dx == 0 && dy == 0) return;
            int ax = Math.Abs(dx), ay = Math.Abs(dy);
            if (ax > 2 * ay) { u.fx = Math.Sign(dx); u.fy = 0; }
            else if (ay > 2 * ax) { u.fx = 0; u.fy = Math.Sign(dy); }
            else { u.fx = Math.Sign(dx); u.fy = Math.Sign(dy); }
        }

        /// <summary>原地轉向（不耗任何行動）。</summary>
        public void Face(Unit u, int tx, int ty)
        {
            SetFacing(u, tx - u.x, ty - u.y);
            if (u.team == 0) UpdateVision();
            Observe();
            AfterAction();
        }

        // 轉身面對某人；方向真的變了才通知畫面
        void TurnToward(Unit u, Unit other)
        {
            int fx = u.fx, fy = u.fy;
            SetFacing(u, other.x - u.x, other.y - u.y);
            if (fx == u.fx && fy == u.fy) return;
            if (u.team == 0) UpdateVision();
            Emit(new Ev { type = EvType.Face, a = u, cell = CellOf(other) });
            Observe();
        }

        /// <summary>AI 用：轉頭看向某一格（張望、盯著敵蹤消失的地方）；會通知畫面。</summary>
        public void LookAt(Unit u, int tx, int ty)
        {
            if (tx < 0 || ty < 0 || tx >= world.W || ty >= world.D) return;
            int fx = u.fx, fy = u.fy;
            SetFacing(u, tx - u.x, ty - u.y);
            if (fx == u.fx && fy == u.fy) return;
            if (u.team == 0) UpdateVision();
            Emit(new Ev { type = EvType.Face, a = u, cell = world.Idx(tx, ty, u.z) });
            Observe();
        }

        // ───────── 敵蹤（雙方對彼此位置的掌握） ─────────

        /// <summary>行蹤不明多少輪之後放棄追查。</summary>
        public const int MemoryRounds = 6;

        /// <summary>敵方發現我方的次數（測試與統計用）。</summary>
        public int alerts;

        // 站在 (x,y,z) 的 t 是否會被對方陣營的某人看見
        bool SeenByFoe(Unit t, int x, int y, int z, out Unit by)
        {
            foreach (var v in units)
                if (v.alive && v.team != t.team && Notices(v, t, x, y, z)) { by = v; return true; }
            by = null;
            return false;
        }

        /// <summary>正前方這個距離以內一定會被看見，潛行無效。</summary>
        public const int PlainSight = 4;

        /// <summary>對方陣營是否已有人的行蹤被 team 這一方掌握（全員提高警覺，警覺 +5）。</summary>
        public bool OnAlert(int team)
        {
            foreach (var x in units) if (x.alive && x.team != team && x.knownCell >= 0) return true;
            return false;
        }

        /// <summary>v 的有效警覺值（被動警覺；已提高警覺時 +5）。</summary>
        public int Perception(Unit v) { return v.Passive + (OnAlert(v.team) ? 5 : 0); }

        /// <summary>
        /// v 是否察覺站在 (x,y,z) 的 t。先看幾何上看不看得到（扇形、餘光、視線）；
        /// 看得到的話，行蹤尚未敗露的我方還能靠潛行躲過：潛行擲骰須高於對方的警覺。
        /// 正前方 PlainSight 格內一定被看見；已經被盯上的人無法憑潛行脫身，得先脫離視線。
        /// 敵方不潛行（玩家的視野與迷霧只看幾何）。
        /// </summary>
        bool Notices(Unit v, Unit t, int x, int y, int z)
        {
            if (!vision.CanSeeCell(v.x, v.y, v.z, v.fx, v.fy, x, y, z)) return false;
            int d = Math.Max(Math.Abs(x - v.x), Math.Abs(y - v.y));
            if (v.blind > 0 && d > 1) return false; // 目盲：只察覺得到身邊的人
            if (t.team != 0 || t.knownCell >= 0) return true;
            // 藏身芒草叢：除非貼到身邊，否則正面也看不清；潛行 +5
            bool grass = GrassAt(world.Idx(x, y, z));
            if (d <= (grass ? 1 : PlainSight) && Vision.InFront(v.x, v.y, v.fx, v.fy, x, y)) return true;
            int per = Perception(v), st = t.stealth + (grass ? 5 : 0) - (t.ran ? 5 : 0);
            string roll = t.stealth + (grass ? "+5（芒草）" : "") + (t.ran ? "−5（奔跑）" : "");
            if (st > per)
            {
                if (!t.stealthTold)
                {
                    t.stealthTold = true;
                    Log("<color=#9FE0C0>" + N(t) + " 潛行 " + roll + " 對 " + N(v) + " 警覺 " + per + "，未被察覺。</color>");
                    Emit(new Ev { type = EvType.Buff, a = t, text = "未被察覺" });
                }
                return false;
            }
            Log(N(t) + " 潛行 " + roll + " 對 " + N(v) + " 警覺 " + per + "，藏不住了。");
            return true;
        }

        // 對方得知 t 在 cell：親眼看見，或是挨了他一招
        void Learn(Unit t, int cell, Unit by, bool sight)
        {
            bool fresh = t.knownCell < 0;
            t.knownCell = cell;
            t.knownAt = round;
            if (sight) t.inSight = true;
            if (!fresh || t.team != 0) return;
            if (exploring) combatPending = true; // 行蹤敗露：自由行動到此為止
            alerts++;
            Log("<color=#FF9C6A>" + N(t) + " 的行蹤被" + (by != null ? " " + N(by) + " " : "敵人") + "發現了！</color>");
            Emit(new Ev { type = EvType.Alert, a = by, b = t });
        }

        /// <summary>對方追丟了 t。</summary>
        public void Forget(Unit t)
        {
            if (t.knownCell < 0) return;
            t.knownCell = -1;
            t.inSight = false;
            if (t.team == 0 && t.alive) Log("<color=#A89C85>敵人失去了 " + N(t) + " 的蹤跡。</color>");
        }

        /// <summary>
        /// 重新判定雙方此刻誰看得見誰。看得見的人，位置即時更新；
        /// 看不見的人保留「最後一次確知的位置」，由 AI 決定何時放棄追查。
        /// 任何會改變位置或面向的動作之後都要呼叫。
        /// </summary>
        public void Observe()
        {
            if (over != 0) return;
            foreach (var t in units)
            {
                Unit by;
                if (!t.alive) { t.inSight = false; continue; }
                if (SeenByFoe(t, t.x, t.y, t.z, out by)) Learn(t, CellOf(t), by, true);
                else t.inSight = false;
            }
            DetectTraps();
        }

        public void UpdateVision()
        {
            vision.BeginUpdate();
            foreach (var h in units)
                if (h.alive && h.team == 0) vision.AddViewer(h.x, h.y, h.z, h.fx, h.fy);
        }

        /// <summary>我方此刻是否有人看得見這個單位。</summary>
        public bool CanSee(Unit t)
        {
            if (t.team == 0) return true;
            foreach (var h in units)
                if (h.alive && h.team == 0 && vision.CanSeeCell(h.x, h.y, h.z, h.fx, h.fy, t.x, t.y, t.z)) return true;
            return false;
        }

        // 攻擊者是否在目標的正後方（背後左右各 45 度）
        static bool Behind(int ax, int ay, Unit t)
        {
            int dx = ax - t.x, dy = ay - t.y, d2 = dx * dx + dy * dy, f2 = t.fx * t.fx + t.fy * t.fy;
            if (d2 == 0 || f2 == 0) return false;
            int dot = dx * t.fx + dy * t.fy;
            return dot < 0 && 2L * dot * dot >= (long)d2 * f2;
        }

        /// <summary>
        /// 自動尋路：算出到 dest 的完整路線，回傳本回合能走到的最遠一格（之後可直接 Move）。
        /// total 為全程所需的移動點數；dest 根本走不到時回傳 -1 且 total 為 -1。
        /// </summary>
        public int AutoMoveCell(Unit u, int dest, out int total)
        {
            total = -1;
            if (dest < 0 || world.occ[dest] >= 0) return -1;
            pf.Flood(CellOf(u), 120, u.Jump, u.team, units);
            if (!pf.Seen(dest)) return -1;
            total = pf.CostOf(dest);
            var path = pf.Path(dest);
            for (int i = path.Length - 1; i > 0; i--)
                if (pf.CostOf(path[i]) <= u.moveLeft && world.occ[path[i]] < 0) return path[i];
            return -1;
        }

        // ───────── 移動 ─────────

        /// <summary>計算 u 的可移動範圍，結果在 pf.reach。</summary>
        public void ComputeReach(Unit u)
        {
            pf.Flood(CellOf(u), Math.Max(0, u.moveLeft), u.Jump, u.team, units);
        }

        /// <summary>沿最近一次 Flood 的結果移動。</summary>
        public bool Move(Unit u, int dest)
        {
            int c = pf.CostOf(dest);
            if (c <= 0 || c > u.moveLeft || world.occ[dest] >= 0) return false;
            var path = pf.Path(dest);
            if (path[0] != CellOf(u)) return false;
            var costs = new int[path.Length];
            for (int i = 0; i < path.Length; i++) costs[i] = pf.CostOf(path[i]);
            return Travel(u, path, costs);
        }

        /// <summary>
        /// 明確的跳躍：往 (dx, dy) 方向能躍到的最遠落點（不管走不走得過去）；跳不了回傳 -1。
        /// cost 是要花的移動力。
        /// </summary>
        public int LeapCell(Unit u, int dx, int dy, out int cost)
        {
            cost = 0;
            int best = -1, range = Pathfinder.LeapRange(u.Jump);
            if (dx == 0 && dy == 0) return -1;
            for (int dist = 2; dist <= range; dist++)
            {
                bool stop; int c;
                int cell = pf.LeapLanding(u.x, u.y, u.z, dx, dy, dist, u.Jump, u.team, units, out stop, out c);
                if (stop) break;
                if (cell >= 0 && world.occ[cell] < 0 && c <= u.moveLeft) { best = cell; cost = c; }
            }
            return best;
        }

        /// <summary>往 (dx, dy) 方向縱躍到最遠的落點。借機攻擊、絆索、鐵蒺藜照常結算。</summary>
        public bool Leap(Unit u, int dx, int dy)
        {
            int cost;
            int cell = LeapCell(u, dx, dy, out cost);
            if (cell < 0 || over != 0 || !u.alive) return false;
            return Travel(u, new[] { CellOf(u), cell }, new[] { 0, cost });
        }

        // 沿著 path 走（或跳）：途中的借機攻擊、絆鈴索、鐵蒺藜、被發現都在這裡結算
        bool Travel(Unit u, int[] path, int[] costs)
        {
            u.ran = exploring && running && u.team == 0; // 自由行動時跑步：快，但容易被發現
            world.occ[path[0]] = -1;
            int from = 0; // 還沒通知畫面的那一段路從哪裡開始
            int end = path.Length - 1; // 實際走到 path 的第幾格（絆索可能讓人半路停下）
            Prop sprung = null, stepped = null;
            for (int i = 0; i + 1 < path.Length; i++)
            {
                // 借機攻擊：從 path[i] 踏出某個敵人的近身範圍，他可以用反應砍一刀
                // （梯雲縱：身法太快，來不及出手）
                if (!u.light) foreach (var f in units)
                {
                    if (!Provokes(u, f, path[i], path[i + 1])) continue;
                    Place(u, path[i]);
                    if (i > from)
                    {
                        SetFacing(u, u.x - world.X(path[i - 1]), u.y - world.Y(path[i - 1]));
                        Emit(new Ev { type = EvType.Move, a = u, path = Slice(path, from, i) });
                        from = i;
                    }
                    int held = world.occ[path[i]]; // 可能正穿過隊友所站的格子
                    f.reactionLeft = false;
                    Log(N(f) + " 趁 " + N(u) + " 抽身之際出手！");
                    Emit(new Ev { type = EvType.Buff, a = f, text = "借機攻擊" });
                    Attack(f, OaSkill(f), u);
                    world.occ[path[i]] = (short)held;
                    if (!u.alive || over != 0)
                    {
                        // 倒在半路上：只扣走到這裡的移動力
                        u.moveLeft -= costs[i];
                        return true;
                    }
                }

                // 鐵蒺藜：我方撒的，只有敵人會踩到；踩到就停在那一格
                var spikes = u.team == 1 ? SpikesAt(path[i + 1]) : null;
                if (spikes != null && world.occ[path[i + 1]] < 0) { end = i + 1; stepped = spikes; break; }

                // 絆鈴索：只有我方會踩到（敵人知道自己埋在哪）
                var trap = u.team == 0 ? TrapAt(path[i + 1]) : null;
                if (trap == null) continue;
                Place(u, path[i]);
                if (!trap.found && Spots(u, trap)) RevealTrap(trap, u);
                if (trap.found) { end = i; break; }           // 看見了：在它前面停下
                if (world.occ[path[i + 1]] >= 0) continue;
                end = i + 1; sprung = trap;                   // 沒看見：一腳踩上去
                break;
            }

            // 半路停下時不能停在隊友身上，往回退到空格
            while (end > 0 && world.occ[path[end]] >= 0) { end--; sprung = null; stepped = null; }
            Place(u, path[end]);
            world.occ[path[end]] = (short)u.id;
            u.moveLeft -= costs[end];
            if (end > 0) SetFacing(u, u.x - world.X(path[end - 1]), u.y - world.Y(path[end - 1]));
            if (u.team == 0) UpdateVision();
            if (end > from) Emit(new Ev { type = EvType.Move, a = u, path = from == 0 && end == path.Length - 1 ? path : Slice(path, from, end) });
            // 途中經過對方的視野也會被看見，對方記得的是最後看到他的那一格
            for (int i = 1; i < end; i++)
            {
                Unit by;
                if (SeenByFoe(u, world.X(path[i]), world.Y(path[i]), world.Z(path[i]), out by)) Learn(u, path[i], by, false);
            }
            if (sprung != null) SpringTrap(u, sprung);
            if (stepped != null) StepOnSpikes(u, stepped);
            if (u.alive && u.team == 0) { PickUp(u, LootAt(CellOf(u))); Gather(u, HerbAt(CellOf(u))); }
            Observe();
            AfterMove(u, costs[end]);
            AfterAction();
            return true;
        }

        // ───────── 借機攻擊 ─────────

        void Place(Unit u, int cell) { u.x = world.X(cell); u.y = world.Y(cell); u.z = world.Z(cell); }

        static int[] Slice(int[] a, int lo, int hi)
        {
            var r = new int[hi - lo + 1];
            Array.Copy(a, lo, r, 0, r.Length);
            return r;
        }

        // 借機攻擊用的招式：不耗內力、不擊退、不定身的近身招
        static Skill OaSkill(Unit f)
        {
            foreach (var sk in f.skills)
                if (sk.kind == SkillKind.Attack && sk.melee && sk.cost == 0 && sk.push == 0 && !sk.stun) return sk;
            return null;
        }

        bool Near(Unit f, int cell)
        {
            return Math.Max(Math.Abs(world.X(cell) - f.x), Math.Abs(world.Y(cell) - f.y)) == 1 && Math.Abs(world.Z(cell) - f.z) <= 2;
        }

        // u 從 a 格踏到 b 格，會不會引來 f 的借機攻擊
        bool Provokes(Unit u, Unit f, int a, int b)
        {
            if (!f.alive || f.team == u.team || !f.reactionLeft || f.stun || f.surprised || OaSkill(f) == null) return false;
            if (!Near(f, a) || Near(f, b)) return false;
            // 得看得見才砍得到：背後有樹或牆擋著就沒辦法
            return vision.CanSeeCell(f.x, f.y, f.z, f.fx, f.fy, world.X(a), world.Y(a), world.Z(a));
        }

        /// <summary>
        /// 沿 path 走會遭哪些敵人借機攻擊（預覽與 AI 評估用，不改變任何狀態）。
        /// 只算走的人此刻知道的敵人。結果加進 list，回傳人數。
        /// </summary>
        public int Provokers(Unit u, int[] path, List<Unit> list)
        {
            int n = 0;
            if (u.light) return 0;
            foreach (var f in units)
            {
                if (!f.alive || f.team == u.team) continue;
                if (u.team == 0 ? !CanSee(f) : !f.inSight) continue;
                for (int i = 0; i + 1 < path.Length; i++)
                {
                    if (!Provokes(u, f, path[i], path[i + 1])) continue;
                    n++;
                    if (list != null) list.Add(f);
                    break;
                }
            }
            return n;
        }

        // ───────── 判定 ─────────

        public int AtkBonus(Unit u, Skill sk) { return u.Mod(sk.ability) + Db.Prof - (Mismatch(u, sk) ? 2 : 0) + (u.bless > 0 ? 2 : 0); }

        /// <summary>這一招與手上的兵器不合（空手視為拳腳）。寬鬆的招式仍可使用，但命中與傷害各 −2。</summary>
        public bool Mismatch(Unit u, Skill sk) { return sk.needs != WeaponClass.None && sk.needs != u.WeaponClass; }

        public bool CanUse(Unit u, Skill sk)
        {
            if (sk.strict && Mismatch(u, sk)) return false; // 嚴格的招式：兵器不合就使不出來
            return over == 0 && u.alive && u.qi >= sk.cost && (sk.bonus ? u.bonusLeft : u.actionLeft);
        }

        /// <summary>從 (ax,ay,az) 用 sk 能否打到站在 (tx,ty,tz) 的目標。</summary>
        public bool CanHit(Skill sk, int ax, int ay, int az, int tx, int ty, int tz)
        {
            int d = Math.Max(Math.Abs(tx - ax), Math.Abs(ty - ay));
            int dz = az - tz;
            if (sk.melee)
            {
                if (d < 1 || d > sk.reach || Math.Abs(dz) > 2) return false;
                // 長兵與軟兵隔著一格以上出手：中間不能有東西擋著
                return d == 1 || VoxelRay.Los(world, ax, ay, az, tx, ty, tz);
            }
            // 居高射得遠，仰射射程縮短
            int rng = sk.range + Math.Max(-2, Math.Min(3, dz / 2));
            return d <= rng && (d > 0 || dz != 0) && VoxelRay.Los(world, ax, ay, az, tx, ty, tz);
        }

        public bool CanAim(Unit u, Skill sk, int cell)
        {
            if (cell < 0) return false;
            int x = world.X(cell), y = world.Y(cell), z = world.Z(cell);
            return world.Standable(x, y, z) && CanHit(sk, u.x, u.y, u.z, x, y, z);
        }

        public bool InBlast(int cell, int radius, int x, int y, int z)
        {
            return Math.Max(Math.Abs(x - world.X(cell)), Math.Abs(y - world.Y(cell))) <= radius
                && Math.Abs(z - world.Z(cell)) <= 2;
        }

        bool FoeAdjacent(Unit u, int x, int y, int z)
        {
            foreach (var f in units)
                if (f.alive && f.team != u.team && Math.Abs(f.x - x) <= 1 && Math.Abs(f.y - y) <= 1 && Math.Abs(f.z - z) <= 1)
                    return true;
            return false;
        }

        /// <summary>優劣勢：&gt;0 優勢、&lt;0 劣勢。</summary>
        public int AdvMode(Unit u, Skill sk, int ax, int ay, int az, Unit t)
        {
            int m = 0, dz = az - t.z;
            if (dz >= 2) m++;                                   // 居高臨下
            if (t.stun) m++;                                    // 目標定身
            if (Behind(ax, ay, t)) m++;                         // 背後偷襲
            if (Unseen(u, t)) m++;                              // 出其不意
            if (u.fright > 0) m--;                              // 膽寒
            if (u.tangle > 0) m--;                              // 被軟劍纏住
            if (u.blind > 0) m--;                               // 目盲
            if (t.blind > 0) m++;                               // 目標目盲
            if (dz <= -2) m--;                                  // 低處仰攻（近戰、遠程皆同）
            if (!sk.melee && FoeAdjacent(u, ax, ay, az)) m--;   // 貼身放遠程
            return Math.Sign(m);
        }

        /// <summary>優劣勢的來源說明（給提示框用），每項一行。</summary>
        public string AdvReasons(Unit u, Skill sk, int ax, int ay, int az, Unit t)
        {
            string s = "";
            int dz = az - t.z;
            if (dz >= 2) s += "居高臨下（優勢）\n";
            if (t.stun) s += "目標定身（優勢）\n";
            if (Behind(ax, ay, t)) s += "背後偷襲（優勢）\n";
            if (Unseen(u, t)) s += Surprises(u, t) ? "出其不意（優勢，對方措手不及）\n" : "出其不意（優勢）\n";
            if (u.fright > 0) s += "膽寒（劣勢）\n";
            if (u.tangle > 0) s += "被纏住（劣勢）\n";
            if (u.blind > 0) s += "目盲（劣勢）\n";
            if (t.blind > 0) s += "目標目盲（優勢）\n";
            if (dz <= -2) s += "低處仰攻（劣勢）\n";
            if (!sk.melee && FoeAdjacent(u, ax, ay, az)) s += "敵人貼身（劣勢）\n";
            if (Mismatch(u, sk)) s += "兵器不合（命中 −2、傷害 −2）\n";
            if (u.bless > 0) s += "神龕祈福（命中 +2）\n";
            int cover = CoverBonus(sk, ax, ay, az, t.x, t.y, t.z);
            if (cover > 0) s += (cover >= 5 ? "大半掩蔽" : "半掩蔽") + "（防禦 +" + cover + "）\n";
            return s.TrimEnd('\n');
        }

        /// <summary>出手的人行蹤尚未被對方掌握：這一擊出其不意。</summary>
        public bool Unseen(Unit u, Unit t) { return u.knownCell < 0; }

        /// <summary>
        /// 突襲：出手的人行蹤未露，而且對方全隊還不知道有任何敵人（毫無戒備），
        /// 被打的人才會措手不及；每人每場只會發生一次。已經開打之後的冷箭只有優勢。
        /// </summary>
        public bool Surprises(Unit u, Unit t) { return Unseen(u, t) && !t.ambushed && !OnAlert(t.team); }

        /// <summary>
        /// 掩蔽：遠程攻擊時，目標身體被地形擋住一部分就比較難射中。
        /// 擋住約一半 → 防禦 +2；擋住大半 → 防禦 +5。近戰不受影響。
        /// </summary>
        public int CoverBonus(Skill sk, int ax, int ay, int az, int tx, int ty, int tz)
        {
            if (sk.melee) return 0;
            int n = VoxelRay.Blocked(world, ax, ay, az, tx, ty, tz);
            return n >= 4 ? 5 : n >= 2 ? 2 : 0;
        }

        /// <summary>從 (ax,ay,az) 用 sk 打 t 的命中率（含優劣勢與掩蔽）。</summary>
        public float ChanceFrom(Unit u, Skill sk, int ax, int ay, int az, Unit t)
        {
            return Dice.HitChance(AtkBonus(u, sk), t.Ac + CoverBonus(sk, ax, ay, az, t.x, t.y, t.z), AdvMode(u, sk, ax, ay, az, t));
        }

        public float ChanceVs(Unit u, Skill sk, Unit t) { return ChanceFrom(u, sk, u.x, u.y, u.z, t); }

        // ───────── 出招 ─────────

        public bool UseSkill(Unit u, Skill sk, Unit target, int cell)
        {
            if (!UseSkillCore(u, sk, target, cell)) return false;
            // 自由行動時先動手：打完這一下就擲先攻
            if (exploring && (sk.kind == SkillKind.Attack || sk.kind == SkillKind.Aoe || sk.kind == SkillKind.Shout)) combatPending = true;
            AfterAction();
            return true;
        }

        bool UseSkillCore(Unit u, Skill sk, Unit target, int cell)
        {
            if (!CanUse(u, sk)) return false;
            switch (sk.kind)
            {
                case SkillKind.Attack:
                    if (target == null || !target.alive || target.team == u.team) return false;
                    if (u.team == 0 && !autoHeroes && !CanSee(target)) return false; // 看不見的敵人不能指定
                    if (!CanHit(sk, u.x, u.y, u.z, target.x, target.y, target.z)) return false;
                    Spend(u, sk);
                    for (int h = 0; h < sk.hits && target.alive && over == 0; h++)
                    {
                        int tx = target.x, ty = target.y;
                        int dealt = Attack(u, sk, target);
                        if (dealt > 0 && sk.sweep != Sweep.Single && over == 0 && u.alive) Splash(u, sk, target, tx, ty, dealt);
                    }
                    return true;

                case SkillKind.Aoe:
                {
                    if (!CanAim(u, sk, cell)) return false;
                    Spend(u, sk);
                    Log(N(u) + " 施展「" + sk.name + "」！");
                    Emit(new Ev { type = EvType.Aoe, a = u, cell = cell, n = sk.radius });
                    int dc = 8 + Db.Prof + u.Mod(sk.ability);
                    var hitList = new List<Unit>();
                    foreach (var t in units)
                        if (t.alive && t.team != u.team && InBlast(cell, sk.radius, t.x, t.y, t.z)) hitList.Add(t);
                    foreach (var t in hitList)
                    {
                        if (!t.alive) continue;
                        int sv = Dice.Die(20) + t.Mod(Stat.Dex);
                        int dmg = Dice.Roll(sk.dmg);
                        bool saved = sv >= dc;
                        if (saved) dmg = Math.Max(1, dmg / 2);
                        Log("　" + N(t) + " 身法豁免 " + sv + " 對 " + dc + (saved ? "，閃開大半，" : "，未能閃避，") + "受 <b>" + dmg + "</b> 傷");
                        Damage(t, dmg, false);
                    }
                    if (hitList.Count == 0) Log("　暗器落空。");
                    else if (u.alive && over == 0) { Learn(u, CellOf(u), null, false); Observe(); } // 出手就暴露位置
                    return true;
                }

                case SkillKind.Shout:
                    if (!CanShout(u, sk, target)) return false;
                    if (u.team == 0 && !autoHeroes && !CanSee(target)) return false;
                    Spend(u, sk);
                    Shout(u, sk, target);
                    return true;

                case SkillKind.Light:
                    Spend(u, sk);
                    u.light = true;
                    u.moveLeft += 4;
                    Log(N(u) + " 提氣施展「" + sk.name + "」，身輕如燕。");
                    Emit(new Ev { type = EvType.Buff, a = u, text = sk.name });
                    return true;

                case SkillKind.Guard:
                    Spend(u, sk);
                    u.guard = true;
                    Log(N(u) + " 運起「" + sk.name + "」，防禦升至 " + u.Ac + "。");
                    Emit(new Ev { type = EvType.Buff, a = u, text = sk.name });
                    return true;

                case SkillKind.Heal:
                {
                    Spend(u, sk);
                    int amt = Math.Max(1, Dice.Roll(sk.dmg) + u.Mod(Stat.Wis));
                    amt = Math.Min(amt, u.maxHp - u.hp);
                    u.hp += amt;
                    Log(N(u) + " 「" + sk.name + "」回復 <b>" + amt + "</b> 氣血。");
                    Emit(new Ev { type = EvType.Heal, a = u, n = amt, hp = u.hp });
                    return true;
                }

                case SkillKind.Rest:
                {
                    Spend(u, sk);
                    int amt = Math.Max(1, Dice.Roll(sk.dmg) + u.Mod(Stat.Wis));
                    amt = Math.Min(amt, u.maxQi - u.qi);
                    u.qi += amt;
                    Log(N(u) + " 調息，回復 <b>" + amt + "</b> 內力。");
                    Emit(new Ev { type = EvType.Buff, a = u, text = "內力 +" + amt });
                    return true;
                }
            }
            return false;
        }

        void Spend(Unit u, Skill sk)
        {
            u.qi -= sk.cost;
            if (sk.bonus) u.bonusLeft = false; else u.actionLeft = false;
        }

        /// <summary>出一招。回傳造成的傷害（沒打中為 0）。</summary>
        int Attack(Unit u, Skill sk, Unit t)
        {
            int mode = AdvMode(u, sk, u.x, u.y, u.z, t);
            SetFacing(u, t.x - u.x, t.y - u.y);
            if (u.team == 0) UpdateVision();
            // 乾坤扇的撥擋：每輪一次，看得見來勢的遠程攻擊改為劣勢
            if (!sk.melee && t.Weapon != null && t.Weapon.fx == GearFx.Deflect && t.reactionLeft && !t.stun && !t.surprised && !Unseen(u, t))
            {
                t.reactionLeft = false;
                mode = mode > 0 ? 0 : -1;
                Log("　" + N(t) + " 揮開「" + t.Weapon.name + "」撥擋來勢！");
                Emit(new Ev { type = EvType.Buff, a = t, text = "撥擋" });
            }
            // 出其不意：對方還不知道出手的人在哪。若對方全隊毫無戒備，被偷襲的人措手不及，丟掉下一個回合
            if (Surprises(u, t))
            {
                t.ambushed = true;
                t.surprised = true;
                Log("<color=#FFE08A>" + N(u) + " 出其不意，" + N(t) + " 措手不及！</color>");
                Emit(new Ev { type = EvType.Buff, a = t, text = "措手不及" });
            }
            Learn(u, CellOf(u), t, false); // 出手就暴露位置
            int r1, r2;
            int v = Dice.D20(mode, out r1, out r2);
            int cover = CoverBonus(sk, u.x, u.y, u.z, t.x, t.y, t.z);
            int bonus = AtkBonus(u, sk), ac = t.Ac + cover;
            // 擲出 20 必中；會心門檻依兵器種類而定（劍 19、短兵 18），但要先打中才算
            bool hit = v == 20 || (v != 1 && v + bonus >= ac), crit = hit && v >= sk.critMin;
            Emit(new Ev { type = EvType.Attack, a = u, b = t, skill = sk, n = v, flag = crit,
                          text = "d20  " + v + (bonus >= 0 ? " + " : " - ") + Math.Abs(bonus) + " = " + (v + bonus) });

            string rolls = mode == 0 ? "[" + r1 + "]" : "[" + r1 + "," + r2 + (mode > 0 ? "]優勢" : "]劣勢");
            string head = N(u) + "「" + sk.name + "」→ " + N(t) + "：d20" + rolls + "+" + bonus + "=" + (v + bonus) + " 對防禦 " + ac + (cover > 0 ? "（掩蔽 +" + cover + "）" : "");
            if (!hit)
            {
                Log(head + "，<color=#9A9A9A>未中</color>");
                Emit(new Ev { type = EvType.Miss, b = t });
                if (!t.stun) TurnToward(t, u);
                return 0;
            }
            int rolled = RollDamage(sk, sk.dmg.n) + sk.dmg.plus;
            if (crit) rolled += RollDamage(sk, sk.dmg.n * (sk.critDice - 1)); // 會心：傷害骰加倍（鈍器三倍）
            int dmg = Math.Max(1, rolled + u.Mod(sk.ability) - (Mismatch(u, sk) ? 2 : 0));
            Log(head + (crit ? "，<color=#FFD36A><b>會心！</b></color>" : "，命中，") + "<b>" + dmg + "</b> 傷");
            Damage(t, dmg, crit);

            if (t.alive && sk.push > 0) Push(t, Math.Sign(t.x - u.x), Math.Sign(t.y - u.y), sk.push);
            if (t.alive && sk.stun)
            {
                int dc = 8 + Db.Prof + u.Mod(sk.ability);
                int sv = Dice.Die(20) + t.Mod(Stat.Con);
                if (sv < dc)
                {
                    t.stun = true;
                    Log("　" + N(t) + " 根骨豁免 " + sv + " 對 " + dc + "，<color=#C9A0E8>穴道被封</color>！");
                    Emit(new Ev { type = EvType.Buff, a = t, text = "定身" });
                }
                else Log("　" + N(t) + " 根骨豁免 " + sv + " 對 " + dc + "，衝開穴道。");
            }
            if (t.alive && over == 0) WeaponFx(u, sk, t, crit);
            if (t.alive && !t.stun) TurnToward(t, u);
            return dmg;
        }

        // 擲 n 顆傷害骰；玄鐵重劍擲出 1、2 可重擲一次
        int RollDamage(Skill sk, int n)
        {
            int total = 0;
            for (int i = 0; i < n; i++)
            {
                int r = Dice.Die(sk.dmg.sides);
                if (sk.fx == GearFx.Reroll && r <= 2) r = Dice.Die(sk.dmg.sides);
                total += r;
            }
            return total;
        }

        // 命中之後：名器的專屬特效，以及抹在兵器上的毒
        void WeaponFx(Unit u, Skill sk, Unit t, bool crit)
        {
            switch (sk.fx)
            {
                case GearFx.Tangle:
                    t.tangle = 2;
                    Log("　軟劍纏上 " + N(t) + " 的手腕，<color=#C9A0E8>出手受制</color>（攻擊劣勢）。");
                    Emit(new Ev { type = EvType.Buff, a = t, text = "纏繞" });
                    break;
                case GearFx.Hook:
                {
                    int d = Math.Max(Math.Abs(t.x - u.x), Math.Abs(t.y - u.y));
                    if (d >= 2)
                    {
                        Log("　" + N(u) + " 回戟一鉤！");
                        Push(t, Math.Sign(u.x - t.x), Math.Sign(u.y - t.y), d - 1, "鉤近");
                    }
                    break;
                }
                case GearFx.Bleed:
                    t.bleed = 3;
                    Log("　倒刺撕開皮肉，" + N(t) + " <color=#D9534F>血流不止</color>。");
                    Emit(new Ev { type = EvType.Buff, a = t, text = "流血" });
                    break;
                case GearFx.CritStun:
                    if (crit)
                    {
                        t.stun = true;
                        Log("　細針正中要穴，" + N(t) + " <color=#C9A0E8>穴道被封</color>！");
                        Emit(new Ev { type = EvType.Buff, a = t, text = "定身" });
                    }
                    break;
                case GearFx.Poison:
                    Envenom(t);
                    break;
                case GearFx.Daze:
                case GearFx.Seal:
                {
                    bool daze = sk.fx == GearFx.Daze;
                    int dc = 8 + Db.Prof + u.Mod(sk.ability) - (daze ? 0 : 2);
                    int sv = Dice.Die(20) + t.Mod(daze ? Stat.Wis : Stat.Con);
                    string what = (daze ? "定力" : "根骨") + "豁免 " + sv + " 對 " + dc;
                    if (sv >= dc) { Log("　" + N(t) + " " + what + "，" + (daze ? "心神未亂。" : "衝開穴道。")); break; }
                    if (daze) t.fright = 2; else t.stun = true;
                    Log("　" + N(t) + " " + what + "，" + (daze ? "<color=#C9A0E8>心神大亂</color>（攻擊劣勢）。" : "<color=#C9A0E8>穴道被封</color>！"));
                    Emit(new Ev { type = EvType.Buff, a = t, text = daze ? "膽寒" : "定身" });
                    break;
                }
            }
            if (u.venom && t.alive)
            {
                u.venom = false;
                Log("　兵器上的毒滲入傷口。");
                Envenom(t);
            }
        }

        // 中毒：根骨豁免 DC 12，失敗則之後每回合開始受 1d4
        void Envenom(Unit t)
        {
            if (!t.alive || t.poison > 0) return;
            int sv = Dice.Die(20) + t.Mod(Stat.Con);
            if (sv >= 12) { Log("　" + N(t) + " 根骨豁免 " + sv + " 對 12，把毒逼了出去。"); return; }
            t.poison = 3;
            Log("　" + N(t) + " 根骨豁免 " + sv + " 對 12，<color=#9FE0C0>中毒</color>！");
            Emit(new Ev { type = EvType.Buff, a = t, text = "中毒" });
        }

        // 回合開始時的流血與毒發。回傳這個人是否還能行動
        bool TickWounds(Unit u)
        {
            if (u.bleed > 0)
            {
                u.bleed--;
                int dmg = Dice.Die(4);
                Log(N(u) + " 傷口流血，受 <b>" + dmg + "</b> 傷。");
                Damage(u, dmg, false);
            }
            if (u.alive && over == 0 && u.poison > 0)
            {
                int dmg = Dice.Die(4);
                Log(N(u) + " 毒性發作，受 <b>" + dmg + "</b> 傷。");
                Damage(u, dmg, false);
                if (u.alive)
                {
                    int sv = Dice.Die(20) + u.Mod(Stat.Con);
                    if (sv >= 12) { u.poison = 0; Log("　" + N(u) + " 根骨豁免 " + sv + " 對 12，毒已逼出。"); }
                    else u.poison--;
                }
            }
            return u.alive && over == 0;
        }

        /// <summary>這個人身上的狀態，空白分隔（給名字旁的標籤用）。</summary>
        public string StatusText(Unit u)
        {
            string s = "";
            if (u.stun) s += "定身 ";
            if (u.surprised) s += "措手不及 ";
            if (u.fright > 0) s += "膽寒 ";
            if (u.blind > 0) s += "目盲 ";
            if (u.tangle > 0) s += "纏繞 ";
            if (u.poison > 0) s += "中毒 ";
            if (u.bleed > 0) s += "流血 ";
            if (u.bless > 0) s += "祈福 ";
            return s.TrimEnd();
        }

        /// <summary>
        /// 兵器的波及：刀橫掃到目標兩側、同在出手者身邊的敵人；長兵貫穿同一直線上的另一名敵人。
        /// 被波及的人不另外擲骰，直接受一半傷害。(tx, ty) 是主目標挨打前的位置。
        /// </summary>
        void Splash(Unit u, Skill sk, Unit main, int tx, int ty, int dealt)
        {
            int half = Math.Max(1, dealt / 2);
            var hit = new List<Unit>();
            foreach (var o in units)
            {
                if (!o.alive || o.team == u.team || o == main || Math.Abs(o.z - u.z) > 2) continue;
                int dx = o.x - u.x, dy = o.y - u.y, d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (sk.sweep == Sweep.Arc)
                {
                    // 在出手者身邊，也緊鄰主目標
                    if (d == 1 && Math.Max(Math.Abs(o.x - tx), Math.Abs(o.y - ty)) == 1) hit.Add(o);
                }
                else
                {
                    // 與主目標同一方向、觸及範圍內的另一格
                    int mx = tx - u.x, my = ty - u.y, md = Math.Max(Math.Abs(mx), Math.Abs(my));
                    if (md == 0 || d < 1 || d > sk.reach || d == md) continue;
                    if (Math.Sign(mx) * d == dx && Math.Sign(my) * d == dy && Math.Abs(mx) % md == 0 && Math.Abs(my) % md == 0
                        && (Math.Abs(mx) == md || mx == 0) && (Math.Abs(my) == md || my == 0)) hit.Add(o);
                }
            }
            foreach (var o in hit)
            {
                if (!o.alive || over != 0) break;
                Log("　" + (sk.sweep == Sweep.Arc ? "刀勢橫掃" : "槍勢貫穿") + "，波及 " + N(o) + "，受 <b>" + half + "</b> 傷");
                Damage(o, half, false);
                if (o.alive && !o.stun) TurnToward(o, u);
            }
        }

        void Damage(Unit t, int n, bool crit)
        {
            t.hp = Math.Max(0, t.hp - n);
            Emit(new Ev { type = EvType.Damage, b = t, n = n, hp = t.hp, flag = crit });
            if (t.hp > 0) return;

            t.alive = false;
            world.occ[CellOf(t)] = -1;
            if (t.team == 0) UpdateVision();
            Log(N(t) + " <color=#D9534F>倒下了。</color>");
            Emit(new Ev { type = EvType.Death, b = t });
            if (t.team == 1) DropLoot(t);
            CheckOver();
            if (over == 0 && t.team == 1) Morale();
        }

        // 一方全滅（倒下、投降、潰逃都算）就分出勝負
        void CheckOver()
        {
            if (over != 0) return;
            bool heroes = false, foes = false;
            foreach (var u in units)
            {
                if (!u.alive) continue;
                if (u.team == 0) heroes = true; else foes = true;
            }
            if (!foes || !heroes)
            {
                over = foes ? -1 : 1;
                Emit(new Ev { type = EvType.GameOver, flag = over == 1 });
            }
        }

        /// <summary>擊退：沿 (sx,sy) 推 n 格。遇牆撞傷；落差 2 層以上摔傷。</summary>
        void Push(Unit t, int sx, int sy, int n, string verb = "震退")
        {
            if (sx == 0 && sy == 0) return;
            var w = world;
            var path = new List<int> { CellOf(t) };
            int cx = t.x, cy = t.y, cz = t.z, fall = 0;
            bool slam = false;

            for (int k = 0; k < n; k++)
            {
                int nx = cx + sx, ny = cy + sy;
                if (nx < 0 || ny < 0 || nx >= w.W || ny >= w.D) break;
                if (w.Get(nx, ny, cz) != Block.Air || w.Get(nx, ny, cz + 1) != Block.Air) { slam = true; break; }
                int fz = cz;
                while (fz > 0 && w.Get(nx, ny, fz - 1) == Block.Air) fz--;
                if (!w.Standable(nx, ny, fz)) break;              // 下面是水或無處落腳
                if (w.occ[w.Idx(nx, ny, fz)] >= 0 || w.blocked[w.Idx(nx, ny, fz)]) break; // 有人或東西擋著
                path.Add(w.Idx(nx, ny, fz));
                int drop = cz - fz;
                cx = nx; cy = ny; cz = fz;
                if (drop >= 2) { fall = drop; break; }
            }

            if (path.Count > 1)
            {
                w.occ[path[0]] = -1;
                w.occ[path[path.Count - 1]] = (short)t.id;
                t.x = cx; t.y = cy; t.z = cz;
                Emit(new Ev { type = EvType.Push, b = t, path = path.ToArray() });
            }
            if (fall > 0)
            {
                int dmg = Dice.Roll(new DiceExpr(fall / 2, 6));
                Log("　" + N(t) + " 被震落 " + fall + " 層，摔傷 <b>" + dmg + "</b>");
                Damage(t, dmg, false);
            }
            else if (slam)
            {
                int dmg = Dice.Roll(new DiceExpr(1, 4));
                Log("　" + N(t) + " 撞上石壁，受 <b>" + dmg + "</b> 傷");
                Damage(t, dmg, false);
            }
            else if (path.Count > 1) Log("　" + N(t) + " 被" + verb + " " + (path.Count - 1) + " 格。");
            Observe();
        }
    }
}
