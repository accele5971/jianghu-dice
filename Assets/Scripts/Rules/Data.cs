namespace Jianghu
{
    public enum Stat { Str, Dex, Con, Int, Wis, Cha }

    public enum SkillKind { Attack, Aoe, Light, Guard, Heal, Rest, Shout }

    public sealed class Skill
    {
        public string name, desc = "";
        public SkillKind kind;
        public bool bonus;      // true：附加動作；false：動作
        public int cost;        // 內力
        public bool melee;
        public int range;       // 遠程射程
        public int radius;      // 範圍半徑
        public Stat ability;
        public DiceExpr dmg;
        public int hits = 1;
        public int push;        // 擊退格數
        public bool stun;       // 命中後可定身

        // 兵器相關
        public int reach = 1;       // 近戰最遠打到幾格
        public int critMin = 20;    // d20 擲出這個數以上且命中就是會心
        public int critDice = 2;    // 會心時傷害骰的倍數
        public Sweep sweep;         // 波及方式（只有兵器的基本攻擊會有）
        public bool basic;          // 兵器的基本攻擊（由手上的兵器產生）
        public WeaponClass needs;   // 這一招是哪一類兵器的招式；None 表示不限
        public GearFx fx;           // 名器的專屬特效（基本攻擊才有）
        public bool strict;         // true：兵器不合就不能用；false：可以勉強用，命中 −2、傷害 −2
    }

    public sealed class UnitTemplate
    {
        public string name, cls;
        public Look look;
        public int[] stats; // 臂力、身法、根骨、悟性、定力、福緣
        public int hp, qi, move, jump; // move、jump 是不含裝備的基礎值；防禦完全由裝備決定
        public Gear[] gear;            // 初始裝備（各自放進對應的欄位）
        public Item[] items;           // 開局帶在行囊裡的道具
        public bool sneak; // 擅長潛行
        public Talent talents; // 技能熟練
        public bool boss;      // 頭目
        public Skill[] skills;
    }

    public static class Db
    {
        public const int Prof = 2;

        public static readonly string[] StatNames = { "臂力", "身法", "根骨", "悟性", "定力", "福緣" };

        // 我方武學
        public static readonly Skill Sword = new Skill { name = "青鋒劍", kind = SkillKind.Attack, melee = true, ability = Stat.Dex, dmg = new DiceExpr(1, 8), desc = "近身一劍。" };
        public static readonly Skill Flurry = new Skill { name = "流雲三劍", kind = SkillKind.Attack, cost = 5, melee = true, ability = Stat.Dex, dmg = new DiceExpr(1, 6), hits = 3, needs = WeaponClass.Sword, desc = "連出三劍，各自擲骰。劍招：拿別的兵器也能使，但命中與傷害各 −2。" };
        public static readonly Skill Fist = new Skill { name = "開碑拳", kind = SkillKind.Attack, melee = true, ability = Stat.Str, dmg = new DiceExpr(1, 10), desc = "剛猛一拳。" };
        public static readonly Skill Palm = new Skill { name = "震山掌", kind = SkillKind.Attack, cost = 3, melee = true, ability = Stat.Str, dmg = new DiceExpr(2, 6), push = 3, needs = WeaponClass.Fist, desc = "擊退 3 格。打落高處會摔傷，撞牆亦傷。掌法：拿著兵器也能使，但命中與傷害各 −2。" };
        public static readonly Skill Knife = new Skill { name = "柳葉飛刀", kind = SkillKind.Attack, range = 6, ability = Stat.Dex, dmg = new DiceExpr(1, 6), desc = "射程 6。居高臨下射得更遠。" };
        public static readonly Skill Rain = new Skill { name = "漫天花雨", kind = SkillKind.Aoe, cost = 4, range = 6, radius = 1, ability = Stat.Dex, dmg = new DiceExpr(3, 4), needs = WeaponClass.Thrown, strict = true, desc = "3×3 範圍。身法豁免成功則傷害減半。手上必須是暗器。" };
        public static readonly Skill Seal = new Skill { name = "點穴", kind = SkillKind.Attack, cost = 3, melee = true, ability = Stat.Dex, dmg = new DiceExpr(1, 4), stun = true, desc = "命中後目標須過根骨豁免，否則定身一回合。" };
        public static readonly Skill Light = new Skill { name = "梯雲縱", kind = SkillKind.Light, bonus = true, cost = 2, desc = "本回合移動 +4、可攀高度 +3，脫離敵人時不會遭借機攻擊。" };
        public static readonly Skill Guard = new Skill { name = "金鐘罩", kind = SkillKind.Guard, bonus = true, cost = 2, desc = "防禦 +4，直到自己下回合開始。" };
        public static readonly Skill Heal = new Skill { name = "運功療傷", kind = SkillKind.Heal, cost = 3, dmg = new DiceExpr(2, 6), desc = "回復 2d6 + 定力調整值的氣血。" };
        public static readonly Skill Rest = new Skill { name = "調息", kind = SkillKind.Rest, dmg = new DiceExpr(1, 4), desc = "回復 1d4 + 定力調整值的內力。" };

        public static readonly Skill Shout = new Skill { name = "喝令", kind = SkillKind.Shout, range = 6, desc = "威嚇檢定（臂力或福緣）對抗敵人的膽氣。成功：負傷過半的嘍囉投降，其餘膽寒（攻擊劣勢）。失敗後對他無效。會暴露位置。" };

        // 敵方武學
        public static readonly Skill Saber = new Skill { name = "單刀", kind = SkillKind.Attack, melee = true, ability = Stat.Str, dmg = new DiceExpr(1, 6) };
        public static readonly Skill Bow = new Skill { name = "長弓", kind = SkillKind.Attack, range = 7, ability = Stat.Dex, dmg = new DiceExpr(1, 6) };
        public static readonly Skill Cleaver = new Skill { name = "鬼頭刀", kind = SkillKind.Attack, melee = true, ability = Stat.Str, dmg = new DiceExpr(1, 10) };
        public static readonly Skill Quake = new Skill { name = "裂地斬", kind = SkillKind.Attack, cost = 3, melee = true, ability = Stat.Str, dmg = new DiceExpr(2, 6), push = 2, needs = WeaponClass.Blade, desc = "擊退 2 格。刀招：拿別的兵器也能使，但命中與傷害各 −2。" };

        public static readonly UnitTemplate[] Heroes =
        {
            new UnitTemplate { name = "沈青嵐", cls = "劍客", look = Looks.Swordsman, stats = new[] { 12, 17, 13, 12, 14, 12 }, hp = 34, qi = 12, move = 6, jump = 2,
                talents = Talent.Persuade | Talent.Perception,
                gear = new[] { Gears.QingFeng, Gears.ConeHat, Gears.Leather, Gears.Greaves, Gears.Shoes, Gears.Sheath },
                items = new[] { Item.Salve, Item.QiPill, Item.Grapnel, Item.Caltrops },
                skills = new[] { Flurry, Heal, Light, Rest, Shout } },
            new UnitTemplate { name = "石破山", cls = "力士", look = Looks.Brawler, stats = new[] { 18, 10, 16, 8, 12, 10 }, hp = 46, qi = 8, move = 6, jump = 1,
                talents = Talent.Athletics | Talent.Intimidate,
                gear = new[] { Gears.Bracers, Gears.Plate, Gears.Tassets, Gears.Shoes },
                items = new[] { Item.Salve, Item.Thunder },
                skills = new[] { Palm, Guard, Rest, Shout } },
            new UnitTemplate { name = "唐小蝶", cls = "暗器", look = Looks.KnifeMaiden, stats = new[] { 9, 18, 11, 15, 12, 14 }, hp = 26, qi = 12, move = 6, jump = 2, sneak = true,
                talents = Talent.Sleight | Talent.Deceive,
                gear = new[] { Gears.WillowKnives, Gears.Robe, Gears.Greaves, Gears.Shoes },
                items = new[] { Item.Salve, Item.QiPill, Item.Lime, Item.SmokeBomb, Item.Venom },
                skills = new[] { Rain, Seal, Light, Rest, Shout } },
        };

        // 敵人的基本攻擊同樣來自手上的兵器
        public static readonly UnitTemplate Bandit = new UnitTemplate { name = "山賊", cls = "嘍囉", look = Looks.Bandit, stats = new[] { 13, 12, 12, 8, 10, 8 }, hp = 14, move = 5, jump = 1,
            gear = new[] { Gears.Hatchet, Gears.Leather, Gears.Trousers }, skills = new Skill[0] };
        public static readonly UnitTemplate Archer = new UnitTemplate { name = "弓手", cls = "嘍囉", look = Looks.Archer, stats = new[] { 10, 14, 10, 8, 12, 8 }, hp = 11, move = 5, jump = 1,
            gear = new[] { Gears.ShortBow, Gears.Robe, Gears.Trousers }, skills = new Skill[0] };
        public static readonly UnitTemplate Boss = new UnitTemplate { name = "厲無咎", cls = "寨主", look = Looks.Warlord, stats = new[] { 16, 12, 16, 10, 12, 14 }, hp = 45, qi = 9, move = 6, jump = 2, boss = true,
            gear = new[] { Gears.GhostBlade, Gears.Helmet, Gears.Plate, Gears.Tassets }, skills = new[] { Quake } };
    }
}
