using System.Collections.Generic;

namespace Jianghu
{
    /// <summary>任務裡的一條目標。</summary>
    public sealed class QuestLine
    {
        public string text;
        public bool done, failed;
    }

    /// <summary>一件任務：主線、支線或挑戰。</summary>
    public sealed class Quest
    {
        public string kind, title, desc;
        public readonly List<QuestLine> lines = new List<QuestLine>();

        public bool Done { get { foreach (var l in lines) if (!l.done) return false; return true; } }
        public bool Failed { get { foreach (var l in lines) if (l.failed) return true; return false; } }

        public Quest Add(string text, bool done, bool failed = false)
        {
            lines.Add(new QuestLine { text = text, done = done, failed = failed && !done });
            return this;
        }
    }

    /// <summary>
    /// 任務。進度不另外記錄，全部從戰局的現況算出來（所以存讀檔不需要特別處理）。
    /// </summary>
    public sealed partial class Battle
    {
        Prop FirstProp(PropKind kind)
        {
            foreach (var p in props) if (p.kind == kind) return p;
            return null;
        }

        public List<Quest> Quests()
        {
            var list = new List<Quest>();
            int foes = 0, beaten = 0, heroes = 0, standing = 0, ambushed = 0;
            Unit chief = null;
            foreach (var u in units)
            {
                if (u.team == 1) { foes++; if (!u.alive) beaten++; if (u.boss) chief = u; if (u.ambushed) ambushed++; }
                else { heroes++; if (u.alive) standing++; }
            }

            var main = new Quest { kind = "主線", title = "蕩平黑風寨", desc = "黑風寨群匪盤踞山道，劫掠過往行旅。上山除去這一害。" };
            if (chief != null) main.Add("擊敗寨主" + chief.name, !chief.alive);
            main.Add("擊敗或逼退所有山賊（" + beaten + " / " + foes + "）", beaten >= foes, standing == 0);
            list.Add(main);

            var npc = FirstProp(PropKind.Npc);
            if (npc != null)
                list.Add(new Quest { kind = "支線", title = "樵夫的指點", desc = "河西岸的木橋頭躲著一位老樵夫，他天天在這山裡砍柴，或許知道寨子的底細。" }
                    .Add("找到" + npc.name + "，問出山寨的佈置", npc.guided, npc.attitude == 0)
                    .Add("取得他的信任，收下他的謝禮", npc.gave, npc.attitude == 0));

            var chest = FirstProp(PropKind.Chest);
            if (chest != null)
                list.Add(new Quest { kind = "支線", title = "亭中舊箱", desc = "涼亭裡擱著一口上了鎖的舊木箱。" }
                    .Add("撬開涼亭裡的" + chest.name, chest.open));

            list.Add(new Quest { kind = "挑戰", title = "出其不意", desc = "趁群匪毫無戒備時先下手為強。" }
                .Add("在被發現之前出手，讓一名敵人措手不及", ambushed > 0, !exploring && started && ambushed == 0 && alerts > 0));

            list.Add(new Quest { kind = "挑戰", title = "全身而退", desc = "三位俠客一個都不能少。" }
                .Add("戰鬥結束時三人都還站著（目前 " + standing + " / " + heroes + "）", over == 1 && standing == heroes, standing < heroes));
            return list;
        }
    }
}
