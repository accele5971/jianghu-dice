using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Jianghu.EditorTools
{
    /// <summary>
    /// 自動試玩：進入 Play、實際操作幾步、讓 AI 把整場打完，途中截圖並記錄所有錯誤。
    /// 無視窗執行：
    ///   Unity.exe -batchmode -projectPath . -executeMethod Jianghu.EditorTools.SmokeTest.Run
    /// 也可在編輯器選單「江湖骰 / 自動試玩」執行。結果寫到 Tests~/shots/。
    /// </summary>
    [InitializeOnLoad]
    static class SmokeTest
    {
        const string Key = "Jianghu.SmokeTest", StartKey = "Jianghu.SmokeTest.Start";
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly List<string> problems = new List<string>();
        static readonly StringBuilder report = new StringBuilder();
        static string outDir;
        static int step;
        static float stepAt;
        static bool midShot, underCover, speedUp, creatorDone;
        static float seenAt, prepAt;
        static int loadedFrom = -1;
        static int prepPhase;
        static UnitView sampleView;
        static Vector3 samplePos;
        static Quaternion sampleRot;
        static int sampleFrame, samplePath;
        static readonly List<float> samples = new List<float>(), turns = new List<float>();

        static SmokeTest()
        {
            if (SessionState.GetBool(Key, false)) Hook();
        }

        [MenuItem("江湖骰/自動試玩")]
        public static void Run()
        {
            SessionState.SetBool(Key, true);
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            Hook();
        }

        static void Hook()
        {
            outDir = Path.Combine(Directory.GetCurrentDirectory(), "Tests~", "shots");
            Directory.CreateDirectory(outDir);
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Log || type == LogType.Warning) return;
            string first = stack != null ? stack.Split('\n')[0] : "";
            problems.Add(type + ": " + msg + "  @ " + first);
        }

        static void Note(string s) { report.AppendLine(s); Debug.Log("[SmokeTest] " + s); }

        static object Get(object o, string name)
        {
            var t = o.GetType();
            var f = t.GetField(name, Any);
            if (f != null) return f.GetValue(o);
            var p = t.GetProperty(name, Any);
            if (p != null) return p.GetValue(o, null);
            throw new Exception("no member " + name);
        }

        static void Set(object o, string name, object v) { o.GetType().GetField(name, Any).SetValue(o, v); }
        static void Call(object o, string name, params object[] args) { o.GetType().GetMethod(name, Any).Invoke(o, args); }
        static object CallR(object o, string name, params object[] args) { return o.GetType().GetMethod(name, Any).Invoke(o, args); }

        // 我方三人目前的位置、氣血、兵器（存檔前後比對用）
        static string HeroState(Battle b)
        {
            var sb = new StringBuilder();
            foreach (var u in b.units)
                if (u.team == 0) sb.Append(u.name).Append('@').Append(u.x).Append(',').Append(u.y).Append(',').Append(u.z).Append(" hp").Append(u.hp)
                    .Append(' ').Append(u.Weapon != null ? u.Weapon.name : "空手").Append(" bag").Append(u.bag.Count).Append("; ");
            return sb.ToString();
        }
        static string savedState = "";

        static void Tick()
        {
            float elapsed = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0);
            if (elapsed > 520) { problems.Add("TIMEOUT at step " + step); Finish(); return; }

            if (!EditorApplication.isPlaying)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling) EditorApplication.EnterPlaymode();
                return;
            }

            try { Drive(); }
            catch (Exception e)
            {
                problems.Add("TEST DRIVER: " + (e.InnerException ?? e));
                Finish();
            }
        }

        static void Next() { step++; stepAt = Time.realtimeSinceStartup; }

        static void Drive()
        {
            var gc = UnityEngine.Object.FindFirstObjectByType<GameController>();
            if (gc == null) return;
            var battle = (Battle)Get(gc, "battle");
            bool playing = (bool)Get(gc, "playing");
            bool myTurn = (bool)Get(gc, "MyTurn");
            float since = Time.realtimeSinceStartup - stepAt;

            // 讀檔之後換了一個新的場景：等它建好，檢查是不是存檔當時的樣子，再試走一步
            if (step == 8)
            {
                if (since < 2.0f) return;
                bool prep = (bool)Get(gc, "preparing"), wasLoaded = (bool)Get(gc, "loaded");
                string now = HeroState(battle);
                Note("Loaded game: from save " + wasLoaded + ", skipped the creator " + !prep + ", exploring " + battle.exploring + ", round " + battle.round + ", state matches the save " + (now == savedState));
                if (!wasLoaded || prep) problems.Add("load: the game did not resume from the save file");
                if (now != savedState) problems.Add("load: hero state differs from the save (" + now + " vs " + savedState + ")");
                if (!battle.exploring) problems.Add("load: the save was made while exploring, the loaded game should still be exploring");
                Shot("M4-loaded");
                ShotWorld("M5-loaded-world", 1600, 900);
                // 讀回來的戰局要能繼續玩：走一步
                battle.ComputeReach(battle.active);
                if (battle.pf.reach.Count > 0)
                {
                    loadedFrom = battle.CellOf(battle.active);
                    Set(gc, "hover", battle.pf.reach[0]);
                    Call(gc, "Click");
                }
                Next();
                return;
            }
            if (step == 9)
            {
                if (since < 1.5f) return;
                bool movedOn = battle.CellOf(battle.active) != loadedFrom || battle.active.team != 0;
                Note("Loaded game: a move after loading worked " + movedOn + ", still playing an animation " + (bool)Get(gc, "playing"));
                if (!movedOn) problems.Add("load: could not move after loading");
                Finish();
                return;
            }

            // 一開始停在創角畫面：先拍照檢查，再按「出戰」
            if ((bool)Get(gc, "preparing"))
            {
                if (seenAt == 0) seenAt = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - seenAt < 1.5f) return;
                if (!creatorDone) { creatorDone = true; CreatorShots(gc, battle); return; }
                // 戰前整備：開面板、等鏡頭移到定位再截圖，然後關掉、出戰
                if (prepPhase == 0) { prepPhase = 1; prepAt = Time.realtimeSinceStartup; Call(gc, "ToggleGear"); return; }
                if (prepPhase == 1)
                {
                    if (Time.realtimeSinceStartup - prepAt < 1.2f) return;
                    prepPhase = 2; prepAt = Time.realtimeSinceStartup;
                    var hud0 = (Hud)Get(gc, "hud");
                    if (!hud0.GearOpen) problems.Add("prep: gear panel did not open before battle");
                    Shot("0d-prep");
                    Call(gc, "OnGearSlot", 0);
                    Shot("0e-prep-weapons");
                    // 在整備畫面換一把兵器、切到另一位俠客，確認都不出錯
                    var hero0 = battle.units[0];
                    Call(gc, "OnGearOption", 1);
                    Note("Prep: swapped " + hero0.name + "'s weapon to " + (hero0.Weapon != null ? hero0.Weapon.name : "none") + ", icons baked so far " + Icons.Count);
                    if (hero0.Weapon == Gears.QingFeng) problems.Add("prep: clicking a weapon in the panel did not equip it");
                    for (int i = 0; i < 12; i++)
                    {
                        var ch = (List<Gear>)Get(gc, "gearChoices");
                        int at = ch.IndexOf(Gears.QingFeng);
                        if (at >= 0) { Call(gc, "OnGearOption", at); break; }
                        Call(gc, "OnGearOption", (int)Get(gc, "gearPageRow"));
                    }
                    if (hero0.Weapon != Gears.QingFeng) problems.Add("prep: could not page through the list and re-equip the original sword");
                    Call(gc, "OnGearTab", 1);
                    return;
                }
                if (prepPhase == 2)
                {
                    if (Time.realtimeSinceStartup - prepAt < 1.0f) return;
                    prepPhase = 3; prepAt = Time.realtimeSinceStartup;
                    Shot("0f-prep-second-hero");
                    Call(gc, "CloseGear");
                    return;
                }
                if (Time.realtimeSinceStartup - prepAt < 0.6f) return;
                Call(gc, "BeginBattle");
                return;
            }

            // 行走取樣：每一幀記下行動者模型的位移，用來檢查走路有沒有中途停頓
            if (sampleView != null && Time.frameCount != sampleFrame)
            {
                sampleFrame = Time.frameCount;
                Vector3 p = sampleView.root.position;
                if (playing) { samples.Add((p - samplePos).magnitude); turns.Add(Quaternion.Angle(sampleRot, sampleView.root.rotation)); }
                samplePos = p; sampleRot = sampleView.root.rotation;
            }

            switch (step)
            {
                case 0: // 等開場事件播完、輪到玩家
                    if (Time.timeScale == 1 && !speedUp) { speedUp = true; Time.timeScale = 4; }
                    if (!myTurn) { stepAt = Time.realtimeSinceStartup; return; } // 輪到玩家後再等一下，讓介面穩定
                    if (since < 0.8f) return;
                    Time.timeScale = 1;
                    Note("Screen " + Screen.width + "x" + Screen.height + ", lit shader: " + Mats.Lit.name + ", unlit shader: " + Mats.Unlit.name
                        + ", color space: " + QualitySettings.activeColorSpace + ", graphics: " + SystemInfo.graphicsDeviceType);
                    Note("Renderers: " + UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length
                        + ", terrain verts: " + GameObject.Find("Terrain").GetComponent<MeshFilter>().sharedMesh.vertexCount);
                    Note("Cover structures: " + battle.world.coverCount + ", unit shader: " + (Shader.Find("Jianghu/UnitLit") != null) + ", silhouette shader: " + (Shader.Find("Jianghu/Silhouette") != null));
                    Note("First player turn: " + battle.active.name + " round " + battle.round + ", reachable cells " + battle.pf.reach.Count);
                    {
                        var mm = GameObject.Find("Markers").GetComponent<MeshFilter>().sharedMesh;
                        Note("Markers: " + mm.vertexCount + " verts, hover " + Get(gc, "hover") + ", reachSet " + ((System.Collections.Generic.HashSet<int>)Get(gc, "reachSet")).Count + ", playing " + playing);
                        Note("Exploring at start: " + battle.exploring + ", round " + battle.round + ", move available " + battle.active.moveLeft);
                        if (!battle.exploring) problems.Add("the game should start in free exploration");
                        if (mm.vertexCount != 0 && battle.exploring && (int)Get(gc, "hover") < 0) problems.Add("no move-range grid should be drawn while exploring");
                    }
                    Shot("1-turn-start");
                    ShotWorld("W1-start", 1600, 900);
                    {
                        // 自動尋路：到敵方陣地的全程花費；面向與視野
                        int total, farCell = battle.world.Idx(18, 14, battle.world.TopStand(18, 14));
                        int first = battle.AutoMoveCell(battle.active, farCell, out total);
                        Note("Auto-path to (18,14): total cost " + total + ", first leg ends at " + (first < 0 ? "none" : battle.world.X(first) + "," + battle.world.Y(first)));
                        if (total <= 0) problems.Add("auto-path to enemy camp should be reachable");
                        battle.ComputeReach(battle.active);
                        int litCols = 0, seenCols = 0, hidden = 0;
                        foreach (byte b in battle.vision.lit) litCols += b;
                        foreach (byte b in battle.vision.seen) seenCols += b;
                        foreach (var en in battle.units) if (en.team == 1 && !battle.CanSee(en)) hidden++;
                        Note("Vision: " + litCols + " columns visible, " + seenCols + " explored of " + battle.vision.lit.Length + "; enemies hidden " + hidden + "/6");

                        // 敵方 AI 依視野行動：開局雙方相距甚遠，敵人不該知道我方在哪，也不該離開駐守位置
                        int knownHeroes = 0, strayed = 0;
                        var facings = new StringBuilder();
                        foreach (var en in battle.units)
                        {
                            if (en.team == 0) { if (en.knownCell >= 0) knownHeroes++; continue; }
                            if (battle.CellOf(en) != en.homeCell) strayed++;
                            facings.Append(' ').Append(en.name).Append('(').Append(en.fx).Append(',').Append(en.fy).Append(')');
                        }
                        Note("AI awareness at start: heroes known to foes " + knownHeroes + "/3, foes off post " + strayed + "/6, alerts " + battle.alerts + "; facings" + facings);
                        if (knownHeroes > 0 || battle.alerts > 0) problems.Add("foes already know where the heroes are at battle start (AI should be blind until it sees them)");
                        if (strayed > 0) problems.Add("foes left their posts before spotting anyone");
                    }
                    ShotScenic("D1-fog-on-low", new Vector3(-9f, 4.2f, 31f), new Vector3(13f, 2.2f, 9f));
                    Call(gc, "SetFog", false); // 以下的風景與特寫不需要迷霧
                    ShotWorld("D2-fog-off-top", 1600, 900);
                    ShotScenic("S1-scenic", new Vector3(-9f, 4.2f, 31f), new Vector3(13f, 2.2f, 9f));
                    ShotScenic("S2-pavilion", new Vector3(1.5f, 2.6f, 15.5f), new Vector3(6.5f, 1.9f, 9.5f));
                    foreach (var n in new[] { "沈青嵐", "石破山", "唐小蝶", "厲無咎", "山賊甲", "弓手甲" })
                    {
                        var go = GameObject.Find(n);
                        if (go != null) ShotClose("C-" + n, go.transform);
                    }
                    PoseShots(gc, battle);
                    PropShots(gc, battle);
                    Call(gc, "SetFog", true);
                    Bench(gc, battle);
                    // 裝備面板：行囊頁與「主手」的更換頁各拍一張
                    Call(gc, "ToggleGear");
                    Shot("8a-gear-bag");
                    if (!((Hud)Get(gc, "hud")).GearOpen) problems.Add("gear panel did not open");
                    Call(gc, "OnGearSlot", 0);
                    Shot("8b-gear-weapons");
                    Call(gc, "CloseGear");
                    {
                        // Esc 選單、設定、任務欄，以及存檔
                        var hudM = (Hud)Get(gc, "hud");
                        Call(gc, "OpenMenu");
                        if (!hudM.SheetOpen) problems.Add("menu did not open");
                        Shot("M1-menu");
                        Call(gc, "OnSheetPick", 3);
                        Shot("M2-settings");
                        int q0 = (int)Get(gc, "quality");
                        Call(gc, "OnSheetPick", 0);
                        bool qChanged = (int)Get(gc, "quality") != q0;
                        Call(gc, "SetQuality", q0);
                        PlayerPrefs.SetInt("jianghu.quality", q0); // 測試不該改掉玩家存下來的畫質
                        Call(gc, "OnSheetPick", 3); // 返回選單
                        Call(gc, "CloseSheet");
                        Call(gc, "ToggleQuests");
                        Shot("M3-quests");
                        Call(gc, "CloseSheet");
                        bool saved = (bool)CallR(gc, "SaveGame");
                        Call(gc, "CloseSheet");
                        savedState = HeroState(battle);
                        string path = GameController.SavePath;
                        long size = File.Exists(path) ? new FileInfo(path).Length : 0;
                        Note("Menu: settings changed quality " + qChanged + ", saved " + saved + " (" + size + " bytes), sheet closed " + !hudM.SheetOpen + ", quests listed " + battle.Quests().Count);
                        if (!qChanged) problems.Add("settings: picking quality did not change it");
                        if (!saved || size < 500) problems.Add("save: no save file was written");
                        if (hudM.SheetOpen) problems.Add("menu sheet did not close");
                        // 暫停：自由行動時空白鍵
                        Call(gc, "SetPaused", true);
                        bool frozen = Time.timeScale == 0 && !(bool)Get(gc, "MyTurn");
                        Call(gc, "SetPaused", false);
                        if (!frozen || Time.timeScale != 1 || !(bool)Get(gc, "MyTurn")) problems.Add("pause should freeze time and input, and resume cleanly");
                    }
                    {
                        // 拿著煙霧彈瞄準時的標記，以及濃煙的樣子（用完就復原，不影響戰局）
                        var act = battle.active;
                        battle.Give(act, Item.SmokeBomb);
                        Set(gc, "aimItem", Item.SmokeBomb);
                        Set(gc, "hover", battle.world.Idx(act.x + 2, act.y, battle.world.TopStand(act.x + 2, act.y)));
                        Call(gc, "RedrawMarkers");
                        var mk = GameObject.Find("Markers").GetComponent<MeshFilter>().sharedMesh;
                        Note("Aiming a smoke bomb: " + mk.vertexCount + " marker verts");
                        if (mk.vertexCount == 0) problems.Add("no markers while aiming an item");
                        ShotWorld("9a-aim-item", 1600, 900);
                        Set(gc, "aimItem", null);
                        Set(gc, "hover", -1);
                        Call(gc, "RedrawMarkers");
                        for (int i = act.bag.Count - 1; i >= 0; i--) if (act.bag[i].item == Item.SmokeBomb && --act.bag[i].n <= 0) act.bag.RemoveAt(i);

                        var pv2 = (PropViews)Get(gc, "propViews");
                        var cells = new List<int>();
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int zz = battle.world.TopStand(act.x + 2 + dx, act.y + dy);
                                if (zz > 0) cells.Add(battle.world.Idx(act.x + 2 + dx, act.y + dy, zz));
                            }
                        pv2.Smoke(999, cells.ToArray(), true);
                        Vector3 sp = Vis.Cell(battle.world, battle.world.Idx(act.x + 2, act.y, battle.world.TopStand(act.x + 2, act.y)));
                        ShotScenic("9b-smoke", sp + new Vector3(2.5f, 2.2f, -4.5f), sp + Vector3.up * 0.5f);
                        pv2.Smoke(999, null, false);
                    }
                    Next();
                    break;

                case 1: // 選攻擊招式（顯示目標標記）
                    if (since < 0.4f) return;
                    Call(gc, "OnSkillButton", 0);
                    Next();
                    break;

                case 2:
                    if (since < 0.4f) return;
                    Note("Selected skill: " + ((Skill)Get(gc, "sel") != null ? ((Skill)Get(gc, "sel")).name : "none"));
                    Shot("2-skill-selected");
                    Call(gc, "OnSkillButton", 0); // 再按一次取消
                    Next();
                    break;

                case 3: // 移動到最遠的可達格
                {
                    if (since < 0.4f) return;
                    var reach = battle.pf.reach;
                    if (reach.Count > 0)
                    {
                        // 優先走到樹下或亭內，以驗證自動掀頂；沒有這種格子才走到最遠處
                        // 自由行動一次能走很遠：只挑我方這一側的格子，免得走進敵人視野而提早開戰
                        var bw = battle.world;
                        int dest = -1;
                        for (int k = reach.Count - 1; k >= 0 && dest < 0; k--)
                            if (bw.X(reach[k]) < 9 && bw.Y(reach[k]) > 12 && battle.PropAt(reach[k]) == null) dest = reach[k];
                        if (dest < 0) dest = reach[0];
                        for (int k = reach.Count - 1; k >= 0; k--)
                            if (bw.X(reach[k]) < 9 && bw.Y(reach[k]) > 12 && bw.CoverAbove(bw.X(reach[k]), bw.Y(reach[k]), bw.Z(reach[k])) >= 0) { dest = reach[k]; underCover = true; break; }
                        Set(gc, "hover", dest);
                        sampleView = ViewOf(gc, battle.active);
                        samplePos = sampleView.root.position; sampleRot = sampleView.root.rotation; sampleFrame = Time.frameCount;
                        samplePath = battle.pf.Path(dest).Length - 1;
                        Call(gc, "Click");
                        Note("Move to cell " + battle.world.X(dest) + "," + battle.world.Y(dest) + "," + battle.world.Z(dest) + " → now at "
                            + battle.active.x + "," + battle.active.y + "," + battle.active.z);
                    }
                    Next();
                    break;
                }

                case 4: // 用一個自身增益招式
                {
                    if (!myTurn || since < 0.6f) return;
                    if (sampleView != null)
                    {
                        // 去掉頭尾還沒起步、已經站定的幀，看中間那一段
                        sampleView = null;
                        int a = 0, z = samples.Count - 1;
                        while (a < z && samples[a] < 1e-4f) a++;
                        while (z > a && samples[z] < 1e-4f) z--;
                        int n = z - a + 1, stalls = 0;
                        float total = 0, maxStep = 0, maxTurn = 0;
                        for (int i = a; i <= z; i++)
                        {
                            total += samples[i];
                            if (samples[i] > maxStep) maxStep = samples[i];
                            if (turns[i] > maxTurn) maxTurn = turns[i];
                            if (samples[i] < 1e-4f) stalls++;
                        }
                        float mean = n > 0 ? total / n : 0;
                        Note("Walk smoothness over " + samplePath + " steps: " + n + " frames, mean step " + mean.ToString("0.000") + ", largest step " + maxStep.ToString("0.000")
                            + " (" + (mean > 0 ? maxStep / mean : 0).ToString("0.0") + "× mean), frames standing still mid-walk " + stalls + ", sharpest turn in one frame " + maxTurn.ToString("0") + "°");
                        if (n < 4) problems.Add("walk animation produced too few frames to judge");
                        else
                        {
                            if (stalls > 0) problems.Add("walk animation stalls mid-path (" + stalls + " frames without movement)");
                            if (maxStep > mean * 4f) problems.Add("walk animation jumps: one frame moved " + (maxStep / mean).ToString("0.0") + "× the average");
                        }
                    }
                    Shot("3-after-move");
                    {
                        int hiddenNow = 0;
                        foreach (bool h in battle.world.coverHidden) if (h) hiddenNow++;
                        Note("After move (under cover: " + underCover + "): " + hiddenNow + " structure(s) auto-hidden");
                        if (underCover && hiddenNow != 1) problems.Add("auto cover hide: expected exactly 1 hidden structure, got " + hiddenNow);
                    }
                    var u = battle.active;
                    for (int i = 0; i < u.skills.Length; i++)
                    {
                        var k = u.skills[i].kind;
                        if ((k == SkillKind.Light || k == SkillKind.Guard || k == SkillKind.Rest) && battle.CanUse(u, u.skills[i]))
                        {
                            Call(gc, "OnSkillButton", i);
                            Note("Used " + u.skills[i].name + ", moveLeft " + u.moveLeft + ", qi " + u.qi);
                            break;
                        }
                    }
                    Next();
                    break;
                }

                case 5: // 之後交給 AI 把整場打完
                    if (!myTurn || since < 0.8f) return;
                    Shot("4-after-buff");
                    CheckCoverPick(gc, battle.world);
                    battle.autoHeroes = true;
                    battle.maxRounds = 80;
                    Time.timeScale = 8;
                    if (battle.exploring)
                    {
                        // 還在自由行動：直接宣告開戰，之後整場交給 AI
                        Call(battle, "BeginCombat");
                        Call(gc, "Flush");
                    }
                    else Call(gc, "EndTurn");
                    Next();
                    break;

                case 6:
                    if (!midShot && since > 5f && playing) { midShot = true; Shot("5-mid-battle"); ShotWorld("W2-mid-battle", 1600, 900); }
                    if (battle.over == 0 || playing || since < 2f) return;
                    Time.timeScale = 1;
                    Next();
                    break;

                case 7:
                    if (since < 1.2f) return;
                    int alive0 = 0, alive1 = 0;
                    foreach (var u in battle.units) if (u.alive) { if (u.team == 0) alive0++; else alive1++; }
                    Note("Battle over = " + battle.over + " at round " + battle.round + ", heroes alive " + alive0 + ", foes alive " + alive1);
                    Note("AI awareness: foes spotted heroes " + battle.alerts + " time(s)");
                    if (battle.alerts == 0) problems.Add("foes never spotted any hero during the whole battle");
                    if (battle.over == -2) problems.Add("battle timed out: the two sides never finished the fight");
                    Shot("6-game-over");
                    AiScenarios();
                    RuleScenarios();
                    PropScenarios();
                    MoveScenarios();
                    GearScenarios();
                    FxScenarios();
                    ExploreScenarios();
                    SaveScenarios();
                    // 最後讀取開場時存的檔：整個場景會重建，下兩步檢查它
                    if (!(bool)CallR(gc, "LoadGame")) { problems.Add("load: LoadGame refused the save made at the start"); Finish(); break; }
                    step = 8; stepAt = Time.realtimeSinceStartup;
                    break;
            }
        }

        // ───────── 敵方 AI 的情境測試（純規則層，另開一場不接畫面的戰鬥） ─────────

        static readonly System.Text.RegularExpressions.Regex Tags = new System.Text.RegularExpressions.Regex("<[^>]+>");

        // 把規則層的事件轉成文字戰報
        static void Dump(Battle b, StringBuilder log)
        {
            foreach (var e in b.Drain())
            {
                if (e.type == EvType.Log) log.AppendLine("  " + Tags.Replace(e.text, ""));
                else if (e.type == EvType.TurnStart) log.AppendLine("[第 " + e.n + " 輪] " + e.a.name);
                else if (e.type == EvType.Move)
                {
                    int c = e.path[e.path.Length - 1];
                    log.AppendLine("  " + e.a.name + " 移動到 (" + b.world.X(c) + "," + b.world.Y(c) + "," + b.world.Z(c) + ")");
                }
                else if (e.type == EvType.Face) log.AppendLine("  " + e.a.name + " 轉向 (" + e.a.fx + "," + e.a.fy + ")");
            }
        }

        static void Teleport(Battle b, Unit u, int cell)
        {
            b.world.occ[b.CellOf(u)] = -1;
            u.x = b.world.X(cell); u.y = b.world.Y(cell); u.z = b.world.Z(cell);
            b.world.occ[cell] = (short)u.id;
        }

        static int FoesAtPost(Battle b)
        {
            int n = 0;
            foreach (var u in b.units) if (u.team == 1 && u.alive && b.CellOf(u) == u.homeCell) n++;
            return n;
        }

        /// <summary>
        /// 一、我方按兵不動：敵人應守在原地張望，不會發現任何人。
        /// 二、讓一名俠客在山賊眼前現身後立刻撤走：敵人應前往查看，撲空後放棄並回防。
        /// 三、雙方都交給 AI 打一整場，輸出文字戰報供人閱讀。
        /// </summary>
        static void AiScenarios()
        {
            var log = new StringBuilder();
            var b = Scenario.Create();
            var w = b.world;
            b.Start();
            Dump(b, log);

            // 一、按兵不動
            log.AppendLine("===== 一、我方按兵不動 6 輪 =====");
            bool scanned = false;
            while (b.over == 0 && b.round <= 6)
            {
                b.EndTurn();
                Dump(b, log);
                foreach (var u in b.units) if (u.team == 1 && (u.fx != u.homeFx || u.fy != u.homeFy)) scanned = true;
            }
            Note("AI scenario 1 (heroes idle): alerts " + b.alerts + ", foes at post " + FoesAtPost(b) + "/6, scanned " + scanned);
            if (b.alerts != 0) problems.Add("AI scenario 1: foes spotted idle heroes far away");
            if (FoesAtPost(b) != 6) problems.Add("AI scenario 1: foes left their posts with no one in sight");
            if (!scanned) problems.Add("AI scenario 1: guards never looked around");

            // 二、現身後撤走。找一格山賊看得見、又離我方三人最遠的地方
            log.AppendLine("===== 二、沈青嵐在山賊甲眼前現身後撤走 =====");
            Unit hero = b.units[0], bandit = b.units[3];
            int home = b.CellOf(hero), spot = -1, spotDist = -1;
            for (int y = 0; y < w.D; y++)
                for (int x = 0; x < w.W; x++)
                {
                    int z = w.TopStand(x, y);
                    if (z < 0 || w.occ[w.Idx(x, y, z)] >= 0 || b.PropAt(w.Idx(x, y, z)) != null) continue;
                    int d = Math.Max(Math.Abs(x - bandit.x), Math.Abs(y - bandit.y));
                    if (d < 3 || d > 5 || !b.vision.CanSeeCell(bandit.x, bandit.y, bandit.z, bandit.fx, bandit.fy, x, y, z)) continue;
                    int far = 99, near = 99;
                    foreach (var h in b.units)
                    {
                        int hd = Math.Max(Math.Abs(x - h.x), Math.Abs(y - h.y));
                        if (h.team == 0) far = Math.Min(far, hd); else near = Math.Min(near, hd);
                    }
                    if (near < 3) continue; // 不能貼著任何敵人，否則不必走過去就知道沒人
                    if (far > spotDist) { spotDist = far; spot = w.Idx(x, y, z); }
                }
            if (spot < 0) { problems.Add("AI scenario 2: no cell in the bandit's view to test with"); }
            else
            {
                hero.stealth = -99; // 這個情境不測潛行：一定被看見
                Teleport(b, hero, spot);
                b.Observe();
                bool seenNow = hero.inSight;
                int alertsAfterShow = b.alerts;
                Teleport(b, hero, home);
                b.Observe();
                bool remembered = hero.knownCell == spot && !hero.inSight;
                Dump(b, log);
                log.AppendLine("  （現身位置 " + w.X(spot) + "," + w.Y(spot) + "," + w.Z(spot) + "，離我方最近 " + spotDist + " 格；已撤回原位）");

                bool visited = false, forgot = false;
                int forgotRound = -1, backRound = -1, start = b.round;
                while (b.over == 0 && b.round <= start + 12)
                {
                    b.EndTurn();
                    Dump(b, log);
                    foreach (var u in b.units)
                        if (u.team == 1 && Math.Max(Math.Abs(u.x - w.X(spot)), Math.Abs(u.y - w.Y(spot))) <= 2) visited = true;
                    if (!forgot && hero.knownCell < 0) { forgot = true; forgotRound = b.round; }
                    if (forgot && backRound < 0 && FoesAtPost(b) == 6) { backRound = b.round; break; }
                }
                Note("AI scenario 2 (show then hide): seen " + seenNow + ", alerts " + alertsAfterShow + ", remembered " + remembered
                    + ", visited " + visited + ", gave up at round " + forgotRound + " (shown at " + start + "), all back at post at round " + backRound + ", alerts in total " + b.alerts);
                if (!seenNow || alertsAfterShow != 1) problems.Add("AI scenario 2: hero standing in the bandit's view was not spotted exactly once");
                if (!remembered) problems.Add("AI scenario 2: foes should remember the last seen cell after the hero left");
                if (!visited) problems.Add("AI scenario 2: no foe went to check the last seen cell");
                if (!forgot) problems.Add("AI scenario 2: foes never gave up the cold trail");
                if (backRound < 0) problems.Add("AI scenario 2: foes did not return to their posts");
            }
            File.WriteAllText(Path.Combine(outDir, "ai-scenarios.txt"), log.ToString(), Encoding.UTF8);

            // 三、整場自動對戰的文字戰報
            log = new StringBuilder();
            b = Scenario.Create();
            b.autoHeroes = true;
            b.maxRounds = 80;
            b.Start();
            Dump(b, log);
            log.AppendLine("結果 over=" + b.over + "，第 " + b.round + " 輪，敵方發現我方 " + b.alerts + " 次");
            File.WriteAllText(Path.Combine(outDir, "ai-battle.txt"), log.ToString(), Encoding.UTF8);
            Note("AI scenario 3 (full auto battle): over " + b.over + " at round " + b.round + ", alerts " + b.alerts);
            if (b.over == -2) problems.Add("AI scenario 3: auto battle timed out");
            if (b.alerts == 0) problems.Add("AI scenario 3: foes never spotted any hero");
        }

        // ───────── 潛行、突襲、掩蔽、借機攻擊的情境測試（純規則層） ─────────

        static int Cheb(Unit u, int x, int y) { return Math.Max(Math.Abs(u.x - x), Math.Abs(u.y - y)); }

        // 找一個可站、沒人、沒有物件、符合條件的格子（屋頂下的亭內、樹冠下的地面也算）
        static int FindCell(Battle b, Func<int, int, int, bool> ok)
        {
            var w = b.world;
            for (int y = 0; y < w.D; y++)
                for (int x = 0; x < w.W; x++)
                    for (int z = 1; z < w.H - 1; z++)
                    {
                        if (!w.Standable(x, y, z)) continue;
                        if (w.occ[w.Idx(x, y, z)] >= 0 || w.blocked[w.Idx(x, y, z)] || b.PropAt(w.Idx(x, y, z)) != null) continue;
                        if (ok(x, y, z)) return w.Idx(x, y, z);
                    }
            return -1;
        }

        static void Expect(bool cond, string what) { if (!cond) problems.Add("rule scenario: " + what); }

        static void RuleScenarios()
        {
            var log = new StringBuilder();
            var b = Scenario.Create();
            var w = b.world;
            b.Start();
            Unit hero = b.units[0], bandit = b.units[3], boss = b.units[8];
            Func<Unit, int, int, int, bool> sees = (v, x, y, z) => b.vision.CanSeeCell(v.x, v.y, v.z, v.fx, v.fy, x, y, z);

            // ── 潛行 ──
            log.AppendLine("===== 潛行對警覺 =====");
            int far = FindCell(b, (x, y, z) =>
            {
                if (!sees(bandit, x, y, z)) return false;
                foreach (var f in b.units) if (f.team == 1 && Cheb(f, x, y) < 5) return false;
                return true;
            });
            bool hidFar = false, caughtFar = false, caughtNear = false, hidBehind = false;
            if (far < 0) problems.Add("rule scenario: no distant cell in the bandit's view");
            else
            {
                hero.stealth = 30; Teleport(b, hero, far); b.Observe();
                hidFar = hero.knownCell < 0;
                hero.stealth = -5; b.Observe();
                caughtFar = hero.knownCell >= 0;
                b.Forget(hero);
            }
            int near = FindCell(b, (x, y, z) => Cheb(bandit, x, y) >= 2 && Cheb(bandit, x, y) <= Battle.PlainSight
                && Vision.InFront(bandit.x, bandit.y, bandit.fx, bandit.fy, x, y) && sees(bandit, x, y, z));
            if (near < 0) problems.Add("rule scenario: no close cell in front of the bandit");
            else
            {
                hero.stealth = 30; Teleport(b, hero, near); b.Observe();
                caughtNear = hero.knownCell >= 0;
                b.Forget(hero);
            }
            // 摸到寨主背後：貼身、不在任何敵人正前方的近距離內
            int behind = FindCell(b, (x, y, z) =>
            {
                if (Cheb(boss, x, y) != 1 || Math.Abs(z - boss.z) > 1) return false;
                foreach (var f in b.units)
                    if (f.team == 1 && Cheb(f, x, y) <= Battle.PlainSight && Vision.InFront(f.x, f.y, f.fx, f.fy, x, y) && sees(f, x, y, z)) return false;
                return true;
            });
            if (behind < 0) problems.Add("rule scenario: no cell behind the boss");
            else
            {
                hero.stealth = 30; Teleport(b, hero, behind); b.Observe();
                hidBehind = hero.knownCell < 0;
            }
            Dump(b, log);
            Note("Rule scenario stealth: hidden far away with a high roll " + hidFar + ", caught with a low roll " + caughtFar
                + ", always caught in plain sight " + caughtNear + ", hidden right behind the boss " + hidBehind);
            Expect(hidFar, "a high stealth roll should keep a distant hero unnoticed");
            Expect(caughtFar, "a low stealth roll should get a distant hero noticed");
            Expect(caughtNear, "a hero within plain sight in front of a guard must be noticed whatever the roll");
            Expect(hidBehind, "a high stealth roll should let a hero stand unnoticed behind the boss");

            // ── 突襲 ──
            log.AppendLine("===== 出其不意與措手不及 =====");
            if (behind >= 0 && hidBehind)
            {
                hero.actionLeft = true;
                b.Face(hero, boss.x, boss.y);
                bool stillHidden = hero.knownCell < 0;
                string why = b.AdvReasons(hero, Db.Sword, hero.x, hero.y, hero.z, boss);
                int adv = b.AdvMode(hero, Db.Sword, hero.x, hero.y, hero.z, boss);
                bool used = b.UseSkill(hero, Db.Sword, boss, -1);
                bool surprised = boss.surprised, exposed = hero.knownCell >= 0;
                Dump(b, log);
                var sb2 = new StringBuilder();
                int r0 = b.round;
                while (b.over == 0 && b.round < r0 + 2) { b.EndTurn(); Dump(b, sb2); }
                log.Append(sb2);
                bool skipped = sb2.ToString().Contains(boss.name + " 措手不及，這一回合來不及反應");
                Note("Rule scenario surprise: still hidden " + stillHidden + ", advantage " + adv + " (" + why.Replace("\n", " / ") + "), attacked " + used
                    + ", boss surprised " + surprised + ", attacker exposed " + exposed + ", boss lost a turn " + skipped);
                Expect(stillHidden && adv > 0 && why.Contains("出其不意"), "an unseen attacker should have advantage");
                Expect(used && surprised && exposed, "an ambush should surprise the target and expose the attacker");
                Expect(skipped && !boss.surprised, "a surprised unit should lose exactly its next turn");
            }

            // ── 掩蔽 ──
            b = Scenario.Create(); w = b.world; b.Start();
            {
                var archer = b.units[6];
                int open = 0, half = 0, most = 0;
                for (int y = 0; y < w.D; y++)
                    for (int x = 0; x < w.W; x++)
                    {
                        int z = w.TopStand(x, y);
                        if (z < 0 || Cheb(archer, x, y) == 0 || !b.CanHit(Db.Bow, archer.x, archer.y, archer.z, x, y, z)) continue;
                        int c = b.CoverBonus(Db.Bow, archer.x, archer.y, archer.z, x, y, z);
                        if (c == 0) open++; else if (c == 2) half++; else most++;
                    }
                int meleeCover = b.CoverBonus(Db.Saber, archer.x, archer.y, archer.z, archer.x + 1, archer.y, archer.z);
                Note("Rule scenario cover: from " + archer.name + "'s post, targets in the open " + open + ", half cover " + half + ", three-quarter cover " + most + "; melee cover " + meleeCover);
                Expect(open > 0, "some cells in bow range should have no cover");
                Expect(half + most > 0, "some cells in bow range should have cover");
                Expect(open > half + most, "most cells in bow range should be in the open (cover is the exception)");
                Expect(meleeCover == 0, "melee attacks ignore cover");
            }

            // ── 借機攻擊 ──
            log.AppendLine("===== 借機攻擊 =====");
            hero = b.units[0]; bandit = b.units[3];
            var bandit2 = b.units[4];
            hero.stealth = -99;
            int adj = FindCell(b, (x, y, z) => Cheb(bandit, x, y) == 1 && Math.Abs(z - bandit.z) <= 1);
            if (adj < 0) problems.Add("rule scenario: no free cell next to the bandit");
            else
            {
                Teleport(b, hero, adj); b.Observe();
                hero.reactionLeft = true; bandit.reactionLeft = true; bandit.moveLeft = 5;
                b.ComputeReach(bandit);
                int dest = -1;
                foreach (int c in b.pf.reach) if (b.pf.CostOf(c) <= 5 && Cheb(hero, w.X(c), w.Y(c)) >= 2) dest = c;
                var list = new List<Unit>();
                int from = b.CellOf(bandit);
                int n = dest < 0 ? -1 : b.Provokers(bandit, b.pf.Path(dest), list);
                bool moved = dest >= 0 && b.Move(bandit, dest);
                Dump(b, log);
                bool placed = !bandit.alive ? w.occ[dest] != bandit.id : b.CellOf(bandit) == dest && w.occ[dest] == bandit.id;
                Note("Rule scenario opportunity attack: preview " + n + ", moved " + moved + ", hero reaction used " + !hero.reactionLeft
                    + ", bandit alive " + bandit.alive + " hp " + bandit.hp + "/" + bandit.maxHp + ", board consistent " + (placed && w.occ[from] < 0));
                Expect(n == 1 && list.Count == 1 && list[0] == hero, "the preview should name the hero as the one who gets a free strike");
                Expect(moved && !hero.reactionLeft, "leaving a foe's reach should provoke an opportunity attack");
                Expect(placed && w.occ[from] < 0, "the board should stay consistent after an interrupted move");
            }
            int adj2 = FindCell(b, (x, y, z) => Cheb(bandit2, x, y) == 1 && Math.Abs(z - bandit2.z) <= 1);
            if (adj2 < 0) problems.Add("rule scenario: no free cell next to the second bandit");
            else
            {
                // 先用梯雲縱抽身：不該挨打；再走一次不用輕功：該挨打
                bool[] struck = new bool[2];
                for (int k = 0; k < 2; k++)
                {
                    Teleport(b, hero, adj2); b.Observe();
                    bandit2.reactionLeft = true; hero.light = k == 0; hero.moveLeft = 6;
                    b.ComputeReach(hero);
                    int dest = -1;
                    foreach (int c in b.pf.reach) if (b.pf.CostOf(c) <= 6 && Cheb(bandit2, w.X(c), w.Y(c)) >= 2) dest = c;
                    if (dest >= 0) b.Move(hero, dest);
                    struck[k] = !bandit2.reactionLeft;
                    Dump(b, log);
                }
                hero.light = false;
                Note("Rule scenario disengage: struck while using light-foot " + struck[0] + ", struck without " + struck[1] + ", hero hp " + hero.hp + "/" + hero.maxHp);
                Expect(!struck[0], "light-foot (梯雲縱) should let a hero leave without provoking");
                Expect(struck[1], "walking away normally should provoke the bandit");
            }
            File.WriteAllText(Path.Combine(outDir, "rule-scenarios.txt"), log.ToString(), Encoding.UTF8);
        }

        // ───────── 地圖物件與 NPC ─────────

        /// <summary>每種地圖物件各拍一張特寫，再拍一張對話框。不改動戰局。</summary>
        static void PropShots(object gc, Battle battle)
        {
            var w = battle.world;
            var pv = (PropViews)Get(gc, "propViews");
            var counts = new int[8];
            var seen = new bool[8];
            var list = new StringBuilder();
            foreach (var p in battle.props)
            {
                counts[(int)p.kind]++;
                if (p.kind != PropKind.Grass) list.Append(' ').Append(p.name).Append('(').Append(w.X(p.cell)).Append(',').Append(w.Y(p.cell)).Append(',').Append(w.Z(p.cell)).Append(')');
                if (seen[(int)p.kind]) continue;
                seen[(int)p.kind] = true;
                Vector3 pos = Vis.Cell(w, p.cell);
                if (p.kind == PropKind.Trap) { p.found = true; pv.Sync(p); }
                ShotScenic("O-" + p.kind, pos + new Vector3(1.5f, 1.0f, -1.9f), pos + Vector3.up * 0.3f);
                if (p.kind == PropKind.Trap) { p.found = false; pv.Sync(p); }
            }
            Note("Map props: boulders " + counts[0] + ", chests " + counts[1] + ", grass " + counts[2] + ", traps " + counts[3] + ", npcs " + counts[4] + ";" + list);
            if (counts[0] < 1 || counts[1] < 1 || counts[2] < 4 || counts[3] < 2 || counts[4] < 1) problems.Add("map props missing: expected a boulder, a chest, 4+ grass cells, 2 traps and an NPC");

            // 掉在地上的道具：在我方附近放三樣，拍一張（留在場上無妨）
            {
                var actor = battle.active;
                Item[] show = { Item.Salve, Item.Thunder, Item.Grapnel };
                Vector3 mid = Vector3.zero;
                int placed = 0;
                for (int i = 0; i < show.Length; i++)
                {
                    int lx = actor.x + 1 + i, ly = actor.y + 2, lz = w.TopStand(lx, ly);
                    if (lz < 0) continue;
                    int lc = w.Idx(lx, ly, lz);
                    if (w.occ[lc] >= 0 || battle.PropAt(lc) != null) continue;
                    var lp = battle.AddProp(PropKind.Loot, lc, show[i].name);
                    lp.item = show[i];
                    pv.Sync(lp);
                    mid += Vis.Cell(w, lc);
                    placed++;
                }
                if (placed > 0)
                {
                    mid /= placed;
                    ShotScenic("O-Loot", mid + new Vector3(0.3f, 1.3f, -2.4f), mid + Vector3.up * 0.15f);
                }
                foreach (var it in new[] { Item.Salve, Item.QiPill, Item.Antidote, Item.Lime, Item.SmokeBomb, Item.Thunder, Item.Caltrops, Item.Venom, Item.Grapnel })
                    if (pv.ItemMesh(it) == null || pv.ItemMesh(it).vertexCount == 0) problems.Add("item " + it.name + " has no model");
            }

            // 對話框：暫時把行動中的俠客挪到路人旁邊，開啟對話、截圖、復原
            Prop npc = null;
            foreach (var p in battle.props) if (p.kind == PropKind.Npc) npc = p;
            if (npc == null) return;
            var u = battle.active;
            int back = battle.CellOf(u);
            int side = FindCell(battle, (x, y, z) => Math.Max(Math.Abs(x - w.X(npc.cell)), Math.Abs(y - w.Y(npc.cell))) == 1 && Math.Abs(z - w.Z(npc.cell)) <= 1);
            if (side < 0) { problems.Add("no free cell next to the NPC"); return; }
            Teleport(battle, u, side);
            Call(gc, "OpenDialog", npc, null);
            Shot("7-dialog");
            var hud = (Hud)Get(gc, "hud");
            if (!hud.DialogOpen) problems.Add("dialog did not open");
            hud.HideDialog();
            Set(gc, "talking", false);
            Set(gc, "talkNpc", null);
            Teleport(battle, u, back);
            pv.Face(npc, Vis.Cell(w, npc.cell) + new Vector3(npc.fx, 0, npc.fy));
            ((List<UnitView>)Get(gc, "views"))[u.id].root.rotation = Quaternion.LookRotation(new Vector3(u.fx, 0, u.fy));
        }

        // 與 cell 正交相鄰、沒人、高度差 1 層內的格子
        static int Orth(Battle b, int cell)
        {
            var w = b.world;
            int cx = w.X(cell), cy = w.Y(cell), cz = w.Z(cell);
            return FindCell(b, (x, y, z) => Math.Abs(x - cx) + Math.Abs(y - cy) == 1 && Math.Abs(z - cz) <= 1);
        }

        static Prop FirstProp(Battle b, PropKind kind, int skip = 0)
        {
            foreach (var p in b.props) if (p.kind == kind && skip-- == 0) return p;
            return null;
        }

        static void ExpectP(bool cond, string what) { if (!cond) problems.Add("prop scenario: " + what); }

        /// <summary>寶箱、絆鈴索、芒草、滾石、路人交談、喝令、士氣，各跑一組純規則層的情境。</summary>
        static void PropScenarios()
        {
            var log = new StringBuilder();

            // ── 寶箱 ──
            log.AppendLine("===== 寶箱 =====");
            var b = Scenario.Create(); var w = b.world; b.Start();
            {
                var hero = b.units[2];
                var chest = FirstProp(b, PropKind.Chest);
                int side = chest != null ? Orth(b, chest.cell) : -1;
                if (side < 0) problems.Add("prop scenario: cannot stand next to the chest");
                else
                {
                    Teleport(b, hero, side);
                    hero.hp = 10;
                    hero.freeLeft = false;
                    bool gated = b.WhyNot(hero, chest) != null && !b.Interact(hero, chest);
                    int tries = 0;
                    while (!chest.open && tries++ < 40) { hero.freeLeft = true; b.Interact(hero, chest); }
                    hero.freeLeft = true;
                    bool emptyAfter = !b.Interact(hero, chest);
                    Dump(b, log);
                    Note("Prop scenario chest: needs the free interaction " + gated + ", opened after " + tries + " tries, bag now holds salve ×" + b.BagCount(hero, Item.Salve) + " and qi pill ×" + b.BagCount(hero, Item.QiPill)
                        + ", cannot be opened twice " + emptyAfter + ", still blocks its cell " + w.blocked[chest.cell]);
                    ExpectP(gated, "opening a chest should need the once-per-turn free interaction");
                    ExpectP(chest.open && b.BagCount(hero, Item.Salve) == 3 && b.BagCount(hero, Item.QiPill) == 2, "a chest's contents (2 salves, 1 qi pill) should go into the opener's bag");
                    ExpectP(emptyAfter, "an empty chest cannot be opened again");
                }
            }

            // ── 絆鈴索 ──
            log.AppendLine("===== 絆鈴索 =====");
            b = Scenario.Create(); w = b.world; b.Start();
            {
                // 在我方出生點旁另外埋兩條，遠離敵人，才不會被其他因素干擾
                Unit brawler = b.units[1], sword = b.units[0];
                int c1 = Orth(b, b.CellOf(brawler));
                Prop t1 = c1 >= 0 ? b.AddProp(PropKind.Trap, c1, "絆鈴索", 20) : null; // 藏得極好：沒人看得出來
                Prop t2 = null;
                int s1 = t1 != null ? b.CellOf(brawler) : -1;
                if (s1 < 0) problems.Add("prop scenario: cannot stand next to the first trap");
                else
                {
                    // 石破山沒看出來，一腳踩上去
                    b.Observe();
                    bool unseen = !t1.found;
                    int hp0 = brawler.hp, alerts0 = b.alerts;
                    brawler.moveLeft = 5;
                    b.ComputeReach(brawler);
                    bool moved = b.Move(brawler, t1.cell);
                    Dump(b, log);
                    Note("Prop scenario trap sprung: unnoticed by a dull eye " + unseen + ", moved " + moved + ", trap gone " + t1.gone + ", hp " + hp0 + " → " + brawler.hp
                        + ", alerts " + alerts0 + " → " + b.alerts + ", hero known " + (brawler.knownCell >= 0));
                    ExpectP(unseen, "a hero with low perception should not notice the tripwire");
                    ExpectP(moved && t1.gone && brawler.hp < hp0, "stepping on a tripwire should spring it and hurt");
                    ExpectP(brawler.knownCell >= 0 && b.alerts > alerts0, "a sprung tripwire should alert the camp");
                }
                int c2 = sword.alive ? Orth(b, b.CellOf(sword)) : -1;
                if (c2 >= 0) t2 = b.AddProp(PropKind.Trap, c2, "絆鈴索", 12);
                if (t2 == null) problems.Add("prop scenario: cannot stand next to the second trap");
                else
                {
                    // 沈青嵐眼尖，就在旁邊，一看就發現，然後拆掉
                    b.Observe();
                    bool found = t2.found, blocked = w.blocked[t2.cell];
                    sword.moveLeft = 6;
                    b.ComputeReach(sword);
                    bool walkable = b.pf.reach.Contains(t2.cell);
                    int tries = 0;
                    while (!t2.gone && tries++ < 60) { sword.actionLeft = true; b.Interact(sword, t2); }
                    Dump(b, log);
                    Note("Prop scenario trap found: noticed by a sharp eye " + found + ", then blocks the path " + (blocked && !walkable) + ", removed after " + tries + " tries, cell free again " + !w.blocked[t2.cell]);
                    ExpectP(found && blocked && !walkable, "a sharp-eyed hero should spot the tripwire and it should then block the path");
                    ExpectP(t2.gone && !w.blocked[t2.cell], "a tripwire should be removable and free its cell");
                }
            }

            // ── 芒草叢 ──
            log.AppendLine("===== 芒草叢 =====");
            b = Scenario.Create(); w = b.world; b.Start();
            {
                Unit hero = b.units[0], bandit = b.units[3];
                int near = FindCell(b, (x, y, z) => Cheb(bandit, x, y) >= 2 && Cheb(bandit, x, y) <= Battle.PlainSight
                    && Vision.InFront(bandit.x, bandit.y, bandit.fx, bandit.fy, x, y) && b.vision.CanSeeCell(bandit.x, bandit.y, bandit.z, bandit.fx, bandit.fy, x, y, z));
                if (near < 0) problems.Add("prop scenario: no cell in front of the bandit for the grass test");
                else
                {
                    hero.stealth = 30; Teleport(b, hero, near); b.Observe();
                    bool caughtBare = hero.knownCell >= 0;
                    b.Forget(hero);
                    b.AddProp(PropKind.Grass, near, "芒草叢");
                    hero.stealth = 30; b.Observe();
                    bool hidHigh = hero.knownCell < 0;
                    int per = b.Perception(bandit);
                    hero.stealth = per - 3; hero.stealthTold = false; b.Observe();
                    bool hidBonus = hero.knownCell < 0;
                    hero.stealth = per - 6; b.Observe();
                    bool caughtLow = hero.knownCell >= 0;
                    Dump(b, log);
                    Note("Prop scenario grass: caught in the open " + caughtBare + ", hidden in grass with a high roll " + hidHigh + ", +5 makes a marginal roll enough " + hidBonus + ", a poor roll still fails " + caughtLow);
                    ExpectP(caughtBare && hidHigh && hidBonus && caughtLow, "tall grass should hide a hero in plain sight and give +5 stealth, but not make them invisible");
                }
            }

            // ── 滾石 ──
            log.AppendLine("===== 滾石 =====");
            b = Scenario.Create(); w = b.world; b.Start();
            {
                Unit hero = b.units[1], bandit = b.units[3];
                var rock = FirstProp(b, PropKind.Boulder);
                if (rock == null) problems.Add("prop scenario: no boulder on the map");
                else
                {
                    int rx = w.X(rock.cell), ry = w.Y(rock.cell), start = rock.cell;
                    int north = w.Idx(rx, ry - 1, w.TopStand(rx, ry - 1)), south = w.Idx(rx, ry + 1, w.TopStand(rx, ry + 1));
                    Teleport(b, hero, north);
                    if (w.occ[south] < 0) Teleport(b, bandit, south);
                    int hp0 = bandit.hp, tries = 0;
                    hero.actionLeft = false;
                    bool gated = !b.Interact(hero, rock);
                    while (rock.cell == start && !rock.gone && bandit.hp == hp0 && tries++ < 30) { hero.actionLeft = true; b.Interact(hero, rock); }
                    Dump(b, log);
                    bool flags = !rock.gone ? w.blocked[rock.cell] && (rock.cell == start || !w.blocked[start]) : !w.blocked[start];
                    Note("Prop scenario boulder at (" + rx + "," + ry + "): needs an action " + gated + ", pushed after " + tries + " tries, rolled to (" + w.X(rock.cell) + "," + w.Y(rock.cell) + "," + w.Z(rock.cell)
                        + "), sunk " + rock.gone + ", bandit hp " + hp0 + " → " + bandit.hp + " alive " + bandit.alive + ", blocking flags consistent " + flags);
                    ExpectP(gated, "pushing a boulder should need an action");
                    ExpectP(bandit.hp < hp0, "a boulder pushed onto a bandit should hurt him");
                    ExpectP(flags, "blocking flags should follow the boulder");
                    ExpectP(bandit.alive || rock.cell != start || rock.gone, "the boulder should roll on after crushing its victim");
                }
            }

            // ── 路人交談 ──
            log.AppendLine("===== 路人交談 =====");
            b = Scenario.Create(); w = b.world; b.Start();
            {
                Unit hero = b.units[0], knife = b.units[2];
                var npc = FirstProp(b, PropKind.Npc);
                int side = npc != null ? Orth(b, npc.cell) : -1;
                if (side < 0) problems.Add("prop scenario: cannot stand next to the NPC");
                else
                {
                    bool farRefused = b.Talk(hero, npc, TalkWay.Persuade) == null;
                    Teleport(b, hero, side);
                    hero.hp = 20;
                    string first = b.Talk(hero, npc, TalkWay.Persuade);
                    bool onceOnly = !b.CanTalk(hero, npc, TalkWay.Persuade);
                    int tries = 1;
                    while (!(npc.guided && npc.gave) && tries++ < 60) { npc.tried = 0; b.Talk(hero, npc, TalkWay.Persuade); }
                    bool revealed = false;
                    int trapsFound = 0;
                    foreach (var e in b.Drain())
                    {
                        if (e.type == EvType.Reveal && e.path.Length > 0) revealed = true;
                        if (e.type == EvType.Log) log.AppendLine("  " + Tags.Replace(e.text, ""));
                    }
                    foreach (var p in b.props) if (p.kind == PropKind.Trap && p.found) trapsFound++;
                    Note("Prop scenario talk (persuade): refused from afar " + farRefused + ", first reply " + (first != null) + ", one try per hero per approach " + onceOnly + ", guided after " + tries
                        + " tries, attitude " + Prop.AttitudeNames[npc.attitude] + ", map revealed " + revealed + ", traps shown " + trapsFound + "/2, salves in bag " + b.BagCount(hero, Item.Salve));
                    ExpectP(farRefused && first != null && onceOnly, "talking needs to be adjacent, and each hero gets one try per approach");
                    ExpectP(npc.guided && revealed && trapsFound == 2, "a persuaded woodcutter should reveal the camp and both tripwires");
                    ExpectP(npc.attitude == 2 && npc.gave && b.BagCount(hero, Item.Salve) == 2, "a friendly woodcutter should hand over a salve");
                }

                // 另一場：威嚇失敗會讓他大叫
                b = Scenario.Create(); w = b.world; b.Start();
                knife = b.units[2];
                npc = FirstProp(b, PropKind.Npc);
                side = npc != null ? Orth(b, npc.cell) : -1;
                if (side >= 0)
                {
                    Teleport(b, knife, side);
                    int tries = 0;
                    bool failed = false;
                    while (!failed && tries++ < 80)
                    {
                        npc.tried = 0; npc.attitude = 1; npc.guided = true; // 只測失敗的後果
                        int before = npc.attitude;
                        b.Talk(knife, npc, TalkWay.Intimidate);
                        failed = npc.attitude < before;
                    }
                    Dump(b, log);
                    Note("Prop scenario talk (failed threat): failed after " + tries + " tries, he cried out and exposed the hero " + (knife.knownCell >= 0) + ", attitude " + Prop.AttitudeNames[npc.attitude]
                        + ", refuses to talk " + (b.WhyNot(knife, npc) != null));
                    ExpectP(failed && knife.knownCell >= 0, "a failed threat should make the woodcutter cry out and expose the hero");
                    ExpectP(npc.attitude == 0 && b.WhyNot(knife, npc) != null, "a hostile woodcutter should refuse to talk");
                }
            }

            // ── 喝令與士氣 ──
            log.AppendLine("===== 喝令與士氣 =====");
            b = Scenario.Create(); w = b.world; b.Start();
            {
                Unit hero = b.units[1], weak = b.units[3], fresh = b.units[4], boss = b.units[8];
                int side = Orth(b, b.CellOf(weak));
                if (side < 0) problems.Add("prop scenario: cannot stand next to the bandit");
                else
                {
                    Teleport(b, hero, side); hero.stealth = -99; b.Observe();
                    weak.hp = 5;
                    int dcWeak = b.ShoutDc(weak), dcFresh = b.ShoutDc(fresh), dcBoss = b.ShoutDc(boss);
                    int cell = b.CellOf(weak);
                    hero.actionLeft = true;
                    bool used = b.UseSkill(hero, Db.Shout, weak, -1);
                    bool yielded = weak.fled && !weak.alive && w.occ[cell] < 0;

                    int side2 = Orth(b, b.CellOf(fresh));
                    bool cowed = false, shrugged = false;
                    if (side2 >= 0)
                    {
                        Teleport(b, hero, side2); b.Observe();
                        int tries = 0;
                        while (fresh.fright == 0 && tries++ < 40)
                        {
                            fresh.defied = 0; hero.actionLeft = true;
                            b.UseSkill(hero, Db.Shout, fresh, -1);
                            if (fresh.fright == 0 && fresh.defied != 0 && !b.CanTarget(hero, Db.Shout, fresh)) shrugged = true;
                        }
                        cowed = fresh.fright > 0 && fresh.alive
                            && b.AdvReasons(fresh, Db.Saber, fresh.x, fresh.y, fresh.z, hero).Contains("膽寒");
                    }
                    Dump(b, log);
                    Note("Prop scenario shout: DC wounded bandit " + dcWeak + " / healthy bandit " + dcFresh + " / boss " + dcBoss + "; wounded bandit surrendered " + (used && yielded)
                        + ", healthy bandit frightened " + cowed + ", a failed shout stops working on him " + shrugged + " (only checked if a failure occurred)");
                    ExpectP(dcWeak < dcFresh && dcFresh < dcBoss, "shout DC should be lowest for a wounded bandit and highest for the boss");
                    ExpectP(used && yielded, "a wounded bandit should surrender to a successful shout and leave the field");
                    ExpectP(cowed, "a healthy bandit should be frightened (attack disadvantage) by a successful shout");
                }

                // 士氣：寨主一倒，嘍囉各自擲定力豁免
                b = Scenario.Create(); w = b.world; b.Start();
                boss = b.units[8];
                Call(b, "Damage", boss, 999, false);
                var sb2 = new StringBuilder();
                Dump(b, sb2);
                log.Append(sb2);
                int fled = 0, stood = 0;
                foreach (var x in b.units) if (x.team == 1 && !x.boss) { if (x.fled) fled++; else if (x.alive) stood++; }
                string text = sb2.ToString();
                int rolls = System.Text.RegularExpressions.Regex.Matches(text, "定力豁免").Count;
                Note("Prop scenario morale: boss down → announced " + text.Contains("軍心大亂") + ", saves rolled " + rolls + ", fled " + fled + ", stood firm " + stood + ", battle over " + b.over);
                ExpectP(text.Contains("軍心大亂") && rolls == 5 && fled + stood == 5, "when the boss falls every remaining bandit should roll morale once");
                ExpectP(fled < 5 ? b.over == 0 : b.over == 1, "the battle should end only if everyone is gone");
            }

            File.WriteAllText(Path.Combine(outDir, "prop-scenarios.txt"), log.ToString(), Encoding.UTF8);
        }

        // ───────── 八方向移動、縱躍、碰撞體積 ─────────

        static void ExpectM(bool cond, string what) { if (!cond) problems.Add("move scenario: " + what); }

        // 以 (x0,y0) 為西南角、寬 cols 深 rows 的一塊地是否全部同高、可站、沒人沒東西、角落沒有擺設
        static bool FlatPatch(Battle b, int x0, int y0, int cols, int rows, out int z)
        {
            var w = b.world;
            z = -1;
            if (x0 < 0 || y0 < 0 || x0 + cols > w.W || y0 + rows > w.D) return false;
            for (int y = y0; y < y0 + rows; y++)
                for (int x = x0; x < x0 + cols; x++)
                {
                    int t = w.TopStand(x, y);
                    if (t < 0 || (z >= 0 && t != z)) return false;
                    z = t;
                    int c = w.Idx(x, y, t);
                    if (w.occ[c] >= 0 || w.blocked[c] || b.PropAt(c) != null) return false;
                    if (w.Get(x, y, t + 2) != Block.Air || w.Get(x, y, t + 3) != Block.Air) return false; // 頭頂要空，才能縱躍
                }
            for (int y = y0; y <= y0 + rows; y++)
                for (int x = x0; x <= x0 + cols; x++)
                    if (w.CornerAt(x, y) != 0) return false;
            return true;
        }

        static void MoveScenarios()
        {
            var b = Scenario.Create();
            var w = b.world;
            b.Start();
            Unit sword = b.units[0], brawler = b.units[1];

            int bamboo = 0, lantern = 0, banner = 0;
            foreach (byte k in w.corner) { if (k == Decor.Bamboo) bamboo++; else if (k == Decor.Lantern) lantern++; else if (k == Decor.Banner) banner++; }
            Note("Move scenario decor: bamboo clumps " + bamboo + ", stone lanterns " + lantern + ", banners " + banner);
            ExpectM(bamboo > 0 && lantern == 4 && banner == 3, "corner decor should be placed by the rules layer (bamboo, 4 lanterns, 3 banners)");

            // 找一塊 3×5 的平地做實驗
            int px = -1, py = -1, pz = -1;
            for (int y = 0; y < w.D && px < 0; y++)
                for (int x = 0; x < w.W; x++)
                {
                    int z;
                    if (FlatPatch(b, x, y, 3, 5, out z)) { px = x; py = y; pz = z; break; }
                }
            if (px < 0) { problems.Add("move scenario: no flat 3x5 patch to test on"); return; }
            int A = w.Idx(px, py + 2, pz), M = w.Idx(px + 1, py + 2, pz), B = w.Idx(px + 2, py + 2, pz), diag = w.Idx(px + 1, py + 3, pz);

            // 斜走：一步到位
            Teleport(b, sword, A);
            sword.moveLeft = 1;
            b.ComputeReach(sword);
            int diagCost = b.pf.CostOf(diag);
            // 角落立了竹叢：斜向過不去
            w.SetCorner(px + 1, py + 3, Decor.Bamboo);
            b.ComputeReach(sword);
            int diagBlocked = b.pf.CostOf(diag);
            w.SetCorner(px + 1, py + 3, Decor.None);
            Note("Move scenario diagonal on flat ground at (" + px + "," + (py + 2) + "): cost " + diagCost + ", with bamboo on the corner " + diagBlocked + " (-1 = cannot)");
            ExpectM(diagCost == 1, "a diagonal step on flat ground should cost 1");
            ExpectM(diagBlocked == -1, "a diagonal step across a corner with bamboo should be impossible");

            // 縱躍：把正前方一排三格都堵住，輕功 2 的人直接躍過，輕功 1 的人只能繞路
            w.blocked[M] = true; w.blocked[w.Idx(px + 1, py + 1, pz)] = true; w.blocked[w.Idx(px + 1, py + 3, pz)] = true;
            sword.moveLeft = 9;
            b.ComputeReach(sword);
            int leapCost = b.pf.CostOf(B);
            var route = leapCost > 0 ? b.pf.Path(B) : new int[0];
            bool leapt = route.Length == 2 && !b.pf.Adjacent(route[0], route[1]);
            bool moved = leapCost > 0 && b.Move(sword, B);
            bool landed = b.CellOf(sword) == B && w.occ[B] == sword.id && w.occ[A] < 0 && sword.moveLeft == 9 - leapCost;
            b.Drain();
            Teleport(b, brawler, A);
            brawler.moveLeft = 9;
            b.ComputeReach(brawler);
            int walkCost = b.pf.CostOf(B);
            w.blocked[M] = false; w.blocked[w.Idx(px + 1, py + 1, pz)] = false; w.blocked[w.Idx(px + 1, py + 3, pz)] = false;
            Note("Move scenario leap over a blocked row: light-footed hero cost " + leapCost + " in one jump " + leapt + ", moved and landed " + (moved && landed)
                + "; heavy hero has to walk round, cost " + walkCost + "; leap ranges by jump 1/2/5 = " + Pathfinder.LeapRange(1) + "/" + Pathfinder.LeapRange(2) + "/" + Pathfinder.LeapRange(5));
            ExpectM(leapCost == 3 && leapt, "a hero with jump 2 should leap over one blocked cell for 3 points");
            ExpectM(moved && landed, "a leap should move the unit and keep the board consistent");
            ExpectM(walkCost > leapCost || walkCost == -1, "a hero with jump 1 cannot leap and must take the long way");

            // 明確的跳躍：前面明明走得過去，也照樣往那個方向躍到最遠
            {
                Teleport(b, brawler, w.Idx(px, py, pz));
                Teleport(b, sword, A);
                sword.moveLeft = 9;
                int jc;
                int land = b.LeapCell(sword, 1, 0, out jc);
                bool jumped = b.Leap(sword, 1, 0) && b.CellOf(sword) == B && sword.moveLeft == 9 - jc && w.occ[A] < 0;
                sword.moveLeft = 2;
                bool tooTired = !b.Leap(sword, -1, 0);                // 跳一次要 3 點
                brawler.moveLeft = 9;
                bool heavyCant = !b.Leap(brawler, 0, 1);              // 輕功 1 的人跳不起來
                sword.moveLeft = 9;
                bool noDir = !b.Leap(sword, 0, 0);
                b.Drain();
                Note("Move scenario jump key: lands 2 cells away " + (land == B) + " for " + jc + " points, moved " + jumped + ", refused without enough movement " + tooTired
                    + ", refused for a hero with jump 1 " + heavyCant + ", no direction refused " + noDir);
                ExpectM(land == B && jc == 3 && jumped, "an explicit leap should carry a light-footed hero 2 cells for 3 points even over open ground");
                ExpectM(tooTired && heavyCant && noDir, "a leap should be refused without movement, without the skill, or without a direction");
            }

            // 滾石的體積：擋視線，而且點得到本體
            // 先把兩人挪到這塊地的角落，中間那一排淨空
            Teleport(b, brawler, w.Idx(px, py, pz));
            Teleport(b, sword, w.Idx(px, py + 4, pz));
            bool losBefore = VoxelRay.Los(w, px, py + 2, pz, px + 2, py + 2, pz);
            var rock = b.AddProp(PropKind.Boulder, M, "山岩", 10);
            bool losAfter = VoxelRay.Los(w, px, py + 2, pz, px + 2, py + 2, pz);
            int coverSide = b.CoverBonus(Db.Bow, px, py + 1, pz, px + 2, py + 2, pz);
            // 從 A 的頭頂高度水平射向滾石的上半部
            int picked = VoxelRay.PickCell(w, px + 0.5f, py + 2.5f, pz + 1.5f, 1, 0, 0);
            Note("Move scenario boulder volume: line of sight across it " + losBefore + " → " + losAfter + ", cover from an angle +" + coverSide
                + ", clicking its upper half picks its cell " + (picked == M));
            ExpectM(losBefore && !losAfter, "a boulder should block line of sight");
            ExpectM(picked == M, "clicking the boulder's body should select its cell");

            // 路人的體積：點身體也選得到
            Prop npc = FirstProp(b, PropKind.Npc);
            if (npc != null)
            {
                int nx = w.X(npc.cell), ny = w.Y(npc.cell), nz = w.Z(npc.cell);
                // 從正上方往下點他的頭頂：沒有體積的話會點到…其實也是同一格，所以改從側面、他胸口的高度水平射過去
                bool clear = nx >= 1 && w.Get(nx - 1, ny, nz + 1) == Block.Air && w.occ[w.Idx(nx - 1, ny, nz)] < 0 && w.propAt[w.Idx(nx - 1, ny, nz + 1)] < 0;
                int hit = clear ? VoxelRay.PickCell(w, nx - 0.5f, ny + 0.5f, nz + 1.5f, 1, 0, 0) : -2;
                Note("Move scenario NPC volume: clicking his upper body picks his cell " + (hit == npc.cell) + (clear ? "" : " (skipped: no clear line from the west)"));
                if (clear) ExpectM(hit == npc.cell, "clicking the NPC's body should select his cell");
            }
        }

        // ───────── 裝備與道具 ─────────

        static void ExpectG(bool cond, string what) { if (!cond) problems.Add("gear scenario: " + what); }

        // 反覆出手直到打中一次（目標氣血先灌滿，才不會被打倒）。回傳主目標這一下掉了多少血
        static int HitOnce(Battle b, Unit u, Unit t)
        {
            for (int i = 0; i < 80; i++)
            {
                t.hp = t.maxHp = 200;
                u.actionLeft = true;
                b.UseSkill(u, u.skills[0], t, -1);
                if (t.hp < 200) return 200 - t.hp;
            }
            return 0;
        }

        // 連打 n 次，回傳會心的比例
        static float CritRate(Battle b, Unit u, Unit t, int n)
        {
            int crits = 0, swings = 0;
            b.Drain();
            for (int i = 0; i < n; i++)
            {
                t.hp = t.maxHp = 500;
                u.actionLeft = true;
                b.UseSkill(u, u.skills[0], t, -1);
                foreach (var e in b.Drain()) if (e.type == EvType.Attack) { swings++; if (e.flag) crits++; }
            }
            return swings > 0 ? crits / (float)swings : 0;
        }

        static void GearScenarios()
        {
            var log = new StringBuilder();

            // ── 初始配裝的數值應與改版前相同 ──
            var b = Scenario.Create();
            var w = b.world;
            Unit sword = b.units[0], brawler = b.units[1], knife = b.units[2], bandit = b.units[3], archer = b.units[6], boss = b.units[8];
            string stats = "";
            foreach (var u in new[] { sword, brawler, knife, bandit, archer, boss })
                stats += " " + u.name + " AC" + u.Ac + " mv" + u.move + " " + u.skills[0].name + "(" + u.skills[0].dmg + (u.skills[0].melee ? " r" + u.skills[0].reach : " rng" + u.skills[0].range) + " crit" + u.skills[0].critMin + ")";
            Note("Gear scenario baseline:" + stats);
            ExpectG(sword.Ac == 15 && brawler.Ac == 14 && knife.Ac == 15 && bandit.Ac == 12 && archer.Ac == 12 && boss.Ac == 15, "starting gear should give the same defence as before (15/14/15/12/12/15)");
            ExpectG(sword.move == 6 && brawler.move == 5 && knife.move == 6 && boss.move == 5, "starting gear should give the same movement as before (plate costs 1)");
            ExpectG(brawler.Clanky && !sword.Clanky && !knife.Clanky, "only the plate-wearer should have stealth disadvantage");
            ExpectG(sword.skills[0].critMin == 19 && sword.skills[0].reach == 1 && bandit.skills[0].sweep == Sweep.Arc && archer.skills[0].range == 7 && !archer.skills[0].melee
                && knife.skills[0].range == 6 && boss.skills[0].dmg.sides == 10, "basic attacks should come from the weapon class (sword crit 19+, blade sweeps, short bow range 7)");
            ExpectG(sword.skills.Length == 6 && sword.skills[1] == Db.Flurry, "skills should be the weapon strike followed by the hero's own arts");

            // ── 戰前換裝 ──
            bool spearOn = b.Equip(sword, Slot.MainHand, Gears.Spear);
            var sp = sword.skills[0];
            bool spearOk = spearOn && sp.reach == 2 && sp.sweep == Sweep.Line && b.stash.Contains(Gears.QingFeng) && !b.stash.Contains(Gears.Spear) && sword.look.weapon == WeaponKind.Spear;
            bool shieldRefused = !b.Equip(sword, Slot.OffHand, Gears.RattanShield);
            bool mismatch = b.Mismatch(sword, Db.Flurry) && b.AtkBonus(sword, Db.Flurry) == sword.Mod(Stat.Dex) + Db.Prof - 2 && b.CanUse(sword, Db.Flurry) == b.CanUse(sword, Db.Heal);
            b.Equip(sword, Slot.MainHand, Gears.SerpentSpear);
            int longReach = sword.skills[0].reach;
            b.Equip(sword, Slot.MainHand, Gears.NineWhip);
            int whipReach = sword.skills[0].reach;
            b.Equip(sword, Slot.MainHand, Gears.Saber);
            bool shieldOn = b.Equip(sword, Slot.OffHand, Gears.RattanShield);
            int acShield = sword.Ac;
            b.Equip(sword, Slot.Upper, Gears.Chain);
            int acChain = sword.Ac; bool chainNoisy = sword.Clanky;
            b.Equip(sword, Slot.Feet, Gears.SwiftBoots);
            b.Equip(sword, Slot.Waist, Gears.Satchel);
            b.Equip(sword, Slot.Charm1, Gears.Mirror);
            b.Equip(sword, Slot.Charm2, Gears.Tally);
            Note("Gear scenario equip: spear gives reach 2 + line + look " + spearOk + ", shield refused with a two-hander " + shieldRefused + ", sword art with a spear is −2 but usable " + mismatch
                + ", serpent spear reach " + longReach + ", whip reach " + whipReach + ", shield on with a one-hander " + shieldOn + " AC " + acShield + ", chain mail AC " + acChain + " noisy " + chainNoisy
                + ", boots move " + sword.move + ", satchel bag " + sword.BagSize + ", two charms AC " + sword.Ac + " intimidate +" + sword.GearSkill(Talent.Intimidate));
            ExpectG(spearOk, "equipping a spear should change the basic attack, return the sword to the stash and change the model");
            ExpectG(shieldRefused, "a shield cannot be held with a two-handed weapon");
            ExpectG(mismatch, "a lenient art used with the wrong weapon should still be usable at −2");
            ExpectG(longReach == 3 && whipReach == 3, "special weapons should modify reach (serpent spear 3) and whips reach 3");
            ExpectG(shieldOn && acShield == 16, "a shield in the off hand should add its defence");
            ExpectG(acChain == 16 && chainNoisy, "chain mail: +2 defence, dexterity capped at +2, noisy (10 + 2 + 2 + 1 greaves + 1 shield)");
            ExpectG(sword.move == 7 && sword.BagSize == 8 && sword.Ac == 17 && sword.GearSkill(Talent.Intimidate) == 2, "boots, satchel and charms should apply their bonuses");

            bool strictBlocked = b.Equip(knife, Slot.MainHand, Gears.LongSword) && !b.CanUse(knife, Db.Rain) && b.Equip(knife, Slot.MainHand, Gears.WillowKnives);
            bool barehand = b.Equip(brawler, Slot.MainHand, null) && brawler.skills[0].name == "空手" && brawler.skills[0].dmg.sides == 4 && !b.Mismatch(brawler, Db.Palm);
            Note("Gear scenario arts: strict art (rain of needles) unusable without thrown weapons " + strictBlocked + ", bare hands count as fists for palm arts " + barehand);
            ExpectG(strictBlocked, "a strict art should be unusable with the wrong weapon and usable again with the right one");
            ExpectG(barehand, "unequipping the weapon should leave a 1d4 unarmed strike that still counts as fists");
            Dump(b, log);

            // ── 開打之後：只能換兵器，而且算一次隨手互動 ──
            b = Scenario.Create(); w = b.world; b.Start();
            sword = b.units[0]; brawler = b.units[1]; knife = b.units[2];
            {
                bool armourLocked = b.WhyNotEquip(sword, Slot.Upper, Gears.Chain) != null && !b.Equip(sword, Slot.Upper, Gears.Chain);
                sword.freeLeft = true;
                bool swapped = b.Equip(sword, Slot.MainHand, Gears.Spear) && !sword.freeLeft;
                bool onceOnly = !b.Equip(sword, Slot.MainHand, Gears.Saber);
                Note("Gear scenario in battle: armour cannot be changed " + armourLocked + ", weapon swap uses the free interaction " + swapped + ", only once a turn " + onceOnly);
                ExpectG(armourLocked && swapped && onceOnly, "in battle only weapons can be swapped, once per turn as the free interaction");
            }

            // ── 觸及、波及 ──
            int px = -1, py = -1, pz = -1;
            for (int y = 0; y < w.D && px < 0; y++)
                for (int x = 0; x < w.W; x++)
                {
                    int z;
                    if (FlatPatch(b, x, y, 3, 5, out z)) { px = x; py = y; pz = z; break; }
                }
            if (px < 0) problems.Add("gear scenario: no flat patch to test reach on");
            else
            {
                Unit f1 = b.units[3], f2 = b.units[4];
                Teleport(b, sword, w.Idx(px + 1, py, pz));
                Teleport(b, f1, w.Idx(px + 1, py + 1, pz));
                Teleport(b, f2, w.Idx(px + 1, py + 2, pz));
                sword.stealth = -99; b.Observe();
                var spear = sword.skills[0];
                bool reach2 = b.CanHit(spear, sword.x, sword.y, sword.z, f2.x, f2.y, f2.z);
                bool swordCant = !b.CanHit(Db.Sword, sword.x, sword.y, sword.z, f2.x, f2.y, f2.z);
                f2.hp = f2.maxHp = 200;
                int main = HitOnce(b, sword, f1);
                int lineSplash = 200 - f2.hp;

                // 換成單刀：兩個敵人並排站在面前
                sword.freeLeft = true;
                b.Equip(sword, Slot.MainHand, Gears.Saber);
                Teleport(b, f2, w.Idx(px, py + 1, pz));
                b.Observe();
                f2.hp = f2.maxHp = 200;
                int main2 = HitOnce(b, sword, f1);
                int arcSplash = 200 - f2.hp;
                // 站遠一點的人不會被橫掃波及
                Teleport(b, f2, w.Idx(px, py + 3, pz));
                f2.hp = f2.maxHp = 200;
                HitOnce(b, sword, f1);
                int farSplash = 200 - f2.hp;
                Dump(b, log);
                Note("Gear scenario reach: spear hits at 2 cells " + reach2 + ", sword cannot " + swordCant + "; spear hit for " + main + " and pierced the one behind for " + lineSplash
                    + "; sabre hit for " + main2 + " and swept the one beside for " + arcSplash + "; someone 2 cells away took " + farSplash);
                ExpectG(reach2 && swordCant, "a polearm should reach 2 cells and a sword only 1");
                ExpectG(main > 0 && lineSplash == Math.Max(1, main / 2), "a polearm should pierce the enemy behind for half damage");
                ExpectG(main2 > 0 && arcSplash == Math.Max(1, main2 / 2) && farSplash == 0, "a blade should sweep an adjacent enemy for half damage, and nobody farther away");

                // ── 會心率：短兵 18 以上、鈍器只有 20 ──
                Teleport(b, f2, w.Idx(px + 2, py + 4, pz));
                sword.freeLeft = true; b.Equip(sword, Slot.MainHand, Gears.DaggerG);
                HitOnce(b, sword, f1); // 先打一下，讓對方轉身面對，之後就沒有背襲與出其不意的優勢
                float dagger = CritRate(b, sword, f1, 500);
                sword.freeLeft = true; b.Equip(sword, Slot.MainHand, Gears.Mace);
                float mace = CritRate(b, sword, f1, 500);
                Note("Gear scenario crit rate over 500 swings: dagger " + (dagger * 100).ToString("0.0") + "% (expect about 15%), mace " + (mace * 100).ToString("0.0") + "% (expect about 5%)");
                ExpectG(dagger > 0.09f && dagger < 0.22f, "a dagger should crit on 18+ (about 15%)");
                ExpectG(mace > 0.015f && mace < 0.095f, "a mace should crit only on 20 (about 5%)");
                ExpectG(Gears.Info(WeaponClass.Blunt).critDice == 3 && sword.skills[0].critDice == 3, "blunt weapons should triple their damage dice on a crit");
            }

            // ── 道具 ──
            b = Scenario.Create(); w = b.world;
            sword = b.units[0]; brawler = b.units[1]; knife = b.units[2];
            bool beforeBattle = b.WhyNotUse(sword, Item.Salve, null) != null;
            b.Start();
            {
                int have = b.BagCount(sword, Item.Salve);
                bool fullRefused = b.WhyNotUse(sword, Item.Salve, null) != null;
                sword.hp = 10; sword.actionLeft = true; sword.bonusLeft = true;
                bool used = b.UseItem(sword, Item.Salve, null);
                bool spent = used && sword.hp > 10 && !sword.actionLeft && sword.bonusLeft && b.BagCount(sword, Item.Salve) == have - 1;
                bool noneLeft = !b.UseItem(sword, Item.Salve, null);
                sword.qi = 2;
                bool pill = b.UseItem(sword, Item.QiPill, null) && sword.qi > 2 && !sword.bonusLeft;
                // 唐小蝶巧手：用藥只花附加動作，還能餵給身邊的隊友
                int side = Orth(b, b.CellOf(brawler));
                bool fed = false, quick = false;
                if (side >= 0)
                {
                    Teleport(b, knife, side);
                    brawler.hp = 10; knife.actionLeft = true; knife.bonusLeft = true;
                    fed = b.UseItem(knife, Item.Salve, brawler) && brawler.hp > 10;
                    quick = knife.actionLeft && !knife.bonusLeft;
                }
                Dump(b, log);
                Note("Gear scenario items: unusable before battle " + beforeBattle + ", refused at full health " + fullRefused + ", salve heals and costs the action " + spent + ", gone after use " + noneLeft
                    + ", qi pill costs the bonus action " + pill + ", fed to an adjacent ally " + fed + ", nimble hands use only the bonus action " + quick);
                ExpectG(beforeBattle && fullRefused && spent && noneLeft, "a salve should heal, cost an action and be consumed");
                ExpectG(pill, "a qi pill should restore qi for a bonus action");
                ExpectG(fed && quick, "a hero with sleight of hand should use items as a bonus action, also on an adjacent ally");
            }
            File.WriteAllText(Path.Combine(outDir, "gear-scenarios.txt"), log.ToString(), Encoding.UTF8);
        }

        // ───────── 存檔與任務 ─────────

        static void ExpectS(bool cond, string what) { if (!cond) problems.Add("save scenario: " + what); }

        static bool Same<T>(T[] a, T[] b) where T : IEquatable<T>
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) return false;
            return true;
        }

        static string QuestText(Battle b)
        {
            var sb = new StringBuilder();
            foreach (var q in b.Quests()) { sb.Append(q.title).Append(q.Done ? "[done]" : q.Failed ? "[failed]" : ""); foreach (var l in q.lines) sb.Append('|').Append(l.text).Append(l.done ? "✓" : l.failed ? "✗" : "○"); sb.Append('\n'); }
            return sb.ToString();
        }

        static void SaveScenarios()
        {
            // 做一堆會留下痕跡的事，存檔，讀回另一場新的戰局，逐項比對
            var b = Scenario.Create();
            var w = b.world;
            b.explore = true;
            b.dropChance = 1;
            b.Start();
            Unit h0 = b.units[0], h1 = b.units[1], h2 = b.units[2];
            b.Equip(h0, Slot.MainHand, Gears.Spear);
            b.Equip(h0, Slot.Feet, Gears.SwiftBoots);
            b.Equip(h0, Slot.Charm1, Gears.Mirror);

            Prop chest = FirstProp(b, PropKind.Chest), npc = FirstProp(b, PropKind.Npc), rock = FirstProp(b, PropKind.Boulder);
            int side = Orth(b, chest.cell);
            if (side >= 0) { Teleport(b, h2, side); for (int i = 0; i < 40 && !chest.open; i++) { h2.freeLeft = true; b.Interact(h2, chest); } }
            side = Orth(b, npc.cell);
            if (side >= 0) { Teleport(b, h0, side); for (int i = 0; i < 60 && !npc.guided; i++) { npc.tried = 0; b.Talk(h0, npc, TalkWay.Persuade); } }
            int near = Orth(b, b.CellOf(h2));
            if (near >= 0) b.UseItem(h2, Item.SmokeBomb, null, near);
            near = Orth(b, b.CellOf(h0));
            if (near >= 0) b.UseItem(h0, Item.Caltrops, null, near);
            Call(b, "Damage", b.units[3], 999, false);   // 倒下一個山賊：掉落物
            // 推一次滾石（推不動也無妨）
            int rx = w.X(rock.cell), ry = w.Y(rock.cell);
            int north = w.Idx(rx, ry - 1, w.TopStand(rx, ry - 1));
            if (w.occ[north] < 0) { Teleport(b, h1, north); for (int i = 0; i < 20 && w.X(rock.cell) == rx && w.Y(rock.cell) == ry && !rock.gone; i++) { h1.actionLeft = true; b.Interact(h1, rock); } }
            h1.poison = 2; h1.hp = 20; h1.stun = false;
            if (b.exploring) Call(b, "BeginCombat"); // 存在回合制進行到一半的時候
            b.UpdateVision(); // 測試裡用瞬移擺人，沒有經過正常移動，這裡補算一次視野
            b.Drain();

            string json = JsonUtility.ToJson(b.Save());
            var d = JsonUtility.FromJson<SaveData>(json);
            var c = Scenario.Create();
            bool ok = c.Restore(d);

            bool unitsSame = ok;
            for (int i = 0; ok && i < b.units.Count; i++)
            {
                Unit x = b.units[i], y = c.units[i];
                bool same = x.x == y.x && x.y == y.y && x.z == y.z && x.hp == y.hp && x.qi == y.qi && x.alive == y.alive && x.fled == y.fled && x.poison == y.poison
                    && x.fx == y.fx && x.fy == y.fy && x.knownCell == y.knownCell && x.Ac == y.Ac && x.move == y.move && x.skills[0].name == y.skills[0].name
                    && x.look.weapon == y.look.weapon && x.look.outfit == y.look.outfit && x.bag.Count == y.bag.Count && x.moveLeft == y.moveLeft && x.actionLeft == y.actionLeft;
                for (int k = 0; same && k < x.gear.Length; k++) same = x.gear[k] == y.gear[k];
                for (int k = 0; same && k < x.bag.Count; k++) same = x.bag[k].item == y.bag[k].item && x.bag[k].n == y.bag[k].n;
                if (!same) { unitsSame = false; problems.Add("save scenario: unit " + x.name + " differs after loading"); }
            }
            bool propsSame = ok && b.props.Count == c.props.Count;
            for (int i = 0; propsSame && i < b.props.Count; i++)
            {
                Prop x = b.props[i], y = c.props[i];
                if (x.kind != y.kind || x.cell != y.cell || x.gone != y.gone || x.open != y.open || x.found != y.found || x.item != y.item || x.attitude != y.attitude || x.guided != y.guided)
                { propsSame = false; problems.Add("save scenario: prop " + x.name + " differs after loading"); }
            }
            string diff = "";
            if (ok)
            {
                if (!Same(b.world.occ, c.world.occ)) diff += " occ";
                if (!Same(b.world.blocked, c.world.blocked)) diff += " blocked";
                if (!Same(b.world.wall, c.world.wall)) diff += " wall";
                if (!Same(b.world.smoke, c.world.smoke)) diff += " smoke";
                if (!Same(b.world.propAt, c.world.propAt)) diff += " propAt";
                if (!Same(b.vision.seen, c.vision.seen)) diff += " seen";
                if (diff.Length > 0) Note("Save scenario: arrays that differ:" + diff);
            }
            bool worldSame = ok && diff.Length == 0;
            bool turnSame = ok && b.round == c.round && b.exploring == c.exploring && b.over == c.over && b.active.id == c.active.id && b.order.Count == c.order.Count
                && b.SmokeCount == c.SmokeCount && b.stash.Count == c.stash.Count && b.alerts == c.alerts;
            for (int i = 0; turnSame && i < b.order.Count; i++) turnSame = b.order[i].id == c.order[i].id;
            for (int i = 0; turnSame && i < b.stash.Count; i++) turnSame = b.stash[i] == c.stash[i];
            bool questsSame = ok && QuestText(b) == QuestText(c);

            // 讀回來的戰局要能繼續打
            bool playable = false;
            if (ok)
            {
                c.autoHeroes = true; c.maxRounds = 80;
                c.EndTurn();
                c.Drain();
                playable = c.over != 0;
            }
            // 版本不對的存檔要被拒絕，而且不能把戰局弄壞
            d.version = 99;
            var fresh = Scenario.Create();
            string before = HeroState(fresh);
            bool rejects = !fresh.Restore(d) && !fresh.Restore(null) && HeroState(fresh) == before;

            int dynamic = 0;
            foreach (var p in b.props) if (p.kind == PropKind.Caltrops || p.kind == PropKind.Loot) dynamic++;
            Note("Save scenario: " + json.Length + " chars, restored " + ok + ", units " + unitsSame + ", props " + propsSame + " (" + b.props.Count + " incl. " + dynamic + " created in play, chest open " + chest.open
                + ", woodcutter guided " + npc.guided + ", clouds " + b.SmokeCount + "), world arrays " + worldSame + ", turn state " + turnSame + ", quests " + questsSame
                + ", battle plays on to an end " + playable + ", bad save rejected " + rejects);
            ExpectS(ok && unitsSame && propsSame && worldSame && turnSame && questsSame, "a loaded battle should match the saved one exactly");
            ExpectS(dynamic >= 2 && chest.open && npc.guided && b.SmokeCount == 1, "the scenario should have exercised loot, caltrops, the chest, the woodcutter and smoke before saving");
            ExpectS(playable, "a loaded battle should play on to its end");
            ExpectS(rejects, "a save of another version should be rejected without touching the battle");

            // ── 任務 ──
            b = Scenario.Create(); w = b.world; b.explore = true; b.Start();
            var q0 = b.Quests();
            bool fresh0 = q0.Count == 5;
            foreach (var q in q0) if (q.Done || q.Failed) fresh0 = false;
            chest = FirstProp(b, PropKind.Chest);
            side = Orth(b, chest.cell);
            if (side >= 0) { Teleport(b, b.units[2], side); for (int i = 0; i < 40 && !chest.open; i++) { b.units[2].freeLeft = true; b.Interact(b.units[2], chest); } }
            bool chestQuest = b.Quests()[2].Done;
            foreach (var u in b.units) if (u.team == 1 && u.alive) Call(b, "Damage", u, 999, false);
            var q1 = b.Quests();
            bool mainDone = q1[0].Done && b.over == 1 && q1[4].Done;
            bool ambushFailedOrOpen = !q1[3].Done;
            Note("Quest scenario: 5 quests all open at the start " + fresh0 + ", chest quest completes " + chestQuest + ", main quest and no-casualty challenge complete on victory " + mainDone
                + ", ambush challenge not credited without an ambush " + ambushFailedOrOpen);
            ExpectS(fresh0 && chestQuest && mainDone && ambushFailedOrOpen, "quests should track the state of the battle");
        }

        // ───────── 自由行動 ─────────

        static void ExpectX(bool cond, string what) { if (!cond) problems.Add("explore scenario: " + what); }

        static void ExploreScenarios()
        {
            var log = new StringBuilder();
            var b = Scenario.Create();
            var w = b.world;
            b.explore = true;
            b.Start();
            Dump(b, log);
            var hero = b.active;
            bool begins = b.exploring && b.round == 0 && hero != null && hero.team == 0 && hero.moveLeft >= Battle.ExploreMove && b.order.Count == 0;

            // 連走好幾段：每段都走得動，行動力會補滿，敵人在這期間有張望
            int legs = 0, walked = 0;
            bool scanned = false;
            for (int k = 0; k < 6 && b.exploring; k++)
            {
                b.ComputeReach(hero);
                int dest = -1, bestCost = 0;
                foreach (int c in b.pf.reach)
                    if (w.X(c) < 8 && w.Y(c) > 13 && b.pf.CostOf(c) > bestCost && b.PropAt(c) == null) { bestCost = b.pf.CostOf(c); dest = c; }
                if (dest < 0 || !b.Move(hero, dest)) break;
                legs++; walked += bestCost;
                foreach (var e in b.units) if (e.team == 1 && (e.fx != e.homeFx || e.fy != e.homeFy)) scanned = true;
            }
            bool free = legs == 6 && b.exploring && hero.moveLeft >= Battle.ExploreMove && hero.actionLeft && walked > 30;

            // 用道具、換人、等候都不會結束自由行動
            hero.hp -= 5;
            bool item = b.UseItem(hero, Item.Salve, null) && hero.actionLeft && b.exploring;
            Unit other = b.units[hero.id == 1 ? 2 : 1];
            bool switched = b.Select(other) && b.active == other && !b.Select(b.units[3]);
            b.EndTurn();
            bool waited = b.exploring && b.round == 0;
            Dump(b, log);
            Note("Explore scenario free roam: starts exploring " + begins + ", walked " + legs + " legs (" + walked + " points) without turns " + free + ", guards kept scanning " + scanned
                + ", item usable " + item + ", switch hero " + switched + ", waiting passes time without combat " + waited);
            ExpectX(begins, "with explore set the battle should start in free roam with no initiative order");
            ExpectX(free && scanned, "free roam should allow unlimited movement while the guards keep scanning");
            ExpectX(item && switched && waited, "items, switching heroes and waiting should all work in free roam");

            // 被發現：擲先攻進入回合制
            Unit bandit = b.units[3];
            hero = b.active;
            int seen = FindCell(b, (x, y, z) => Cheb(bandit, x, y) >= 2 && Cheb(bandit, x, y) <= Battle.PlainSight
                && Vision.InFront(bandit.x, bandit.y, bandit.fx, bandit.fy, x, y) && b.vision.CanSeeCell(bandit.x, bandit.y, bandit.z, bandit.fx, bandit.fy, x, y, z));
            if (seen < 0) problems.Add("explore scenario: no cell in the bandit's sight");
            else
            {
                Teleport(b, hero, seen);
                b.Face(hero, bandit.x, bandit.y); // 任何一個動作之後都會重新判定誰看見誰
                var sb2 = new StringBuilder(); Dump(b, sb2); log.Append(sb2);
                bool fight = !b.exploring && b.round >= 1 && b.order.Count == b.units.Count && sb2.ToString().Contains("遭遇戰") && sb2.ToString().Contains("先攻");
                Note("Explore scenario spotted: combat begins with an initiative roll " + fight + " (round " + b.round + ", alerts " + b.alerts + ")");
                ExpectX(fight, "being spotted should end free roam and roll initiative");
            }

            // 先動手：突襲，對方措手不及
            b = Scenario.Create(); w = b.world; b.explore = true; b.Start();
            {
                Unit h = b.units[0], boss = b.units[8];
                int behind = FindCell(b, (x, y, z) =>
                {
                    if (Cheb(boss, x, y) != 1 || Math.Abs(z - boss.z) > 1) return false;
                    foreach (var f in b.units)
                        if (f.team == 1 && Cheb(f, x, y) <= Battle.PlainSight && Vision.InFront(f.x, f.y, f.fx, f.fy, x, y) && b.vision.CanSeeCell(f.x, f.y, f.z, f.fx, f.fy, x, y, z)) return false;
                    return true;
                });
                if (behind < 0) problems.Add("explore scenario: no cell behind the boss");
                else
                {
                    h.stealth = 30;
                    Teleport(b, h, behind);
                    bool hit = b.UseSkill(h, h.skills[0], boss, -1);
                    int spins = 0;
                    while (b.over == 0 && b.round < 2 && spins++ < 30) b.EndTurn(); // 等寨主的第一個回合過去
                    var sb3 = new StringBuilder(); Dump(b, sb3); log.Append(sb3);
                    string t3 = sb3.ToString();
                    bool ambush = hit && !b.exploring && boss.ambushed && t3.Contains("出其不意") && t3.Contains("遭遇戰") && t3.Contains(boss.name + " 措手不及，這一回合來不及反應");
                    Note("Explore scenario ambush: striking first starts combat with the target surprised " + ambush);
                    ExpectX(ambush, "attacking from free roam should start combat with a surprise round");
                }
            }

            // 跑步比較容易被發現
            b = Scenario.Create(); w = b.world; b.explore = true; b.Start();
            {
                Unit h = b.units[0], guard = b.units[3];
                int far = FindCell(b, (x, y, z) =>
                {
                    if (!b.vision.CanSeeCell(guard.x, guard.y, guard.z, guard.fx, guard.fy, x, y, z)) return false;
                    foreach (var f in b.units) if (f.team == 1 && Cheb(f, x, y) < 5) return false;
                    return true;
                });
                if (far < 0) problems.Add("explore scenario: no distant cell in the guard's view");
                else
                {
                    Teleport(b, h, far);
                    h.stealth = b.Perception(guard) + 3; h.ran = false; h.stealthTold = false;
                    b.Observe();
                    bool walkHidden = h.knownCell < 0;
                    h.ran = true;
                    b.Observe();
                    bool runSeen = h.knownCell >= 0;
                    Dump(b, log);
                    Note("Explore scenario running: a marginal stealth roll hides a walker " + walkHidden + ", but not a runner " + runSeen);
                    ExpectX(walkHidden && runSeen, "running should cost 5 stealth");
                }
            }
            File.WriteAllText(Path.Combine(outDir, "explore-scenarios.txt"), log.ToString(), Encoding.UTF8);
        }

        // ───────── 效能基準 ─────────

        static readonly StringBuilder benchOut = new StringBuilder();

        // 跑 n 次，記下每次平均花多少毫秒、配置多少記憶體
        static double Measure(string name, int n, Action body)
        {
            body(); // 暖身：第一次常包含 JIT 與快取建立
            GC.Collect();
            long mem0 = GC.GetTotalMemory(false);
            int gc0 = GC.CollectionCount(0);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < n; i++) body();
            sw.Stop();
            long mem1 = GC.GetTotalMemory(false);
            double ms = sw.Elapsed.TotalMilliseconds / n;
            string alloc = GC.CollectionCount(0) != gc0 ? "(gc ran)" : ((mem1 - mem0) / n) + " B";
            string line = "Bench " + name.PadRight(34) + ms.ToString("0.0000").PadLeft(10) + " ms   alloc " + alloc;
            benchOut.AppendLine(line);
            Note(line);
            return ms;
        }

        /// <summary>量測規則層與畫面層的熱點。數字寫進 result.txt 與 bench.txt，方便改動前後比較。</summary>
        static void Bench(object gc, Battle battle)
        {
            benchOut.Length = 0;
            var w = battle.world;
            var u = battle.active;
            var views = (List<UnitView>)Get(gc, "views");

            // 畫面上有多少個要畫的物件
            int total = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length, onUnits = 0;
            foreach (var v in views) onUnits += v.root.GetComponentsInChildren<MeshRenderer>(true).Length;
            string who = "Bench active unit: " + u.name + " (jump " + u.Jump + ", move left " + u.moveLeft + ", reach " + battle.pf.reach.Count + " cells)";
            benchOut.AppendLine(who); Note(who);
            string head = "Bench renderers: " + total + " in the scene, " + onUnits + " on the " + views.Count + " characters (" + (onUnits / (float)views.Count).ToString("0.0") + " each)";
            benchOut.AppendLine(head); Note(head);

            // 畫一幀要多久（含剔除與送出繪製指令；陰影也算在內）
            var cam = Camera.main;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            Measure("render one frame 1600x900", 40, () => cam.Render());
            // 一幀的時間花在哪：關掉陰影、藏起角色各量一次
            var q0 = QualitySettings.shadows;
            QualitySettings.shadows = ShadowQuality.Disable;
            Measure("render, shadows off", 40, () => cam.Render());
            QualitySettings.shadows = q0;
            {
                // 三段畫質各要多久（量完還原）
                int q1 = (int)Get(gc, "quality");
                for (int level = 0; level <= 2; level++)
                {
                    Call(gc, "SetQuality", level);
                    for (int k = 0; k < 4; k++) cam.Render(); // 換設定後頭幾幀要重建陰影貼圖，不算
                    Measure("render, quality " + level, 40, () => cam.Render());
                }
                string picked = "Bench default quality on this machine: " + q1 + " (0 = no real-time shadows)";
                benchOut.AppendLine(picked); Note(picked);
                Call(gc, "SetQuality", q1);
            }
            foreach (var v in views) v.root.gameObject.SetActive(false);
            Measure("render, characters hidden", 40, () => cam.Render());
            foreach (var v in views) v.root.gameObject.SetActive(!v.dead);
            Call(gc, "UpdateFog");
            string qs = "Bench quality: shadow cascades " + QualitySettings.shadowCascades + ", distance " + QualitySettings.shadowDistance + ", resolution " + QualitySettings.shadowResolution
                + ", AA " + QualitySettings.antiAliasing + ", pixel lights " + QualitySettings.pixelLightCount + ", gpu " + SystemInfo.graphicsDeviceName;
            benchOut.AppendLine(qs); Note(qs);
            int mergedParts = 0;
            foreach (var v in views) mergedParts += v.MergedCount;
            string mg = "Bench merged character parts: " + mergedParts;
            benchOut.AppendLine(mg); Note(mg);
            cam.targetTexture = null;
            rt.Release();
            string stats = "Bench last frame: batches " + UnityEditor.UnityStats.batches + ", draw calls " + UnityEditor.UnityStats.drawCalls + ", set-pass " + UnityEditor.UnityStats.setPassCalls
                + ", shadow casters " + UnityEditor.UnityStats.shadowCasters + ", tris " + UnityEditor.UnityStats.triangles;
            benchOut.AppendLine(stats); Note(stats);

            // 畫面層：每走一步、每次游標移動會做的事
            Measure("view UpdateFog", 200, () => Call(gc, "UpdateFog"));
            int far = w.Idx(18, 14, w.TopStand(18, 14));
            Set(gc, "hover", far);
            Measure("view RedrawMarkers (far hover)", 100, () => Call(gc, "RedrawMarkers"));
            Measure("view UpdateTip (far hover)", 200, () => Call(gc, "UpdateTip"));
            int nearCell = battle.pf.reach.Count > 0 ? battle.pf.reach[battle.pf.reach.Count / 2] : far;
            Set(gc, "hover", nearCell);
            Measure("view RedrawMarkers (near hover)", 200, () => Call(gc, "RedrawMarkers"));
            Measure("view UpdateTip (near hover)", 200, () => Call(gc, "UpdateTip"));
            Measure("view Refresh", 100, () => Call(gc, "Refresh"));
            Set(gc, "hover", -1);
            Call(gc, "Refresh");
            ((Hud)Get(gc, "hud")).HideTip();

            // 建一個角色模型要多久（換裝、創角時每改一次就重建）
            var holder = new GameObject("BenchHolder").transform;
            Measure("build one character model", 20, () => { var v = new UnitView(battle.units[0], w, holder); v.Dispose(); });
            UnityEngine.Object.Destroy(holder.gameObject);

            // 規則層（固定用沈青嵐量，才不會因為誰先攻而每次不同）
            var h0 = battle.units[0];
            Measure("rules Flood (move 6, jump 2)", 2000, () => battle.pf.Flood(battle.CellOf(h0), 6, h0.Jump, h0.team, battle.units));
            Measure("rules Flood (budget 120)", 300, () => battle.pf.Flood(battle.CellOf(h0), 120, h0.Jump, h0.team, battle.units));
            battle.ComputeReach(u);
            Measure("rules Observe", 2000, () => battle.Observe());
            Measure("rules UpdateVision", 1000, () => battle.UpdateVision());
            var foe = battle.units[6];
            Measure("rules CoverBonus", 20000, () => battle.CoverBonus(Db.Bow, foe.x, foe.y, foe.z, u.x, u.y, u.z));
            battle.Drain();
            Measure("rules whole auto battle", 5, () =>
            {
                var b2 = Scenario.Create();
                b2.autoHeroes = true; b2.maxRounds = 80;
                b2.Start();
                b2.Drain();
            });
            File.WriteAllText(Path.Combine(outDir, "bench.txt"), benchOut.ToString(), Encoding.UTF8);
        }

        // ───────── 裝備與道具的模型 ─────────

        static void Wear(Battle b, Unit u, params Gear[] set)
        {
            foreach (var g in set) b.Equip(u, g.slot == Slot.Charm1 && u.gear[(int)Slot.Charm1] != null ? Slot.Charm2 : g.slot, g);
        }

        /// <summary>讓一位俠客換上幾套不同的裝備各拍一張，確認每件穿上身都有模型；拍完換回原樣。</summary>
        static void GearPortraits(object gc, Battle b, Unit hero)
        {
            var original = (Gear[])hero.gear.Clone();
            var sets = new[]
            {
                new[] { Gears.Halberd, Gears.Helmet, Gears.Plate, Gears.Tassets, Gears.IronBoots, Gears.Vambraces, Gears.Mirror },
                new[] { Gears.SoftSword, Gears.RattanShield, Gears.Hood, Gears.NightSuit, Gears.Trousers, Gears.SwiftBoots, Gears.Cloak, Gears.Satchel, Gears.Jade },
                new[] { Gears.NineWhip, Gears.Chain, Gears.Greaves, Gears.Wraps, Gears.Sash, Gears.CloudBoots, Gears.Tally, Gears.Quiver },
                new[] { Gears.MeteorHammer, Gears.IronShield, Gears.Leather, Gears.Greaves },
                new[] { Gears.WolfClub, Gears.Robe }, new[] { Gears.DarkIron }, new[] { Gears.SerpentSpear }, new[] { Gears.CosmosFan }, new[] { Gears.SleeveBolt }, new[] { Gears.JudgeBrush },
            };
            string[] names = { "E1-plate-halberd", "E2-night-softsword-shield", "E3-chain-whip", "E4-meteor-ironshield", "E5-wolfclub", "E6-darkiron", "E7-serpentspear", "E8-cosmosfan", "E9-sleevebolt", "E10-judgebrush" };
            // 測試要用到、但行李裡沒有的，先放進去，拍完再拿走
            var lent = new List<Gear>();
            foreach (var set in sets)
                foreach (var g in set)
                    if (!b.stash.Contains(g) && Array.IndexOf(hero.gear, g) < 0 && !lent.Contains(g)) { b.stash.Add(g); lent.Add(g); }
            for (int k = 0; k < sets.Length; k++)
            {
                // 先全部卸下，再穿上這一套
                for (int i = 0; i < hero.gear.Length; i++) if (hero.gear[i] != null) b.Equip(hero, (Slot)i, null);
                Wear(b, hero, sets[k]);
                foreach (var g in sets[k])
                    if (Array.IndexOf(hero.gear, g) < 0 && !(g.slot == Slot.OffHand && hero.Weapon != null && hero.Weapon.twoHanded)) problems.Add("gear portrait: could not equip " + g.name);
                Call(gc, "RebuildView", hero);
                int renderers = ViewOf(gc, hero).root.GetComponentsInChildren<MeshRenderer>().Length;
                Note("Gear portrait " + names[k] + ": " + renderers + " parts");
                Portrait(gc, hero, names[k], false);
            }
            for (int i = 0; i < hero.gear.Length; i++) if (hero.gear[i] != null) b.Equip(hero, (Slot)i, null);
            for (int i = 0; i < original.Length; i++) if (original[i] != null) b.Equip(hero, (Slot)i, original[i]);
            for (int i = 0; i < original.Length; i++) if (hero.gear[i] != original[i]) problems.Add("gear portrait: slot " + i + " was not restored");
            foreach (var g in lent) b.stash.Remove(g);
            b.Drain();
            Call(gc, "RebuildView", hero);
        }

        /// <summary>把所有裝備與道具的圖示排成一張總表，方便一眼看完每個模型。</summary>
        static void IconSheet()
        {
            var texes = new List<Texture2D>();
            var labels = new List<string>();
            foreach (var g in Gears.All) { texes.Add(Icons.Of(g)); labels.Add(g.name); }
            foreach (var it in new[] { Item.Salve, Item.QiPill, Item.Antidote, Item.Lime, Item.SmokeBomb, Item.Thunder, Item.Caltrops, Item.Venom, Item.Grapnel }) { texes.Add(Icons.Of(it)); labels.Add(it.name); }

            const int cols = 10, cell = 104;
            int rows = (texes.Count + cols - 1) / cols;
            var sheet = new Texture2D(cols * cell, rows * cell, TextureFormat.RGB24, false);
            var bg = new Color32(38, 32, 26, 255);
            var fill = new Color32[cols * cell * rows * cell];
            for (int i = 0; i < fill.Length; i++) fill[i] = bg;
            sheet.SetPixels32(fill);
            int blank = 0, missing = 0;
            var order = new StringBuilder();
            for (int k = 0; k < texes.Count; k++)
            {
                order.Append(k % cols == 0 ? "\n  " : "、").Append(labels[k]);
                var t = texes[k];
                if (t == null) { missing++; problems.Add("no icon for " + labels[k]); continue; }
                var px = t.GetPixels32();
                int solid = 0;
                int ox = (k % cols) * cell + 4, oy = (rows - 1 - k / cols) * cell + 4;
                for (int y = 0; y < t.height; y++)
                    for (int x = 0; x < t.width; x++)
                    {
                        var c = px[x + y * t.width];
                        if (c.a < 128) continue;
                        solid++;
                        sheet.SetPixel(ox + x, oy + y, c);
                    }
                if (solid < 60) { blank++; problems.Add("icon for " + labels[k] + " is empty (" + solid + " px)"); }
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(outDir, "A1-icons.png"), sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
            Note("Icon sheet: " + texes.Count + " icons (" + Gears.All.Count + " gear + 9 items), missing " + missing + ", blank " + blank + "; order left to right, top to bottom:" + order);
        }

        // ───────── 名器特效、新道具與新狀態 ─────────

        static void ExpectF(bool cond, string what) { if (!cond) problems.Add("fx scenario: " + what); }

        // 換上兵器（戰鬥中換要用掉隨手互動，測試時每次補回）
        static void Arm(Battle b, Unit u, Gear g)
        {
            u.freeLeft = true;
            if (u.Weapon != g) b.Equip(u, Slot.MainHand, g);
        }

        // 反覆用基本攻擊打 t，直到 done() 成立或打了 max 次；每次先把目標補滿血
        static int Until(Battle b, Unit u, Unit t, int max, Func<bool> done)
        {
            int n = 0;
            while (n < max && !done())
            {
                n++;
                t.hp = t.maxHp = 300;
                u.actionLeft = true;
                b.UseSkill(u, u.skills[0], t, -1);
            }
            return n;
        }

        static float MeanDie(Battle b, Skill sk, int n)
        {
            var m = typeof(Battle).GetMethod("RollDamage", Any);
            long sum = 0;
            for (int i = 0; i < n; i++) sum += (int)m.Invoke(b, new object[] { sk, 1 });
            return sum / (float)n;
        }

        static void FxScenarios()
        {
            var log = new StringBuilder();
            var b = Scenario.Create();
            var w = b.world;
            b.Start();
            Unit sword = b.units[0], brawler = b.units[1], knife = b.units[2], f1 = b.units[3], f2 = b.units[4], archer = b.units[6];

            int px = -1, py = -1, pz = -1;
            for (int y = 0; y < w.D && px < 0; y++)
                for (int x = 0; x < w.W; x++)
                {
                    int z;
                    if (FlatPatch(b, x, y, 3, 5, out z)) { px = x; py = y; pz = z; break; }
                }
            if (px < 0) { problems.Add("fx scenario: no flat patch to test on"); return; }
            Func<int, int, int> at = (dx, dy) => w.Idx(px + dx, py + dy, pz);

            // ── 名器 ──
            Teleport(b, sword, at(1, 0));
            Teleport(b, f1, at(1, 1));
            sword.stealth = -99; b.Observe();

            Arm(b, sword, Gears.SoftSword);
            int softReach = sword.skills[0].reach;
            f1.tangle = 0;
            Until(b, sword, f1, 80, () => f1.tangle > 0);
            bool tangled = f1.tangle == 2 && b.AdvReasons(f1, f1.skills[0], f1.x, f1.y, f1.z, sword).Contains("被纏住");
            f1.tangle = 0;

            Arm(b, sword, Gears.Halberd);
            Teleport(b, f1, at(1, 2));
            Until(b, sword, f1, 80, () => b.CellOf(f1) != at(1, 2));
            bool hooked = b.CellOf(f1) == at(1, 1) && w.occ[at(1, 2)] < 0 && w.occ[at(1, 1)] == f1.id;

            Arm(b, sword, Gears.WolfClub);
            f1.bleed = 0;
            Until(b, sword, f1, 80, () => f1.bleed > 0);
            int hpB = f1.hp;
            bool bleeding = f1.bleed == 3;
            Call(b, "TickWounds", f1);
            bool bled = bleeding && f1.hp < hpB && f1.bleed == 2;
            f1.bleed = 0;

            Arm(b, sword, Gears.JudgeBrush);
            f1.stun = false;
            int sealTries = Until(b, sword, f1, 200, () => f1.stun);
            bool sealedUp = f1.stun;
            f1.stun = false;

            Arm(b, sword, Gears.DarkIron);
            float heavyMean = MeanDie(b, sword.skills[0], 4000);
            Arm(b, sword, Gears.LongSword);
            float plainMean = MeanDie(b, sword.skills[0], 4000);

            // 遠程的名器：站開一點
            Teleport(b, f1, at(1, 3));
            b.Observe();
            Arm(b, sword, Gears.SleeveBolt);
            var bolt = sword.skills[0];
            f1.poison = 0;
            Until(b, sword, f1, 120, () => f1.poison > 0);
            bool poisoned = f1.poison == 3;
            int hpP = f1.hp;
            Call(b, "TickWounds", f1);
            bool poisonHurts = poisoned && f1.hp < hpP;
            f1.poison = 0;

            Arm(b, sword, Gears.TideFlute);
            var flute = sword.skills[0];
            f1.fright = 0;
            Until(b, sword, f1, 200, () => f1.fright > 0);
            bool dazed = f1.fright == 2;
            f1.fright = 0;

            Arm(b, sword, Gears.BoneNeedles);
            f1.stun = false;
            b.Drain();
            bool critSeen = false, stunOnCrit = false, stunWithoutCrit = false;
            for (int i = 0; i < 600 && !critSeen; i++)
            {
                f1.hp = f1.maxHp = 300; f1.stun = false;
                sword.actionLeft = true;
                b.UseSkill(sword, sword.skills[0], f1, -1);
                bool crit = false;
                foreach (var e in b.Drain()) if (e.type == EvType.Attack && e.flag) crit = true;
                if (crit) { critSeen = true; stunOnCrit = f1.stun; }
                else if (f1.stun) stunWithoutCrit = true;
            }
            f1.stun = false;

            // 乾坤扇：弓手射過來時自動撥擋一次
            Arm(b, sword, Gears.CosmosFan);
            archer.knownCell = b.CellOf(archer);
            Teleport(b, archer, at(1, 4));
            Teleport(b, f1, at(0, 4));
            sword.reactionLeft = true;
            b.Drain();
            Call(b, "Attack", archer, archer.skills[0], sword);
            var sbD = new StringBuilder(); Dump(b, sbD);
            bool deflected = sbD.ToString().Contains("撥擋") && !sword.reactionLeft;
            Call(b, "Attack", archer, archer.skills[0], sword);
            var sbD2 = new StringBuilder(); Dump(b, sbD2);
            bool onlyOnce = !sbD2.ToString().Contains("撥擋");
            log.Append(sbD).Append(sbD2);

            Note("Fx scenario named weapons: soft sword reach " + softReach + " tangles " + tangled + "; halberd hooks a foe 2 cells away to the front " + hooked
                + "; wolf club bleeds for 3 turns " + bled + "; judge's brush seals " + sealedUp + " (after " + sealTries + " hits or misses)"
                + "; dark iron sword mean die " + heavyMean.ToString("0.00") + " vs plain sword " + plainMean.ToString("0.00")
                + "; sleeve bolt range " + bolt.range + " one-handed " + !Gears.SleeveBolt.twoHanded + " poisons " + poisonHurts
                + "; tide flute is ranged " + (!flute.melee && flute.range == 4) + " dazes " + dazed
                + "; bone needles stun on a crit " + stunOnCrit + " (crit seen " + critSeen + "), never otherwise " + !stunWithoutCrit
                + "; cosmos fan deflects one shot a round " + (deflected && onlyOnce));
            ExpectF(softReach == 2 && tangled, "the soft sword should reach 2 cells and tangle its target");
            ExpectF(hooked, "the halberd should hook a target 2 cells away to the adjacent cell");
            ExpectF(bled, "the wolf-tooth club should cause 3 turns of bleeding that hurts at turn start");
            ExpectF(sealedUp, "the judge's brush should be able to seal a target");
            ExpectF(heavyMean > 6.05f && heavyMean < 6.55f && plainMean > 4.3f && plainMean < 4.7f, "the dark iron sword should reroll 1s and 2s (mean about 6.3 on a d10)");
            ExpectF(bolt.range == 5 && !bolt.melee && poisonHurts, "the sleeve bolt should be a short one-handed ranged weapon that poisons");
            ExpectF(!flute.melee && flute.range == 4 && dazed, "the tide flute should attack at range 4 and frighten");
            ExpectF(critSeen && stunOnCrit && !stunWithoutCrit, "bone needles should stun on a crit and only on a crit");
            ExpectF(deflected && onlyOnce, "the cosmos fan should deflect one ranged attack per round");
            int plain = 0, named = 0;
            foreach (var g in Gears.All) if (g.IsWeapon) { if (g.Named) named++; else plain++; }
            Note("Fx scenario catalogue: " + plain + " plain weapons without effects, " + named + " named weapons with one each");
            ExpectF(named == 9 && plain >= 20 && Gears.LongSword.fx == GearFx.None, "plain weapons should have no effect; 9 named weapons should");

            // ── 道具 ──
            b = Scenario.Create(); w = b.world; b.Start();
            sword = b.units[0]; brawler = b.units[1]; knife = b.units[2]; f1 = b.units[3]; f2 = b.units[4];
            px = -1;
            for (int y = 0; y < w.D && px < 0; y++)
                for (int x = 0; x < w.W; x++)
                {
                    int z;
                    if (FlatPatch(b, x, y, 3, 5, out z)) { px = x; py = y; pz = z; break; }
                }
            if (px < 0) { problems.Add("fx scenario: no flat patch for the item tests"); return; }
            at = (dx, dy) => w.Idx(px + dx, py + dy, pz);
            Teleport(b, knife, at(1, 0));
            Teleport(b, sword, at(0, 0));
            Teleport(b, brawler, at(2, 0));
            Teleport(b, f1, at(1, 3));
            Teleport(b, f2, at(2, 4));
            knife.stealth = sword.stealth = brawler.stealth = -99; b.Observe();

            // 石灰粉
            b.Give(knife, Item.Lime, 40);
            f1.blind = 0;
            int limeTries = 0;
            while (f1.blind == 0 && limeTries++ < 40) { knife.actionLeft = knife.bonusLeft = true; b.UseItem(knife, Item.Lime, f1); }
            bool blinded = f1.blind == 2 && b.AdvReasons(f1, f1.skills[0], f1.x, f1.y, f1.z, knife).Contains("目盲（劣勢）")
                && b.AdvReasons(knife, knife.skills[0], knife.x, knife.y, knife.z, f1).Contains("目標目盲（優勢）");
            bool limeFar = b.WhyNotUse(knife, Item.Lime, f2) != null; // f2 在 4 格外
            f1.blind = 0;

            // 淬毒藥
            knife.bonusLeft = true;
            bool venomOn = b.UseItem(knife, Item.Venom, null) && knife.venom && b.WhyNotUse(knife, Item.Venom, null) != null;
            Until(b, knife, f1, 80, () => !knife.venom);
            bool venomSpent = !knife.venom;
            // 解毒丹
            b.Give(sword, Item.Antidote);
            bool cureRefused = b.WhyNotUse(sword, Item.Antidote, null) != null;
            sword.poison = 3; sword.actionLeft = true;
            bool cured = b.UseItem(sword, Item.Antidote, null) && sword.poison == 0;

            // 霹靂彈：炸 f1 與 f2 之間，兩人都在 3×3 內
            f1.hp = f1.maxHp = 200; f2.hp = f2.maxHp = 200;
            Teleport(b, f2, at(2, 3));
            b.Forget(brawler);
            brawler.actionLeft = true;
            int hpS = sword.hp;
            bool boom = b.UseItem(brawler, Item.Thunder, null, at(1, 4));
            bool bombHit = boom && f1.hp < 200 && f2.hp < 200 && sword.hp == hpS && brawler.knownCell >= 0;

            // 鐵蒺藜：撒在身邊，敵人踏進去就觸發
            sword.actionLeft = true;
            bool farRefused = b.WhyNotUse(sword, Item.Caltrops, null, at(0, 2)) != null;
            bool laid = b.UseItem(sword, Item.Caltrops, null, at(0, 1));
            Prop spikes = b.PropAt(at(0, 1));
            Teleport(b, f2, at(0, 2));
            f2.moveLeft = 5; f2.hp = f2.maxHp = 200;
            b.ComputeReach(f2);
            bool stepped = laid && spikes != null && spikes.kind == PropKind.Caltrops && b.Move(f2, at(0, 1)) && spikes.gone && b.CellOf(f2) == at(0, 1);
            bool spikeOutcome = f2.hp == 200 ? f2.moveLeft > 0 : f2.moveLeft == 0; // 豁免成功沒事；失敗受傷且走不動

            // 飛爪
            sword.actionLeft = true;
            bool nearRefused = b.WhyNotUse(sword, Item.Grapnel, null, at(1, 1)) != null;
            int from = b.CellOf(sword);
            bool swung = b.UseItem(sword, Item.Grapnel, null, at(1, 2)) && b.CellOf(sword) == at(1, 2) && w.occ[from] < 0 && w.occ[at(1, 2)] == sword.id && !sword.actionLeft;
            Dump(b, log);

            Note("Fx scenario items: lime blinds " + blinded + " (after " + limeTries + " throws), out of range refused " + limeFar + "; venom applied " + venomOn + " and spent on the next hit " + venomSpent
                + "; antidote refused when healthy " + cureRefused + " cures " + cured + "; thunder bomb hits both foes, spares the hero outside and reveals the thrower " + bombHit
                + "; caltrops must be adjacent " + farRefused + ", triggered by a foe " + stepped + ", outcome consistent " + spikeOutcome
                + "; grapnel refuses 1 cell " + nearRefused + " and swings 2+ cells " + swung);
            ExpectF(blinded && limeFar, "lime should blind a foe within 3 cells (attack disadvantage, attacked with advantage)");
            ExpectF(venomOn && venomSpent, "venom should coat the weapon and be spent on the next hit");
            ExpectF(cureRefused && cured, "the antidote should cure poison and be refused when not poisoned");
            ExpectF(bombHit, "the thunder bomb should damage everyone in the 3×3 area and reveal the thrower");
            ExpectF(farRefused && stepped && spikeOutcome, "caltrops should be laid next to the user and trigger when a foe steps in");
            ExpectF(nearRefused && swung, "the grapnel should swing the user to a visible cell 2 to 5 cells away");

            // 煙霧彈：擋住視線，下一輪結束後散去
            b = Scenario.Create(); w = b.world; b.Start();
            knife = b.units[2];
            px = -1;
            for (int y = 0; y < w.D && px < 0; y++)
                for (int x = 0; x < w.W; x++)
                {
                    int z;
                    if (FlatPatch(b, x, y, 3, 5, out z)) { px = x; py = y; pz = z; break; }
                }
            if (px >= 0)
            {
                Teleport(b, knife, w.Idx(px + 1, py, pz));
                bool before = VoxelRay.Los(w, px + 1, py, pz, px + 1, py + 4, pz);
                knife.actionLeft = knife.bonusLeft = true;
                int r0 = b.round;
                bool thrown = b.UseItem(knife, Item.SmokeBomb, null, w.Idx(px + 1, py + 2, pz));
                bool blocked = !VoxelRay.Los(w, px + 1, py, pz, px + 1, py + 4, pz);
                bool inside = !VoxelRay.Los(w, px + 1, py + 2, pz, px + 1, py + 4, pz); // 站在煙裡也看不出去
                bool besideOk = VoxelRay.Los(w, px + 1, py, pz, px, py, pz);            // 煙外的近處不受影響
                int clouds = b.SmokeCount;
                int guard = 0;
                while (b.over == 0 && b.round <= r0 + 2 && guard++ < 60) b.EndTurn();
                bool cleared = b.SmokeCount == 0 && VoxelRay.Los(w, px + 1, py, pz, px + 1, py + 4, pz);
                Dump(b, log);
                Note("Fx scenario smoke: line of sight " + before + " → blocked " + blocked + ", blind from inside " + inside + ", clear beside it " + besideOk + ", clouds " + clouds
                    + ", gone two rounds later " + cleared + " (round " + r0 + " → " + b.round + ")");
                ExpectF(before && thrown && blocked && inside && besideOk && clouds == 1, "a smoke bomb should block line of sight through and out of the cloud");
                ExpectF(cleared || b.over != 0, "smoke should clear after the next round");
            }
            // ── 掉落物 ──
            b = Scenario.Create(); w = b.world; b.Start();
            {
                Unit hero = b.units[0], foe = b.units[3], chief = b.units[8], other = b.units[4];
                b.dropChance = 0;
                int oc = b.CellOf(other);
                Call(b, "Damage", other, 999, false);
                bool none = b.PropAt(oc) == null;
                b.dropChance = 1;
                int fc = b.CellOf(foe);
                Call(b, "Damage", foe, 999, false);
                Prop loot = b.PropAt(fc);
                bool dropped = loot != null && loot.kind == PropKind.Loot && loot.item != null && !w.blocked[fc];
                int side = dropped ? Orth(b, fc) : -1;
                bool picked = false, remote = false;
                if (side >= 0)
                {
                    int had = b.BagCount(hero, loot.item);
                    remote = b.WhyNot(hero, loot) != null;       // 離得遠不能撿
                    Teleport(b, hero, side);
                    hero.moveLeft = 6;
                    b.ComputeReach(hero);
                    picked = b.Move(hero, fc) && loot.gone && b.BagCount(hero, loot.item) == had + 1 && b.PropAt(fc) == null;
                }
                int bc = b.CellOf(chief);
                Call(b, "Damage", chief, 999, false);
                Prop big = b.PropAt(bc);
                bool bossDrop = big != null && big.item == Item.Thunder;
                Dump(b, log);
                Note("Fx scenario loot: nothing with drop chance 0 " + none + ", a fallen bandit drops an item " + dropped + (dropped ? " (" + loot.item.name + ")" : "")
                    + ", cannot be taken from afar " + remote + ", picked up by walking onto it " + picked + ", the boss always drops a thunder bomb " + bossDrop);
                ExpectF(none && dropped && remote && picked, "fallen bandits should drop loot that a hero picks up by walking onto it");
                ExpectF(bossDrop, "the boss should always drop his item");
            }
            File.WriteAllText(Path.Combine(outDir, "fx-scenarios.txt"), log.ToString(), Encoding.UTF8);
        }

        /// <summary>只畫 3D 場景的高解析截圖（不含介面），用來檢查模型、光影與霧。</summary>
        static void ShotWorld(string name, int w, int h)
        {
            var cam = Camera.main;
            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            canvas.enabled = false;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            canvas.enabled = true;
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            Note("shot " + name);
        }

        /// <summary>把幾個角色擺成走路、出招、受擊的姿勢各拍一張，檢查關節動作。</summary>
        static void PoseShots(object gc, Battle battle)
        {
            var views = (System.Collections.Generic.List<UnitView>)Get(gc, "views");
            foreach (var v in views)
            {
                string n = v.unit.name;
                if (n == "沈青嵐") { v.PoseAttack(0.36f, true); ShotClose("P-劍客-高舉", v.root); v.PoseAttack(0.58f, true); ShotClose("P-劍客-揮下", v.root); }
                else if (n == "石破山") { v.PoseWalk(1.35f); ShotClose("P-力士-走路", v.root); }
                else if (n == "唐小蝶") { v.PoseHurt(1f); ShotClose("P-暗器-受擊", v.root); }
                else if (n == "弓手甲") { v.PoseAttack(0.5f, false); ShotClose("P-弓手-引弓", v.root); }
                else if (n == "厲無咎") { v.PoseWalk(4.5f); ShotClose("P-寨主-走路", v.root); }
                else continue;
                v.PoseIdle();
            }
        }

        static UnitView ViewOf(object gc, Unit u)
        {
            return ((System.Collections.Generic.List<UnitView>)Get(gc, "views"))[u.id];
        }

        static void Portrait(object gc, Unit u, string name, bool face)
        {
            var t = ViewOf(gc, u).root;
            if (face) ShotScenic(name, t.position + Vector3.up * 0.93f + t.forward * 0.78f + t.right * 0.20f, t.position + Vector3.up * 0.88f);
            else ShotScenic(name, t.position + Vector3.up * 0.85f + t.forward * 2.3f + t.right * 0.8f, t.position + Vector3.up * 0.55f);
        }

        /// <summary>創角畫面、五官調整、男女體態、全部預設造型，逐一拍照。</summary>
        static void CreatorShots(object gc, Battle battle)
        {
            var creator = Get(gc, "creator");
            Shot("0a-creator-body");
            Call(creator, "SelectTab", 1); Shot("0b-creator-eyes");
            Call(creator, "SelectTab", 4); Shot("0c-creator-outfit");
            Call(creator, "SelectTab", 0);

            var hero = battle.units[0];
            Look original = hero.look;

            // 五官：同一個人改三種臉
            hero.look = Looks.Swordsman.Clone(); hero.look.headwear = Headwear.None;
            Call(gc, "RebuildView", hero); Portrait(gc, hero, "F1-face-default", true);

            var l = hero.look;
            l.eyes = EyeStyle.Big; l.eyeSize = 1.4f; l.eyeSpacing = 0.6f; l.brow = Brow.Calm; l.browHeight = 0.6f;
            l.nose = NoseStyle.Small; l.mouth = MouthStyle.Smile; l.mouthSize = 1.3f; l.faceLength = 0.9f; l.faceWidth = 1.1f;
            Call(gc, "RebuildView", hero); Portrait(gc, hero, "F2-face-big-eyes-smile", true);

            l.eyes = EyeStyle.Narrow; l.eyeSize = 0.9f; l.eyeSpacing = -0.6f; l.eyeHeight = 0.5f; l.brow = Brow.Fierce; l.browSize = 1.5f; l.browHeight = -0.3f;
            l.nose = NoseStyle.Wide; l.noseSize = 1.4f; l.mouth = MouthStyle.Frown; l.mouthHeight = -0.6f; l.faceLength = 1.18f; l.faceWidth = 0.9f;
            l.beard = Beard.LongBeard; l.hairColor = 0xE6E6E0; l.hair = HairStyle.Topknot;
            Call(gc, "RebuildView", hero); Portrait(gc, hero, "F3-face-narrow-eyes-beard", true);

            // 體態：男女各兩種
            hero.look = Looks.Swordsman.Clone(); hero.look.headwear = Headwear.None; hero.look.outfit = Outfit.ShortJacket; hero.look.sleeve = Sleeve.Narrow;
            Call(gc, "RebuildView", hero); Portrait(gc, hero, "B1-male-standard", false);
            hero.look.Body(BodyType.Burly); hero.look.chest = 1.25f; hero.look.shoulder = 1.3f; hero.look.sleeve = Sleeve.Bare;
            Call(gc, "RebuildView", hero); Portrait(gc, hero, "B2-male-burly", false);
            hero.look = Looks.Swordswoman.Clone(); hero.look.headwear = Headwear.None; hero.look.outfit = Outfit.ShortJacket; hero.look.sleeve = Sleeve.Narrow;
            Call(gc, "RebuildView", hero); Portrait(gc, hero, "B3-female-standard", false);
            hero.look.waist = 0.86f; hero.look.hips = 1.22f; hero.look.legs = 1.12f; hero.look.height = 1.08f; hero.look.outfit = Outfit.Robe; hero.look.sleeve = Sleeve.Wide;
            Call(gc, "RebuildView", hero); Portrait(gc, hero, "B4-female-tall-robe", false);

            hero.look = original;
            Call(gc, "RebuildView", hero);
            Call(creator, "Show", hero.look, 0);

            GearPortraits(gc, battle, hero);
            IconSheet();
            Wardrobe(battle);
            Note("creator shots done");
        }

        /// <summary>衣櫃展示：把全部預設造型排成四個一組的人偶拍照。</summary>
        static void Wardrobe(Battle battle)
        {
            var w = battle.world;
            var parent = new GameObject("Wardrobe").transform;
            for (int group = 0; group * 4 < Looks.All.Length; group++)
            {
                var made = new System.Collections.Generic.List<UnitView>();
                float topY = 0;
                for (int i = 0; i < 4 && group * 4 + i < Looks.All.Length; i++)
                {
                    int k = group * 4 + i, x = 2 + i, y = 22, z = w.TopStand(x, y);
                    var u = new Unit { id = 0, name = Looks.Names[k], team = (k >= 3 && k <= 5) || k == 15 ? 1 : 0, look = Looks.All[k].Clone(), x = x, y = y, z = z, hp = 1, maxHp = 1 };
                    var v = new UnitView(u, w, parent);
                    v.root.rotation = Quaternion.identity; // 面向 +Z，也就是鏡頭
                    made.Add(v);
                    topY += v.root.position.y / 4f;
                }
                ShotScenic("G" + (group + 1) + "-wardrobe", new Vector3(4.0f, topY + 1.35f, 26.3f), new Vector3(4.0f, topY + 0.52f, 22.5f));
                foreach (var v in made) v.Dispose();
            }
            UnityEngine.Object.Destroy(parent.gameObject);
        }

        /// <summary>低角度的風景照：檢查遠山、霧與場景物件的整體氛圍。</summary>
        static void ShotScenic(string name, Vector3 from, Vector3 lookAt)
        {
            var cam = Camera.main;
            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation;
            cam.transform.position = from;
            cam.transform.LookAt(lookAt);
            canvas.enabled = false;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            canvas.enabled = true;
            cam.transform.SetPositionAndRotation(p0, r0);
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            Note("shot " + name);
        }

        /// <summary>角色特寫：把鏡頭暫時移到模型前方（下一幀鏡頭控制會自動復位）。</summary>
        static void ShotClose(string name, Transform target)
        {
            var cam = Camera.main;
            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation;
            Vector3 focus = target.position + Vector3.up * 0.6f;
            cam.transform.position = focus + target.forward * 2.0f + target.right * 1.1f + Vector3.up * 0.9f;
            cam.transform.LookAt(focus);
            canvas.enabled = false;
            var rt = new RenderTexture(1000, 800, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1000, 800, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1000, 800), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            canvas.enabled = true;
            cam.transform.SetPositionAndRotation(p0, r0);
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            Note("shot " + name);
        }

        /// <summary>掀頂前後，從正上方點涼亭中央，應分別點到屋頂與亭內地面。</summary>
        static void CheckCoverPick(object gc, VoxelWorld w)
        {
            Call(gc, "SetCoverMode", 2); // 全部顯示
            int shown = VoxelRay.PickCell(w, 6.5f, 9.5f, 15.9f, 0, 0, -1);
            Call(gc, "SetCoverMode", 1); // 全部掀開
            int hidden = VoxelRay.PickCell(w, 6.5f, 9.5f, 15.9f, 0, 0, -1);
            Shot("4b-all-cover-hidden");
            Call(gc, "SetCoverMode", 0); // 回自動

            Note("Pick pavilion centre from above: cover shown → z " + (shown < 0 ? -1 : w.Z(shown)) + ", cover hidden → z " + (hidden < 0 ? -1 : w.Z(hidden)));
            if (shown != w.Idx(6, 9, 8)) problems.Add("pick with roof shown should hit roof top (6,9,8)");
            if (hidden != w.Idx(6, 9, 3)) problems.Add("pick with roof hidden should hit pavilion floor (6,9,3)");
            int hiddenCount = 0;
            foreach (bool h in w.coverHidden) if (h) hiddenCount++;
            Note("Auto mode: " + hiddenCount + " structure(s) hidden for the active unit");
        }

        /// <summary>把鏡頭與介面一起畫到貼圖並存成 PNG（批次模式沒有 Game 視窗可截）。</summary>
        static void Shot(string name)
        {
            var cam = Camera.main;
            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            int w = Screen.width, h = Screen.height;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var prevMode = canvas.renderMode;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases();
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            canvas.renderMode = prevMode;
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            Note("shot " + name);
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(Key, false);
            report.AppendLine();
            report.AppendLine(problems.Count == 0 ? "RESULT: OK, no errors or exceptions" : "RESULT: " + problems.Count + " problem(s)");
            foreach (var p in problems) report.AppendLine("  " + p);
            File.WriteAllText(Path.Combine(outDir, "result.txt"), report.ToString(), Encoding.UTF8);
            if (Application.isBatchMode) EditorApplication.Exit(problems.Count == 0 ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }
    }
}
