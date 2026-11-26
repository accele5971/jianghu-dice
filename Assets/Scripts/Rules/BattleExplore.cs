using System;

namespace Jianghu
{
    /// <summary>
    /// 自由行動（探索）：開場還沒被敵人發現時，不擲先攻、不分回合，我方可以不限步數走動、換人、用道具、互動。
    /// 世界仍在運轉：我方每走滿一輪的路程，敵人就張望一次、我方重擲潛行、身上的狀態也過一輪。
    /// 被敵人察覺，或我方先動手（攻擊、喝令、丟石灰或霹靂彈），就擲先攻進入回合制。
    /// 先動手而對方毫無戒備時，照突襲的規則：被打的人措手不及，丟掉第一個回合。
    /// </summary>
    public sealed partial class Battle
    {
        /// <summary>開場是否先進入自由行動（要在 Start 之前設定；預設否，直接擲先攻）。</summary>
        public bool explore;

        /// <summary>目前是否在自由行動中。</summary>
        public bool exploring { get; private set; }

        /// <summary>戰鬥場景是否已經開始（Start 呼叫過）。</summary>
        public bool started { get; private set; }

        /// <summary>自由行動時每次可以走的距離（走完會自動補滿，只是限制一次點擊能走多遠）。</summary>
        public const int ExploreMove = 12;

        /// <summary>我方走多少點移動算過了一輪。</summary>
        public const int TickCost = 6;

        /// <summary>玩家是否按著跑步鍵（自由行動時跑步的人潛行 −5）。由畫面層設定。</summary>
        public bool running;

        bool combatPending, inTick;
        int exploreClock; // 這一輪已經走了多少
        int clock;        // 從開場算起過了幾輪（自由行動與回合制都算），濃煙等效果用它計時

        void BeginExplore()
        {
            exploring = true;
            foreach (var h in units) if (h.team == 0) Refill(h);
            foreach (var h in units) if (h.team == 0 && h.alive) { active = h; break; }
            Log("<color=#9FE0C0>群匪尚未察覺。可以自由行動；被發現或先動手，才會擲先攻。</color>");
            Emit(new Ev { type = EvType.TurnStart, a = active, n = 0 });
            Observe();
            AfterAction();
        }

        void Refill(Unit u)
        {
            u.moveLeft = Math.Max(u.move, ExploreMove);
            u.actionLeft = u.bonusLeft = u.freeLeft = u.reactionLeft = true;
        }

        /// <summary>自由行動時切換控制的俠客。</summary>
        public bool Select(Unit u)
        {
            if (!exploring || over != 0 || u == null || u.team != 0 || !u.alive || u == active) return false;
            active = u;
            Refill(u);
            Emit(new Ev { type = EvType.TurnStart, a = u, n = 0 });
            return true;
        }

        // 我方走了一段路：時間跟著往前走
        void AfterMove(Unit u, int cost)
        {
            if (exploring && !inTick && u.team == 0) exploreClock += Math.Max(1, cost);
        }

        /// <summary>
        /// 我方每做完一件事之後呼叫：自由行動時讓時間前進、補滿行動力；有人被發現或先動了手就開戰。
        /// 回合制時不做任何事。
        /// </summary>
        void AfterAction()
        {
            if (!exploring || inTick || over != 0) return;
            while (exploreClock >= TickCost && !combatPending && over == 0) { exploreClock -= TickCost; ExploreTick(); }
            if (over != 0) return;
            if (combatPending) { BeginCombat(); return; }
            foreach (var h in units) if (h.team == 0 && h.alive) Refill(h);
        }

        // 自由行動時過了一輪：我方的增益結束、重擲潛行、狀態發作；敵人各自張望或走回崗位
        void ExploreTick()
        {
            inTick = true;
            clock++;
            ExpireSmoke();
            foreach (var h in units)
            {
                if (!h.alive || h.team != 0) continue;
                h.guard = false; h.light = false;
                if (h.fright > 0) h.fright--;
                if (h.blind > 0) h.blind--;
                if (h.tangle > 0) h.tangle--;
                RollStealth(h);
                TickWounds(h);
            }
            for (int i = 0; i < units.Count && over == 0 && !combatPending; i++)
            {
                var e = units[i];
                if (!e.alive || e.team != 1) continue;
                e.moveLeft = e.move;
                e.actionLeft = e.bonusLeft = e.reactionLeft = true;
                if (e.fright > 0) e.fright--;
                if (e.blind > 0) e.blind--;
                if (e.tangle > 0) e.tangle--;
                Observe();
                if (!combatPending) EnemyAI.Take(this, e);
            }
            inTick = false;
        }

        // 被發現了，或有人先動手：擲先攻，進入回合制
        void BeginCombat()
        {
            combatPending = false;
            exploring = false;
            exploreClock = 0;
            Log("<color=#FF9C6A>遭遇戰！</color>");
            Emit(new Ev { type = EvType.Combat });
            RollInitiative();
            turn = -1;
            Advance();
        }
    }
}
