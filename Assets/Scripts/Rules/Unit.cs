namespace Jianghu
{
    public sealed class Unit
    {
        public int id;
        public string name, cls;
        public int team; // 0 我方、1 敵方
        public Look look;
        public int[] stats;
        public int hp, maxHp, qi, maxQi;
        public int armor, move, jump;   // 由裝備算出來的現值（見 RefreshGear）
        public int baseMove, baseJump;  // 不含裝備的基礎值
        public Skill[] skills;          // 第 0 個永遠是手上兵器的基本攻擊，其後是自己的武學
        public Skill[] arts = new Skill[0]; // 自己的武學（不含基本攻擊）

        // 裝備與行囊
        public readonly Gear[] gear = new Gear[Gears.SlotCount];
        public readonly System.Collections.Generic.List<ItemStack> bag = new System.Collections.Generic.List<ItemStack>();
        public Outfit baseOutfit;       // 原本穿的衣服款式（脫掉鎧甲時換回這個）
        public int dexCap = 99;         // 護甲對身法加值的上限
        public bool noisy;              // 身上有會發出聲響或沉重的裝備：潛行有劣勢
        public int gearStealth, gearPerception;

        public Gear Weapon { get { return gear[(int)Slot.MainHand]; } }
        public WeaponClass WeaponClass { get { return Weapon != null ? Weapon.cls : WeaponClass.Fist; } }
        /// <summary>行囊格數：基本 6 格，腰間的囊袋可以再加。</summary>
        public int BagSize { get { int n = 6; foreach (var g in gear) if (g != null) n += g.bag; return n; } }

        /// <summary>裝備對某項技能檢定的加成。</summary>
        public int GearSkill(Talent t)
        {
            int n = 0;
            foreach (var g in gear) if (g != null && (g.skill & t) != 0) n += g.skillBonus;
            return n;
        }

        /// <summary>
        /// 裝備有變動時呼叫：重算防禦、移動、輕功、潛行與警覺加成，
        /// 並依手上的兵器產生基本攻擊（種類決定範圍、會心、傷害，個別兵器可再增減）。
        /// </summary>
        public void RefreshGear()
        {
            armor = 0; dexCap = 99; noisy = false; gearStealth = 0; gearPerception = 0;
            move = baseMove; jump = baseJump;
            foreach (var g in gear)
            {
                if (g == null) continue;
                armor += g.armor;
                if (g.dexCap < dexCap) dexCap = g.dexCap;
                if (g.noisy || g.heavy) noisy = true;
                move += g.move; jump += g.jump;
                gearStealth += g.stealth; gearPerception += g.perception;
            }
            if (move < 1) move = 1;

            var w = Weapon;
            var cls = w != null ? w.cls : WeaponClass.None; // 沒拿兵器就是空手
            var info = Gears.Info(cls);
            int mod = w != null ? w.reachMod : 0;
            var strike = new Skill
            {
                name = w != null ? w.name : "空手", kind = SkillKind.Attack, basic = true,
                melee = info.range == 0 && (w == null || w.range == 0),
                range = w != null && w.range > 0 ? w.range : info.range > 0 ? System.Math.Max(2, info.range + mod) : 0,
                reach = info.range == 0 ? System.Math.Max(1, info.reach + mod) : 1,
                fx = w != null ? w.fx : GearFx.None,
                ability = info.ability, dmg = w != null && w.dmg.n > 0 ? w.dmg : info.dmg,
                critMin = info.critMin, critDice = info.critDice, sweep = info.sweep,
            };
            strike.desc = Gears.Summary(w) + (info.note.Length > 0 ? "。" + info.note : "") + (w != null && w.Named ? " ★" + Gears.FxText[(int)w.fx] + "。" : "");
            var list = new Skill[arts.Length + 1];
            list[0] = strike;
            System.Array.Copy(arts, 0, list, 1, arts.Length);
            skills = list;
        }

        public int x, y, z;
        public int fx = 1, fy; // 面向（八方位）：決定視野扇形，也決定哪一邊算背後
        public bool alive = true;
        public int init;

        // 本回合資源
        public int moveLeft;
        public bool actionLeft, bonusLeft;

        // 狀態
        public bool guard; // 金鐘罩
        public bool light; // 梯雲縱
        public bool stun;  // 定身

        // 駐守位置與面向：沒有敵蹤時回到這裡張望
        public int homeCell = -1, homeFx, homeFy;
        public int scan; // 張望的節拍

        // 對方陣營對這個單位的掌握（同陣營共享）
        public int knownCell = -1; // 對方最後一次確知的位置；-1 表示行蹤不明
        public int knownAt;        // 那是第幾輪的事
        public bool inSight;       // 此刻正被對方某人看著

        // 潛行、突襲、借機攻擊
        public bool sneak;               // 擅長潛行（潛行檢定加熟練）
        public int stealth;              // 本回合的潛行擲骰（自己回合開始時擲）
        public bool stealthTold;         // 本回合已在戰報提過潛行成功
        public bool surprised;           // 措手不及：跳過下一個回合
        public bool ambushed;            // 已被偷襲過（措手不及每場只發生一次）
        public bool reactionLeft = true; // 反應：每輪一次借機攻擊

        // 技能、互動、士氣
        public Talent talents;         // 技能熟練
        public bool boss;              // 頭目：不會投降、不受士氣影響
        public bool freeLeft = true;   // 每回合一次的隨手互動（開箱）
        public int fright;             // 膽寒：大於 0 時攻擊有劣勢，每逢自己回合開始減 1
        public bool fled;              // 已投降或潰逃（離開戰場，不算陣亡）
        public int defied;             // 哪些人的喝令對他已經無效（依喝令者 id 的位元）

        // 狀態（數字是剩餘回合，每逢自己回合開始減 1）
        public int blind;   // 目盲：攻擊劣勢、被攻擊有優勢、只看得到身邊一格
        public int poison;  // 中毒：每回合開始受 1d4，再擲根骨豁免，成功則解除
        public int bleed;   // 流血：每回合開始受 1d4
        public int tangle;  // 纏繞：攻擊劣勢
        public int bless;   // 祈福：命中 +2
        public bool ran;    // 自由行動時上一次移動是用跑的：潛行 −5
        public bool venom;  // 兵器上抹了毒：下一次命中使對方中毒

        /// <summary>被動警覺：10 + 定力調整值。</summary>
        public int Passive { get { return 10 + Mod(Stat.Wis) + ((talents & Talent.Perception) != 0 ? Db.Prof : 0) + gearPerception; } }
        /// <summary>潛行加值：身法調整值，擅長者再加熟練。</summary>
        public int StealthBonus { get { return Mod(Stat.Dex) + (sneak ? Db.Prof : 0) + gearStealth; } }
        /// <summary>身上有重甲、鐵器或沉重的兵器：潛行有劣勢。</summary>
        public bool Clanky { get { return noisy; } }

        public int Mod(Stat s) { return Dice.Mod(stats[(int)s]); }
        public int Ac { get { return 10 + System.Math.Min(Mod(Stat.Dex), dexCap) + armor + (guard ? 4 : 0); } }
        public int Jump { get { return jump + (light ? 3 : 0); } }
    }
}
