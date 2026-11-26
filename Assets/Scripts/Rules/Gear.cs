using System;
using System.Collections.Generic;

namespace Jianghu
{
    /// <summary>裝備欄位，共 11 個。飾品有兩欄，飾品類的裝備兩欄都能放。</summary>
    public enum Slot { MainHand, OffHand, Head, Upper, Lower, Hands, Waist, Feet, Back, Charm1, Charm2 }

    /// <summary>兵器種類：決定攻擊範圍、會心門檻與傷害骰。</summary>
    public enum WeaponClass { None, Sword, Blade, Polearm, Dagger, Blunt, Fist, Flexible, Thrown, Bow, Exotic }

    /// <summary>護甲分類。</summary>
    public enum ArmorClass { None, Cloth, Light, Medium, Heavy }

    /// <summary>名器的專屬特效（普通兵器沒有）。</summary>
    public enum GearFx
    {
        None,
        Tangle,   // 纏繞：命中後目標攻擊劣勢，到他下個回合結束
        Reroll,   // 重劍無鋒：傷害骰擲出 1、2 重擲一次
        Hook,     // 鉤拉：命中 2 格外的目標後把他拉到身前
        Bleed,    // 血槽：命中後流血三回合，每回合 1d4
        CritStun, // 封穴：會心時定身
        Poison,   // 淬毒：命中後根骨豁免，失敗則中毒
        Deflect,  // 撥擋：每輪一次，用反應讓一次遠程攻擊劣勢
        Daze,     // 亂神：命中後定力豁免，失敗則膽寒
        Seal,     // 點穴：命中後根骨豁免，失敗則定身
    }

    /// <summary>基本攻擊的波及方式。</summary>
    public enum Sweep { Single, Arc, Line }

    /// <summary>一個兵器種類的基本數值。</summary>
    public sealed class WeaponClassInfo
    {
        public string name;
        public Stat ability;
        public DiceExpr dmg;
        public int critMin = 20;  // d20 擲出這個數以上且命中，就是會心
        public int critDice = 2;  // 會心時傷害骰的倍數
        public int reach = 1;     // 近戰：最遠打到幾格
        public int range;         // 遠程：射程（大於 0 表示是遠程兵器）
        public Sweep sweep;
        public string note = "";
    }

    /// <summary>一件裝備。兵器、護甲、飾品共用這個類別，用不到的欄位留預設值。</summary>
    public sealed class Gear
    {
        public string name, desc = "";
        public Slot slot;

        // 兵器
        public WeaponClass cls;
        public DiceExpr dmg;        // 有填（n > 0）就取代種類的傷害骰
        public int reachMod;        // 增減攻擊範圍（近戰是觸及格數，遠程是射程）
        public bool twoHanded;      // 佔用主手與副手
        public bool heavy;          // 沉重：潛行有劣勢
        public WeaponKind model;    // 手上顯示的模型
        public int range;           // 大於 0：不管種類，基本攻擊改為這個射程的遠程攻擊（洞簫的音波）
        public GearFx fx;           // 專屬特效；有特效的就是名器

        // 護甲
        public ArmorClass armorClass;
        public int armor;           // 防禦加值
        public int dexCap = 99;     // 身法加值上限
        public bool noisy;          // 行動有聲響：潛行有劣勢
        public Headwear headwear;   // 頭部裝備顯示的模型
        public int outfit = -1;     // 上身裝備顯示的服裝款式（Outfit）；-1 表示穿在原本的衣服外面，不改款式

        // 加成
        public int move, jump, bag, stealth, perception;
        public Talent skill;        // 對哪一項技能檢定有加成
        public int skillBonus;

        public bool IsWeapon { get { return cls != WeaponClass.None; } }
        public bool Named { get { return fx != GearFx.None; } }
        public bool IsCharm { get { return slot == Slot.Charm1 || slot == Slot.Charm2; } }

        /// <summary>這件裝備能不能放進某個欄位。</summary>
        public bool Fits(Slot s) { return IsCharm ? s == Slot.Charm1 || s == Slot.Charm2 : s == slot; }
    }

    /// <summary>裝備目錄：兵器種類表、所有裝備、初始配裝。</summary>
    public static class Gears
    {
        public const int SlotCount = 11;
        public static readonly string[] SlotNames = { "主手", "副手", "頭", "上身", "下身", "手", "腰", "足", "背", "飾品", "飾品" };
        public static readonly string[] ArmorNames = { "", "布衣", "輕甲", "中甲", "重甲" };

        public static readonly WeaponClassInfo[] Classes =
        {
            new WeaponClassInfo { name = "空手", ability = Stat.Str, dmg = new DiceExpr(1, 4) },
            new WeaponClassInfo { name = "劍", ability = Stat.Dex, dmg = new DiceExpr(1, 8), critMin = 19, note = "輕靈，容易打出會心。" },
            new WeaponClassInfo { name = "刀", ability = Stat.Str, dmg = new DiceExpr(1, 8), sweep = Sweep.Arc, note = "橫掃：波及目標兩側、同在你身邊的敵人（傷害減半）。" },
            new WeaponClassInfo { name = "長兵", ability = Stat.Str, dmg = new DiceExpr(1, 8), reach = 2, sweep = Sweep.Line, note = "可打 2 格外；直線貫穿：波及同一直線上的另一名敵人（傷害減半）。" },
            new WeaponClassInfo { name = "短兵", ability = Stat.Dex, dmg = new DiceExpr(1, 4), critMin = 18, note = "傷害低，但最容易打出會心。" },
            new WeaponClassInfo { name = "鈍器", ability = Stat.Str, dmg = new DiceExpr(1, 10), critDice = 3, note = "勢大力沉；會心時傷害骰三倍。" },
            new WeaponClassInfo { name = "拳腳", ability = Stat.Str, dmg = new DiceExpr(1, 6), note = "貼身短打。" },
            new WeaponClassInfo { name = "軟兵", ability = Stat.Dex, dmg = new DiceExpr(1, 6), reach = 3, note = "鞭索之屬，可打 3 格外。" },
            new WeaponClassInfo { name = "暗器", ability = Stat.Dex, dmg = new DiceExpr(1, 6), critMin = 19, range = 6, note = "射程 6；容易打出會心。" },
            new WeaponClassInfo { name = "弓弩", ability = Stat.Dex, dmg = new DiceExpr(1, 8), range = 8, note = "射程 8。" },
            new WeaponClassInfo { name = "奇門", ability = Stat.Dex, dmg = new DiceExpr(1, 4), note = "扇、簫、筆之類的奇門兵器。" },
        };

        public static WeaponClassInfo Info(WeaponClass c) { return Classes[(int)c]; }

        public static readonly List<Gear> All = new List<Gear>();

        static Gear Add(Gear g) { All.Add(g); return g; }

        static Gear W(string name, WeaponClass cls, WeaponKind model, string desc = "")
        {
            return Add(new Gear { name = name, slot = Slot.MainHand, cls = cls, model = model, desc = desc });
        }

        // ── 兵器 ──
        public static readonly Gear QingFeng = W("青鋒劍", WeaponClass.Sword, WeaponKind.Sword, "沈家祖傳的三尺青鋒。");
        public static readonly Gear LongSword = W("長劍", WeaponClass.Sword, WeaponKind.Sword);
        public static readonly Gear HeavySword = With(W("重劍", WeaponClass.Sword, WeaponKind.Cleaver, "無鋒重劍，須雙手持握。"), g => { g.dmg = new DiceExpr(1, 10); g.twoHanded = true; g.heavy = true; });
        public static readonly Gear Saber = W("單刀", WeaponClass.Blade, WeaponKind.Saber);
        public static readonly Gear Hatchet = With(W("柴刀", WeaponClass.Blade, WeaponKind.Saber, "山賊慣用的粗刀。"), g => g.dmg = new DiceExpr(1, 6));
        public static readonly Gear GhostBlade = With(W("鬼頭刀", WeaponClass.Blade, WeaponKind.Cleaver, "刀背鑄有鬼面，沉重異常。"), g => { g.dmg = new DiceExpr(1, 10); g.heavy = true; });
        public static readonly Gear Spear = With(W("長槍", WeaponClass.Polearm, WeaponKind.Spear), g => g.twoHanded = true);
        public static readonly Gear Staff = With(W("齊眉棍", WeaponClass.Polearm, WeaponKind.Staff), g => { g.dmg = new DiceExpr(1, 6); g.twoHanded = true; });
        public static readonly Gear SerpentSpear = With(W("丈八蛇矛", WeaponClass.Polearm, WeaponKind.Spear, "矛身特長，比尋常長兵多打一格。"), g => { g.reachMod = 1; g.twoHanded = true; g.heavy = true; });
        public static readonly Gear DaggerG = W("匕首", WeaponClass.Dagger, WeaponKind.TwinBlades);
        public static readonly Gear TwinBlades = With(W("雙刃", WeaponClass.Dagger, WeaponKind.TwinBlades, "左右各一柄短刃。"), g => { g.dmg = new DiceExpr(1, 6); g.twoHanded = true; });
        public static readonly Gear Mace = With(W("銅錘", WeaponClass.Blunt, WeaponKind.Mace), g => g.heavy = true);
        public static readonly Gear Bracers = With(W("鐵護腕", WeaponClass.Fist, WeaponKind.Fists, "裹住雙拳的精鐵護腕。"), g => g.dmg = new DiceExpr(1, 8));
        public static readonly Gear NineWhip = W("九節鞭", WeaponClass.Flexible, WeaponKind.Staff);
        public static readonly Gear MeteorHammer = With(W("流星錘", WeaponClass.Flexible, WeaponKind.Mace, "鏈長錘重，比尋常軟兵多打一格。"), g => { g.dmg = new DiceExpr(1, 8); g.reachMod = 1; g.heavy = true; });
        public static readonly Gear WillowKnives = W("柳葉飛刀", WeaponClass.Thrown, WeaponKind.Knives, "唐門獨有的薄刃飛刀。");
        public static readonly Gear Pebbles = With(W("飛蝗石", WeaponClass.Thrown, WeaponKind.Knives, "隨手可得，但打不遠。"), g => { g.dmg = new DiceExpr(1, 4); g.reachMod = -1; });
        public static readonly Gear LongBow = With(W("長弓", WeaponClass.Bow, WeaponKind.Bow), g => g.twoHanded = true);
        public static readonly Gear ShortBow = With(W("短弓", WeaponClass.Bow, WeaponKind.Bow, "輕便，射程較短。"), g => { g.dmg = new DiceExpr(1, 6); g.reachMod = -1; g.twoHanded = true; });
        public static readonly Gear Fan = W("摺扇", WeaponClass.Exotic, WeaponKind.Fan);
        public static readonly Gear Flute = W("洞簫", WeaponClass.Exotic, WeaponKind.Flute);

        // ── 名器：各有一個專屬特效 ──
        public static readonly Gear SoftSword = With(W("繞指柔", WeaponClass.Sword, WeaponKind.Sword, "百煉軟劍，可纏腰而藏。"), g => { g.reachMod = 1; g.fx = GearFx.Tangle; });
        public static readonly Gear DarkIron = With(W("玄鐵重劍", WeaponClass.Sword, WeaponKind.Cleaver, "重劍無鋒，大巧不工。"), g => { g.dmg = new DiceExpr(1, 10); g.twoHanded = true; g.heavy = true; g.fx = GearFx.Reroll; });
        public static readonly Gear Halberd = With(W("方天畫戟", WeaponClass.Polearm, WeaponKind.Spear, "戟旁月牙可鉤可啄。"), g => { g.twoHanded = true; g.fx = GearFx.Hook; });
        public static readonly Gear WolfClub = With(W("狼牙棒", WeaponClass.Blunt, WeaponKind.Mace, "棒頭滿佈倒刺。"), g => { g.heavy = true; g.fx = GearFx.Bleed; });
        public static readonly Gear BoneNeedles = With(W("透骨針", WeaponClass.Thrown, WeaponKind.Knives, "細如牛毛，專打穴道。"), g => { g.dmg = new DiceExpr(1, 4); g.fx = GearFx.CritStun; });
        public static readonly Gear SleeveBolt = With(W("袖箭", WeaponClass.Bow, WeaponKind.Knives, "藏於袖中的機括，箭頭淬毒；單手可用，射程短。"), g => { g.dmg = new DiceExpr(1, 6); g.reachMod = -3; g.fx = GearFx.Poison; });
        public static readonly Gear CosmosFan = With(W("乾坤扇", WeaponClass.Exotic, WeaponKind.Fan, "精鋼為骨，開闔之間可撥落暗器。"), g => { g.dmg = new DiceExpr(1, 6); g.fx = GearFx.Deflect; });
        public static readonly Gear TideFlute = With(W("碧海潮生簫", WeaponClass.Exotic, WeaponKind.Flute, "以內力吹奏，簫聲傷人心神。"), g => { g.range = 4; g.fx = GearFx.Daze; });
        public static readonly Gear JudgeBrush = With(W("判官筆", WeaponClass.Exotic, WeaponKind.Flute, "鐵筆專點人身要穴。"), g => { g.dmg = new DiceExpr(1, 6); g.fx = GearFx.Seal; });

        public static readonly string[] FxText =
        {
            "",
            "纏繞：命中後目標攻擊劣勢，到他下個回合結束",
            "重劍無鋒：傷害骰擲出 1、2 可重擲一次",
            "鉤拉：命中 2 格外的敵人後把他拉到身前",
            "血槽：命中後流血三回合，每回合 1d4",
            "封穴：會心時目標定身一回合",
            "淬毒：命中後目標根骨豁免（DC 12），失敗則中毒",
            "撥擋：每輪一次，自動讓一次射向自己的遠程攻擊劣勢",
            "亂神：命中後目標定力豁免，失敗則膽寒",
            "點穴：命中後目標根骨豁免，失敗則定身一回合",
        };

        // ── 副手 ──
        public static readonly Gear RattanShield = Add(new Gear { name = "藤牌", slot = Slot.OffHand, armorClass = ArmorClass.Light, armor = 1 });
        public static readonly Gear IronShield = Add(new Gear { name = "鐵盾", slot = Slot.OffHand, armorClass = ArmorClass.Heavy, armor = 2, noisy = true });

        // ── 頭 ──
        public static readonly Gear ConeHat = Add(new Gear { name = "斗笠", slot = Slot.Head, armorClass = ArmorClass.Cloth, headwear = Headwear.ConeHat });
        public static readonly Gear Headwrap = Add(new Gear { name = "頭巾", slot = Slot.Head, armorClass = ArmorClass.Cloth, headwear = Headwear.Headwrap });
        public static readonly Gear Hood = Add(new Gear { name = "兜帽", slot = Slot.Head, armorClass = ArmorClass.Cloth, headwear = Headwear.Hood, stealth = 1, desc = "遮住面目。" });
        public static readonly Gear Helmet = Add(new Gear { name = "鐵盔", slot = Slot.Head, armorClass = ArmorClass.Heavy, armor = 1, headwear = Headwear.Helmet });

        // ── 上身 ──
        public static readonly Gear Robe = Add(new Gear { name = "布袍", slot = Slot.Upper, armorClass = ArmorClass.Cloth });
        public static readonly Gear NightSuit = Add(new Gear { name = "夜行衣", slot = Slot.Upper, armorClass = ArmorClass.Cloth, stealth = 2, outfit = (int)Outfit.ShortJacket, desc = "一身玄黑，利於潛行。" });
        public static readonly Gear Leather = Add(new Gear { name = "皮甲", slot = Slot.Upper, armorClass = ArmorClass.Light, armor = 1 });
        public static readonly Gear Chain = Add(new Gear { name = "鎖子甲", slot = Slot.Upper, armorClass = ArmorClass.Medium, armor = 2, dexCap = 2, noisy = true, outfit = (int)Outfit.Armor });
        public static readonly Gear Plate = Add(new Gear { name = "鐵甲", slot = Slot.Upper, armorClass = ArmorClass.Heavy, armor = 3, dexCap = 0, noisy = true, move = -1, outfit = (int)Outfit.Armor, desc = "厚重，行動遲緩。" });

        // ── 下身 ──
        public static readonly Gear Trousers = Add(new Gear { name = "布褲", slot = Slot.Lower, armorClass = ArmorClass.Cloth });
        public static readonly Gear Greaves = Add(new Gear { name = "皮護腿", slot = Slot.Lower, armorClass = ArmorClass.Light, armor = 1 });
        public static readonly Gear Tassets = Add(new Gear { name = "鐵裙甲", slot = Slot.Lower, armorClass = ArmorClass.Heavy, armor = 1, noisy = true });

        // ── 手 ──
        public static readonly Gear Wraps = Add(new Gear { name = "皮護腕", slot = Slot.Hands, armorClass = ArmorClass.Light, skill = Talent.Sleight, skillBonus = 1, desc = "貼手靈便，巧手 +1。" });
        public static readonly Gear Vambraces = Add(new Gear { name = "鐵臂鎧", slot = Slot.Hands, armorClass = ArmorClass.Heavy, armor = 1 });

        // ── 腰 ──
        public static readonly Gear Sash = Add(new Gear { name = "寬腰帶", slot = Slot.Waist, skill = Talent.Athletics, skillBonus = 1, desc = "束腰提勁，蠻力 +1。" });
        public static readonly Gear Satchel = Add(new Gear { name = "百寶囊", slot = Slot.Waist, bag = 2, desc = "行囊多 2 格。" });

        // ── 足 ──
        public static readonly Gear Shoes = Add(new Gear { name = "布鞋", slot = Slot.Feet, armorClass = ArmorClass.Cloth });
        public static readonly Gear SwiftBoots = Add(new Gear { name = "快靴", slot = Slot.Feet, armorClass = ArmorClass.Light, move = 1, desc = "移動 +1。" });
        public static readonly Gear CloudBoots = Add(new Gear { name = "登雲靴", slot = Slot.Feet, armorClass = ArmorClass.Light, jump = 1, desc = "輕功 +1。" });
        public static readonly Gear IronBoots = Add(new Gear { name = "鐵靴", slot = Slot.Feet, armorClass = ArmorClass.Heavy, armor = 1, noisy = true });

        // ── 背 ──
        public static readonly Gear Sheath = Add(new Gear { name = "劍鞘", slot = Slot.Back });
        public static readonly Gear Quiver = Add(new Gear { name = "箭袋", slot = Slot.Back });
        public static readonly Gear Cloak = Add(new Gear { name = "披風", slot = Slot.Back, stealth = 1, desc = "裹身藏形，潛行 +1。" });

        // ── 飾品 ──
        public static readonly Gear Jade = Add(new Gear { name = "玉佩", slot = Slot.Charm1, perception = 1, desc = "溫潤清心，警覺 +1。" });
        public static readonly Gear Mirror = Add(new Gear { name = "護心鏡", slot = Slot.Charm1, armor = 1, desc = "防禦 +1。" });
        public static readonly Gear Tally = Add(new Gear { name = "虎符", slot = Slot.Charm1, skill = Talent.Intimidate, skillBonus = 2, desc = "威嚇 +2。" });

        static Gear With(Gear g, Action<Gear> set) { set(g); return g; }

        /// <summary>隊伍一開始放在行李裡的備用裝備。</summary>
        public static List<Gear> StarterStash()
        {
            return new List<Gear>
            {
                LongSword, Saber, Spear, SerpentSpear, NineWhip, Mace, DaggerG, LongBow, Fan,
                SoftSword, DarkIron, Halberd, WolfClub, BoneNeedles, SleeveBolt, CosmosFan, TideFlute, JudgeBrush,
                MeteorHammer, IronShield, Vambraces, IronBoots, Jade,
                RattanShield, Hood, Helmet, NightSuit, Chain, Wraps, Sash, Satchel, SwiftBoots, CloudBoots, Cloak, Mirror, Tally,
            };
        }

        /// <summary>一件裝備的一行摘要（面板與提示用）。</summary>
        public static string Summary(Gear g)
        {
            if (g == null) return "（空）";
            var sb = new System.Text.StringBuilder();
            if (g.IsWeapon)
            {
                var c = Info(g.cls);
                var d = g.dmg.n > 0 ? g.dmg : c.dmg;
                sb.Append(c.name).Append(' ').Append(d);
                if (g.range > 0) sb.Append(" 射程 ").Append(g.range);
                else if (c.range > 0) sb.Append(" 射程 ").Append(c.range + g.reachMod);
                else if (c.reach + g.reachMod > 1) sb.Append(" 觸及 ").Append(c.reach + g.reachMod);
                if (c.critMin < 20) sb.Append(" 會心 ").Append(c.critMin).Append('+');
                if (c.critDice > 2) sb.Append(" 會心×").Append(c.critDice);
                if (c.sweep == Sweep.Arc) sb.Append(" 橫掃");
                if (c.sweep == Sweep.Line) sb.Append(" 貫穿");
                if (g.twoHanded) sb.Append(" 雙手");
                if (g.heavy) sb.Append(" 沉重");
                if (g.Named) sb.Append(" ★").Append(FxText[(int)g.fx].Substring(0, FxText[(int)g.fx].IndexOf('：')));
            }
            else
            {
                if (g.armorClass != ArmorClass.None) sb.Append(ArmorNames[(int)g.armorClass]);
                if (g.armor != 0) sb.Append(" 防禦+").Append(g.armor);
                if (g.dexCap < 99) sb.Append(" 身法上限+").Append(g.dexCap);
                if (g.noisy) sb.Append(" 有聲響");
            }
            if (g.move != 0) sb.Append(" 移動").Append(g.move > 0 ? "+" : "").Append(g.move);
            if (g.jump != 0) sb.Append(" 輕功+").Append(g.jump);
            if (g.stealth != 0) sb.Append(" 潛行+").Append(g.stealth);
            if (g.perception != 0) sb.Append(" 警覺+").Append(g.perception);
            if (g.bag != 0) sb.Append(" 行囊+").Append(g.bag);
            if (g.skillBonus != 0) sb.Append(' ').Append(TalentName(g.skill)).Append('+').Append(g.skillBonus);
            return sb.ToString().Trim();
        }

        public static string TalentName(Talent t)
        {
            switch (t)
            {
                case Talent.Athletics: return "蠻力";
                case Talent.Sleight: return "巧手";
                case Talent.Persuade: return "說服";
                case Talent.Intimidate: return "威嚇";
                case Talent.Deceive: return "欺瞞";
                case Talent.Perception: return "察覺";
            }
            return "";
        }
    }

    public enum ItemKind { Heal, Qi, Cure, Lime, Smoke, Bomb, Caltrops, Venom, Hook }

    /// <summary>道具要對什麼使用。</summary>
    public enum Aim { Self, Ally, Foe, Cell }

    /// <summary>消耗品。</summary>
    public sealed class Item
    {
        public string name, desc;
        public bool bonus;      // 用附加動作（否則用動作）
        public ItemKind kind;
        public Aim aim;
        public int range;       // 投擲或施用的距離
        public int radius;      // 範圍半徑（1 表示 3×3）
        public DiceExpr amount;

        public static readonly Item Salve = new Item { name = "金創藥", desc = "動作。回復 2d4+2 氣血，可餵給身邊的隊友。", kind = ItemKind.Heal, aim = Aim.Ally, range = 1, amount = new DiceExpr(2, 4, 2) };
        public static readonly Item QiPill = new Item { name = "養氣丹", desc = "附加動作。回復 1d4+1 內力。", bonus = true, kind = ItemKind.Qi, aim = Aim.Self, amount = new DiceExpr(1, 4, 1) };
        public static readonly Item Antidote = new Item { name = "解毒丹", desc = "動作。解除中毒，可餵給身邊的隊友。", kind = ItemKind.Cure, aim = Aim.Ally, range = 1 };
        public static readonly Item Lime = new Item { name = "石灰粉", desc = "動作。撒向 3 格內的敵人：身法豁免 DC 13，失敗則目盲到他下個回合結束。", kind = ItemKind.Lime, aim = Aim.Foe, range = 3 };
        public static readonly Item SmokeBomb = new Item { name = "煙霧彈", desc = "動作。擲向 4 格內：3×3 的濃煙持續到下一輪結束，隔著煙互相看不見。", kind = ItemKind.Smoke, aim = Aim.Cell, range = 4, radius = 1 };
        public static readonly Item Thunder = new Item { name = "霹靂彈", desc = "動作。擲向 5 格內：3×3 範圍 2d6，身法豁免 DC 13 減半，不分敵我；巨響會暴露位置。", kind = ItemKind.Bomb, aim = Aim.Cell, range = 5, radius = 1, amount = new DiceExpr(2, 6) };
        public static readonly Item Caltrops = new Item { name = "鐵蒺藜", desc = "動作。撒在身邊一格：敵人踏入時身法豁免 DC 13，失敗受 1d4 並停下。", kind = ItemKind.Caltrops, aim = Aim.Cell, range = 1, amount = new DiceExpr(1, 4) };
        public static readonly Item Venom = new Item { name = "淬毒藥", desc = "附加動作。抹在兵器上：下一次命中使目標中毒（根骨豁免 DC 12）。", bonus = true, kind = ItemKind.Venom, aim = Aim.Self };
        public static readonly Item Grapnel = new Item { name = "飛爪百鍊索", desc = "動作。盪到 5 格內、高低差 6 層內看得見的落腳處，不會引來借機攻擊。", kind = ItemKind.Hook, aim = Aim.Cell, range = 5 };
    }

    /// <summary>行囊裡的一疊道具。</summary>
    public sealed class ItemStack
    {
        public Item item;
        public int n;
    }
}
