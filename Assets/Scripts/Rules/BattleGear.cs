using System;
using System.Collections.Generic;

namespace Jianghu
{
    /// <summary>戰鬥規則：換裝、行囊、道具。</summary>
    public sealed partial class Battle
    {
        /// <summary>隊伍共用的備用裝備（行李）。</summary>
        public readonly List<Gear> stash = Gears.StarterStash();

        /// <summary>戰鬥是否已經開打（開打後只能換手上的兵器與盾）。</summary>
        public bool Fighting { get { return round > 0; } }

        // ───────── 換裝 ─────────

        /// <summary>現在能不能更動這個欄位；可以回傳 null，不行回傳原因。</summary>
        public string WhyNotEquip(Unit u, Slot slot, Gear g)
        {
            if (over != 0 || !u.alive) return "無法更換";
            if (g != null && !g.Fits(slot)) return "放不進這個欄位";
            if (Fighting)
            {
                if (slot != Slot.MainHand && slot != Slot.OffHand) return "戰鬥中來不及更換，只能換手上的兵器";
                if (!u.freeLeft) return "本回合已做過一次隨手互動";
            }
            if (g != null && slot == Slot.OffHand && u.Weapon != null && u.Weapon.twoHanded) return "雙手兵器佔住了副手";
            return null;
        }

        /// <summary>
        /// 從行李取一件裝備換上（g 為 null 表示卸下），原本那件放回行李。
        /// 戰鬥中換兵器算一次隨手互動；換上雙手兵器時副手的東西會自動收起。
        /// </summary>
        public bool Equip(Unit u, Slot slot, Gear g)
        {
            if (WhyNotEquip(u, slot, g) != null) return false;
            if (g != null && !stash.Remove(g)) return false;
            var old = u.gear[(int)slot];
            if (old != null) stash.Add(old);
            u.gear[(int)slot] = g;
            if (g != null && g.twoHanded && u.gear[(int)Slot.OffHand] != null)
            {
                stash.Add(u.gear[(int)Slot.OffHand]);
                u.gear[(int)Slot.OffHand] = null;
            }
            if (Fighting) u.freeLeft = false;

            u.RefreshGear();
            if (u.moveLeft > u.move) u.moveLeft = u.move;
            ApplyLook(u, slot, g);
            Log(N(u) + (g != null ? " 換上「" + g.name + "」" : " 卸下「" + (old != null ? old.name : "") + "」")
                + (g != null && old != null ? "，收起「" + old.name + "」。" : "。"));
            Emit(new Ev { type = EvType.Gear, a = u });
            if (u.team == 0) UpdateVision();
            Observe();
            return true;
        }

        // 外觀跟著裝備走：手上的兵器、頭上戴的、身上穿的
        static void ApplyLook(Unit u, Slot slot, Gear g)
        {
            if (u.look == null) return;
            if (slot == Slot.MainHand) u.look.weapon = g != null ? g.model : WeaponKind.None;
            else if (slot == Slot.Head) u.look.headwear = g != null ? g.headwear : Headwear.None;
            else if (slot == Slot.Upper) u.look.outfit = g != null && g.outfit >= 0 ? (Outfit)g.outfit : u.baseOutfit; // 布衣、皮甲穿在原本的衣服外面
            else if (slot == Slot.Back) u.look.back = g == Gears.Cloak ? BackItem.Cape : g == Gears.Quiver ? BackItem.Quiver : g == Gears.Sheath ? BackItem.Sheath : BackItem.None;
        }

        // ───────── 行囊 ─────────

        public int BagCount(Unit u, Item item)
        {
            foreach (var s in u.bag) if (s.item == item) return s.n;
            return 0;
        }

        /// <summary>把道具放進行囊（同種疊在一起）。滿了回傳 false。</summary>
        public bool Give(Unit u, Item item, int n = 1)
        {
            foreach (var s in u.bag) if (s.item == item) { s.n += n; return true; }
            if (u.bag.Count >= u.BagSize) return false;
            u.bag.Add(new ItemStack { item = item, n = n });
            return true;
        }

        // 身懷巧手的人用附加動作就能取用道具
        bool QuickHands(Unit u) { return (u.talents & Talent.Sleight) != 0; }

        public bool ItemIsBonus(Unit u, Item item) { return item.bonus || QuickHands(u); }

        /// <summary>現在能不能拿出這個道具（不看對象，只看數量與動作）；可以回傳 null。</summary>
        public string WhyNotStart(Unit u, Item item)
        {
            if (over != 0 || !u.alive || BagCount(u, item) <= 0) return "無法使用";
            if (!Fighting) return "開戰後才能使用";
            if (ItemIsBonus(u, item) ? !u.bonusLeft : !u.actionLeft) return ItemIsBonus(u, item) ? "需要一個附加動作" : "需要一個動作";
            return null;
        }

        static int Cheb(Unit u, int x, int y) { return Math.Max(Math.Abs(x - u.x), Math.Abs(y - u.y)); }

        /// <summary>
        /// 現在能不能用這個道具；可以回傳 null，不行回傳原因。
        /// 依道具的對象（Item.aim）：自己不用指定；隊友用 target（null 表示自己）；敵人用 target；落點用 cell。
        /// </summary>
        public string WhyNotUse(Unit u, Item item, Unit target, int cell = -1)
        {
            if (over != 0 || !u.alive || BagCount(u, item) <= 0) return "無法使用";
            if (!Fighting) return "開戰後才能使用";
            if (ItemIsBonus(u, item) ? !u.bonusLeft : !u.actionLeft) return ItemIsBonus(u, item) ? "需要一個附加動作" : "需要一個動作";
            var w = world;
            switch (item.aim)
            {
                case Aim.Self:
                    if (item.kind == ItemKind.Qi && u.qi >= u.maxQi) return "內力已滿";
                    if (item.kind == ItemKind.Venom && u.venom) return "兵器上已經有毒";
                    return null;

                case Aim.Ally:
                {
                    var t = target ?? u;
                    if (!t.alive || t.team != u.team) return "只能對自己或隊友使用";
                    if (t != u && (Cheb(u, t.x, t.y) > 1 || Math.Abs(t.z - u.z) > 2)) return "要站在隊友身邊";
                    if (item.kind == ItemKind.Heal && t.hp >= t.maxHp) return "氣血已滿";
                    if (item.kind == ItemKind.Cure && t.poison <= 0) return "沒有中毒";
                    return null;
                }

                case Aim.Foe:
                    if (target == null || !target.alive || target.team == u.team) return "要指定一名敵人";
                    if (Cheb(u, target.x, target.y) > item.range) return "太遠";
                    if (!VoxelRay.Los(w, u.x, u.y, u.z, target.x, target.y, target.z)) return "看不到";
                    return null;

                default:
                {
                    if (cell < 0) return "要指定落點";
                    int x = w.X(cell), y = w.Y(cell), z = w.Z(cell), d = Cheb(u, x, y);
                    if (!w.Standable(x, y, z)) return "不能丟在這裡";
                    if (d > item.range) return "太遠";
                    if (item.kind == ItemKind.Caltrops)
                        return d == 1 && Math.Abs(z - u.z) <= 1 && w.occ[cell] < 0 && !w.blocked[cell] && PropAt(cell) == null ? null : "要撒在身邊的空地";
                    if (item.kind == ItemKind.Hook)
                    {
                        if (d < 2) return "太近，用走的就行";
                        if (Math.Abs(z - u.z) > 6) return "太高或太低";
                        if (w.occ[cell] >= 0 || w.blocked[cell]) return "落腳處被佔住了";
                    }
                    if (d > 0 && !VoxelRay.Los(w, u.x, u.y, u.z, x, y, z)) return "看不到";
                    return null;
                }
            }
        }

        public bool UseItem(Unit u, Item item, Unit target, int cell = -1)
        {
            if (WhyNotUse(u, item, target, cell) != null) return false;
            if (ItemIsBonus(u, item)) u.bonusLeft = false; else u.actionLeft = false;
            for (int i = 0; i < u.bag.Count; i++)
                if (u.bag[i].item == item) { if (--u.bag[i].n <= 0) u.bag.RemoveAt(i); break; }
            var w = world;

            switch (item.kind)
            {
                case ItemKind.Heal:
                {
                    var t = target ?? u;
                    int amt = Math.Min(Dice.Roll(item.amount), t.maxHp - t.hp);
                    t.hp += amt;
                    Log(N(u) + (t == u ? " 服下" : " 替 " + N(t) + " 敷上") + "「" + item.name + "」，回復 <b>" + amt + "</b> 氣血。");
                    Emit(new Ev { type = EvType.Heal, a = t, n = amt, hp = t.hp });
                    break;
                }
                case ItemKind.Qi:
                {
                    int amt = Math.Min(Dice.Roll(item.amount), u.maxQi - u.qi);
                    u.qi += amt;
                    Log(N(u) + " 服下「" + item.name + "」，回復 <b>" + amt + "</b> 內力。");
                    Emit(new Ev { type = EvType.Buff, a = u, text = "內力 +" + amt });
                    break;
                }
                case ItemKind.Cure:
                {
                    var t = target ?? u;
                    t.poison = 0;
                    Log(N(u) + (t == u ? " 服下" : " 餵 " + N(t) + " 服下") + "「" + item.name + "」，毒性盡解。");
                    Emit(new Ev { type = EvType.Buff, a = t, text = "解毒" });
                    break;
                }
                case ItemKind.Venom:
                    u.venom = true;
                    Log(N(u) + " 在兵器上抹了「" + item.name + "」。");
                    Emit(new Ev { type = EvType.Buff, a = u, text = "淬毒" });
                    break;

                case ItemKind.Lime:
                {
                    SetFacing(u, target.x - u.x, target.y - u.y);
                    if (u.team == 0) UpdateVision();
                    Emit(new Ev { type = EvType.Throw, a = u, cell = CellOf(target), text = item.name, item = item });
                    Learn(u, CellOf(u), target, false); // 出手就暴露位置
                    int sv = Dice.Die(20) + target.Mod(Stat.Dex);
                    if (sv >= 13) Log(N(u) + " 朝 " + N(target) + " 撒出「" + item.name + "」：身法豁免 " + sv + " 對 13，偏頭避開。");
                    else
                    {
                        target.blind = 2;
                        Log(N(u) + " 朝 " + N(target) + " 撒出「" + item.name + "」：身法豁免 " + sv + " 對 13，<color=#C9A0E8>雙目難睜</color>！");
                        Emit(new Ev { type = EvType.Buff, a = target, text = "目盲" });
                    }
                    Observe();
                    break;
                }
                case ItemKind.Smoke:
                {
                    SetFacing(u, w.X(cell) - u.x, w.Y(cell) - u.y);
                    Emit(new Ev { type = EvType.Throw, a = u, cell = cell, text = item.name, item = item });
                    Log(N(u) + " 擲出「" + item.name + "」，濃煙四起。");
                    AddSmoke(cell, item.radius);
                    break;
                }
                case ItemKind.Bomb:
                {
                    SetFacing(u, w.X(cell) - u.x, w.Y(cell) - u.y);
                    if (u.team == 0) UpdateVision();
                    Log(N(u) + " 擲出「" + item.name + "」，轟然炸開！");
                    Emit(new Ev { type = EvType.Aoe, a = u, cell = cell, n = item.radius, item = item });
                    var hit = new List<Unit>();
                    foreach (var t in units) if (t.alive && InBlast(cell, item.radius, t.x, t.y, t.z)) hit.Add(t);
                    foreach (var t in hit)
                    {
                        if (!t.alive || over != 0) continue;
                        int sv = Dice.Die(20) + t.Mod(Stat.Dex), dmg = Dice.Roll(item.amount);
                        bool saved = sv >= 13;
                        if (saved) dmg = Math.Max(1, dmg / 2);
                        Log("　" + N(t) + " 身法豁免 " + sv + " 對 13" + (saved ? "，撲開大半，" : "，首當其衝，") + "受 <b>" + dmg + "</b> 傷");
                        Damage(t, dmg, false);
                    }
                    if (u.alive && over == 0) { Learn(u, CellOf(u), null, false); Observe(); } // 巨響：全寨都知道了
                    break;
                }
                case ItemKind.Caltrops:
                {
                    SetFacing(u, w.X(cell) - u.x, w.Y(cell) - u.y);
                    var p = AddProp(PropKind.Caltrops, cell, item.name, 13);
                    Log(N(u) + " 在腳邊撒下「" + item.name + "」。");
                    Emit(new Ev { type = EvType.Prop, prop = p, a = u });
                    break;
                }
                case ItemKind.Hook:
                {
                    int from = CellOf(u);
                    SetFacing(u, w.X(cell) - u.x, w.Y(cell) - u.y);
                    w.occ[from] = -1;
                    Place(u, cell);
                    w.occ[cell] = (short)u.id;
                    if (u.team == 0) UpdateVision();
                    Log(N(u) + " 甩出「" + item.name + "」，凌空盪了過去。");
                    Emit(new Ev { type = EvType.Move, a = u, path = new[] { from, cell } });
                    Observe();
                    break;
                }
            }
            return true;
        }

        // ───────── 濃煙 ─────────

        sealed class Cloud { public int id, until; public List<int> voxels = new List<int>(); }
        readonly List<Cloud> clouds = new List<Cloud>();
        int cloudSeq;

        /// <summary>目前有幾團濃煙。</summary>
        public int SmokeCount { get { return clouds.Count; } }

        // 以 cell 為中心放一團濃煙，持續到下一輪結束
        void AddSmoke(int cell, int radius)
        {
            var w = world;
            int cx = w.X(cell), cy = w.Y(cell), cz = w.Z(cell);
            var c = new Cloud { id = ++cloudSeq, until = round + 1 };
            var shown = new List<int>();
            for (int y = cy - radius; y <= cy + radius; y++)
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if (x < 0 || y < 0 || x >= w.W || y >= w.D) continue;
                    bool any = false;
                    for (int z = Math.Max(1, cz - 1); z <= Math.Min(w.H - 1, cz + 2); z++)
                    {
                        int i = w.Idx(x, y, z);
                        if (w.v[i] != Block.Air) continue;
                        w.smoke[i] = true;
                        c.voxels.Add(i);
                        if (!any && w.Standable(x, y, z)) { shown.Add(i); any = true; }
                    }
                }
            clouds.Add(c);
            Emit(new Ev { type = EvType.Smoke, n = c.id, path = shown.ToArray(), flag = true });
            UpdateVision();
            Observe();
        }

        // 過了時效的濃煙散去
        void ExpireSmoke()
        {
            bool changed = false;
            for (int i = clouds.Count - 1; i >= 0; i--)
            {
                if (round <= clouds[i].until) continue;
                Emit(new Ev { type = EvType.Smoke, n = clouds[i].id, flag = false });
                clouds.RemoveAt(i);
                changed = true;
            }
            if (!changed) return;
            Array.Clear(world.smoke, 0, world.smoke.Length);
            foreach (var c in clouds) foreach (int v in c.voxels) world.smoke[v] = true;
            Log("<color=#A89C85>濃煙漸漸散去。</color>");
            UpdateVision();
            Observe();
        }

        // 撿到東西：放得下就收進行囊，放不下就當場用掉
        void Loot(Unit u, Item item)
        {
            if (Give(u, item))
            {
                Log("　" + N(u) + " 把「" + item.name + "」收進行囊。");
                Emit(new Ev { type = EvType.Buff, a = u, text = "得 " + item.name });
                return;
            }
            if (item.kind == ItemKind.Heal || item.kind == ItemKind.Qi)
            {
                int amt = Dice.Roll(item.amount);
                if (item.kind == ItemKind.Heal) { amt = Math.Min(amt, u.maxHp - u.hp); u.hp += amt; Emit(new Ev { type = EvType.Heal, a = u, n = amt, hp = u.hp }); }
                else { amt = Math.Min(amt, u.maxQi - u.qi); u.qi += amt; }
                Log("　行囊已滿，" + N(u) + " 當場服下「" + item.name + "」，回復 <b>" + amt + "</b>。");
            }
            else Log("　行囊已滿，「" + item.name + "」只好留在原地。");
        }
    }
}
