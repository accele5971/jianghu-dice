using System;
using System.Collections.Generic;

namespace Jianghu
{
    // ───────── 存檔資料 ─────────
    // 全部是公開欄位的純資料，畫面層用 JsonUtility 轉成文字檔。
    // 地圖本身不存：地形每次都由同一個種子生成，只存「會變的東西」。

    [Serializable]
    public sealed class UnitSave
    {
        public int hp, qi, x, y, z, fx, fy, init, moveLeft, homeCell, homeFx, homeFy, scan, knownCell, knownAt, stealth;
        public int fright, blind, poison, bleed, tangle, defied, baseOutfit, bless, hone, mend, silver, fleeTurns;
        public bool alive, fled, actionLeft, bonusLeft, freeLeft, reactionLeft, guard, light, stun, surprised, ambushed, inSight, ran, venom, shaken, fleeing;
        public string[] gear;      // 11 個欄位的裝備名稱，空欄是空字串
        public string[] bagItems;
        public int[] bagCounts;
        public Look look;
    }

    [Serializable]
    public sealed class PropSave
    {
        public int kind, cell, dc, attitude, tried, fx, fy, link, silver, owner, shop, sx, sy;
        public string name, item;
        public string[] gears, items;
        public bool gone, open, found, guided, gave;
    }

    [Serializable]
    public sealed class CloudSave
    {
        public int id, until;
        public int[] voxels, shown;
    }

    [Serializable]
    public sealed class SaveData
    {
        public const int Current = 5; // 出場人物或物件的初始配置一改，就要加一
        public int version = Current;
        public string savedAt = "";
        public string place = "", outerJson = "";
        public UnitSave[] units;
        public PropSave[] props;
        public CloudSave[] clouds;
        public int[] order;
        public string[] stash;
        public byte[] seen;
        public int turn, round, clock, over, active, alerts, foesAtStart, exploreClock, cloudSeq, silver;
        public bool exploring, started, moraleHalf, moraleBoss;
    }

    /// <summary>換場景時跟著人走的東西：三位俠客的狀態、共用行李、銀兩。</summary>
    [Serializable]
    public sealed class PartySave
    {
        public UnitSave[] heroes;
        public string[] stash;
        public int silver;
    }

    /// <summary>戰局的儲存與還原。</summary>
    public sealed partial class Battle
    {
        /// <summary>把目前的戰局寫成存檔資料。</summary>
        public SaveData Save()
        {
            var d = new SaveData
            {
                turn = turn, round = round, clock = clock, over = over, active = active != null ? active.id : -1, alerts = alerts,
                foesAtStart = foesAtStart, exploreClock = exploreClock, cloudSeq = cloudSeq, silver = silver,
                exploring = exploring, started = started, moraleHalf = moraleHalf, moraleBoss = moraleBoss,
                seen = (byte[])vision.seen.Clone(), place = place, outerJson = outerJson,
            };

            d.units = new UnitSave[units.Count];
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                var s = new UnitSave
                {
                    hp = u.hp, qi = u.qi, x = u.x, y = u.y, z = u.z, fx = u.fx, fy = u.fy, init = u.init, moveLeft = u.moveLeft,
                    homeCell = u.homeCell, homeFx = u.homeFx, homeFy = u.homeFy, scan = u.scan, knownCell = u.knownCell, knownAt = u.knownAt, stealth = u.stealth,
                    fright = u.fright, blind = u.blind, poison = u.poison, bleed = u.bleed, tangle = u.tangle, defied = u.defied, baseOutfit = (int)u.baseOutfit, bless = u.bless,
                    alive = u.alive, fled = u.fled, actionLeft = u.actionLeft, bonusLeft = u.bonusLeft, freeLeft = u.freeLeft, reactionLeft = u.reactionLeft,
                    guard = u.guard, light = u.light, stun = u.stun, surprised = u.surprised, ambushed = u.ambushed, inSight = u.inSight, ran = u.ran, venom = u.venom,
                    hone = u.hone, mend = u.mend, silver = u.silver, fleeTurns = u.fleeTurns, shaken = u.shaken, fleeing = u.fleeing,
                    look = u.look != null ? u.look.Clone() : new Look(),
                };
                s.gear = new string[Gears.SlotCount];
                for (int k = 0; k < s.gear.Length; k++) s.gear[k] = u.gear[k] != null ? u.gear[k].name : "";
                s.bagItems = new string[u.bag.Count];
                s.bagCounts = new int[u.bag.Count];
                for (int k = 0; k < u.bag.Count; k++) { s.bagItems[k] = u.bag[k].item.name; s.bagCounts[k] = u.bag[k].n; }
                d.units[i] = s;
            }

            d.props = new PropSave[props.Count];
            for (int i = 0; i < props.Count; i++)
            {
                var p = props[i];
                d.props[i] = new PropSave
                {
                    kind = (int)p.kind, cell = p.cell, dc = p.dc, attitude = p.attitude, tried = p.tried, fx = p.fx, fy = p.fy,
                    name = p.name, item = p.item != null ? p.item.name : "", gone = p.gone, open = p.open, found = p.found, guided = p.guided, gave = p.gave,
                    link = p.link, gears = new string[p.gears.Count], items = new string[p.items.Count],
                    silver = p.silver, owner = p.owner, shop = (int)p.shop, sx = p.sx, sy = p.sy,
                };
                for (int k = 0; k < p.gears.Count; k++) d.props[i].gears[k] = p.gears[k].name;
                for (int k = 0; k < p.items.Count; k++) d.props[i].items[k] = p.items[k].name;
            }

            d.clouds = new CloudSave[clouds.Count];
            for (int i = 0; i < clouds.Count; i++)
                d.clouds[i] = new CloudSave { id = clouds[i].id, until = clouds[i].until, voxels = clouds[i].voxels.ToArray(), shown = clouds[i].shown.ToArray() };

            d.order = new int[order.Count];
            for (int i = 0; i < order.Count; i++) d.order[i] = order[i].id;
            d.stash = new string[stash.Count];
            for (int i = 0; i < stash.Count; i++) d.stash[i] = stash[i].name;
            return d;
        }

        /// <summary>
        /// 把存檔資料套回這場戰局。要對一場剛由 Scenario.Create() 建好的戰局呼叫
        /// （同樣的地圖、同樣的出場人物與物件順序），存檔只覆蓋會變動的部分。
        /// 資料對不上（版本不同、人數不符）回傳 false，戰局維持原樣。
        /// </summary>
        public bool Restore(SaveData d)
        {
            if (d != null && (d.place ?? "") != place) return false; // 存檔是另一個場景的
            if (d == null || d.version != SaveData.Current || d.units == null || d.units.Length != units.Count || d.props == null || d.props.Length < 1) return false;
            var w = world;

            // 人
            for (int i = 0; i < w.occ.Length; i++) w.occ[i] = -1;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                var s = d.units[i];
                u.hp = s.hp; u.qi = s.qi; u.x = s.x; u.y = s.y; u.z = s.z; u.fx = s.fx; u.fy = s.fy; u.init = s.init; u.moveLeft = s.moveLeft;
                u.homeCell = s.homeCell; u.homeFx = s.homeFx; u.homeFy = s.homeFy; u.scan = s.scan; u.knownCell = s.knownCell; u.knownAt = s.knownAt; u.stealth = s.stealth;
                u.fright = s.fright; u.blind = s.blind; u.poison = s.poison; u.bleed = s.bleed; u.tangle = s.tangle; u.defied = s.defied; u.baseOutfit = (Outfit)s.baseOutfit; u.bless = s.bless;
                u.alive = s.alive; u.fled = s.fled; u.actionLeft = s.actionLeft; u.bonusLeft = s.bonusLeft; u.freeLeft = s.freeLeft; u.reactionLeft = s.reactionLeft;
                u.guard = s.guard; u.light = s.light; u.stun = s.stun; u.surprised = s.surprised; u.ambushed = s.ambushed; u.inSight = s.inSight; u.ran = s.ran; u.venom = s.venom;
                u.hone = s.hone; u.mend = s.mend; u.silver = s.silver; u.fleeTurns = s.fleeTurns; u.shaken = s.shaken; u.fleeing = s.fleeing;
                if (s.look != null) u.look = s.look;
                for (int k = 0; k < u.gear.Length; k++) u.gear[k] = s.gear != null && k < s.gear.Length ? Gears.Find(s.gear[k]) : null;
                u.RefreshGear();
                u.bag.Clear();
                if (s.bagItems != null)
                    for (int k = 0; k < s.bagItems.Length; k++)
                    {
                        var it = Item.Find(s.bagItems[k]);
                        if (it != null) { var stack = new ItemStack { item = it, n = s.bagCounts[k] }; StampSeq(stack); u.bag.Add(stack); }
                    }
                if (u.alive) w.occ[CellOf(u)] = (short)u.id;
            }

            // 物件：先把碰撞體積全部清掉，再依存檔重蓋
            Array.Clear(w.blocked, 0, w.blocked.Length);
            Array.Clear(w.wall, 0, w.wall.Length);
            Array.Clear(w.smoke, 0, w.smoke.Length);
            for (int i = 0; i < w.propAt.Length; i++) w.propAt[i] = -1;
            for (int i = 0; i < d.props.Length; i++)
            {
                var s = d.props[i];
                Prop p;
                if (i < props.Count) p = props[i];
                else { p = new Prop { id = props.Count }; props.Add(p); } // 戰鬥中才出現的：鐵蒺藜、掉落物
                p.kind = (PropKind)s.kind; p.cell = s.cell; p.dc = s.dc; p.attitude = s.attitude; p.tried = s.tried; p.fx = s.fx; p.fy = s.fy;
                p.name = s.name; p.item = Item.Find(s.item); p.gone = s.gone; p.open = s.open; p.found = s.found; p.guided = s.guided; p.gave = s.gave;
                p.link = s.link; p.silver = s.silver; p.owner = s.owner; p.shop = (ShopKind)s.shop; p.sx = Math.Max(1, s.sx); p.sy = Math.Max(1, s.sy);
                p.items.Clear();
                if (s.items != null) foreach (var n in s.items) { var it = Item.Find(n); if (it != null) p.items.Add(it); }
                p.gears.Clear();
                if (s.gears != null) foreach (var gn in s.gears) { var g = Gears.Find(gn); if (g != null) p.gears.Add(g); }
            }
            foreach (var p in props)
            {
                if (p.Solid) Stamp(p, p.cell, true);
                if (p.Armed && p.found) w.blocked[p.cell] = true; // 已發現的絆索不能走
            }

            // 濃煙
            clouds.Clear();
            if (d.clouds != null)
                foreach (var c in d.clouds)
                {
                    var cl = new Cloud { id = c.id, until = c.until };
                    cl.voxels.AddRange(c.voxels);
                    cl.shown.AddRange(c.shown);
                    foreach (int v in c.voxels) w.smoke[v] = true;
                    clouds.Add(cl);
                }

            // 回合與進度
            order.Clear();
            if (d.order != null) foreach (int id in d.order) order.Add(units[id]);
            turn = d.turn; round = d.round; clock = d.clock; over = d.over; alerts = d.alerts;
            foesAtStart = d.foesAtStart; exploreClock = d.exploreClock; cloudSeq = d.cloudSeq;
            exploring = d.exploring; started = d.started; moraleHalf = d.moraleHalf; moraleBoss = d.moraleBoss; silver = d.silver;
            active = d.active >= 0 && d.active < units.Count ? units[d.active] : null;
            combatPending = false; inTick = false;

            outerJson = d.outerJson ?? "";
            stash.Clear();
            if (d.stash != null) foreach (var n in d.stash) { var g = Gears.Find(n); if (g != null) stash.Add(g); }
            if (d.seen != null && d.seen.Length == vision.seen.Length) Array.Copy(d.seen, vision.seen, d.seen.Length);
            UpdateVision();
            Drain();
            return true;
        }

        /// <summary>把隊伍的狀態打包（換場景時帶著走）。</summary>
        public PartySave SaveParty()
        {
            var d = Save();
            var list = new List<UnitSave>();
            for (int i = 0; i < units.Count; i++) if (units[i].team == 0) list.Add(d.units[i]);
            return new PartySave { heroes = list.ToArray(), stash = d.stash, silver = silver };
        }

        /// <summary>
        /// 把另一個場景帶來的隊伍狀態套到這裡的三位俠客身上：氣血、內力、裝備、行囊、相貌、身上的狀態，
        /// 以及共用行李與銀兩。位置與回合資源不動（那是這個場景自己的事）。
        /// </summary>
        public void LoadParty(PartySave p)
        {
            if (p == null || p.heroes == null) return;
            int k = 0;
            foreach (var u in units)
            {
                if (u.team != 0 || k >= p.heroes.Length) continue;
                var s = p.heroes[k++];
                u.hp = s.hp; u.qi = s.qi; u.poison = s.poison; u.bleed = s.bleed; u.bless = s.bless; u.blind = s.blind;
                u.hone = s.hone; u.mend = s.mend; u.venom = s.venom; u.baseOutfit = (Outfit)s.baseOutfit;
                if (s.look != null) u.look = s.look.Clone();
                for (int i = 0; i < u.gear.Length; i++) u.gear[i] = s.gear != null && i < s.gear.Length ? Gears.Find(s.gear[i]) : null;
                u.RefreshGear();
                u.bag.Clear();
                if (s.bagItems != null)
                    for (int i = 0; i < s.bagItems.Length; i++)
                    {
                        var it = Item.Find(s.bagItems[i]);
                        if (it != null) { var stack = new ItemStack { item = it, n = s.bagCounts[i] }; StampSeq(stack); u.bag.Add(stack); }
                    }
                if (u.alive && !s.alive) { u.alive = false; world.occ[CellOf(u)] = -1; }
            }
            stash.Clear();
            if (p.stash != null) foreach (var n in p.stash) { var g = Gears.Find(n); if (g != null) stash.Add(g); }
            silver = p.silver;
        }

        /// <summary>目前場上的每一團濃煙（讀檔後畫面層要把它們畫回來）。</summary>
        public void EachCloud(Action<int, int[]> visit)
        {
            foreach (var c in clouds) visit(c.id, c.shown.ToArray());
        }
    }
}
