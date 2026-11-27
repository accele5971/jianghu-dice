using System;
using System.Collections.Generic;

namespace Jianghu
{
    /// <summary>店家的一項買賣或服務（給對話框列選項用）。</summary>
    public sealed class Offer
    {
        public int id;
        public string label, note;
        public int price;
        public string why; // 現在不能選的原因；可以選則為 null
    }

    /// <summary>
    /// 第三批地圖物件：盔甲架、木箱、鐵箱、收納盒、水桶、水井、房舍（民宅、鐵匠鋪、茶攤、食樓、繡坊）。
    /// 另有銀兩，以及敵人受傷後的膽氣檢定（逃跑）。
    /// 房舍目前只是框架：一棟佔 3×2 格的屋子，走到旁邊互動會列出幾項服務。
    /// </summary>
    public sealed partial class Battle
    {
        /// <summary>隊伍共用的銀兩。</summary>
        public int silver = 30;

        /// <summary>敵人受傷後是否檢定膽氣（測試可以關掉）。</summary>
        public bool nerves = true;

        bool EmptyBucketNear(Prop well)
        {
            foreach (var q in props)
                if (q.kind == PropKind.Bucket && !q.gone && q.open && Near1(q.cell, well.cell)) return true;
            return false;
        }

        bool Near1(int a, int b)
        {
            return Math.Max(Math.Abs(world.X(a) - world.X(b)), Math.Abs(world.Y(a) - world.Y(b))) <= 1 && Math.Abs(world.Z(a) - world.Z(b)) <= 2;
        }

        // 鐵箱：巧手開鎖，或用蠻力硬撬（難 3），取成功率高的
        bool PryByForce(Unit u, Prop p)
        {
            return Odds(CheckBonus(u, Stat.Str, Talent.Athletics), p.dc + 3) > Odds(CheckBonus(u, Stat.Dex, Talent.Sleight), p.dc);
        }

        string WhyNot3(Unit u, Prop p)
        {
            switch (p.kind)
            {
                case PropKind.ArmorRack:
                case PropKind.Crate:
                case PropKind.Box:
                case PropKind.IronChest:
                    if (p.open) return "已經空了";
                    return u.freeLeft ? null : "本回合已做過一次隨手互動";
                case PropKind.Bucket:
                    if (p.open) return "桶裡沒水了";
                    return u.freeLeft ? null : "本回合已做過一次隨手互動";
                case PropKind.Well:
                    if ((p.tried & (1 << u.id)) != 0 && u.blind <= 0 && !EmptyBucketNear(p)) return "已經喝過了";
                    return u.actionLeft ? null : "需要一個動作";
                case PropKind.Building:
                    if (u.team != 0) return "無法互動";
                    return Fighting ? "打起來了，店家大門緊閉" : null;
            }
            return "無法互動";
        }

        string Describe3(Unit u, Prop p)
        {
            switch (p.kind)
            {
                case PropKind.ArmorRack:
                    return p.open ? "已經拿空了。" : "取走：隨手互動（每回合一次）。架上的護具會收進行李：" + GearNames(p.gears);
                case PropKind.Crate:
                    return p.open ? "已經空了。" : "打開：隨手互動（每回合一次），不耗動作。裡面的東西收進行囊。";
                case PropKind.Box:
                    return p.open ? "已經空了。" : "一只收納用的小木盒。打開：隨手互動（每回合一次），不耗動作。";
                case PropKind.IronChest:
                {
                    if (p.open) return "已經空了。";
                    bool force = PryByForce(u, p);
                    return "上了重鎖。打開：隨手互動（每回合一次）\n巧手檢定對 DC " + p.dc + "（" + Pct(CheckBonus(u, Stat.Dex, Talent.Sleight), p.dc) + "），或蠻力硬撬對 DC " + (p.dc + 3)
                         + "（" + Pct(CheckBonus(u, Stat.Str, Talent.Athletics), p.dc + 3) + "）；" + u.name + " 會用" + (force ? "蠻力" : "巧手") + "。";
                }
                case PropKind.Bucket:
                    return p.open ? "空桶。到井邊打水可以再裝滿。" : "提起來沖洗：隨手互動。洗去石灰（解除目盲），桶就空了。";
                case PropKind.Well:
                    return "打水：動作。喝了回復 1d4 氣血與 1d4 內力（每人一次），洗去石灰，並把旁邊的空桶裝滿。";
                case PropKind.Building:
                    return Prop.ShopNames[(int)p.shop] + "。走到旁邊點它（或按 F）進去看看；打起來就不營業了。\n身上銀兩：" + silver + " 兩";
            }
            return "";
        }

        bool Interact3(Unit u, Prop p)
        {
            var w = world;
            switch (p.kind)
            {
                case PropKind.ArmorRack:
                case PropKind.Crate:
                case PropKind.Box:
                case PropKind.IronChest:
                    OpenContainer(u, p);
                    return true;

                case PropKind.Bucket:
                    u.freeLeft = false;
                    p.open = true;
                    Log(N(u) + " 提起「" + p.name + "」兜頭澆下" + (u.blind > 0 ? "，沖掉了眼裡的石灰。" : "，精神一振。"));
                    u.blind = 0;
                    Emit(new Ev { type = EvType.Prop, prop = p, a = u, text = "沖洗" });
                    if (u.team == 0) UpdateVision();
                    Observe();
                    return true;

                case PropKind.Well:
                {
                    u.actionLeft = false;
                    SetFacing(u, w.X(p.cell) - u.x, w.Y(p.cell) - u.y);
                    if ((p.tried & (1 << u.id)) == 0)
                    {
                        p.tried |= 1 << u.id;
                        int hp = Math.Min(Dice.Die(4), u.maxHp - u.hp), qi = Math.Min(Dice.Die(4), u.maxQi - u.qi);
                        u.hp += hp; u.qi += qi;
                        Log(N(u) + " 從「" + p.name + "」打上一桶清水喝了幾口，回復 <b>" + hp + "</b> 氣血、<b>" + qi + "</b> 內力。");
                        Emit(new Ev { type = EvType.Heal, a = u, n = hp, hp = u.hp });
                    }
                    else Log(N(u) + " 從「" + p.name + "」打上一桶清水。");
                    if (u.blind > 0) { u.blind = 0; Log("　" + N(u) + " 洗去了眼裡的石灰。"); if (u.team == 0) UpdateVision(); }
                    foreach (var q in props)
                        if (q.kind == PropKind.Bucket && !q.gone && q.open && Near1(q.cell, p.cell))
                        {
                            q.open = false;
                            Log("　順手把「" + q.name + "」裝滿了。");
                            Emit(new Ev { type = EvType.Prop, prop = q });
                        }
                    Emit(new Ev { type = EvType.Prop, prop = p, a = u, text = "打水" });
                    Observe();
                    return true;
                }

                case PropKind.Building:
                    // 進門：規則上不花任何東西，畫面層接著打開店家的對話框
                    Emit(new Ev { type = EvType.Prop, prop = p, a = u });
                    return true;
            }
            return false;
        }

        // 打開容器：裝備進共用行李、銀兩入帳、道具進行囊
        void OpenContainer(Unit u, Prop p)
        {
            u.freeLeft = false;
            SetFacing(u, world.X(p.cell) - u.x, world.Y(p.cell) - u.y);
            if (p.kind == PropKind.IronChest)
            {
                bool force = PryByForce(u, p);
                string roll;
                int dc = force ? p.dc + 3 : p.dc;
                int v = force ? Check(u, Stat.Str, Talent.Athletics, out roll) : Check(u, Stat.Dex, Talent.Sleight, out roll);
                string how = force ? "蠻力" : "巧手";
                if (v < dc)
                {
                    Log(N(u) + (force ? " 撬不開「" : " 打不開「") + p.name + "」的重鎖：" + how + " " + roll + " 對 DC " + dc + "。");
                    Emit(new Ev { type = EvType.Buff, a = u, text = "鎖打不開" });
                    return;
                }
                Log(N(u) + (force ? " 硬生生撬開了「" : " 撥開了「") + p.name + "」的重鎖：" + how + " " + roll + " 對 DC " + dc + "。");
            }
            p.open = true;
            Log(N(u) + " 打開「" + p.name + "」：" + LootText(p) + "。");
            Emit(new Ev { type = EvType.Prop, prop = p, a = u, text = p.gears.Count > 0 ? "得 " + GearNames(p.gears) : null });
            stash.AddRange(p.gears);
            if (p.silver > 0) { silver += p.silver; Emit(new Ev { type = EvType.Buff, a = u, text = "銀 +" + p.silver }); }
            foreach (var it in p.items) Loot(u, it);
            p.items.Clear();
            p.silver = 0;
        }

        // ───────── 房舍：買賣與服務（框架） ─────────

        /// <summary>店家開口的第一句話。</summary>
        public string ShopHello(Prop p)
        {
            switch (p.shop)
            {
                case ShopKind.Smithy: return "爐火正旺，打鐵的漢子頭也不抬：「要磨刀還是買傢伙？價錢寫在牆上。」";
                case ShopKind.TeaStall: return "布棚底下擺著兩張矮桌，老闆娘提著大茶壺迎上來：「客官歇歇腳，喝碗熱茶？」";
                case ShopKind.Inn: return "跑堂的把抹布往肩上一搭：「幾位裡邊請！小店的湯麵遠近馳名。」";
                case ShopKind.Weaver: return "繡架後頭的婦人放下針線：「衣裳破了只管拿來，加層襯裡也成。」";
                default: return "木門開了一條縫，一個婦人探出頭來：「山上的賊人又下來了？……幾位不是他們一夥的吧。」";
            }
        }

        Offer Sell(int id, string label, string note, int price, string why)
        {
            if (why == null && silver < price) why = "銀兩不夠";
            return new Offer { id = id, label = label, note = note, price = price, why = why };
        }

        string RoomFor(Unit u, Item it) { return u.bag.Count >= u.BagSize && BagCount(u, it) == 0 ? "行囊滿了" : null; }

        /// <summary>這家店現在能做的事。</summary>
        public List<Offer> Offers(Unit u, Prop p)
        {
            var list = new List<Offer>();
            if (p.kind != PropKind.Building) return list;
            switch (p.shop)
            {
                case ShopKind.Home:
                    list.Add(Sell(0, "討碗水喝", "回復 1d4+1 氣血（每人一次）", 0, (p.tried & (1 << u.id)) != 0 ? "已經喝過了" : null));
                    list.Add(Sell(1, "打聽山寨", "問問寨裡那夥人的底細", 0, p.guided ? "已經問過了" : null));
                    break;
                case ShopKind.Smithy:
                    list.Add(Sell(0, "磨利兵器", "手上兵器的基本攻擊傷害 +1", 15, u.hone > 0 ? "已經磨過了" : u.Weapon == null ? "手上沒有兵器" : null));
                    list.Add(Sell(1, "買鐵蒺藜", Item.Caltrops.desc, 8, RoomFor(u, Item.Caltrops)));
                    list.Add(Sell(2, "買霹靂彈", Item.Thunder.desc, 25, RoomFor(u, Item.Thunder)));
                    break;
                case ShopKind.TeaStall:
                    list.Add(Sell(0, "喝碗熱茶", "內力回滿", 3, u.qi >= u.maxQi ? "內力已滿" : null));
                    list.Add(Sell(1, "買養氣丹", Item.QiPill.desc, 12, RoomFor(u, Item.QiPill)));
                    break;
                case ShopKind.Inn:
                    list.Add(Sell(0, "吃碗湯麵", "氣血回滿，解除中毒", 6, u.hp >= u.maxHp && u.poison <= 0 ? "氣血已滿" : null));
                    list.Add(Sell(1, "買金創藥", Item.Salve.desc, 10, RoomFor(u, Item.Salve)));
                    list.Add(Sell(2, "買解毒丹", Item.Antidote.desc, 8, RoomFor(u, Item.Antidote)));
                    break;
                case ShopKind.Weaver:
                    list.Add(Sell(0, "縫補加襯", "防禦 +1", 12, u.mend > 0 ? "已經加過襯裡了" : null));
                    list.Add(Sell(1, "買披風", Gears.Summary(Gears.Cloak), 20, null));
                    list.Add(Sell(2, "買快靴", Gears.Summary(Gears.SwiftBoots), 30, null));
                    break;
            }
            return list;
        }

        /// <summary>做一筆買賣。回傳店家說的話；做不成回傳 null。</summary>
        public string Trade(Unit u, Prop p, int id)
        {
            if (WhyNot(u, p) != null) return null;
            Offer o = null;
            foreach (var x in Offers(u, p)) if (x.id == id) o = x;
            if (o == null || o.why != null) return null;
            silver -= o.price;
            string reply = "";
            switch (p.shop)
            {
                case ShopKind.Home:
                    if (id == 0)
                    {
                        p.tried |= 1 << u.id;
                        int amt = Math.Min(Dice.Die(4) + 1, u.maxHp - u.hp);
                        u.hp += amt;
                        Emit(new Ev { type = EvType.Heal, a = u, n = amt, hp = u.hp });
                        reply = "她遞出一瓢井水。「慢點喝。」（回復 " + amt + " 氣血）";
                    }
                    else
                    {
                        p.guided = true;
                        var sb = new System.Text.StringBuilder("「那夥人我見得多了。");
                        foreach (var f in units)
                            if (f.team == 1 && f.alive) sb.Append(f.name).Append(Db.AiNames[(int)f.ai]).Append("，");
                        sb.Append("魯鈍的只會盯著眼前的人砍，機警的會丟東西、踢火油桶，兇悍的打起來不要命。傷重了多半會跑，只有那寨主死也不退。」");
                        reply = sb.ToString();
                    }
                    break;
                case ShopKind.Smithy:
                    if (id == 0) { u.hone = 1; u.RefreshGear(); reply = "砂輪火星四濺。「成了，削鐵如泥不敢說，砍人夠用。」（基本攻擊傷害 +1）"; }
                    else { var it = id == 1 ? Item.Caltrops : Item.Thunder; Give(u, it); reply = "「拿好，別在我鋪子裡弄響了。」（得 " + it.name + "）"; }
                    break;
                case ShopKind.TeaStall:
                    if (id == 0) { u.qi = u.maxQi; reply = "一碗熱茶下肚，丹田暖洋洋的。（內力回滿）"; }
                    else { Give(u, Item.QiPill); reply = "「自家配的方子，提神得很。」（得 養氣丹）"; }
                    break;
                case ShopKind.Inn:
                    if (id == 0)
                    {
                        int amt = u.maxHp - u.hp;
                        u.hp = u.maxHp; u.poison = 0;
                        Emit(new Ev { type = EvType.Heal, a = u, n = amt, hp = u.hp });
                        reply = "連湯帶麵吃了個乾淨，渾身是勁。（氣血回滿）";
                    }
                    else { var it = id == 1 ? Item.Salve : Item.Antidote; Give(u, it); reply = "「掌櫃的說江湖朋友都用得上。」（得 " + it.name + "）"; }
                    break;
                case ShopKind.Weaver:
                    if (id == 0) { u.mend = 1; reply = "她在衣甲裡密密縫了一層熟皮。（防禦 +1）"; }
                    else { var g = id == 1 ? Gears.Cloak : Gears.SwiftBoots; stash.Add(g); reply = "「針腳細著呢，穿不壞。」（" + g.name + " 收進行李）"; }
                    break;
            }
            Log(N(u) + " 在「" + p.name + "」" + o.label + (o.price > 0 ? "，花了 " + o.price + " 兩銀子（剩 " + silver + " 兩）。" : "。"));
            Emit(new Ev { type = EvType.Gear, a = u });
            return reply;
        }

        // ───────── 膽氣：受傷之後會不會逃 ─────────

        static readonly int[] FleeLow = { 40, 25, 15 }, FleeBig = { 35, 50, 60 }, FleeDc = { 12, 10, 8 };

        /// <summary>
        /// 敵人挨了一下還站著：傷勢第一次掉到門檻以下，或這一下就去掉一大截氣血，要擲定力豁免，
        /// 失敗就轉身逃跑（之後的回合只顧往外跑，跑出視線或地圖邊緣就離開戰場）；成功則繼續打。
        /// 門檻與難度依性情而定；寨主還在場時 +2，頭目自己不逃。
        /// </summary>
        void Nerve(Unit t, int n)
        {
            if (!nerves || t.team != 1 || t.boss || t.fleeing || over != 0 || !started) return;
            int k = (int)t.ai;
            bool heavy = n * 100 >= FleeBig[k] * t.maxHp;
            bool hurt = !t.shaken && t.hp * 100 <= FleeLow[k] * t.maxHp;
            if (!heavy && !hurt) return;
            if (hurt) t.shaken = true;
            bool led = false;
            foreach (var x in units) if (x.team == 1 && x.boss && x.alive) led = true;
            int sv = Dice.Die(20) + t.Mod(Stat.Wis) + (led ? 2 : 0), dc = FleeDc[k];
            string why = heavy ? "挨了這一記重創" : "傷勢沉重";
            if (sv >= dc)
            {
                Log("　" + N(t) + why + "：定力豁免 " + sv + " 對 " + dc + (t.ai == AiKind.Fierce ? "，反倒激起了凶性。" : "，咬牙撐住。"));
                return;
            }
            t.fleeing = true;
            t.fleeTurns = 0;
            Log("<color=#DCB361>　" + N(t) + why + "：定力豁免 " + sv + " 對 " + dc + "，心生怯意，想要逃跑！</color>");
            Emit(new Ev { type = EvType.Buff, a = t, text = "想逃" });
        }

        /// <summary>
        /// 逃跑中的人此刻能不能脫身：到了地圖邊緣、沒有人盯著，或已經逃了三個回合。
        /// 脫身就離開戰場（身上的東西一起帶走）。回傳是否離開了。
        /// </summary>
        public bool Escape(Unit u)
        {
            if (!u.alive || !u.fleeing || over != 0) return false;
            bool edge = u.x == 0 || u.y == 0 || u.x == world.W - 1 || u.y == world.D - 1;
            if (!edge && u.inSight && u.fleeTurns < 3) return false;
            Rout(u, edge ? "逃出了山谷" : "趁亂逃得不見蹤影", "逃走");
            if (over == 0 && u.team == 1) Morale();
            return true;
        }

        /// <summary>逃跑的人被逼到無路可走：回頭拚命。</summary>
        public void StandAndFight(Unit u)
        {
            if (!u.fleeing) return;
            u.fleeing = false;
            Log(N(u) + " 無路可逃，回過頭來拚命。");
            Emit(new Ev { type = EvType.Buff, a = u, text = "困獸之鬥" });
        }
    }
}
