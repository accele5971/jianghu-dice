using System;
using System.Collections.Generic;

namespace Jianghu
{
    /// <summary>
    /// 戰鬥規則的後半：技能檢定、地圖物件互動（滾石、寶箱、芒草、絆鈴索）、
    /// 與人打交道（路人交談、喝令、士氣）。
    /// </summary>
    public sealed partial class Battle
    {
        public readonly List<Prop> props = new List<Prop>();
        int foesAtStart;
        bool moraleHalf, moraleBoss;

        // ───────── 檢定 ─────────

        public int CheckBonus(Unit u, Stat s, Talent t) { return u.Mod(s) + ((u.talents & t) != 0 ? Db.Prof : 0) + u.GearSkill(t); }

        /// <summary>技能檢定：d20 + 屬性調整值 + 熟練（有的話）。text 是給戰報看的算式。</summary>
        public int Check(Unit u, Stat s, Talent t, out string text)
        {
            int r = Dice.Die(20), bonus = CheckBonus(u, s, t);
            text = "d20[" + r + "]" + (bonus >= 0 ? "+" : "") + bonus + "=" + (r + bonus);
            return r + bonus;
        }

        /// <summary>檢定成功率（給提示框與對話選項用）。</summary>
        public static float Odds(int bonus, int dc) { return Math.Max(0f, Math.Min(1f, (21 - (dc - bonus)) / 20f)); }

        // 威嚇看臂力或福緣，取高的
        static Stat Menace(Unit u) { return u.stats[(int)Stat.Str] >= u.stats[(int)Stat.Cha] ? Stat.Str : Stat.Cha; }

        // ───────── 物件 ─────────

        public Prop AddProp(PropKind kind, int cell, string name, int dc = 0)
        {
            var p = new Prop { id = props.Count, kind = kind, cell = cell, name = name, dc = dc };
            props.Add(p);
            if (p.Solid) Stamp(p, cell, true);
            return p;
        }

        /// <summary>加入一個已經填好資料的物件（房舍這類佔地大於一格的用）。</summary>
        public Prop AddProp(Prop p)
        {
            p.id = props.Count;
            props.Add(p);
            if (p.Solid) Stamp(p, p.cell, true);
            return p;
        }

        /// <summary>
        /// 把物件的碰撞體積寫進（或移出）世界：佔住格子不能走進去、滑鼠可以點到本體；
        /// 滾石與路人有兩層高，滾石還會擋住視線與射擊。
        /// </summary>
        void Stamp(Prop p, int cell, bool on)
        {
            var w = world;
            if (p.kind == PropKind.Building && !p.keeper)
            {
                // 房舍：整塊佔地都不能走，四層高，擋視線
                for (int dy = 0; dy < p.sy; dy++)
                    for (int dx = 0; dx < p.sx; dx++)
                        for (int dz = 0; dz < 4; dz++)
                        {
                            int c = cell + dx + dy * w.W + dz * w.WD;
                            if (c >= w.propAt.Length) continue;
                            if (dz == 0) w.blocked[c] = on;
                            w.propAt[c] = (short)(on ? p.id : -1);
                            w.wall[c] = on;
                        }
                return;
            }
            w.blocked[cell] = on;
            w.propAt[cell] = (short)(on ? p.id : -1);
            bool tall = p.kind == PropKind.Boulder || p.kind == PropKind.Npc || p.kind == PropKind.Barricade || p.kind == PropKind.Rack || p.kind == PropKind.Shrine || p.kind == PropKind.Notice
                     || p.kind == PropKind.ArmorRack || p.kind == PropKind.Well || p.kind == PropKind.Building || p.kind == PropKind.Door;
            int up = cell + w.WD;
            if (tall && up < w.propAt.Length) w.propAt[up] = (short)(on ? p.id : -1);
            if (p.kind == PropKind.Boulder || p.kind == PropKind.Barricade)
            {
                w.wall[cell] = on;
                if (up < w.wall.Length) w.wall[up] = on;
            }
        }

        /// <summary>這一格上還在的物件（沒有則 null）。</summary>
        public Prop PropAt(int cell)
        {
            foreach (var p in props) if (p.cell == cell && !p.gone) return p;
            // 房舍佔好幾格
            int short_ = cell >= 0 && cell < world.propAt.Length ? world.propAt[cell] : -1;
            if (short_ >= 0 && short_ < props.Count && props[short_].kind == PropKind.Building && !props[short_].gone) return props[short_];
            return null;
        }

        Prop TrapAt(int cell)
        {
            foreach (var p in props) if (p.cell == cell && p.Armed) return p;
            return null;
        }

        // ───────── 掉落物 ─────────


        Prop LootAt(int cell)
        {
            foreach (var p in props) if (p.cell == cell && p.kind == PropKind.Loot && !p.gone) return p;
            return null;
        }

        /// <summary>
        /// 敵人倒下（或棄械投降）：還沒用掉的道具、身上的裝備與銀兩都留在原地，走上去就能搜刮。
        /// 那一格已經有別的東西就往旁邊找空地；找不到就併進原地的那一包。
        /// </summary>
        void DropLoot(Unit t)
        {
            bool any = t.bag.Count > 0 || t.silver > 0;
            foreach (var g in t.gear) if (g != null) any = true;
            if (!any) return;
            int cell = CellOf(t);
            Prop p = LootAt(cell);
            if (p == null && PropAt(cell) != null)
            {
                int free = -1;
                for (int k = 0; k < 8 && free < 0; k++)
                {
                    int nx = t.x + NX[k], ny = t.y + NY[k];
                    for (int z = t.z + 1; z >= t.z - 1; z--)
                    {
                        if (!world.Standable(nx, ny, z)) continue;
                        int c = world.Idx(nx, ny, z);
                        if (world.occ[c] < 0 && !world.blocked[c] && PropAt(c) == null) free = c;
                        break;
                    }
                }
                if (free < 0) return;
                cell = free;
            }
            if (p == null) p = AddProp(PropKind.Loot, cell, N0(t) + "的遺物");
            foreach (var s in t.bag) for (int k = 0; k < s.n; k++) p.items.Add(s.item);
            if (p.item == null && p.items.Count > 0) p.item = p.items[0];
            foreach (var g in t.gear) if (g != null) p.gears.Add(g);
            p.silver += t.silver;
            t.bag.Clear();
            t.silver = 0;
            Log("　" + N(t) + " 身上的東西散落一地：" + LootText(p) + "。");
            Emit(new Ev { type = EvType.Prop, prop = p });
        }

        static readonly int[] NX = { 1, -1, 0, 0, 1, 1, -1, -1 }, NY = { 0, 0, 1, -1, 1, -1, 1, -1 };
        static string N0(Unit u) { return u.name; }

        /// <summary>一包東西裡有什麼（給戰報與提示框）。</summary>
        public static string LootText(Prop p)
        {
            var sb = new System.Text.StringBuilder();
            var seen = new List<Item>();
            foreach (var it in p.items)
            {
                if (seen.Contains(it)) continue;
                seen.Add(it);
                int n = 0;
                foreach (var x in p.items) if (x == it) n++;
                sb.Append(sb.Length > 0 ? "、" : "").Append(it.name).Append(n > 1 ? "×" + n : "");
            }
            foreach (var g in p.gears) sb.Append(sb.Length > 0 ? "、" : "").Append(g.name);
            if (p.silver > 0) sb.Append(sb.Length > 0 ? "、" : "").Append("銀 ").Append(p.silver).Append(" 兩");
            return sb.Length > 0 ? sb.ToString() : "空無一物";
        }

        /// <summary>
        /// 搜刮腳下（或身邊）的掉落物：裝備進共用行李、銀兩入帳、道具進行囊；
        /// 行囊放不下的道具留在原地。回傳是否拿到任何東西。
        /// </summary>
        bool PickUp(Unit u, Prop p)
        {
            if (p == null || p.gone || p.kind != PropKind.Loot) return false;
            var got = new System.Text.StringBuilder();
            foreach (var g in p.gears) { stash.Add(g); got.Append(got.Length > 0 ? "、" : "").Append(g.name); }
            p.gears.Clear();
            if (p.silver > 0) { silver += p.silver; got.Append(got.Length > 0 ? "、" : "").Append("銀 ").Append(p.silver).Append(" 兩"); p.silver = 0; }
            for (int i = 0; i < p.items.Count; )
            {
                if (!Give(u, p.items[i])) { i++; continue; }
                got.Append(got.Length > 0 ? "、" : "").Append(p.items[i].name);
                p.items.RemoveAt(i);
            }
            if (got.Length == 0)
            {
                if (p.items.Count == 0) { p.gone = true; return false; } // 空包
                Log("　" + N(u) + " 的行囊滿了，「" + p.items[0].name + "」留在原地。");
                return false;
            }
            if (p.items.Count == 0) p.gone = true;
            else Log("　行囊滿了，還有 " + p.items.Count + " 件拿不走。");
            Log(N(u) + " 搜刮到：" + got + "。");
            Emit(new Ev { type = EvType.Prop, prop = p, a = u, text = "得 " + got });
            return true;
        }

        Prop SpikesAt(int cell)
        {
            foreach (var p in props) if (p.cell == cell && p.Spiked) return p;
            return null;
        }

        // 敵人踩到鐵蒺藜
        void StepOnSpikes(Unit u, Prop p)
        {
            p.gone = true;
            Emit(new Ev { type = EvType.Prop, prop = p, a = u, text = "鐵蒺藜！" });
            int sv = Dice.Die(20) + u.Mod(Stat.Dex);
            if (sv >= p.dc) { Log(N(u) + " 踩進「" + p.name + "」：身法豁免 " + sv + " 對 " + p.dc + "，踮腳避過。"); return; }
            int dmg = Dice.Die(4);
            u.moveLeft = 0;
            Log(N(u) + " 踩中「" + p.name + "」：身法豁免 " + sv + " 對 " + p.dc + "，腳底被刺穿，受 <b>" + dmg + "</b> 傷，這回合走不動了。");
            Damage(u, dmg, false);
        }

        bool GrassAt(int cell)
        {
            foreach (var p in props) if (p.cell == cell && p.kind == PropKind.Grass) return true;
            return false;
        }

        /// <summary>u 是否站在物件旁邊（相鄰或同格，高度差 2 層內）。</summary>
        public bool Beside(Unit u, Prop p)
        {
            int px = world.X(p.cell), py = world.Y(p.cell);
            int dx = u.x < px ? px - u.x : u.x > px + p.sx - 1 ? u.x - (px + p.sx - 1) : 0;
            int dy = u.y < py ? py - u.y : u.y > py + p.sy - 1 ? u.y - (py + p.sy - 1) : 0;
            return Math.Max(dx, dy) <= 1 && Math.Abs(world.Z(p.cell) - u.z) <= 2;
        }

        /// <summary>現在能不能互動；可以回傳 null，不行回傳原因。</summary>
        public string WhyNot(Unit u, Prop p)
        {
            if (over != 0 || !u.alive || p.gone) return "無法互動";
            if (p.kind == PropKind.Grass) return "走進去即可藏身";
            if (p.kind == PropKind.Caltrops) return p.owner == u.team ? "敵人踩到才有用" : "小心別踩上去";
            if (p.kind == PropKind.Loot) return !Beside(u, p) ? "要先走到旁邊（走上去會自動搜刮）" : null;
            if (p.kind == PropKind.Trap && !p.found) return "無法互動";
            string extra;
            if (WhyNotExtra(u, p, out extra)) return extra;
            if (!Beside(u, p)) return "要先走到旁邊";
            switch (p.kind)
            {
                case PropKind.Chest:
                    if (p.open) return "已經空了";
                    return u.freeLeft ? null : "本回合已做過一次隨手互動";
                case PropKind.Boulder:
                    if (world.X(p.cell) == u.x && world.Y(p.cell) == u.y) return "無法互動";
                    return u.actionLeft ? null : "需要一個動作";
                case PropKind.Trap:
                    return u.actionLeft ? null : "需要一個動作";
                case PropKind.Npc:
                    return p.attitude > 0 ? null : "他不肯再跟你們說話";
            }
            return "無法互動";
        }

        /// <summary>提示框用的說明：這是什麼、怎麼互動、成功率。</summary>
        public string Describe(Unit u, Prop p)
        {
            switch (p.kind)
            {
                case PropKind.Boulder:
                    return "推落：動作，蠻力檢定對 DC " + p.dc + "（成功率 " + Pct(CheckBonus(u, Stat.Str, Talent.Athletics), p.dc) + "）\n"
                         + "往背對你的方向滾，遇下坡愈滾愈遠；砸中者受 2d6 傷，身法豁免減半。";
                case PropKind.Chest:
                    if (p.open) return "已經空了。";
                    return "開箱：隨手互動（每回合一次），不耗動作；裡面的東西會收進行囊\n上了鎖：巧手檢定對 DC " + p.dc + "（成功率 " + Pct(CheckBonus(u, Stat.Dex, Talent.Sleight), p.dc) + "）";
                case PropKind.Grass:
                    return "藏身其中：潛行 +5；除非敵人貼到身邊，否則正面也看不清。";
                case PropKind.Trap:
                    return "踩上去鈴聲大作，全寨都會知道你在哪，還會被竹釘所傷。\n拆除：動作，巧手檢定對 DC " + p.dc + "（成功率 "
                         + Pct(CheckBonus(u, Stat.Dex, Talent.Sleight), p.dc) + "）；差 5 以上會碰響鈴鐺。";
                case PropKind.Loot:
                    return LootText(p) + "\n走上去自動搜刮；站在旁邊也可以點它（不耗動作）。裝備收進行李，道具收進行囊。";
                case PropKind.Caltrops:
                    return (p.owner == u.team ? "對方" : "你們") + "踏入時身法豁免 DC " + p.dc + "，失敗受 1d4 並停下。踩過一次就散了。";
                case PropKind.Npc:
                    return "態度：" + Prop.AttitudeNames[p.attitude] + "\n交談不耗動作。走到旁邊後點他。";
            }
            return DescribeExtra(u, p);
        }

        static string Pct(int bonus, int dc) { return (int)Math.Round(Odds(bonus, dc) * 100) + "%"; }

        /// <summary>與滾石、寶箱、絆索互動。回傳是否有做事（成功或失敗都算）。</summary>
        public bool Interact(Unit u, Prop p)
        {
            if (!InteractCore(u, p)) return false;
            AfterAction();
            return true;
        }

        bool InteractCore(Unit u, Prop p)
        {
            if (WhyNot(u, p) != null) return false;
            if (IsExtra(p.kind)) return InteractExtra(u, p);
            switch (p.kind)
            {
                case PropKind.Chest: OpenChest(u, p); return true;
                case PropKind.Boulder: PushBoulder(u, p); return true;
                case PropKind.Trap: Disarm(u, p); return true;
                case PropKind.Loot: return PickUp(u, p);
            }
            return false;
        }

        // ───────── 寶箱 ─────────

        void OpenChest(Unit u, Prop p)
        {
            u.freeLeft = false;
            SetFacing(u, world.X(p.cell) - u.x, world.Y(p.cell) - u.y);
            string roll;
            int v = Check(u, Stat.Dex, Talent.Sleight, out roll);
            if (v < p.dc)
            {
                Log(N(u) + " 撬不開「" + p.name + "」的鎖：巧手 " + roll + " 對 DC " + p.dc + "。");
                Emit(new Ev { type = EvType.Buff, a = u, text = "鎖撬不開" });
                return;
            }
            p.open = true;
            Log(N(u) + " 撬開了「" + p.name + "」：巧手 " + roll + " 對 DC " + p.dc + "。");
            Emit(new Ev { type = EvType.Prop, prop = p, a = u });

            // 箱裡是兩瓶金創藥、一瓶養氣丹、一粒解毒丹，收進行囊
            Loot(u, Item.Salve);
            Loot(u, Item.Salve);
            Loot(u, Item.QiPill);
            Loot(u, Item.Antidote);
        }

        // ───────── 滾石 ─────────

        void PushBoulder(Unit u, Prop p)
        {
            var w = world;
            int sx = Math.Sign(w.X(p.cell) - u.x), sy = Math.Sign(w.Y(p.cell) - u.y);
            u.actionLeft = false;
            SetFacing(u, sx, sy);
            if (u.team == 0) UpdateVision();

            string roll;
            int v = Check(u, Stat.Str, Talent.Athletics, out roll);
            if (v < p.dc)
            {
                Log(N(u) + " 使勁去推「" + p.name + "」，紋絲不動：蠻力 " + roll + " 對 DC " + p.dc + "。");
                Emit(new Ev { type = EvType.Buff, a = u, text = "推不動" });
                return;
            }
            Log(N(u) + " 發力推落「" + p.name + "」：蠻力 " + roll + " 對 DC " + p.dc + "。");

            // 一格一格滾：平地會慢下來（最多再滾 3 格），每落下一階就重新加速
            var path = new List<int> { p.cell };
            int cx = w.X(p.cell), cy = w.Y(p.cell), cz = w.Z(p.cell), momentum = 3, dropped = 0, shown = 0;
            bool sunk = false;
            Stamp(p, p.cell, false);

            for (int step = 0; step < 16 && momentum > 0 && over == 0; step++)
            {
                int nx = cx + sx, ny = cy + sy;
                if (nx < 0 || ny < 0 || nx >= w.W || ny >= w.D) break;
                if (w.Get(nx, ny, cz) != Block.Air || w.Get(nx, ny, cz + 1) != Block.Air) break; // 撞上坡或牆
                int fz = cz;
                while (fz > 0 && w.Get(nx, ny, fz - 1) == Block.Air) fz--;
                int c = w.Idx(nx, ny, fz);
                if (!w.Standable(nx, ny, fz)) { path.Add(c); sunk = true; break; } // 滾進水裡
                if (w.blocked[c]) break;

                path.Add(c);
                int drop = cz - fz;
                if (drop > 0) { dropped += drop; momentum = 3; } else momentum--;

                int id = w.occ[c];
                if (id >= 0)
                {
                    // 砸到人：先讓畫面滾到這裡，再結算傷害
                    var t = units[id];
                    Emit(new Ev { type = EvType.Prop, prop = p, a = u, path = path.GetRange(shown, path.Count - shown).ToArray() });
                    shown = path.Count - 1;
                    int dmg = Dice.Roll(new DiceExpr(2, 6)) + (dropped >= 2 ? Dice.Roll(new DiceExpr(1, 6)) : 0);
                    int sv = Dice.Die(20) + t.Mod(Stat.Dex);
                    bool saved = sv >= 13;
                    if (saved) dmg = Math.Max(1, dmg / 2);
                    Log("　滾石砸向 " + N(t) + "：身法豁免 " + sv + " 對 13" + (saved ? "，閃開大半，" : "，閃避不及，") + "受 <b>" + dmg + "</b> 傷");
                    Damage(t, dmg, false);
                    if (t.alive)
                    {
                        // 人沒倒：石頭被擋下，停在前一格
                        path.RemoveAt(path.Count - 1);
                        Emit(new Ev { type = EvType.Prop, prop = p, a = u, path = new[] { c, path[path.Count - 1] } });
                        shown = path.Count - 1;
                        break;
                    }
                }
                cx = nx; cy = ny; cz = fz;
            }

            int rest = path[path.Count - 1];
            if (path.Count - 1 > shown) Emit(new Ev { type = EvType.Prop, prop = p, a = u, path = path.GetRange(shown, path.Count - shown).ToArray() });
            p.cell = rest;
            if (sunk)
            {
                p.gone = true;
                Log("　「" + p.name + "」滾進河裡，沉了下去。");
                Emit(new Ev { type = EvType.Prop, prop = p, a = u });
            }
            else
            {
                Stamp(p, rest, true);
                if (path.Count > 1) Log("　「" + p.name + "」滾了 " + (path.Count - 1) + " 格才停下。");
                else Log("　「" + p.name + "」被前面的地勢擋住，沒滾出去。");
            }
            Observe();
        }

        // ───────── 絆鈴索 ─────────

        // u 是否看得出這條絆索：2 格內、看得到、被動警覺不低於它的隱蔽度
        bool Spots(Unit u, Prop trap)
        {
            int tx = world.X(trap.cell), ty = world.Y(trap.cell), tz = world.Z(trap.cell);
            if (Math.Max(Math.Abs(tx - u.x), Math.Abs(ty - u.y)) > 2 || u.Passive < trap.dc) return false;
            return VoxelRay.Los(world, u.x, u.y, u.z, tx, ty, tz);
        }

        void RevealTrap(Prop trap, Unit by)
        {
            if (trap.found || trap.gone) return;
            trap.found = true;
            world.blocked[trap.cell] = true; // 看見了就不會再不小心走上去
            if (by != null) Log("<color=#FFE08A>" + N(by) + " 察覺前方草間橫著一條「" + trap.name + "」（警覺 " + by.Passive + " 對 " + trap.dc + "）。</color>");
            Emit(new Ev { type = EvType.Prop, prop = trap, a = by });
        }

        // 我方站定之後，眼尖的人會發現附近的絆索
        void DetectTraps()
        {
            foreach (var p in props)
            {
                if (!p.Armed || p.found) continue;
                foreach (var u in units)
                    if (u.alive && u.team == 0 && Spots(u, p)) { RevealTrap(p, u); break; }
            }
        }

        // 一腳踩上去
        void SpringTrap(Unit u, Prop trap)
        {
            trap.found = true;
            trap.gone = true;
            world.blocked[trap.cell] = false;
            Emit(new Ev { type = EvType.Prop, prop = trap, a = u, text = "鈴聲大作！" });
            int sv = Dice.Die(20) + u.Mod(Stat.Dex);
            bool saved = sv >= 13;
            int dmg = Dice.Roll(new DiceExpr(1, 6));
            if (saved) dmg = Math.Max(1, dmg / 2);
            Log("<color=#FF9C6A>" + N(u) + " 踩中「" + trap.name + "」，鈴聲大作！</color>身法豁免 " + sv + " 對 13"
                + (saved ? "，及時縮腳，" : "，被竹釘刺中，") + "受 <b>" + dmg + "</b> 傷" + (saved ? "" : "，這回合走不動了") + "。");
            if (!saved) u.moveLeft = 0;
            Damage(u, dmg, false);
            if (u.alive && over == 0) Learn(u, CellOf(u), null, false); // 鈴一響，全寨都知道人在哪
        }

        void Disarm(Unit u, Prop trap)
        {
            u.actionLeft = false;
            SetFacing(u, world.X(trap.cell) - u.x, world.Y(trap.cell) - u.y);
            string roll;
            int v = Check(u, Stat.Dex, Talent.Sleight, out roll);
            if (v >= trap.dc)
            {
                trap.gone = true;
                world.blocked[trap.cell] = false;
                Log(N(u) + " 悄悄割斷了「" + trap.name + "」：巧手 " + roll + " 對 DC " + trap.dc + "。");
                Emit(new Ev { type = EvType.Prop, prop = trap, a = u, text = "絆索已除" });
                return;
            }
            if (v <= trap.dc - 5)
            {
                trap.gone = true;
                world.blocked[trap.cell] = false;
                Log("<color=#FF9C6A>" + N(u) + " 失手碰響了「" + trap.name + "」的鈴鐺！</color>巧手 " + roll + " 對 DC " + trap.dc + "。");
                Emit(new Ev { type = EvType.Prop, prop = trap, a = u, text = "鈴聲大作！" });
                Learn(u, CellOf(u), null, false);
                Observe();
                return;
            }
            Log(N(u) + " 一時解不開「" + trap.name + "」：巧手 " + roll + " 對 DC " + trap.dc + "。");
            Emit(new Ev { type = EvType.Buff, a = u, text = "解不開" });
        }

        // ───────── 路人交談 ─────────

        /// <summary>最近一次交談檢定的算式（給對話框顯示）。</summary>
        public string lastCheck = "";

        public int TalkDc(Prop npc) { return npc.attitude >= 2 ? 8 : npc.attitude == 1 ? 12 : 16; }

        public int TalkBonus(Unit u, TalkWay way)
        {
            return way == TalkWay.Persuade ? CheckBonus(u, Stat.Cha, Talent.Persuade)
                 : way == TalkWay.Deceive ? CheckBonus(u, Stat.Cha, Talent.Deceive)
                 : CheckBonus(u, Menace(u), Talent.Intimidate);
        }

        static int TriedBit(Unit u, TalkWay way) { return 1 << (u.id * 3 + (int)way); }

        /// <summary>這個人還能不能用這種方式開口（每人每種方式只有一次機會）。</summary>
        public bool CanTalk(Unit u, Prop npc, TalkWay way)
        {
            return npc.kind == PropKind.Npc && WhyNot(u, npc) == null && (npc.tried & TriedBit(u, way)) == 0 && !(npc.guided && npc.gave);
        }

        /// <summary>
        /// 與路人交談。成功會指路（揭露寨子的地形與所有絆索）；說服成功還會變友善、送草藥。
        /// 威嚇失敗他會大叫，欺瞞失敗他會起疑。回傳他說的話（給對話框顯示）。
        /// </summary>
        public string Talk(Unit u, Prop npc, TalkWay way)
        {
            if (!CanTalk(u, npc, way)) return null;
            npc.tried |= TriedBit(u, way);
            int dc = TalkDc(npc);
            string roll;
            Stat s = way == TalkWay.Intimidate ? Menace(u) : Stat.Cha;
            Talent tal = way == TalkWay.Persuade ? Talent.Persuade : way == TalkWay.Deceive ? Talent.Deceive : Talent.Intimidate;
            int v = Check(u, s, tal, out roll);
            bool ok = v >= dc;
            string head = N(u) + " 對 " + npc.name + " " + Prop.WayNames[(int)way] + "：" + roll + " 對 DC " + dc;
            string reply;

            lastCheck = head + (ok ? "，<color=#9FE0C0>成功</color>" : "，<color=#9A9A9A>失敗</color>");

            if (ok)
            {
                Log(head + "，<color=#9FE0C0>成功</color>。");
                if (way != TalkWay.Intimidate && npc.attitude < 2) npc.attitude++;
                reply = way == TalkWay.Persuade ? "「幾位是來除那夥山賊的？好、好！老漢天天在這山裡砍柴，他們的底細我清楚。」"
                      : way == TalkWay.Deceive ? "「原來是官府的差爺……小老兒知無不言。」"
                      : "「好漢饒命！我說、我全說！」";
                if (!npc.guided) reply += "\n" + Guide(npc);
                if (way == TalkWay.Persuade && npc.attitude >= 2 && !npc.gave)
                {
                    npc.gave = true;
                    reply += "\n「這瓶金創藥，幾位帶著。」";
                    Log("　" + npc.name + " 送上一瓶金創藥。");
                    Loot(u, Item.Salve);
                }
            }
            else
            {
                Log(head + "，<color=#9A9A9A>失敗</color>。");
                if (way == TalkWay.Persuade) reply = "「老漢只是個砍柴的，什麼都不知道……」";
                else if (way == TalkWay.Deceive)
                {
                    npc.attitude = Math.Max(0, npc.attitude - 1);
                    reply = "「差爺？差爺哪有不帶腰牌的……」他往後縮了縮，眼神滿是戒備。";
                }
                else
                {
                    npc.attitude = Math.Max(0, npc.attitude - 1);
                    reply = "「救命啊——有強人！」他扯開嗓子大叫，聲音傳遍了山谷。";
                    Log("<color=#FF9C6A>　" + npc.name + " 的叫聲驚動了山寨！</color>");
                    Learn(u, CellOf(u), null, false);
                    Observe();
                }
                if (npc.attitude == 0) reply += "\n（他不肯再跟你們說話了。）";
            }
            Emit(new Ev { type = EvType.Prop, prop = npc, a = u });
            AfterAction();
            return reply;
        }

        // 指路：寨子一帶的地形與所有絆索都畫給你看
        string Guide(Prop npc)
        {
            npc.guided = true;
            var cols = new List<int>();
            foreach (var f in units)
            {
                if (f.team != 1) continue;
                int hx = world.X(f.homeCell), hy = world.Y(f.homeCell);
                for (int y = Math.Max(0, hy - 2); y <= Math.Min(world.D - 1, hy + 2); y++)
                    for (int x = Math.Max(0, hx - 2); x <= Math.Min(world.W - 1, hx + 2); x++)
                    {
                        int i = x + y * world.W;
                        if (vision.seen[i] == 0) { vision.seen[i] = 1; cols.Add(i); }
                    }
            }
            int traps = 0, archers = 0, blades = 0;
            foreach (var p in props) if (p.Armed && !p.found) { RevealTrap(p, null); traps++; }
            foreach (var f in units)
            {
                if (f.team != 1 || !f.alive || f.boss) continue;
                if (OaSkill(f) == null) archers++; else blades++;
            }
            Emit(new Ev { type = EvType.Reveal, path = cols.ToArray() });
            Log("<color=#FFE08A>　" + npc.name + " 在地上畫出山寨的佈置" + (traps > 0 ? "，還指出 " + traps + " 處絆鈴索" : "") + "。</color>");
            return "「寨裡使刀的 " + blades + " 個、放箭的 " + archers + " 個守在高處，寨主坐鎮中間。"
                 + (traps > 0 ? "進寨的路上埋了 " + traps + " 條絆鈴索，踩上去鈴一響，全寨都出來了。" : "") + "」";
        }

        // ───────── 喝令與士氣 ─────────

        bool Bloodied(Unit t) { return t.hp * 2 <= t.maxHp; }

        int FoesAlive()
        {
            int n = 0;
            foreach (var x in units) if (x.team == 1 && x.alive) n++;
            return n;
        }

        /// <summary>喝令的難度：10 + 對方定力調整值；頭目 +4；負傷過半 −4；同伴折損過半 −2。</summary>
        public int ShoutDc(Unit t)
        {
            int lost = t.team == 1 && FoesAlive() * 2 <= foesAtStart ? 2 : 0;
            return 10 + t.Mod(Stat.Wis) + (t.boss ? 4 : 0) - (Bloodied(t) ? 4 : 0) - lost;
        }

        public int ShoutBonus(Unit u) { return CheckBonus(u, Menace(u), Talent.Intimidate); }

        /// <summary>喝令成功後他會投降（否則只是膽寒）。</summary>
        public bool WouldYield(Unit t) { return !t.boss && Bloodied(t); }

        public bool CanShout(Unit u, Skill sk, Unit t)
        {
            if (t == null || !t.alive || t.team == u.team || (t.defied & (1 << u.id)) != 0) return false;
            int d = Math.Max(Math.Abs(t.x - u.x), Math.Abs(t.y - u.y));
            return d <= sk.range && VoxelRay.Los(world, u.x, u.y, u.z, t.x, t.y, t.z);
        }

        /// <summary>這一招現在能不能指定 t（攻擊看射程與視線，喝令看距離與是否已經無效）。</summary>
        public bool CanTarget(Unit u, Skill sk, Unit t)
        {
            if (sk.kind == SkillKind.Shout) return CanShout(u, sk, t);
            return sk.kind == SkillKind.Attack && CanHit(sk, u.x, u.y, u.z, t.x, t.y, t.z);
        }

        void Shout(Unit u, Skill sk, Unit t)
        {
            SetFacing(u, t.x - u.x, t.y - u.y);
            if (u.team == 0) UpdateVision();
            Learn(u, CellOf(u), t, false); // 一開口就暴露位置
            int dc = ShoutDc(t);
            string roll;
            int v = Check(u, Menace(u), Talent.Intimidate, out roll);
            Emit(new Ev { type = EvType.Buff, a = u, text = sk.name });
            string head = N(u) + " 對 " + N(t) + " 厲聲喝令：威嚇 " + roll + " 對 DC " + dc;
            if (v < dc)
            {
                t.defied |= 1 << u.id;
                Log(head + "，" + N(t) + " 不為所動。");
                Emit(new Ev { type = EvType.Buff, a = t, text = "不為所動" });
            }
            else if (WouldYield(t))
            {
                Log(head + "。");
                DropLoot(t); // 棄械：兵器、盔甲、身上的東西都留下
                Rout(t, "膽氣已喪，棄械投降", "投降");
                if (over == 0 && t.team == 1) Morale();
            }
            else
            {
                t.fright = 2; // 持續到他下一個回合結束
                Log(head + "，" + N(t) + " <color=#C9A0E8>心生膽寒</color>（攻擊劣勢）。");
                Emit(new Ev { type = EvType.Buff, a = t, text = "膽寒" });
            }
            if (!t.stun && t.alive) TurnToward(t, u);
            Observe();
        }

        // 離開戰場（投降或潰逃），不算陣亡但算被擊敗
        void Rout(Unit t, string why, string label)
        {
            t.alive = false;
            t.fled = true;
            world.occ[CellOf(t)] = -1;
            Log(N(t) + " <color=#DCB361>" + why + "。</color>");
            Emit(new Ev { type = EvType.Flee, b = t, text = label });
            CheckOver();
        }

        /// <summary>
        /// 士氣：寨主倒下，或同伴折損過半時，各檢定一次。
        /// 還站著的嘍囉擲定力豁免（寨主倒下 DC 13，折損過半 DC 10），失敗就潰逃。
        /// </summary>
        void Morale()
        {
            bool bossAlive = false, hadBoss = false;
            foreach (var x in units)
                if (x.team == 1 && x.boss) { hadBoss = true; if (x.alive) bossAlive = true; }

            int dc;
            if (hadBoss && !bossAlive && !moraleBoss) { moraleBoss = true; dc = 13; Log("<color=#DCB361>寨主已倒，群匪軍心大亂！</color>"); }
            else if (FoesAlive() * 2 <= foesAtStart && !moraleHalf) { moraleHalf = true; dc = 10; Log("<color=#DCB361>群匪折損過半，軍心動搖。</color>"); }
            else return;

            var rest = new List<Unit>();
            foreach (var x in units) if (x.team == 1 && x.alive && !x.boss) rest.Add(x);
            foreach (var x in rest)
            {
                if (over != 0) break;
                int sv = Dice.Die(20) + x.Mod(Stat.Wis);
                if (sv >= dc) Log("　" + N(x) + " 定力豁免 " + sv + " 對 " + dc + "，咬牙死戰。");
                else Rout(x, "定力豁免 " + sv + " 對 " + dc + "，鬥志全失，抱頭鼠竄", "潰逃");
            }
        }
    }
}
