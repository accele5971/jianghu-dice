using System;

namespace Jianghu
{
    /// <summary>技能熟練（檢定時加熟練加值）。</summary>
    [Flags]
    public enum Talent { None = 0, Athletics = 1, Sleight = 2, Persuade = 4, Intimidate = 8, Deceive = 16, Perception = 32 }

    public enum PropKind { Boulder, Chest, Grass, Trap, Npc, Caltrops, Loot, Barrel, Rack, Herb, Shrine, Barricade, Notice, Vine }

    /// <summary>與 NPC 交談的三種方式。</summary>
    public enum TalkWay { Persuade, Intimidate, Deceive }

    /// <summary>
    /// 地圖上可以互動的東西：滾石、寶箱、芒草叢、絆鈴索、路人。
    /// 不是戰鬥單位，沒有回合；滾石、寶箱、路人會佔住格子。
    /// </summary>
    public sealed class Prop
    {
        public int id;
        public PropKind kind;
        public string name;
        public int cell;
        public int dc;        // 滾石：蠻力；寶箱：開鎖；絆索：察覺與拆除
        public Item item;     // 掉在地上的道具（Loot）、藥草採下後變成的成藥（Herb）
        public int link = -1; // 藤蔓通往的崖頂格子
        public readonly System.Collections.Generic.List<Gear> gears = new System.Collections.Generic.List<Gear>(); // 兵器架上的兵器
        public bool gone;     // 已從地圖上消失（滾石落水、絆索被觸發或拆除）
        public bool open;     // 寶箱已打開
        public bool found;    // 絆索已被我方發現

        // 路人
        public Look look;
        public int fx, fy;
        public int attitude = 1; // 0 敵視、1 中立、2 友善
        public int tried;        // 誰用哪種方式試過了（每人每種方式一次）
        public bool guided;      // 已經指過路
        public bool gave;        // 已經送過草藥

        /// <summary>是否佔住格子（不能走進去、不能被擊退進去）。</summary>
        public bool Solid
        {
            get
            {
                return !gone && (kind == PropKind.Boulder || kind == PropKind.Chest || kind == PropKind.Npc || kind == PropKind.Barrel
                    || kind == PropKind.Rack || kind == PropKind.Shrine || kind == PropKind.Barricade || kind == PropKind.Notice);
            }
        }

        /// <summary>絆索是否還有作用。</summary>
        public bool Armed { get { return kind == PropKind.Trap && !gone; } }

        /// <summary>鐵蒺藜是否還在地上。</summary>
        public bool Spiked { get { return kind == PropKind.Caltrops && !gone; } }

        public static readonly string[] AttitudeNames = { "敵視", "中立", "友善" };
        public static readonly string[] WayNames = { "說服", "威嚇", "欺瞞" };
    }
}
