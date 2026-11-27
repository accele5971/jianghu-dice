using System;
using System.Collections.Generic;

namespace Jianghu
{
    /// <summary>
    /// 第二批地圖物件：火油桶、兵器架、藥草、神龕、拒馬、告示牌、藤蔓。
    /// </summary>
    public sealed partial class Battle
    {
        static bool IsExtra(PropKind k) { return k >= PropKind.Barrel; }

        Prop HerbAt(int cell)
        {
            foreach (var p in props) if (p.cell == cell && p.kind == PropKind.Herb && !p.gone) return p;
            return null;
        }

        // 第二批物件能不能互動。回傳 true 表示這裡已經判定完（why 為 null 就是可以）
        bool WhyNotExtra(Unit u, Prop p, out string why)
        {
            why = null;
            if (!IsExtra(p.kind)) return false;
            if (!Beside(u, p)) { why = p.kind == PropKind.Herb ? "要先走到旁邊（走上去會自動採下）" : "要先走到旁邊"; return true; }
            switch (p.kind)
            {
                case PropKind.Barrel:
                    if (world.X(p.cell) == u.x && world.Y(p.cell) == u.y) why = "無法互動";
                    else if (!u.actionLeft) why = "需要一個動作";
                    break;
                case PropKind.Barricade:
                    if (!u.actionLeft) why = "需要一個動作";
                    break;
                case PropKind.Rack:
                    if (p.open) why = "已經拿空了";
                    else if (!u.freeLeft) why = "本回合已做過一次隨手互動";
                    break;
                case PropKind.Herb:
                    if (u.bag.Count >= u.BagSize && BagCount(u, p.item) == 0) why = "行囊滿了";
                    break;
                case PropKind.Shrine:
                    if ((p.tried & (1 << u.id)) != 0) why = "已經祈過福了";
                    else if (!u.actionLeft) why = "需要一個動作";
                    break;
                case PropKind.Vine:
                    if (p.link < 0 || world.occ[p.link] >= 0 || world.blocked[p.link]) why = "上面有人擋著";
                    else if (!u.actionLeft) why = "需要一個動作";
                    break;
            }
            return true;
        }

        string DescribeExtra(Unit u, Prop p)
        {
            switch (p.kind)
            {
                case PropKind.Barrel:
                    return "踢出去：動作。往背對你的方向滾 2 格後炸開：3×3 範圍 3d6，身法豁免 DC 13 減半，不分敵我；\n炸得開拒馬，會引爆旁邊的油桶，巨響會暴露位置。霹靂彈也能引爆它。";
                case PropKind.Barricade:
                    return "擋路，也擋視線。拆毀：動作，蠻力檢定對 DC " + p.dc + "（成功率 " + Pct(CheckBonus(u, Stat.Str, Talent.Athletics), p.dc) + "）；也可以用火油桶或霹靂彈炸開。";
                case PropKind.Rack:
                    if (p.open) return "已經拿空了。";
                    return "取走：隨手互動（每回合一次）。架上的兵器會收進行李：" + GearNames(p.gears);
                case PropKind.Herb:
                    return "採下可得「" + p.item.name + "」。走上去自動採，站在旁邊也可以點它。";
                case PropKind.Shrine:
                    return (p.tried & (1 << u.id)) != 0 ? "已經祈過福了。" : "祈福：動作，每人一次。之後五個回合命中 +2。";
                case PropKind.Notice:
                    return "走到旁邊點它，看看上面寫了什麼。";
                case PropKind.Vine:
                    return "攀上去：動作。順著藤蔓爬到崖頂（高 " + (p.link >= 0 ? world.Z(p.link) - world.Z(p.cell) : 0) + " 層），輕功再差也上得去。";
            }
            return "";
        }

        static string GearNames(List<Gear> list)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++) sb.Append(i > 0 ? "、" : "").Append(list[i].name);
            return sb.ToString();
        }

        bool InteractExtra(Unit u, Prop p)
        {
            var w = world;
            switch (p.kind)
            {
                case PropKind.Barrel:
                    KickBarrel(u, p);
                    return true;

                case PropKind.Barricade:
                {
                    u.actionLeft = false;
                    SetFacing(u, w.X(p.cell) - u.x, w.Y(p.cell) - u.y);
                    string roll;
                    int v = Check(u, Stat.Str, Talent.Athletics, out roll);
                    if (v < p.dc)
                    {
                        Log(N(u) + " 使勁去扳「" + p.name + "」，紋絲不動：蠻力 " + roll + " 對 DC " + p.dc + "。");
                        Emit(new Ev { type = EvType.Buff, a = u, text = "拆不動" });
                        return true;
                    }
                    Log(N(u) + " 把「" + p.name + "」連根拔起，扔到一旁：蠻力 " + roll + " 對 DC " + p.dc + "。");
                    Smash(p, u);
                    return true;
                }

                case PropKind.Rack:
                    u.freeLeft = false;
                    p.open = true;
                    stash.AddRange(p.gears);
                    Log(N(u) + " 取下「" + p.name + "」上的兵器：" + GearNames(p.gears) + "，收進行李。");
                    Emit(new Ev { type = EvType.Prop, prop = p, a = u, text = "得 " + GearNames(p.gears) });
                    return true;

                case PropKind.Herb:
                    return Gather(u, p);

                case PropKind.Shrine:
                    u.actionLeft = false;
                    p.tried |= 1 << u.id;
                    u.bless = 5;
                    Log(N(u) + " 在「" + p.name + "」前合十祈福，心神一定（五個回合內命中 +2）。");
                    Emit(new Ev { type = EvType.Buff, a = u, text = "祈福" });
                    return true;

                case PropKind.Notice:
                    p.open = true;
                    Log(N(u) + " 讀了「" + p.name + "」。");
                    Emit(new Ev { type = EvType.Prop, prop = p, a = u });
                    return true;

                case PropKind.Vine:
                {
                    int from = CellOf(u);
                    u.actionLeft = false;
                    SetFacing(u, w.X(p.link) - u.x, w.Y(p.link) - u.y);
                    w.occ[from] = -1;
                    Place(u, p.link);
                    w.occ[p.link] = (short)u.id;
                    if (u.team == 0) UpdateVision();
                    Log(N(u) + " 抓著「" + p.name + "」攀上崖頂。");
                    Emit(new Ev { type = EvType.Move, a = u, path = new[] { from, p.link } });
                    Observe();
                    return true;
                }
            }
            return false;
        }

        /// <summary>告示牌上寫的字（畫面層顯示用）。</summary>
        public string NoticeText(Prop p)
        {
            return "<b>懸　賞</b>\n\n黑風寨匪首厲無咎，聚眾盤踞後山，劫掠行旅，殺傷人命。\n凡能擒殺此獠、蕩平山寨者，賞銀五百兩。\n\n"
                 + "<color=#A89C85>告示下方有人用炭筆添了幾行小字：\n「橋東口設了拒馬，還埋著絆鈴索，莫走正路。」\n「寨裡堆著火油，一點就炸。」\n「山崖北面有老藤可攀。」</color>";
        }

        // 採藥；回傳是否採到
        bool Gather(Unit u, Prop p)
        {
            if (p == null || p.gone || p.item == null) return false;
            if (!Give(u, p.item)) { Log("　" + N(u) + " 的行囊滿了，「" + p.name + "」先留著。"); return false; }
            p.gone = true;
            Log(N(u) + " 採下「" + p.name + "」，製成「" + p.item.name + "」。");
            Emit(new Ev { type = EvType.Prop, prop = p, a = u, text = "得 " + p.item.name });
            return true;
        }

        // 拒馬被拆掉或炸掉
        void Smash(Prop p, Unit by)
        {
            if (p.gone) return;
            p.gone = true;
            Stamp(p, p.cell, false);
            Emit(new Ev { type = EvType.Prop, prop = p, a = by, text = "拒馬已毀" });
            if (by != null && by.team == 0) UpdateVision();
        }

        // ───────── 火油桶 ─────────

        void KickBarrel(Unit u, Prop p)
        {
            var w = world;
            int sx = Math.Sign(w.X(p.cell) - u.x), sy = Math.Sign(w.Y(p.cell) - u.y);
            u.actionLeft = false;
            SetFacing(u, sx, sy);
            if (u.team == 0) UpdateVision();

            // 最多滾 2 格：被牆、坡、人、別的東西擋住就停
            var path = new List<int> { p.cell };
            int cx = w.X(p.cell), cy = w.Y(p.cell), cz = w.Z(p.cell);
            Stamp(p, p.cell, false);
            for (int step = 0; step < 2; step++)
            {
                int nx = cx + sx, ny = cy + sy;
                if (nx < 0 || ny < 0 || nx >= w.W || ny >= w.D) break;
                if (w.Get(nx, ny, cz) != Block.Air || w.Get(nx, ny, cz + 1) != Block.Air) break;
                int fz = cz;
                while (fz > 0 && w.Get(nx, ny, fz - 1) == Block.Air) fz--;
                int c = w.Idx(nx, ny, fz);
                if (!w.Standable(nx, ny, fz) || w.occ[c] >= 0 || w.blocked[c]) break;
                path.Add(c);
                cx = nx; cy = ny; cz = fz;
            }
            if (path.Count == 1)
            {
                // 一步都滾不出去：不點火，免得炸到自己
                Stamp(p, p.cell, true);
                Log(N(u) + " 想把「" + p.name + "」踢出去，卻被前面擋住了。");
                Emit(new Ev { type = EvType.Buff, a = u, text = "踢不動" });
                return;
            }
            p.cell = path[path.Count - 1];
            Stamp(p, p.cell, true);
            Log(N(u) + " 點著引線，一腳把「" + p.name + "」踢了出去！");
            Emit(new Ev { type = EvType.Prop, prop = p, a = u, path = path.ToArray() });
            Explode(p, u);
            Observe();
        }

        // 油桶炸開：3×3 範圍 3d6，身法豁免 DC 13 減半，不分敵我；炸毀拒馬，引爆別的油桶
        void Explode(Prop p, Unit by)
        {
            if (p.gone) return;
            var w = world;
            p.gone = true;
            Stamp(p, p.cell, false);
            int cell = p.cell;
            Log("<color=#FF9C6A>「" + p.name + "」轟然炸開！</color>");
            Emit(new Ev { type = EvType.Blast, prop = p, cell = cell, n = 1 });

            var hit = new List<Unit>();
            foreach (var t in units) if (t.alive && InBlast(cell, 1, t.x, t.y, t.z)) hit.Add(t);
            foreach (var t in hit)
            {
                if (!t.alive || over != 0) continue;
                int sv = Dice.Die(20) + t.Mod(Stat.Dex), dmg = Dice.Roll(new DiceExpr(3, 6));
                bool saved = sv >= 13;
                if (saved) dmg = Math.Max(1, dmg / 2);
                Log("　" + N(t) + " 身法豁免 " + sv + " 對 13" + (saved ? "，撲開大半，" : "，被烈焰吞沒，") + "受 <b>" + dmg + "</b> 傷");
                Damage(t, dmg, false);
            }

            var near = new List<Prop>();
            foreach (var q in props)
                if (!q.gone && q != p && InBlast(cell, 1, w.X(q.cell), w.Y(q.cell), w.Z(q.cell))) near.Add(q);
            foreach (var q in near)
            {
                if (q.kind == PropKind.Barricade) { Log("　「" + q.name + "」被炸得四分五裂。"); Smash(q, by); }
                else if (q.kind == PropKind.Barrel) Explode(q, by);
            }
            if (by != null && by.alive && over == 0) Learn(by, CellOf(by), null, false); // 巨響：全寨都知道了
        }

        // 別的爆炸（霹靂彈）波及到的油桶與拒馬
        void BlastProps(int cell, int radius, Unit by)
        {
            var w = world;
            var near = new List<Prop>();
            foreach (var q in props)
                if (!q.gone && InBlast(cell, radius, w.X(q.cell), w.Y(q.cell), w.Z(q.cell))) near.Add(q);
            foreach (var q in near)
            {
                if (q.kind == PropKind.Barricade) { Log("　「" + q.name + "」被炸得四分五裂。"); Smash(q, by); }
                else if (q.kind == PropKind.Barrel) Explode(q, by);
            }
        }
    }
}
