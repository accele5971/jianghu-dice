using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace Jianghu
{
    /// <summary>
    /// 控制器：建立場景、處理輸入與選取模式，並把規則層輸出的事件串依序播成動畫。
    /// 規則層已同步結算完畢，這裡只負責「演出」，播放期間鎖住輸入。
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        static readonly Color32 CMove = new Color32(70, 150, 235, 105);
        static readonly Color32 CPath = new Color32(255, 222, 120, 190);
        static readonly Color32 CTarget = new Color32(225, 60, 45, 150);
        static readonly Color32 CTargetHot = new Color32(255, 120, 90, 220);
        static readonly Color32 CBlast = new Color32(255, 140, 40, 170);
        static readonly Color32 CCursor = new Color32(255, 255, 255, 70);
        static readonly Color32 CThreat = new Color32(230, 70, 50, 70);
        static readonly Color32 CFar = new Color32(235, 235, 225, 105); // 自動尋路中，本回合走不到的那一段
        static readonly Color32 CProp = new Color32(255, 214, 90, 150); // 現在可以互動的地圖物件

        // 地圖物件與對話
        PropViews propViews;
        bool talking;       // 對話框開著，鎖住其他操作
        Prop talkNpc;

        Battle battle;
        VoxelWorld world;
        Camera cam;
        CameraRig rig;
        Hud hud;
        Markers markers;
        Fx fx;
        TerrainView terrain;

        // 遮蔽物顯示：0 自動（只掀開行動者頭頂的樹冠、屋頂）、1 全部掀開、2 全部顯示
        static readonly string[] CoverModeNames = { "自動（行動者頭頂的樹冠、屋頂淡出）", "全部掀開", "全部顯示" };
        int coverMode;
        Unit coverFocus;

        // 創角：開戰前先編輯我方角色的外觀
        Creator creator;
        bool preparing = true;
        int editHero;
        List<Unit> heroes;

        // 迷霧：依畫面上模型的位置與朝向計算（規則層的座標比動畫超前）
        FogView fog;
        Vision shown;
        bool fogOn;
        bool[] spotted;        // 每個敵人是否已被發現過

        // 輸入狀態
        float nextStepAt;      // WASD 連續移動的節流
        Vector3 rmbDown;       // 右鍵按下的位置：沒拖曳就放開算「點一下」，用來轉向
        int farCost = -1;      // 游標所指的遠處格子全程要幾點移動；-1 表示走不到
        int farKey = -1;       // pfThreat 目前存的是從哪個位置出發的全圖尋路（-1 表示要重算）

        Pathfinder pfThreat;  // 預覽敵人可達範圍用，另開一份才不會蓋掉玩家的尋路結果
        Sfx sfx;
        string[] tags;        // 每個單位目前顯示的狀態標籤
        int lastTeam = -1;
        readonly System.Text.StringBuilder tipSb = new System.Text.StringBuilder(256);
        readonly List<Unit> tipFoes = new List<Unit>();
        Transform activeRing;
        readonly List<UnitView> views = new List<UnitView>();
        readonly HashSet<int> reachSet = new HashSet<int>();

        bool playing;       // 正在播放事件，鎖輸入
        Skill sel;          // 目前選取、等待指定目標的招式
        int hover = -1;     // 滑鼠所指的格子
        bool hoverDirty = true;
        Vector3 lastMouse;
        float activeUntil;  // 按需渲染：在此時間前保持全速

        // 裝備與行囊面板
        bool gearOpen;
        int gearSlot = -1;      // 面板上選中的欄位；-1 表示沒選（右邊顯示行囊）
        Unit gearUnit;
        readonly List<Ev> gearPending = new List<Ev>(); // 面板開著時發生、等關閉後再播的事件
        readonly List<Gear> gearChoices = new List<Gear>();
        int gearPage;           // 可換的裝備太多時分頁
        int gearPageRow = -1;   // 右邊哪一列是「下一頁」
        Item aimItem;           // 已經拿在手上、等待指定對象或落點的道具

        // 畫質：0 省電（無即時陰影、角色腳下用圓影、不做反鋸齒）、1 標準（陰影較近較粗）、2 精緻
        const string QualityKey = "jianghu.quality";
        static readonly string[] QualityNames = { "省電（無即時陰影）", "標準", "精緻" };
        int quality = 2;
        Light sunLight;

        void ApplyQuality(bool announce)
        {
            quality = Mathf.Clamp(quality, 0, 2);
            QualitySettings.shadows = quality == 0 ? ShadowQuality.Disable : ShadowQuality.All;
            QualitySettings.shadowCascades = 2;
            QualitySettings.shadowDistance = quality == 1 ? 40 : 60;
            QualitySettings.shadowResolution = quality == 1 ? ShadowResolution.Medium : ShadowResolution.High;
            QualitySettings.antiAliasing = quality == 0 ? 0 : quality == 1 ? 2 : 4;
            if (sunLight != null) sunLight.shadows = quality == 0 ? LightShadows.None : quality == 1 ? LightShadows.Hard : LightShadows.Soft;
            UnitView.BlobShadows = quality == 0;
            foreach (var v in views) v.SetBlob(quality == 0);
            if (announce)
            {
                PlayerPrefs.SetInt(QualityKey, quality);
                hud.Log("<color=#A89C85>畫質：" + QualityNames[quality] + "</color>");
            }
        }

        void SetQuality(int level) { quality = level; ApplyQuality(false); }

        // ───────── 選單、設定、任務欄、暫停 ─────────

        enum Sheet { None, Menu, Settings, Quests, Message }
        Sheet sheet;
        bool paused;            // 自由行動時按空白鍵暫停
        bool loaded;            // 這一局是從存檔讀回來的（跳過創角）
        static SaveData pendingLoad;
        const string VolumeKey = "jianghu.volume";
        readonly HashSet<string> questSeen = new HashSet<string>();

        /// <summary>存檔的位置（自動試玩用另一個檔名，才不會蓋掉玩家的存檔）。</summary>
        public static string SavePath
        {
            get { return System.IO.Path.Combine(Application.persistentDataPath, Application.isBatchMode ? "jianghu-save-test.json" : "jianghu-save.json"); }
        }

        bool CanSave { get { return !preparing && !playing && !talking && battle.over == 0 && battle.active != null && battle.active.team == 0; } }

        void OpenMenu()
        {
            sheet = Sheet.Menu;
            hud.HideTip();
            bool has = System.IO.File.Exists(SavePath);
            string when = has ? System.IO.File.GetLastWriteTime(SavePath).ToString("yyyy-MM-dd HH:mm") : "";
            hud.ShowSheet("選　單", has ? "<color=#A89C85>上次存檔：" + when + "</color>" : "<color=#A89C85>還沒有存檔。</color>",
                new[] { "<b>返回遊戲</b> <size=13>Esc</size>", "<b>存檔</b>" + (CanSave ? "" : " <size=13>輪到你行動時才能存</size>"), "<b>讀檔</b>", "<b>設定</b>", "<b>退出遊戲</b>" },
                new[] { true, CanSave, has, true, true }, OnSheetPick);
        }

        void OpenSettings()
        {
            sheet = Sheet.Settings;
            int vol = Mathf.RoundToInt(AudioListener.volume * 100);
            hud.ShowSheet("設　定", "<color=#A89C85>點選項目切換。畫質愈高愈吃效能；沒有顯示卡的電腦建議用省電。</color>",
                new[] { "<b>畫質</b>　" + QualityNames[quality], "<b>音量</b>　" + vol + "%", "<b>遮蔽物</b>　" + CoverModeNames[coverMode].Split('（')[0], "<b>返回</b>" },
                null, OnSheetPick);
        }

        void ToggleQuests()
        {
            if (sheet == Sheet.Quests) { CloseSheet(); return; }
            if (gearOpen || talking) return;
            sheet = Sheet.Quests;
            hud.HideTip();
            var sb = new System.Text.StringBuilder();
            foreach (var q in battle.Quests())
            {
                string mark = q.Done ? "<color=#9FE0C0>【完成】</color>" : q.Failed ? "<color=#F0958A>【失敗】</color>" : "";
                sb.Append("<size=20><b><color=#DCB361>").Append(q.kind).Append("　").Append(q.title).Append("</color></b></size> ").Append(mark).Append('\n');
                sb.Append("<color=#A89C85>").Append(q.desc).Append("</color>\n");
                foreach (var l in q.lines)
                    sb.Append(l.done ? "　<color=#9FE0C0>✓ " : l.failed ? "　<color=#F0958A>✗ " : "　<color=#EDDFC2>○ ").Append(l.text).Append("</color>\n");
                sb.Append('\n');
            }
            hud.ShowSheet("任　務", sb.ToString(), new[] { "<b>關閉</b> <size=13>J</size>" }, null, OnSheetPick);
        }

        void ShowMessage(string title, string body)
        {
            sheet = Sheet.Message;
            hud.ShowSheet(title, body, new[] { "<b>返回選單</b>", "<b>返回遊戲</b>" }, null, OnSheetPick);
        }

        void CloseSheet()
        {
            sheet = Sheet.None;
            hud.HideSheet();
            hoverDirty = true;
        }

        void OnSheetPick(int i)
        {
            sfx.Click();
            switch (sheet)
            {
                case Sheet.Menu:
                    if (i == 0) CloseSheet();
                    else if (i == 1) SaveGame();
                    else if (i == 2) LoadGame();
                    else if (i == 3) OpenSettings();
                    else if (i == 4) QuitGame();
                    break;
                case Sheet.Settings:
                    if (i == 0) { quality = (quality + 1) % 3; ApplyQuality(true); OpenSettings(); }
                    else if (i == 1)
                    {
                        float v = Mathf.Round(AudioListener.volume * 4) / 4f - 0.25f; // 100 → 75 → 50 → 25 → 0 → 100
                        if (v < -0.01f) v = 1f;
                        AudioListener.volume = Mathf.Clamp01(v);
                        PlayerPrefs.SetFloat(VolumeKey, AudioListener.volume);
                        OpenSettings();
                    }
                    else if (i == 2) { SetCoverMode((coverMode + 1) % 3); OpenSettings(); }
                    else OpenMenu();
                    break;
                case Sheet.Quests:
                    CloseSheet();
                    break;
                case Sheet.Message:
                    if (i == 0) OpenMenu(); else CloseSheet();
                    break;
            }
        }

        /// <summary>把目前的戰局寫進存檔。</summary>
        bool SaveGame()
        {
            if (!CanSave) return false;
            try
            {
                var d = battle.Save();
                d.savedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                System.IO.File.WriteAllText(SavePath, JsonUtility.ToJson(d), System.Text.Encoding.UTF8);
                hud.Log("<color=#9FE0C0>已存檔。</color>");
                ShowMessage("存　檔", "進度已經存好了。\n<color=#A89C85>" + d.savedAt + "</color>");
                return true;
            }
            catch (System.Exception e)
            {
                ShowMessage("存　檔", "<color=#F0958A>存檔失敗：" + e.Message + "</color>");
                return false;
            }
        }

        /// <summary>讀取存檔：整個場景依存檔重新建立。</summary>
        bool LoadGame()
        {
            try
            {
                if (!System.IO.File.Exists(SavePath)) return false;
                var d = JsonUtility.FromJson<SaveData>(System.IO.File.ReadAllText(SavePath, System.Text.Encoding.UTF8));
                // 先在一場新的戰局上試套一次，確定存檔可用再換場
                if (d == null || !Scenario.Create().Restore(d)) { ShowMessage("讀　檔", "<color=#F0958A>這份存檔無法讀取（可能是舊版本留下的）。</color>"); return false; }
                pendingLoad = d;
                Time.timeScale = 1;
                Destroy(gameObject);
                Bootstrap.NewGame();
                return true;
            }
            catch (System.Exception e)
            {
                ShowMessage("讀　檔", "<color=#F0958A>讀檔失敗：" + e.Message + "</color>");
                return false;
            }
        }

        void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void SetPaused(bool on)
        {
            paused = on;
            Time.timeScale = on ? 0 : 1;
            hud.SetPause(on);
        }

        // 讀檔之後：跳過創角，把畫面補成存檔當時的樣子
        void ResumeLoaded()
        {
            preparing = false;
            hud.SetBattleUi(true);
            hud.SetPrep(false);
            rig.yaw = 125; rig.pitch = 42; rig.dist = 20;
            for (int i = 0; i < shown.seen.Length; i++) shown.seen[i] = battle.vision.seen[i];
            foreach (var u in battle.units)
            {
                if (u.team == 1) spotted[u.id] = u.knownCell >= 0 || !u.alive;
                if (!u.alive) continue;
                string st = battle.StatusText(u);
                tags[u.id] = st.Length > 0 ? st : null;
                hud.SetTags(u, tags[u.id]);
                hud.SetBar(u, u.hp);
                hud.SetQi(u, u.qi);
            }
            SetFog(true);
            foreach (var q in battle.Quests()) foreach (var l in q.lines) if (l.done) questSeen.Add(q.title + l.text); // 已完成的不重複通知
            hud.Log("<color=#9FE0C0>讀取存檔完成。</color>");
            var evs = new List<Ev> { new Ev { type = EvType.TurnStart, a = battle.active, n = battle.exploring ? 0 : battle.round, text = battle.StatusText(battle.active) } };
            StartCoroutine(Play(evs));
        }

        // 任務有新進度時在戰報提示一次
        void CheckQuests()
        {
            foreach (var q in battle.Quests())
            {
                bool news = false;
                foreach (var l in q.lines)
                    if (l.done && questSeen.Add(q.title + l.text)) { hud.Log("<color=#FFE08A>任務進度：" + q.title + " ✓ " + l.text + "</color>"); news = true; }
                if (news && q.Done) { StartCoroutine(hud.Banner("任務完成：" + q.title, new Color(1f, 0.88f, 0.5f))); sfx.Heal(); }
            }
        }

        /// <summary>開場是否先進入自由行動。</summary>
        public static bool StartInExploration = true;

        /// <summary>把規則層累積的事件播出來（測試用）。</summary>
        void Flush() { StartCoroutine(Play(battle.Drain())); }

        // 自由行動時換下一位俠客
        void NextHero()
        {
            int at = heroes.IndexOf(battle.active);
            for (int k = 1; k <= heroes.Count; k++)
            {
                var h = heroes[(at + k) % heroes.Count];
                if (battle.Select(h)) { sfx.Click(); StartCoroutine(Play(battle.Drain())); return; }
            }
        }

        bool MyTurn { get { return !playing && !talking && !gearOpen && !listOpen && !paused && sheet == Sheet.None && battle.over == 0 && battle.active != null && battle.active.team == 0; } }

        // ───────── 建立 ─────────

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            // 沒有顯示卡（遠端桌面、虛擬機常見的軟體繪圖）時，即時陰影會佔掉一幀大半的時間，預設改用省電畫質
            bool software = SystemInfo.graphicsDeviceName.IndexOf("Basic Render", System.StringComparison.OrdinalIgnoreCase) >= 0
                || SystemInfo.graphicsDeviceName.IndexOf("llvmpipe", System.StringComparison.OrdinalIgnoreCase) >= 0
                || SystemInfo.graphicsDeviceName.IndexOf("SwiftShader", System.StringComparison.OrdinalIgnoreCase) >= 0;
            quality = PlayerPrefs.GetInt(QualityKey, software ? 0 : 2);
            ApplyQuality(false);

            battle = Scenario.Create();
            if (pendingLoad != null) { loaded = battle.Restore(pendingLoad); pendingLoad = null; }
            world = battle.world;
            AudioListener.volume = PlayerPrefs.GetFloat(VolumeKey, 1f);

            // 鏡頭與燈光自備；場景裡原有的先關掉，避免重複
            foreach (var c in Camera.allCameras) c.gameObject.SetActive(false);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.gameObject.SetActive(false);

            var camGo = new GameObject("Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(transform, false);
            cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.875f, 0.865f, 0.820f); // 宣紙色
            cam.fieldOfView = 38;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 400;
            rig = camGo.AddComponent<CameraRig>();
            rig.yaw = 125;
            rig.boundsMax = new Vector3(world.W, world.H * Vis.H, world.D);
            rig.dist = 20;
            rig.pitch = 42;

            var sun = new GameObject("Sun", typeof(Light));
            sun.transform.SetParent(transform, false);
            sun.transform.rotation = Quaternion.Euler(52, 150, 0);
            var light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.intensity = 0.85f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.86f;
            sunLight = light;
            ApplyQuality(false);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.43f, 0.43f);
            // 遠景薄霧：顏色與背景相同，地圖遠端會淡入天色
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = cam.backgroundColor;
            RenderSettings.fogStartDistance = 20;
            RenderSettings.fogEndDistance = 64;

            terrain = TerrainMesher.Build(world, transform, cam.backgroundColor);
            markers = new Markers(world, terrain, transform);
            fx = new Fx(transform);
            pfThreat = new Pathfinder(world);
            fog = new FogView(world, new Color(0.77f, 0.78f, 0.76f));
            shown = new Vision(world);
            spotted = new bool[battle.units.Count];
            sfx = new Sfx(gameObject);
            tags = new string[battle.units.Count];

            hud = new Hud(transform);
            hud.onSkill = OnSkillButton;
            hud.onEnd = EndTurn;
            hud.onAgain = Restart;
            hud.onBag = ToggleGear;

            foreach (var u in battle.units)
            {
                views.Add(new UnitView(u, world, transform));
                hud.AddBar(u);
            }

            propViews = new PropViews(battle, transform);
            if (loaded)
            {
                // 讀檔：倒下的人不顯示；物件依存檔的狀態顯示（開過的箱子、現形的絆索、掉落物、濃煙）
                foreach (var u in battle.units)
                    if (!u.alive) { views[u.id].dead = true; views[u.id].root.gameObject.SetActive(false); hud.RemoveBar(u); }
                foreach (var p in battle.props) propViews.Sync(p);
                battle.EachCloud((id, cells) => propViews.Smoke(id, cells, true));
            }

            var ringGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ringGo.GetComponent<Collider>());
            activeRing = ringGo.transform;
            activeRing.SetParent(transform, false);
            ringGo.GetComponent<MeshFilter>().sharedMesh = UnitView.RingMesh(0.52f, 0.075f); // 金色細圈，套在陣營圈外
            var rr = ringGo.GetComponent<MeshRenderer>();
            rr.sharedMaterial = Mats.Glow(new Color(1f, 0.85f, 0.35f, 0.95f));
            rr.shadowCastingMode = ShadowCastingMode.Off;
            rr.receiveShadows = false;
            ringGo.SetActive(false);
        }

        void Start()
        {
            hud.Log("<color=#DCB361>黑風寨群匪盤踞山道，三俠拔劍而上。</color>");
            hud.Log("<color=#A89C85>先為三位俠客定下相貌衣著，再按「出戰」。</color>");

            heroes = battle.units.FindAll(u => u.team == 0);
            if (loaded) { ResumeLoaded(); return; }
            if (!Application.isBatchMode) LoadLooks();

            var names = new string[heroes.Count];
            for (int i = 0; i < names.Length; i++) names[i] = heroes[i].name;
            creator = new Creator(hud, names);
            creator.onHero = EditHero;
            creator.onChanged = () => RebuildView(heroes[editHero]);
            creator.onReplace = look =>
            {
                var h = heroes[editHero];
                look.weapon = h.Weapon != null ? h.Weapon.model : WeaponKind.None; // 造型不含兵器：手上的以裝備為準
                h.look = look;
                RebuildView(h);
                creator.Show(look, editHero);
            };
            creator.onStart = BeginBattle;

            hud.SetBattleUi(false);
            hud.SetPrep(true);
            rig.pitch = 12;
            rig.dist = 4.2f;
            EditHero(0);
        }

        // ───────── 創角 ─────────

        const string SaveKey = "jianghu.look.";

        /// <summary>切換要編輯的角色，鏡頭拉到他面前。面板在左側，所以把人擺在畫面偏右。</summary>
        void EditHero(int index)
        {
            editHero = index;
            var u = heroes[index];
            var v = views[u.id];
            rig.yaw = v.root.eulerAngles.y + 180 - 20;
            Vector3 right = Quaternion.Euler(0, rig.yaw, 0) * Vector3.right;
            rig.FocusExact(v.root.position + Vector3.up * 0.60f - right * 0.42f);
            creator.Show(u.look, index);
        }

        /// <summary>外觀資料改了：丟掉舊模型，依新資料重建。</summary>
        void RebuildView(Unit u)
        {
            var old = views[u.id];
            var rot = old.root.rotation;
            if (activeRing.parent == old.root) activeRing.SetParent(transform, false);
            old.Dispose();
            var v = new UnitView(u, world, transform);
            v.root.rotation = rot;
            views[u.id] = v;
        }

        void LoadLooks()
        {
            for (int i = 0; i < heroes.Count; i++)
            {
                string json = PlayerPrefs.GetString(SaveKey + i, "");
                if (string.IsNullOrEmpty(json)) continue;
                try
                {
                    var look = JsonUtility.FromJson<Look>(json);
                    if (look == null) continue;
                    heroes[i].look = look;
                    RebuildView(heroes[i]);
                }
                catch (System.Exception) { /* 存檔格式不合就用預設造型 */ }
            }
            // 存檔只記長相；手上的兵器以裝備為準
            foreach (var h in heroes)
            {
                var want = h.Weapon != null ? h.Weapon.model : WeaponKind.None;
                if (h.look != null && h.look.weapon != want) { h.look.weapon = want; RebuildView(h); }
            }
        }

        void SaveLooks()
        {
            for (int i = 0; i < heroes.Count; i++) PlayerPrefs.SetString(SaveKey + i, JsonUtility.ToJson(heroes[i].look));
            PlayerPrefs.Save();
        }

        /// <summary>創角結束，開始戰鬥。</summary>
        void BeginBattle()
        {
            if (!preparing) return;
            preparing = false;
            if (!Application.isBatchMode) SaveLooks();
            if (gearOpen) CloseGear();
            creator.Hide();
            hud.SetPrep(false);
            hud.SetBattleUi(true);
            rig.yaw = 125;
            rig.pitch = 42;
            rig.dist = 20;
            SetFog(true);
            battle.explore = StartInExploration;
            battle.Start();
            StartCoroutine(Play(battle.Drain()));
        }

        void OnDestroy()
        {
            if (fog != null) fog.Dispose();
            if (propViews != null) propViews.Dispose();
            Time.timeScale = 1;
            OnDemandRendering.renderFrameInterval = 1;
        }

        void Restart()
        {
            Destroy(gameObject);
            Bootstrap.NewGame();
        }

        // ───────── 每幀 ─────────

        void Update()
        {
            var mouse = Input.mousePosition;
            bool moved = mouse != lastMouse;
            lastMouse = mouse;

            // 按需渲染：有動畫、輸入或鏡頭變動才全速，否則降頻省電
            if (playing || moved || rig.Changed || Input.anyKey || Input.mouseScrollDelta.y != 0)
                activeUntil = Time.unscaledTime + 0.5f;
            OnDemandRendering.renderFrameInterval = Time.unscaledTime < activeUntil ? 1 : 4;

            if (Input.GetKeyDown(KeyCode.X)) SetCoverMode((coverMode + 1) % 3);
            if (Input.GetKeyDown(KeyCode.F3)) { quality = (quality + 1) % 3; ApplyQuality(true); }
            if (terrain.Tick(Time.unscaledDeltaTime)) activeUntil = Time.unscaledTime + 0.3f;
            if (fog.Tick(Time.unscaledDeltaTime)) activeUntil = Time.unscaledTime + 0.3f;

            // 按鈕點過後不保留焦點，否則空白鍵會重複觸發該按鈕
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);

            // Esc：由內而外關掉開著的東西；都沒開就叫出選單
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (sheet == Sheet.Settings || sheet == Sheet.Message) OpenMenu();
                else if (sheet != Sheet.None) CloseSheet();
                else if (listOpen) CloseList();
                else if (gearOpen) CloseGear();
                else if (talking) CloseDialog();
                else if (sel != null || aimItem != null) { sel = null; aimItem = null; Refresh(); }
                else { if (paused) SetPaused(false); OpenMenu(); }
                return;
            }
            if (sheet != Sheet.None) { if (Input.GetKeyDown(KeyCode.J) && sheet == Sheet.Quests) CloseSheet(); return; }
            if (Input.GetKeyDown(KeyCode.J) && !preparing) { ToggleQuests(); return; }
            if (Input.GetKeyDown(KeyCode.I)) ToggleGear();
            if (Input.GetKeyDown(KeyCode.B)) ToggleBag();
            // 空白鍵：自由行動時暫停／繼續
            if (Input.GetKeyDown(KeyCode.Space) && !preparing && !gearOpen && !listOpen && !talking && battle.over == 0 && (paused || (battle.exploring && !playing)))
            {
                SetPaused(!paused);
                return;
            }
            if (battle.over != 0 || gearOpen || listOpen || paused) return;

            if (MyTurn)
            {
                var u = battle.active;
                // 空白鍵：回合制是結束回合（自由行動時是暫停，在上面處理了）
                if (Input.GetKeyDown(KeyCode.Space) && !battle.exploring) { EndTurn(); return; }
                if (Input.GetKeyDown(KeyCode.Tab) && battle.exploring) { NextHero(); return; }
                battle.running = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                for (int i = 0; i < u.skills.Length && i < 9; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { OnSkillButton(i); if (playing) return; }
                if (Input.GetKeyDown(KeyCode.F)) { InteractNearest(); if (playing || talking) return; }
                KeyboardMove();
                if (playing) return;
            }

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (moved || rig.Changed || hoverDirty)
            {
                hoverDirty = false;
                int h = overUi ? -1 : Pick();
                if (h != hover)
                {
                    hover = h;
                    RedrawMarkers();
                    UpdateTip();
                }
            }

            if (MyTurn && !overUi && Input.GetMouseButtonDown(0)) Click();

            // 右鍵：拖曳是轉視角；沒拖曳就放開則是讓角色原地轉向游標所指的方向
            if (Input.GetMouseButtonDown(1)) rmbDown = mouse;
            if (Input.GetMouseButtonUp(1) && (mouse - rmbDown).sqrMagnitude < 36 && MyTurn && !overUi && hover >= 0) FaceHover();
        }

        void LateUpdate()
        {
            hud.Follow(cam, views);
        }

        int Pick()
        {
            var r = cam.ScreenPointToRay(Input.mousePosition);
            // Unity (x, y高, z) → 遊戲 (x, y, z高)。畫面上每層只有 Vis.H 高，換算回層數
            return VoxelRay.PickCell(world, r.origin.x, r.origin.z, r.origin.y / Vis.H, r.direction.x, r.direction.z, r.direction.y / Vis.H);
        }

        Unit UnitAt(int cell)
        {
            if (cell < 0) return null;
            int id = world.occ[cell];
            if (id < 0) return null;
            var u = battle.units[id];
            return Visible(u) ? u : null; // 迷霧裡的敵人當作不存在，不能讓游標洩漏位置
        }

        // ───────── 玩家操作 ─────────

        void OnSkillButton(int i)
        {
            if (!MyTurn) return;
            var u = battle.active;
            if (i >= u.skills.Length) return;
            var sk = u.skills[i];
            if (!battle.CanUse(u, sk)) return;
            sfx.Click();
            aimItem = null;

            if (sk.kind == SkillKind.Attack || sk.kind == SkillKind.Aoe || sk.kind == SkillKind.Shout)
            {
                sel = sel == sk ? null : sk; // 再按一次取消
                Refresh();
            }
            else if (battle.UseSkill(u, sk, null, -1))
            {
                StartCoroutine(Play(battle.Drain()));
            }
        }

        void Click()
        {
            var u = battle.active;
            if (hover < 0) return;

            if (battle.exploring && sel == null && aimItem == null)
            {
                // 自由行動時點另一位俠客：換他行動
                var pick = UnitAt(hover);
                if (pick != null && pick.team == 0 && pick != u) { if (battle.Select(pick)) { sfx.Click(); StartCoroutine(Play(battle.Drain())); } return; }
            }

            if (aimItem != null)
            {
                // 手上拿著道具：這一下是指定對象或落點
                var who = UnitAt(hover);
                string why = battle.WhyNotUse(u, aimItem, who, hover);
                if (why != null) { Toast(Vis.Cell(world, hover), why); return; }
                var item = aimItem;
                aimItem = null;
                if (battle.UseItem(u, item, who, hover)) StartCoroutine(Play(battle.Drain()));
                return;
            }

            if (sel == null)
            {
                // 點到地圖物件：互動（芒草叢不用互動，照常走進去）
                var prop = PropAt(hover);
                bool walkOn = prop != null && (prop.kind == PropKind.Grass || prop.kind == PropKind.Caltrops
                    || ((prop.kind == PropKind.Loot || prop.kind == PropKind.Herb || prop.kind == PropKind.Vine) && !battle.Beside(u, prop))); // 掉落物、藥草：不在旁邊就走過去
                if (prop != null && !walkOn) { TryInteract(prop); return; }

                if (hover == battle.CellOf(u) || world.occ[hover] >= 0) return; // 點到自己或有人的格子
                if (reachSet.Contains(hover))
                {
                    if (battle.Move(u, hover)) StartCoroutine(Play(battle.Drain()));
                    return;
                }

                // 本回合走不到：自動尋路，走到路線上本回合能到的最遠處
                int total;
                int cell = battle.AutoMoveCell(u, hover, out total);
                if (cell >= 0 && battle.Move(u, cell))
                {
                    hud.Log("<color=#A89C85>" + u.name + " 朝遠處前進（全程需 " + total + " 點移動）。</color>");
                    StartCoroutine(Play(battle.Drain()));
                }
                else
                {
                    battle.ComputeReach(u); // 尋路用掉了共用的搜尋結果，還原成本回合的移動範圍
                    Toast(Vis.Cell(world, hover), total < 0 ? "無法抵達" : "移動力已用盡");
                }
                return;
            }

            bool ok = sel.kind == SkillKind.Aoe
                ? battle.UseSkill(u, sel, null, hover)
                : battle.UseSkill(u, sel, UnitAt(hover), -1);
            if (ok) StartCoroutine(Play(battle.Drain()));
        }

        // ───────── 裝備與行囊 ─────────

        /// <summary>拿著道具瞄準時的標記：可用的對象或落點，以及範圍道具會波及的格子。</summary>
        void AimMarkers(Unit u)
        {
            var item = aimItem;
            if (item.aim == Aim.Cell)
            {
                for (int y = Mathf.Max(0, u.y - item.range); y <= Mathf.Min(world.D - 1, u.y + item.range); y++)
                    for (int x = Mathf.Max(0, u.x - item.range); x <= Mathf.Min(world.W - 1, u.x + item.range); x++)
                        for (int z = 1; z < world.H - 1; z++)
                        {
                            if (!world.Standable(x, y, z)) continue;
                            int c = world.Idx(x, y, z);
                            if (battle.WhyNotUse(u, item, null, c) == null) markers.Add(c, CProp, 0.01f);
                        }
                if (item.radius > 0 && hover >= 0 && battle.WhyNotUse(u, item, null, hover) == null)
                {
                    int cx = world.X(hover), cy = world.Y(hover), cz = world.Z(hover);
                    for (int dy = -item.radius; dy <= item.radius; dy++)
                        for (int dx = -item.radius; dx <= item.radius; dx++)
                            for (int z = cz - 2; z <= cz + 2; z++)
                                if (world.Standable(cx + dx, cy + dy, z)) markers.Add(world.Idx(cx + dx, cy + dy, z), CBlast, 0.02f);
                }
                return;
            }
            foreach (var t in battle.units)
            {
                if (!t.alive || !Visible(t)) continue;
                if (item.aim == Aim.Foe ? t.team == u.team : t.team != u.team) continue;
                if (battle.WhyNotUse(u, item, t) != null) continue;
                int c = battle.CellOf(t);
                markers.Add(c, item.aim == Aim.Foe ? (c == hover ? CTargetHot : CTarget) : CProp);
            }
        }

        /// <summary>開關裝備面板。戰前看的是正在編輯的那位俠客，戰鬥中看的是輪到的人。</summary>
        void ToggleGear()
        {
            if (gearOpen) { CloseGear(); return; }
            if (playing || talking || listOpen || battle.over != 0) return;
            Unit u = preparing ? heroes[editHero] : battle.active;
            if (u == null || u.team != 0) return;
            sfx.Click();
            gearUnit = u;
            gearSlot = -1;
            gearOpen = true;
            sel = null;
            aimItem = null;
            hud.HideTip();
            hud.SetGearSide(preparing);
            if (preparing) FramePrep();
            ShowGearPanel();
        }

        // 戰前整備：面板靠左，鏡頭把正在整備的人擺到畫面右側，換裝立刻看得到
        void FramePrep()
        {
            var v = views[gearUnit.id];
            rig.yaw = v.root.eulerAngles.y + 180 - 20;
            Vector3 right = Quaternion.Euler(0, rig.yaw, 0) * Vector3.right;
            float half = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * rig.dist * cam.aspect;
            rig.FocusExact(v.root.position + Vector3.up * 0.60f - right * (half * 0.66f));
        }

        void OnGearTab(int i)
        {
            if (!preparing || i >= heroes.Count) return;
            sfx.Click();
            editHero = i;
            gearUnit = heroes[i];
            gearSlot = -1;
            gearPage = 0;
            FramePrep();
            ShowGearPanel();
        }

        void ShowGearPanel()
        {
            var u = gearUnit;
            var slots = new string[Gears.SlotCount];
            for (int i = 0; i < slots.Length; i++)
            {
                var g = u.gear[i];
                bool held = i == (int)Slot.OffHand && u.Weapon != null && u.Weapon.twoHanded;
                slots[i] = "<color=#A89C85>" + Gears.SlotNames[i] + "</color>　" + (held ? "<color=#8A8274>（雙手兵器）</color>" : g != null ? "<b>" + g.name + "</b>" : "<color=#8A8274>—</color>")
                    + (g != null ? "　<size=13><color=#C9BFA8>" + Gears.Summary(g) + "</color></size>" : "");
            }

            var options = new List<string>();
            var enabled = new List<bool>();
            string rightTitle, detail;
            gearChoices.Clear();
            if (gearSlot < 0)
            {
                // 沒選欄位：右邊是行囊
                rightTitle = "行囊　" + u.bag.Count + " / " + u.BagSize + " 格";
                foreach (var s in u.bag)
                {
                    string why = battle.WhyNotStart(u, s.item);
                    options.Add("<b>" + s.item.name + "</b> ×" + s.n + "　<size=13><color=#C9BFA8>" + (why ?? (battle.ItemIsBonus(u, s.item) ? "附加動作" : "動作")
                        + (s.item.aim == Aim.Self ? "，點擊使用" : "，點擊後指定對象")) + "</color></size>");
                    enabled.Add(why == null);
                }
                gearPageRow = -1;
                detail = "點左邊的欄位更換裝備，點右邊的道具使用。\n防禦 " + u.Ac + "　移動 " + u.move + "　輕功 " + u.jump + (u.Clanky ? "　<color=#F0958A>潛行劣勢</color>" : "")
                    + "\n" + u.skills[0].name + "：" + u.skills[0].desc;
            }
            else
            {
                var slot = (Slot)gearSlot;
                var cur = u.gear[gearSlot];
                rightTitle = "更換「" + Gears.SlotNames[gearSlot] + "」";
                string block = battle.WhyNotEquip(u, slot, null);
                options.Add("<color=#C9BFA8>卸下</color>");
                enabled.Add(cur != null && block == null);
                gearChoices.Add(null);
                // 一頁 9 件，多的翻頁；名器用金色標出
                var fits = new List<Gear>();
                foreach (var e in battle.BagEntries(BagFilter.All, listSort)) if (e.gear != null && e.gear.Fits(slot)) fits.Add(e.gear); // 照行李整理選的排序
                int pages = Mathf.Max(1, (fits.Count + 8) / 9);
                if (gearPage >= pages) gearPage = 0;
                for (int k = gearPage * 9; k < fits.Count && k < gearPage * 9 + 9; k++)
                {
                    var g = fits[k];
                    string why = battle.WhyNotEquip(u, slot, g);
                    options.Add((g.Named ? "<b><color=#FFD36A>★" + g.name + "</color></b>" : "<b>" + g.name + "</b>") + "　<size=13><color=#C9BFA8>" + Gears.Summary(g) + "</color></size>");
                    enabled.Add(why == null);
                    gearChoices.Add(g);
                }
                gearPageRow = -1;
                if (pages > 1)
                {
                    gearPageRow = options.Count;
                    options.Add("<color=#C9BFA8>下一頁　" + (gearPage + 1) + " / " + pages + "</color>");
                    enabled.Add(true);
                    gearChoices.Add(null);
                }
                detail = (block != null ? "<color=#F0958A>" + block + "</color>\n" : battle.Fighting ? "戰鬥中換兵器算一次隨手互動（每回合一次）。\n" : "")
                    + (cur != null ? cur.name + "：" + Gears.Summary(cur) + (cur.desc.Length > 0 ? "。" + cur.desc : "") : "這個欄位是空的。")
                    + (cur != null && cur.Named ? "\n<color=#FFD36A>★" + Gears.FxText[(int)cur.fx] + "</color>"
                       : cur != null && cur.IsWeapon ? "\n" + Gears.Info(cur.cls).name + "：" + Gears.Info(cur.cls).note : "");
            }
            hud.ShowGear(u.name + "  <size=16><color=#A89C85>" + u.cls + "</color></size>", slots, gearSlot, rightTitle, options.ToArray(), enabled.ToArray(), detail,
                OnGearSlot, OnGearOption, CloseGear);

            // 圖示：左邊是身上穿的，右邊是行囊裡的道具或可換的裝備
            var slotIcons = new Texture[Gears.SlotCount];
            for (int i = 0; i < slotIcons.Length; i++) slotIcons[i] = Icons.Of(u.gear[i]);
            var optIcons = new Texture[options.Count];
            if (gearSlot < 0) for (int i = 0; i < u.bag.Count && i < optIcons.Length; i++) optIcons[i] = Icons.Of(u.bag[i].item);
            else for (int i = 0; i < gearChoices.Count && i < optIcons.Length; i++) optIcons[i] = Icons.Of(gearChoices[i]);
            hud.SetGearIcons(slotIcons, optIcons);

            if (preparing)
            {
                var names = new string[heroes.Count];
                for (int i = 0; i < names.Length; i++) names[i] = heroes[i].name;
                hud.SetGearTabs(names, heroes.IndexOf(u), OnGearTab);
            }
            else hud.SetGearTabs(null, 0, null);
        }

        void OnGearSlot(int i)
        {
            sfx.Click();
            gearSlot = gearSlot == i ? -1 : i; // 再點一次回到行囊
            gearPage = 0;
            ShowGearPanel();
        }

        void OnGearOption(int i)
        {
            var u = gearUnit;
            sfx.Click();
            if (gearSlot < 0)
            {
                if (i >= u.bag.Count) return;
                var item = u.bag[i].item;
                if (battle.WhyNotStart(u, item) != null) return;
                // 只能對自己用的直接用；其餘先關面板，再到場上指定對象或落點
                if (item.aim == Aim.Self) { if (battle.UseItem(u, item, null)) { CloseGear(); return; } }
                else { aimItem = item; CloseGear(); return; }
            }
            else if (i == gearPageRow) gearPage++;
            else if (i < gearChoices.Count && battle.Equip(u, (Slot)gearSlot, gearChoices[i]))
            {
                // 換裝立刻反映在模型上；其餘事件等面板關閉再播
                foreach (var e in battle.Drain())
                {
                    if (e.type == EvType.Log) hud.Log(e.text);
                    else if (e.type == EvType.Gear) Regear(e.a);
                    else gearPending.Add(e);
                }
            }
            ShowGearPanel();
        }

        // 裝備變了：重建模型，並把行動者腳下的金圈接回去
        void Regear(Unit u)
        {
            RebuildView(u);
            if (!preparing && battle.active == u)
            {
                activeRing.SetParent(views[u.id].root, false);
                activeRing.localPosition = new Vector3(0, 0.05f, 0);
            }
        }

        void CloseGear()
        {
            if (!gearOpen) return;
            gearOpen = false;
            gearUnit = null;
            hud.HideGear();
            if (preparing) { gearPending.Clear(); battle.Drain(); EditHero(editHero); return; }
            var evs = battle.Drain();
            evs.InsertRange(0, gearPending);
            gearPending.Clear();
            StartCoroutine(Play(evs));
        }

        // ───────── 地圖物件與對話 ─────────

        /// <summary>這一格上玩家看得到的物件（還沒被發現的絆索不算）。</summary>
        Prop PropAt(int cell)
        {
            if (cell < 0) return null;
            var p = battle.PropAt(cell);
            return p != null && (p.kind != PropKind.Trap || p.found) ? p : null;
        }

        void TryInteract(Prop p)
        {
            var u = battle.active;
            string why = battle.WhyNot(u, p);
            if (why != null) { Toast(Vis.Cell(world, p.cell), why); return; }
            sfx.Click();
            if (p.kind == PropKind.Npc) { OpenDialog(p, null); return; }
            if (!battle.Interact(u, p)) return;
            if (p.kind == PropKind.Building) { OpenShop(p, null); return; }
            StartCoroutine(Play(battle.Drain()));
            if (p.kind == PropKind.Notice)
            {
                // 告示牌：把上面寫的字攤開來看
                sheet = Sheet.Message;
                hud.ShowSheet(p.name, battle.NoticeText(p), new[] { "<b>看完了</b>" }, null, _ => CloseSheet());
            }
        }

        /// <summary>F 鍵：與身邊第一個能互動的物件互動。</summary>
        void InteractNearest()
        {
            var u = battle.active;
            foreach (var p in battle.props)
            {
                if (p.kind == PropKind.Grass || PropAt(p.cell) != p || battle.WhyNot(u, p) != null) continue;
                TryInteract(p);
                return;
            }
            Toast(views[u.id].root.position, "身邊沒有可以互動的東西");
        }

        /// <summary>打開（或更新）與路人的對話框。reply 是他剛說的話，null 表示剛開口。</summary>
        void OpenDialog(Prop npc, string reply)
        {
            var u = battle.active;
            talking = true;
            talkNpc = npc;
            sel = null;
            hud.HideTip();
            propViews.Face(npc, views[u.id].root.position);
            views[u.id].FaceTo(propViews.Position(npc));

            var labels = new string[4];
            var enabled = new bool[4];
            int dc = battle.TalkDc(npc);
            for (int i = 0; i < 3; i++)
            {
                var way = (TalkWay)i;
                int pct = Mathf.RoundToInt(Battle.Odds(battle.TalkBonus(u, way), dc) * 100);
                enabled[i] = battle.CanTalk(u, npc, way);
                labels[i] = "<b>" + Prop.WayNames[i] + "</b>\n<size=13>" + (enabled[i] ? "成功率 " + pct + "%" : "已試過或無話可說") + "</size>";
            }
            labels[3] = "<b>離開</b>";
            enabled[3] = true;

            string body = reply ?? "一個背著柴捆的老人縮在樹後，緊張地打量著" + u.name + "。\n「幾、幾位好漢……有何貴幹？」";
            hud.ShowDialog(npc.name + "  <size=16><color=#A89C85>" + Prop.AttitudeNames[npc.attitude] + "</color></size>", body, labels, enabled, OnDialogPick);
        }

        /// <summary>打開（或更新）店家的對話框：最多三項買賣或服務，加上「離開」。</summary>
        void OpenShop(Prop shop, string reply)
        {
            var u = battle.active;
            talking = true;
            talkNpc = shop;
            sel = null;
            hud.HideTip();
            var offers = battle.Offers(u, shop);
            var labels = new string[4];
            var enabled = new bool[4];
            bool store = battle.Goods(shop).Count > 0;
            for (int i = 0; i < 3; i++)
            {
                if (i == 2 && store) { labels[i] = "<b>買　賣</b>\n<size=13>看看貨，或把用不著的賣掉</size>"; enabled[i] = true; continue; }
                if (i >= offers.Count || i == 2) { labels[i] = ""; enabled[i] = false; continue; }
                var o = offers[i];
                string note = o.why ?? o.note ?? "";
                if (note.Length > 20) note = note.Substring(0, 19) + "…";
                enabled[i] = o.why == null;
                labels[i] = "<b>" + o.label + "</b>" + (o.price > 0 ? "　" + o.price + " 兩" : "") + "\n<size=13>" + note + "</size>";
            }
            labels[3] = "<b>離開</b>";
            enabled[3] = true;
            hud.ShowDialog(shop.name + "  <size=16><color=#A89C85>" + Prop.ShopNames[(int)shop.shop] + "　銀兩 " + battle.silver + "</color></size>",
                reply ?? battle.ShopHello(shop), labels, enabled, OnDialogPick);
        }

        void OnDialogPick(int i)
        {
            if (!talking || talkNpc == null) return;
            sfx.Click();
            if (talkNpc.kind == PropKind.Building)
            {
                var offers = battle.Offers(battle.active, talkNpc);
                if (i == 2 && battle.Goods(talkNpc).Count > 0)
                {
                    hud.HideDialog();
                    OpenList(talkNpc);
                    return;
                }
                if (i < 2 && i < offers.Count)
                {
                    string said = battle.Trade(battle.active, talkNpc, offers[i].id);
                    if (said != null) { OpenShop(talkNpc, said); return; }
                }
                CloseDialog();
                return;
            }
            if (i < 3)
            {
                string reply = battle.Talk(battle.active, talkNpc, (TalkWay)i);
                if (reply != null)
                {
                    // 檢定的算式直接顯示在對話框裡；戰報與其餘事件等對話結束再播
                    OpenDialog(talkNpc, "<size=15>" + battle.lastCheck + "</size>\n" + reply);
                    return;
                }
            }
            CloseDialog();
        }

        void CloseDialog()
        {
            talking = false;
            talkNpc = null;
            hud.HideDialog();
            StartCoroutine(Play(battle.Drain()));
        }

        // ───────── 行李整理與商店 ─────────

        bool listOpen;
        Prop listShop;                 // 正在逛的店；null 表示在整理行李
        int listMode;                  // 商店：0 買、1 賣
        BagFilter listFilter;
        BagSort listSort;
        int listPage, listSel = -1;
        readonly List<BagEntry> listBag = new List<BagEntry>();
        readonly List<Good> listGoods = new List<Good>();
        const int ListRows = 11;

        /// <summary>B 鍵：開關行李整理。</summary>
        void ToggleBag()
        {
            if (listOpen) { if (listShop == null) CloseList(); return; }
            if (playing || talking || gearOpen || preparing || battle.over != 0 || sheet != Sheet.None) return;
            var u = battle.active;
            if (u == null || u.team != 0) return;
            sfx.Click();
            OpenList(null);
        }

        /// <summary>打開清單面板：shop 為 null 是整理行李，否則是那家店的買賣。</summary>
        void OpenList(Prop shop)
        {
            listOpen = true;
            listShop = shop;
            listMode = 0;
            listFilter = BagFilter.All;
            listPage = 0;
            listSel = -1;
            sel = null;
            aimItem = null;
            hud.HideTip();
            ShowList();
        }

        static string GearLine(Gear g)
        {
            return (g.Named ? "<b><color=#FFD36A>★" + g.name + "</color></b>" : "<b>" + g.name + "</b>")
                + "　<size=13><color=#C9BFA8>" + Gears.SlotNames[(int)g.slot] + "・" + Gears.Summary(g) + "</color></size>";
        }

        static string GearDetail(Gear g)
        {
            return g.name + "（" + Gears.SlotNames[(int)g.slot] + "）：" + Gears.Summary(g) + (g.desc.Length > 0 ? "。" + g.desc : "")
                + (g.Named ? "\n<color=#FFD36A>★" + Gears.FxText[(int)g.fx] + "</color>" : g.IsWeapon ? "\n" + Gears.Info(g.cls).name + "：" + Gears.Info(g.cls).note : "");
        }

        void ShowList()
        {
            var u = battle.active;
            bool shop = listShop != null, buying = shop && listMode == 0;
            listBag.Clear();
            listGoods.Clear();
            if (buying)
            {
                // 「取得順序」在這裡就是店家擺貨的順序
                var all = battle.Goods(listShop);
                var keep = new List<Good>();
                foreach (var g in all)
                    if (listFilter == BagFilter.All || (g.gear != null ? Battle.FilterOf(g.gear) : BagFilter.Item) == listFilter) keep.Add(g);
                IEnumerable<Good> sorted = keep;
                if (listSort == BagSort.Name) sorted = System.Linq.Enumerable.OrderBy(keep, g => g.Name, System.StringComparer.Ordinal);
                else if (listSort == BagSort.Type)
                    sorted = System.Linq.Enumerable.ThenBy(System.Linq.Enumerable.OrderBy(keep, g => g.gear != null ? (int)g.gear.slot * 100 + (int)g.gear.cls : 10000 + (int)g.item.kind), g => g.Name, System.StringComparer.Ordinal);
                listGoods.AddRange(sorted);
            }
            else listBag.AddRange(battle.BagEntries(listFilter, listSort, shop ? u : null)); // 賣東西只賣自己行囊裡的道具

            int count = buying ? listGoods.Count : listBag.Count;
            int pages = Mathf.Max(1, (count + ListRows - 1) / ListRows);
            listPage = Mathf.Clamp(listPage, 0, pages - 1);
            if (listSel >= count) listSel = -1;

            int n = Mathf.Min(ListRows, count - listPage * ListRows);
            var rows = new string[n];
            var enabled = new bool[n];
            var icons = new Texture[n];
            for (int i = 0; i < n; i++)
            {
                int k = listPage * ListRows + i;
                if (buying)
                {
                    var g = listGoods[k];
                    string why = battle.WhyNotBuy(u, listShop, g);
                    rows[i] = (g.gear != null ? GearLine(g.gear) : "<b>" + g.item.name + "</b>　<size=13><color=#C9BFA8>道具</color></size>")
                        + "　<color=#FFD36A>" + g.price + " 兩</color>" + (why != null ? "　<size=13><color=#F0958A>" + why + "</color></size>" : "");
                    enabled[i] = true;
                    icons[i] = g.gear != null ? Icons.Of(g.gear) : Icons.Of(g.item);
                }
                else
                {
                    var e = listBag[k];
                    rows[i] = e.gear != null ? GearLine(e.gear)
                            : "<b>" + e.item.name + "</b> ×" + e.n + "　<size=13><color=#C9BFA8>道具" + (shop ? "" : "・" + e.holder.name + " 的行囊") + "</color></size>";
                    if (shop) rows[i] += "　<color=#FFD36A>賣 " + (e.gear != null ? Battle.SellPrice(e.gear) : Battle.SellPrice(e.item)) + " 兩</color>";
                    enabled[i] = true;
                    icons[i] = e.gear != null ? Icons.Of(e.gear) : Icons.Of(e.item);
                }
            }

            string detail;
            if (listSel >= 0)
            {
                Gear dg = buying ? listGoods[listSel].gear : listBag[listSel].gear;
                Item di = buying ? listGoods[listSel].item : listBag[listSel].item;
                detail = dg != null ? GearDetail(dg) : di.name + "：" + di.desc;
                if (buying)
                {
                    string why = battle.WhyNotBuy(u, listShop, listGoods[listSel]);
                    detail += why != null ? "\n<color=#F0958A>" + why + "</color>" : "\n<color=#9FE0C0>再點一次買下（" + listGoods[listSel].price + " 兩）。" + (dg != null ? "裝備會收進共用行李。" : "道具會收進 " + u.name + " 的行囊。") + "</color>";
                }
                else if (shop) detail += "\n<color=#9FE0C0>再點一次賣出（得 " + (dg != null ? Battle.SellPrice(dg) : Battle.SellPrice(di)) + " 兩）。</color>";
            }
            else detail = buying ? "點一下看說明，再點一次買下。"
                        : shop ? "這裡列的是共用行李裡的備用裝備，以及 " + u.name + " 行囊裡的道具。點一下看說明，再點一次賣出（半價）。\n身上穿戴的要先卸下才能賣。"
                        : "共用行李裡的備用裝備，以及三人行囊裡的道具。上方可以依種類篩選、換排序方式；換裝時的清單也會照這個排序。\n要換裝請按 I。";

            hud.ShowList(shop ? listShop.name + "  <size=16><color=#A89C85>" + Prop.ShopNames[(int)listShop.shop] + "</color></size>" : "行　李",
                "銀兩 " + battle.silver + " 兩", shop ? new[] { "<b>買</b>", "<b>賣</b>" } : null, listMode,
                Battle.FilterNames, (int)listFilter, Battle.SortNames, (int)listSort,
                rows, enabled, icons, listSel >= 0 && listSel / ListRows == listPage ? listSel % ListRows : -1,
                (count == 0 ? "沒有東西　" : "共 " + count + " 件　") + (listPage + 1) + " / " + pages, listPage > 0, listPage < pages - 1,
                detail, shop ? "<b>返回</b>" : "<b>關閉</b> <size=13>B</size>", OnListPick);
        }

        void OnListPick(int kind, int i)
        {
            if (!listOpen) return;
            sfx.Click();
            var u = battle.active;
            switch (kind)
            {
                case 0: listMode = i; listPage = 0; listSel = -1; break;
                case 1: listFilter = (BagFilter)i; listPage = 0; listSel = -1; break;
                case 2: listSort = (BagSort)i; listPage = 0; listSel = -1; break;
                case 4: listPage--; break;
                case 5: listPage++; break;
                case 6: CloseList(); return;
                case 3:
                {
                    int k = listPage * ListRows + i;
                    if (k != listSel || listShop == null) { listSel = k; break; } // 第一下：看說明
                    bool done;
                    if (listMode == 0) done = k < listGoods.Count && battle.Buy(u, listShop, listGoods[k]);
                    else
                    {
                        done = k < listBag.Count && (listBag[k].gear != null ? battle.SellGear(u, listShop, listBag[k].gear) : battle.SellItem(u, listShop, listBag[k].item));
                        if (done) listSel = -1; // 賣掉之後下一件會補上來，別讓連點把它也賣了
                    }
                    if (done) { sfx.Buff(); foreach (var e in battle.Drain()) if (e.type == EvType.Log) hud.Log(e.text); else gearPending.Add(e); }
                    break;
                }
            }
            ShowList();
        }

        void CloseList()
        {
            if (!listOpen) return;
            listOpen = false;
            hud.HideList();
            var shop = listShop;
            listShop = null;
            if (shop != null) { OpenShop(shop, null); return; } // 回到店家的對話框
            hoverDirty = true;
        }

        /// <summary>WASD：依鏡頭方向讓角色走一格；按住會連續走。</summary>
        void KeyboardMove()
        {
            int dx, dy;
            if (!ReadMoveKeys(out dx, out dy)) { nextStepAt = 0; return; }
            if (Time.unscaledTime < nextStepAt) return;

            var u = battle.active;
            if (sel != null || aimItem != null) { sel = null; aimItem = null; Refresh(); }

            // 按住 Alt：往這個方向縱躍（能跳多遠跳多遠）
            if (JumpHeld)
            {
                if (battle.Leap(u, dx, dy)) { nextStepAt = Time.unscaledTime + 0.25f; StartCoroutine(Play(battle.Drain())); }
                else
                {
                    nextStepAt = Time.unscaledTime + 0.5f;
                    Toast(views[u.id].root.position, Pathfinder.LeapRange(u.Jump) < 2 ? "輕功不足，跳不起來" : u.moveLeft < 3 ? "移動力不夠跳" : "這個方向跳不過去");
                }
                return;
            }
            int best = StepCell(u, dx, dy);

            if (best >= 0 && battle.Move(u, best))
            {
                nextStepAt = 0; // 之後的每一步由 Play 裡的 ChainStep 接續，不必等
                StartCoroutine(Play(battle.Drain()));
            }
            else
            {
                nextStepAt = Time.unscaledTime + 0.5f;
                Toast(views[u.id].root.position, u.moveLeft <= 0 ? "移動力已用盡" : "此路不通");
            }
        }

        /// <summary>原地轉向游標所指的格子（不耗行動），視野隨之改變。</summary>
        void FaceHover()
        {
            var u = battle.active;
            if (hover == battle.CellOf(u)) return;
            battle.Face(u, world.X(hover), world.Y(hover));
            views[u.id].root.rotation = Quaternion.LookRotation(new Vector3(u.fx, 0, u.fy));
            sfx.Click();
            UpdateFog();
            RedrawMarkers();
        }

        void Toast(Vector3 world, string text)
        {
            sfx.Miss();
            StartCoroutine(hud.Float(cam, world + Vector3.up * 0.9f, text, new Color(1f, 0.62f, 0.52f), 24));
        }

        void EndTurn()
        {
            if (!MyTurn) return;
            aimItem = null;
            battle.EndTurn();
            StartCoroutine(Play(battle.Drain()));
        }

        /// <summary>事件播完或選取改變後，重新整理可操作狀態。</summary>
        void Refresh()
        {
            if (MyTurn)
            {
                var u = battle.active;
                if (sel != null && !battle.CanUse(u, sel)) sel = null;
                battle.ComputeReach(u);
                reachSet.Clear();
                foreach (int c in battle.pf.reach) reachSet.Add(c);
                hud.ShowUnit(u, u.hp, true);
                foreach (var x in battle.units) hud.SetQi(x, x.qi);
                hud.SetActions(u, battle, sel);
                hud.SetHint(aimItem != null ? "「" + aimItem.name + "」：點選" + (aimItem.aim == Aim.Cell ? "落點" : aimItem.aim == Aim.Foe ? "敵人" : "自己或身邊的隊友") + "（Esc 取消）"
                    : battle.exploring && sel == null ? "自由行動：WASD 走 · Shift 跑（易被發現）· Alt＋方向 跳 · F 互動 · Tab 換人 · 空白鍵 暫停"
                    : sel == null ? "Alt＋方向縱躍 · WASD 或點格子移動 · 點遠處自動尋路 · 右鍵點地面轉向"
                    : sel.kind == SkillKind.Aoe ? "「" + sel.name + "」：點選落點（Esc 取消）"
                    : "「" + sel.name + "」：點選紅色標記的目標（Esc 取消）");
            }
            else
            {
                reachSet.Clear();
                hud.SetActions(null, battle, null);
                hud.SetHint("");
            }
            hoverDirty = true;
            if (!preparing) CheckQuests();
            farKey = -1; // 場上的佔位可能變了，全圖尋路要重算
            if (hover < -1) hover = -1;
            RedrawMarkers(); // 直接重畫，不等游標移動
            UpdateTip();
        }

        void RedrawMarkers()
        {
            markers.Clear();
            if (MyTurn)
            {
                var u = battle.active;
                if (aimItem != null) AimMarkers(u);
                else if (sel == null)
                {
                    if (!battle.exploring) foreach (int c in battle.pf.reach) markers.Add(c, CMove); // 自由行動不畫移動範圍
                    farCost = -1;
                    if (hover >= 0 && reachSet.Contains(hover))
                        foreach (int c in battle.pf.Path(hover)) markers.Add(c, CPath, 0.02f);
                    else if (hover >= 0 && world.occ[hover] < 0)
                    {
                        // 遠處：預覽自動尋路的路線。本回合走得到的一段是金色，之後的是淺色
                        // 全圖尋路只跟「人在哪」有關，跟游標指哪無關：同一個位置只算一次，游標移動時直接查
                        int key = battle.CellOf(u) * 16 + u.Jump;
                        if (farKey != key) { pfThreat.Flood(battle.CellOf(u), 120, u.Jump, u.team, battle.units, u.Climb); farKey = key; }
                        if (pfThreat.Seen(hover))
                        {
                            farCost = pfThreat.CostOf(hover);
                            foreach (int c in pfThreat.Path(hover))
                                markers.Add(c, pfThreat.CostOf(c) <= u.moveLeft ? CPath : CFar, 0.02f);
                        }
                    }

                    // 現在就能互動的物件：金色標記
                    foreach (var p in battle.props)
                        if (p.kind != PropKind.Grass && PropAt(p.cell) == p && battle.WhyNot(u, p) == null) markers.Add(p.cell, CProp, 0.02f);

                    // 游標指向敵人：標出它一回合走得到的範圍
                    var foe = UnitAt(hover);
                    if (foe != null && foe.team != u.team)
                    {
                        pfThreat.Flood(hover, foe.move, foe.Jump, foe.team, battle.units, foe.Climb);
                        farKey = -1; // 同一份搜尋結果被敵人的範圍蓋掉了
                        foreach (int c in pfThreat.reach) markers.Add(c, CThreat, 0.015f);
                    }
                }
                else if (sel.kind == SkillKind.Attack || sel.kind == SkillKind.Shout)
                {
                    foreach (var t in battle.units)
                    {
                        if (!t.alive || t.team == u.team) continue;
                        if (!Visible(t)) continue;
                        if (!battle.CanTarget(u, sel, t)) continue;
                        int c = battle.CellOf(t);
                        markers.Add(c, c == hover ? CTargetHot : CTarget);
                    }
                }
                else if (sel.kind == SkillKind.Aoe && battle.CanAim(u, sel, hover))
                {
                    int cx = world.X(hover), cy = world.Y(hover), cz = world.Z(hover);
                    for (int dy = -sel.radius; dy <= sel.radius; dy++)
                        for (int dx = -sel.radius; dx <= sel.radius; dx++)
                            for (int z = cz - 2; z <= cz + 2; z++)
                                if (world.Standable(cx + dx, cy + dy, z)) markers.Add(world.Idx(cx + dx, cy + dy, z), CBlast);
                }
            }
            if (hover >= 0) markers.Add(hover, CCursor, 0.03f);
            markers.Commit();
        }

        void UpdateTip()
        {
            if (hover < 0) { hud.HideTip(); return; }
            var t = UnitAt(hover);
            var sb = tipSb;
            sb.Length = 0;

            if (aimItem != null && MyTurn)
            {
                // 拿著道具瞄準：說明與這一格能不能用
                string why = battle.WhyNotUse(battle.active, aimItem, t, hover);
                sb.Append("<b>").Append(aimItem.name).Append("</b>\n").Append(aimItem.desc);
                if (t != null) sb.Append("\n對象：").Append(t.name);
                sb.Append(why == null ? "\n<color=#FFE08A>點擊使用</color>" : "\n<color=#F0958A>" + why + "</color>");
                hud.ShowTip(sb.ToString());
                return;
            }

            if (t == null)
            {
                // 空格：高度差、移動消耗、範圍招能否施放
                if (!MyTurn) { hud.HideTip(); return; }
                var u = battle.active;

                // 地圖物件：名稱、說明、現在能不能互動
                var prop = PropAt(hover);
                if (prop != null && prop.kind != PropKind.Grass && sel == null)
                {
                    sb.Append("<b>").Append(prop.name).Append("</b>\n").Append(battle.Describe(u, prop));
                    string why = battle.WhyNot(u, prop);
                    sb.Append(why == null ? "\n<color=#FFE08A>點擊或按 F 互動</color>" : "\n<color=#F0958A>" + why + "</color>");
                    hud.ShowTip(sb.ToString());
                    return;
                }
                if (prop != null && prop.kind == PropKind.Grass)
                    sb.Append("<b>").Append(prop.name).Append("</b>\n<color=#9FE0C0>").Append(battle.Describe(u, prop)).Append("</color>\n");

                int hz = world.Z(hover), dz = hz - u.z;
                sb.Append("高度 ").Append(hz);
                if (dz != 0) sb.Append("（").Append(dz > 0 ? "+" : "").Append(dz).Append("）");
                if (sel == null)
                {
                    if (reachSet.Contains(hover))
                    {
                        sb.Append("\n移動消耗 <b>").Append(battle.pf.CostOf(hover)).Append("</b> / ").Append(u.moveLeft);
                        // 這條路會不會挨借機攻擊
                        tipFoes.Clear();
                        var route = battle.pf.Path(hover);
                        for (int i = 1; i < route.Length; i++)
                            if (!battle.pf.Adjacent(route[i - 1], route[i])) { sb.Append("\n<color=#9FE0C0>途中施展輕功縱躍</color>"); break; }
                        if (battle.Provokers(u, route, tipFoes) > 0)
                        {
                            sb.Append("\n<color=#F0958A>抽身會遭借機攻擊：");
                            for (int i = 0; i < tipFoes.Count; i++) sb.Append(i > 0 ? "、" : "").Append(tipFoes[i].name);
                            sb.Append("</color>");
                        }
                    }
                    else if (farCost >= 0)
                    {
                        int turns = Mathf.CeilToInt((farCost - u.moveLeft) / (float)Mathf.Max(1, u.move)) + 1;
                        sb.Append("\n全程需 <b>").Append(farCost).Append("</b> 點移動，約 ").Append(turns).Append(" 回合\n<color=#A89C85>點擊：先走到本回合最遠處</color>");
                    }
                    else sb.Append("\n<color=#F0958A>無法抵達</color>");
                }
                else if (sel.kind == SkillKind.Aoe)
                {
                    sb.Append(battle.CanAim(u, sel, hover) ? "\n<color=#FFE08A>可施放</color>" : "\n<color=#9A9A9A>打不到（距離或視線）</color>");
                }
                hud.ShowTip(sb.ToString());
                return;
            }

            sb.Append("<b>").Append(t.name).Append("</b>  <color=#A89C85>").Append(t.cls).Append("</color>\n");
            sb.Append("氣血 ").Append(views[t.id].shownHp).Append('/').Append(t.maxHp);
            sb.Append("　防禦 ").Append(t.Ac).Append("　高度 ").Append(t.z);
            if (!string.IsNullOrEmpty(tags[t.id])) sb.Append("\n<color=#C9A0E8>").Append(tags[t.id]).Append("</color>");
            // 潛行與警覺：我方看自己藏得如何，看敵人有多警覺
            if (t.team == 0)
                sb.Append(t.knownCell >= 0 ? "\n<color=#F0958A>行蹤已敗露</color>" : "\n<color=#9FE0C0>行蹤未露</color>　本回合潛行 " + t.stealth);
            else
                sb.Append("\n警覺 ").Append(battle.Perception(t)).Append(battle.OnAlert(t.team) ? "（已有戒備）" : "（尚未察覺）");

            if (MyTurn && t.team != battle.active.team)
            {
                var u = battle.active;
                if (sel != null && sel.kind == SkillKind.Attack)
                {
                    if (battle.CanHit(sel, u.x, u.y, u.z, t.x, t.y, t.z))
                    {
                        int m = u.Mod(sel.ability);
                        int lo = Mathf.Max(1, sel.dmg.n + sel.dmg.plus + m);
                        int hi = Mathf.Max(1, sel.dmg.n * sel.dmg.sides + sel.dmg.plus + m);
                        sb.Append("\n<color=#FFE08A>命中率 ").Append(Mathf.RoundToInt(battle.ChanceVs(u, sel, t) * 100)).Append("%</color>");
                        sb.Append("　傷害 ").Append(lo).Append("～").Append(hi);
                        if (sel.hits > 1) sb.Append(" ×").Append(sel.hits);
                        string why = battle.AdvReasons(u, sel, u.x, u.y, u.z, t);
                        if (why.Length > 0) sb.Append("\n<color=#9FE0C0>").Append(why).Append("</color>");
                    }
                    else sb.Append("\n<color=#9A9A9A>打不到（距離或視線）</color>");
                }
                else if (sel != null && sel.kind == SkillKind.Shout)
                {
                    if (battle.CanTarget(u, sel, t))
                    {
                        int dc = battle.ShoutDc(t);
                        sb.Append("\n<color=#FFE08A>喝令成功率 ").Append(Mathf.RoundToInt(Battle.Odds(battle.ShoutBonus(u), dc) * 100)).Append("%</color>（DC ").Append(dc).Append("）");
                        sb.Append(battle.WouldYield(t) ? "\n<color=#9FE0C0>他已負傷過半：成功就會投降</color>" : "\n成功：膽寒（攻擊劣勢）");
                    }
                    else sb.Append((t.defied & (1 << u.id)) != 0 ? "\n<color=#9A9A9A>你的喝令對他已經無效</color>" : "\n<color=#9A9A9A>太遠或看不到</color>");
                }
                else if (sel == null) sb.Append("\n<color=#F0958A>紅色範圍：它一回合走得到的地方</color>");
            }
            hud.ShowTip(sb.ToString());
        }

        // ───────── 事件播放 ─────────

        static readonly Color BannerBlue = new Color(0.62f, 0.82f, 1f), BannerRed = new Color(1f, 0.62f, 0.56f);

        IEnumerator Play(List<Ev> evs)
        {
            playing = true;
            sel = null;
            hud.SetActions(null, battle, null);
            hud.SetHint("");
            hud.HideTip();
            markers.Clear();
            markers.Commit();

            // 按住 WASD 時，一步走完直接接下一步，不回到待機狀態，走起來才連貫
            for (bool more = true; more; more = ChainStep(out evs))
            foreach (var e in evs)
            {
                // 走完路之後若接著別的演出，先把走路的人收回站姿
                if (walker != null && e.type != EvType.Log && e.type != EvType.Move) { walker.PoseIdle(); walker = null; }

                switch (e.type)
                {
                    case EvType.Log:
                        hud.Log(e.text);
                        break;

                    case EvType.TurnStart:
                    {
                        var v = views[e.a.id];
                        // 金鐘罩、梯雲縱在自己的回合開始時結束；還在身上的狀態（中毒、流血等）由規則層給
                        tags[e.a.id] = string.IsNullOrEmpty(e.text) ? null : e.text;
                        hud.SetTags(e.a, tags[e.a.id]);
                        if (e.n == 0) hud.SetExplore(true, e.a.name); // 自由行動中換人
                        else { hud.SetExplore(false, null); hud.SetTurn(battle, e.a, e.n, IsShown); }
                        bool visible = Visible(e.a);
                        if (visible) hud.ShowUnit(e.a, v.shownHp, false);
                        activeRing.gameObject.SetActive(true);
                        activeRing.SetParent(v.root, false);
                        activeRing.localPosition = new Vector3(0, 0.05f, 0);
                        if (visible) rig.Focus(v.root.position + Vector3.up);
                        coverFocus = visible ? e.a : null;
                        UpdateCover();
                        UpdateFog();
                        if (e.n == 0) sfx.Click();
                        else if (e.a.team == 0)
                        {
                            StartCoroutine(hud.Banner(e.a.name + " 的回合", BannerBlue));
                            sfx.Turn();
                        }
                        else if (lastTeam != 1) StartCoroutine(hud.Banner("敵方行動", BannerRed));
                        lastTeam = e.a.team;
                        yield return new WaitForSeconds(0.2f);
                        break;
                    }

                    case EvType.Move:
                        // 迷霧裡的敵人快轉帶過，也不把鏡頭帶過去
                        walker = views[e.a.id];
                        yield return AnimWalk(walker, e.path, !Visible(e.a) ? 0.03f : e.a.team == 0 && e.a.ran ? 0.095f : 0.16f, Visible(e.a));
                        break;

                    case EvType.Push:
                        views[e.b.id].PoseHurt(0.9f); // 被震飛時維持後仰
                        yield return AnimPath(views[e.b.id], e.path, 0.07f, false);
                        break;

                    case EvType.Attack:
                        // 擲骰結果飄在出手者頭上；擲出 20 用金色
                        Float(views[e.a.id], e.text, e.flag ? new Color(1f, 0.83f, 0.3f) : new Color(0.86f, 0.90f, 0.96f), 19, 1.8f);
                        // 出手的人會暴露位置
                        if (!views[e.a.id].dead) views[e.a.id].root.gameObject.SetActive(true);
                        sfx.Swing();
                        yield return AnimAttack(views[e.a.id], views[e.b.id], e.skill);
                        break;

                    case EvType.Miss:
                        sfx.Miss();
                        Float(views[e.b.id], "閃避", new Color(0.82f, 0.82f, 0.82f), 22);
                        yield return new WaitForSeconds(0.3f);
                        break;

                    case EvType.Damage:
                    {
                        var v = views[e.b.id];
                        v.shownHp = e.hp;
                        hud.SetBar(e.b, e.hp);
                        Float(v, e.flag ? "會心 " + e.n : "-" + e.n, e.flag ? new Color(1f, 0.83f, 0.3f) : new Color(1f, 0.42f, 0.36f), e.flag ? 34 : 26);
                        sfx.Hit(e.flag);
                        rig.Shake(e.flag ? 0.32f : 0.1f);
                        yield return Hurt(v);
                        yield return new WaitForSeconds(0.18f);
                        break;
                    }

                    case EvType.Heal:
                    {
                        var v = views[e.a.id];
                        v.shownHp = e.hp;
                        hud.SetBar(e.a, e.hp);
                        sfx.Heal();
                        Float(v, "+" + e.n, new Color(0.5f, 0.92f, 0.56f), 26);
                        yield return new WaitForSeconds(0.35f);
                        break;
                    }

                    case EvType.Buff:
                        if (e.text == "定身" || e.text == "措手不及" || e.text == "膽寒" || e.text == "目盲" || e.text == "纏繞" || e.text == "中毒" || e.text == "流血"
                            || e.text == Db.Guard.name || e.text == Db.Light.name)
                        {
                            tags[e.a.id] = string.IsNullOrEmpty(tags[e.a.id]) ? e.text : tags[e.a.id] + " " + e.text;
                            hud.SetTags(e.a, tags[e.a.id]);
                        }
                        sfx.Buff();
                        Float(views[e.a.id], e.text, new Color(0.9f, 0.82f, 0.5f), 22);
                        yield return new WaitForSeconds(0.3f);
                        break;

                    case EvType.Aoe:
                    {
                        var caster = views[e.a.id];
                        var from = caster.root.position + Vector3.up * UnitView.ChestHeight;
                        var to = Vis.Cell(world, e.cell);
                        caster.FaceTo(to);
                        for (float k = 0; k < 0.6f; k += Time.deltaTime / 0.30f) { caster.PoseAttack(k, false); yield return null; }
                        sfx.Swing();
                        if (e.item != null) yield return fx.Toss(propViews.ItemMesh(e.item), propViews.PropMaterial, from, to + Vector3.up * 0.2f, 0.45f);
                        else yield return fx.Shot(from, to + Vector3.up * 0.2f, 0.22f);
                        caster.PoseIdle();
                        yield return fx.Ring(to, e.n);
                        break;
                    }

                    case EvType.Death:
                    {
                        var v = views[e.b.id];
                        v.dead = true;
                        if (activeRing.parent == v.root)
                        {
                            activeRing.SetParent(transform, false);
                            activeRing.gameObject.SetActive(false);
                        }
                        hud.RemoveBar(e.b);
                        sfx.Fall();
                        // 先仰倒，再沉入地面
                        var rot0 = v.root.rotation;
                        for (float t = 0; t < 1; t += Time.deltaTime / 0.28f)
                        {
                            v.root.rotation = rot0 * Quaternion.Euler(-88 * t * t, 0, 0);
                            yield return null;
                        }
                        for (float t = 0; t < 1; t += Time.deltaTime / 0.3f)
                        {
                            v.root.localScale = Vector3.one * (1 - t);
                            yield return null;
                        }
                        v.root.gameObject.SetActive(false);
                        UpdateCover();
                        UpdateFog();
                        break;
                    }

                    case EvType.Face:
                        views[e.a.id].FaceTo(Vis.Cell(world, e.cell));
                        UpdateFog();
                        break;

                    case EvType.Alert:
                        // 我方被發現：被發現的人頭上飄字；發現者若在視野內，頭上冒紅色驚嘆號
                        Float(views[e.b.id], "行蹤敗露", new Color(1f, 0.55f, 0.35f), 24, 1.75f);
                        if (e.a != null) Float(views[e.a.id], "！", new Color(1f, 0.4f, 0.3f), 40, 1.5f);
                        sfx.Miss();
                        yield return new WaitForSeconds(0.35f);
                        break;

                    case EvType.Prop:
                        if (e.path != null && (e.prop.kind == PropKind.Boulder || e.prop.kind == PropKind.Barrel))
                        {
                            // 滾石：鏡頭跟到起點，一路滾過去
                            rig.Focus(Vis.Cell(world, e.path[0]) + Vector3.up);
                            sfx.Fall();
                            yield return propViews.Roll(e.prop, e.path, 0.11f);
                            rig.Shake(0.18f);
                        }
                        else
                        {
                            propViews.Sync(e.prop);
                            if (e.prop.kind == PropKind.Trap || (e.prop.kind == PropKind.Caltrops && e.text != null))
                            {
                                Vector3 at = Vis.Cell(world, e.prop.cell) + Vector3.up * 0.6f;
                                StartCoroutine(hud.Float(cam, at, e.text ?? "絆鈴索！", new Color(1f, 0.80f, 0.35f), 26));
                                if (e.prop.gone) sfx.Miss(); else sfx.Turn();
                                yield return new WaitForSeconds(0.35f);
                            }
                            else if (e.prop.kind == PropKind.Chest) { sfx.Buff(); yield return new WaitForSeconds(0.25f); }
                            else if (e.prop.kind == PropKind.Loot && e.text != null)
                            {
                                sfx.Buff();
                                StartCoroutine(hud.Float(cam, Vis.Cell(world, e.prop.cell) + Vector3.up * 0.8f, e.text, new Color(1f, 0.84f, 0.36f), 24));
                            }
                            else if (e.prop.kind == PropKind.Boulder && e.prop.gone) sfx.Fall();
                            else if (e.prop.kind >= PropKind.ArmorRack && e.prop.kind != PropKind.Building)
                            {
                                sfx.Buff();
                                if (e.text != null) StartCoroutine(hud.Float(cam, Vis.Cell(world, e.prop.cell) + Vector3.up * 0.9f, e.text, new Color(1f, 0.84f, 0.36f), 24));
                                yield return new WaitForSeconds(0.2f);
                            }
                        }
                        break;

                    case EvType.Flee:
                    {
                        // 投降或潰逃：飄字後轉身縮小消失
                        var v = views[e.b.id];
                        Float(v, e.text, new Color(0.86f, 0.70f, 0.38f), 30);
                        v.dead = true;
                        if (activeRing.parent == v.root)
                        {
                            activeRing.SetParent(transform, false);
                            activeRing.gameObject.SetActive(false);
                        }
                        hud.RemoveBar(e.b);
                        sfx.Miss();
                        if (v.root.gameObject.activeSelf)
                        {
                            var rot0 = v.root.rotation;
                            for (float k = 0; k < 1; k += Time.deltaTime / 0.45f)
                            {
                                v.root.rotation = rot0 * Quaternion.Euler(0, 180 * Mathf.Min(1, k * 2), 0);
                                v.root.localScale = Vector3.one * (1 - k * k);
                                yield return null;
                            }
                        }
                        v.root.gameObject.SetActive(false);
                        UpdateCover();
                        UpdateFog();
                        break;
                    }

                    case EvType.Gear:
                        Regear(e.a);
                        break;

                    case EvType.Blast:
                    {
                        // 爆炸：物件消失，一圈火光，鏡頭大震
                        if (e.prop != null) propViews.Sync(e.prop);
                        Vector3 at = Vis.Cell(world, e.cell);
                        rig.Focus(at + Vector3.up);
                        sfx.Fall();
                        rig.Shake(0.45f);
                        StartCoroutine(hud.Float(cam, at + Vector3.up * 0.8f, "轟！", new Color(1f, 0.62f, 0.25f), 44));
                        yield return fx.Ring(at, e.n);
                        break;
                    }

                    case EvType.Combat:
                        // 自由行動結束：遭遇戰
                        hud.SetExplore(false, null);
                        lastTeam = -1;
                        StartCoroutine(hud.Banner("遭遇戰！", BannerRed));
                        sfx.Fall();
                        yield return new WaitForSeconds(0.9f);
                        break;

                    case EvType.Throw:
                    {
                        // 把東西丟出去：一個拋物線飛行物
                        var thrower = views[e.a.id];
                        var to = Vis.Cell(world, e.cell);
                        thrower.FaceTo(to);
                        for (float k = 0; k < 0.6f; k += Time.deltaTime / 0.25f) { thrower.PoseAttack(k, false); yield return null; }
                        sfx.Swing();
                        if (e.item != null) yield return fx.Toss(propViews.ItemMesh(e.item), propViews.PropMaterial, thrower.root.position + Vector3.up * UnitView.ChestHeight, to + Vector3.up * 0.25f, 0.42f);
                        else yield return fx.Shot(thrower.root.position + Vector3.up * UnitView.ChestHeight, to + Vector3.up * 0.4f, 0.22f);
                        thrower.PoseIdle();
                        break;
                    }

                    case EvType.Smoke:
                        propViews.Smoke(e.n, e.path, e.flag);
                        if (e.flag) sfx.Miss();
                        UpdateFog();
                        break;

                    case EvType.Reveal:
                        // 有人指路：把那一帶的地形記進「已探索」
                        foreach (int i in e.path) shown.seen[i] = 1;
                        UpdateFog();
                        break;

                    case EvType.Wait:
                        yield return new WaitForSeconds(e.n / 1000f);
                        break;

                    case EvType.GameOver:
                        yield return new WaitForSeconds(0.5f);
                        if (e.flag) sfx.Heal(); else sfx.Fall();
                        hud.ShowOver(e.flag, battle.round);
                        break;
                }
            }

            if (walker != null) { walker.PoseIdle(); walker = null; }
            playing = false;
            UpdateFog();
            Refresh();
        }

        static bool JumpHeld { get { return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt); } }

        UnitView walker;   // 剛走完路、還維持著走路姿勢的人
        float walkPhase;   // 走路擺動的相位，連續行走時不歸零

        /// <summary>
        /// 事件播完時若玩家還按著方向鍵，就直接規劃下一步並回傳它的事件，讓行走不中斷。
        /// 只有「單純走了一步」才接續；途中發生別的事（被發現、挨打、踩到絆索）就停下來。
        /// </summary>
        bool ChainStep(out List<Ev> evs)
        {
            evs = null;
            var u = battle.active;
            if (talking || battle.over != 0 || u == null || u.team != 0 || battle.autoHeroes || !u.alive || u.moveLeft <= 0 || sel != null) return false;
            if (walker == null || walker.unit != u) return false;
            int dx, dy;
            if (!ReadMoveKeys(out dx, out dy)) return false;
            if (JumpHeld)
            {
                // 連續跳：每一下之間稍微停頓，免得一口氣把移動力跳光
                if (Time.unscaledTime < nextStepAt || !battle.Leap(u, dx, dy)) return false;
                nextStepAt = Time.unscaledTime + 0.25f;
            }
            else
            {
                battle.ComputeReach(u);
                int cell = StepCell(u, dx, dy);
                if (cell < 0 || !battle.Move(u, cell)) return false;
            }
            evs = battle.Drain();
            hud.ShowUnit(u, u.hp, true);
            return true;
        }

        /// <summary>讀 WASD（依鏡頭方向），收成八個格子方向之一。沒按回傳 false。</summary>
        bool ReadMoveKeys(out int dx, out int dy)
        {
            dx = dy = 0;
            Vector3 dir = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) dir += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) dir += Vector3.back;
            if (Input.GetKey(KeyCode.A)) dir += Vector3.left;
            if (Input.GetKey(KeyCode.D)) dir += Vector3.right;
            if (dir == Vector3.zero) return false;
            dir = (Quaternion.Euler(0, rig.yaw, 0) * dir).normalized;
            // 每 45 度一個方向
            dx = dir.x > 0.38f ? 1 : dir.x < -0.38f ? -1 : 0;
            dy = dir.z > 0.38f ? 1 : dir.z < -0.38f ? -1 : 0;
            return dx != 0 || dy != 0;
        }

        /// <summary>
        /// 往 (dx, dy) 走一步會到哪一格（需先 ComputeReach）。斜向走不通時沿著障礙滑行，
        /// 改走其中一個正向；都不行回傳 -1。
        /// </summary>
        int StepCell(Unit u, int dx, int dy)
        {
            int cell = ReachAt(u.x + dx, u.y + dy);
            if (cell < 0)
            {
                // 前面那格走不過去：跳得過去就自動縱躍到對面
                int cost;
                int land = battle.LeapCell(u, dx, dy, out cost);
                if (land >= 0 && battle.pf.CostOf(land) > 0 && battle.pf.CostOf(land) <= u.moveLeft) return land;
            }
            if (cell < 0 && dx != 0 && dy != 0)
            {
                cell = ReachAt(u.x + dx, u.y);
                if (cell < 0) cell = ReachAt(u.x, u.y + dy);
            }
            return cell;
        }

        // 這一直行裡，本回合走得到而且最省力的格子
        int ReachAt(int x, int y)
        {
            int best = -1, bestCost = int.MaxValue;
            foreach (int c in battle.pf.reach)
            {
                if (world.X(c) != x || world.Y(c) != y) continue;
                int cost = battle.pf.CostOf(c);
                if (cost < bestCost) { bestCost = cost; best = c; }
            }
            return best;
        }

        bool IsShown(Unit u) { return !views[u.id].dead; }

        void Float(UnitView v, string text, Color color, int size, float height = 1.45f)
        {
            if (!v.root.gameObject.activeSelf) return; // 迷霧裡的單位不飄字
            StartCoroutine(hud.Float(cam, v.root.position + Vector3.up * height, text, color, size));
        }

        // ───────── 迷霧與視野 ─────────

        /// <summary>這個單位目前是否顯示在畫面上（我方永遠顯示；敵方要在視野內）。</summary>
        bool Visible(Unit t) { return !fogOn || t.team == 0 || views[t.id].root.gameObject.activeSelf; }

        void SetFog(bool on)
        {
            fogOn = on;
            fog.SetEnabled(on);
            UpdateFog();
        }

        // 模型目前所在的格子與朝向
        void ViewPose(UnitView v, out int x, out int y, out int z, out int fx, out int fy)
        {
            Vector3 p = v.root.position, f = v.root.forward;
            x = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, world.W - 1);
            y = Mathf.Clamp(Mathf.FloorToInt(p.z), 0, world.D - 1);
            z = Mathf.RoundToInt(p.y / Vis.H);
            fx = Mathf.RoundToInt(f.x);
            fy = Mathf.RoundToInt(f.z);
        }

        /// <summary>重新計算我方的視野：更新迷霧貼圖，並決定每個敵人此刻要不要顯示。</summary>
        void UpdateFog()
        {
            if (!fogOn)
            {
                foreach (var v in views) if (!v.dead && !v.root.gameObject.activeSelf) v.root.gameObject.SetActive(true);
                return;
            }

            int x, y, z, fx, fy;
            shown.BeginUpdate();
            foreach (var v in views)
            {
                if (v.dead || v.unit.team != 0) continue;
                ViewPose(v, out x, out y, out z, out fx, out fy);
                shown.AddViewer(x, y, z, fx, fy);
            }
            fog.Apply(shown);

            foreach (var e in views)
            {
                if (e.dead || e.unit.team == 0) continue;
                int ex, ey, ez, efx, efy;
                ViewPose(e, out ex, out ey, out ez, out efx, out efy);
                bool seen = false;
                foreach (var v in views)
                {
                    if (v.dead || v.unit.team != 0) continue;
                    ViewPose(v, out x, out y, out z, out fx, out fy);
                    if (shown.CanSeeCell(x, y, z, fx, fy, ex, ey, ez)) { seen = true; break; }
                }
                if (e.root.gameObject.activeSelf != seen) e.root.gameObject.SetActive(seen);
                if (seen && !spotted[e.unit.id])
                {
                    // 第一次進入視野：戰報提示，頭上冒驚嘆號
                    spotted[e.unit.id] = true;
                    hud.Log("<color=#FFE08A>發現 " + Battle.N(e.unit) + "！</color>");
                    StartCoroutine(hud.Float(cam, e.root.position + Vector3.up * 1.5f, "！", new Color(1f, 0.85f, 0.3f), 40));
                }
            }
            hoverDirty = true;
        }

        // ───────── 掀頂 ─────────

        void SetCoverMode(int mode)
        {
            coverMode = mode;
            hud.Log("<color=#A89C85>遮蔽物顯示：" + CoverModeNames[mode] + "</color>");
            UpdateCover();
        }

        /// <summary>
        /// 依模式決定每個遮蔽結構要不要掀開。自動模式下，只掀開目前行動者頭頂那一個；
        /// 位置取自畫面上的模型（規則層的座標比動畫超前）。掀開的結構同時不可點選。
        /// </summary>
        void UpdateCover()
        {
            int focus = -1;
            if (coverMode == 0 && coverFocus != null && IsShown(coverFocus))
            {
                var p = views[coverFocus.id].root.position;
                focus = world.CoverAbove(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z), Mathf.RoundToInt(p.y / Vis.H));
            }

            bool changed = false;
            for (int s = 0; s < world.coverCount; s++)
            {
                bool hide = coverMode == 1 || (coverMode == 0 && s == focus);
                if (world.coverHidden[s] == hide) continue;
                world.coverHidden[s] = hide;
                terrain.SetHidden(s, hide);
                changed = true;
            }
            if (changed) hoverDirty = true; // 可點選的格子變了，重新拾取
        }

        // ───────── 動畫 ─────────

        /// <summary>
        /// 沿路徑移動。兩格都在地面且只差一層時貼著坡面走，其餘（上下橋面、屋頂、落差大）用跳的。
        /// walk 為 true 時播放走路姿勢與腳步聲；被擊退時為 false。
        /// </summary>
        // 第 i 段（path[i-1] → path[i]）是不是貼著地面走的緩坡；否則是攀爬、跳落或縱躍
        bool GroundSeg(int[] path, int i)
        {
            if (i < 1 || i >= path.Length) return false;
            Vector3 a = Vis.Cell(world, path[i - 1]), b = Vis.Cell(world, path[i]);
            return battle.pf.Adjacent(path[i - 1], path[i]) && Mathf.Abs(b.y - a.y) <= Vis.H + 0.01f
                && terrain.IsGroundCell(path[i - 1]) && terrain.IsGroundCell(path[i]);
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        /// <summary>
        /// 行走動畫。整條路徑一氣呵成：格與格之間不停頓（多出來的時間帶到下一段）、
        /// 轉彎走弧線並平滑轉身、腳步擺動依實際走的距離推進、鏡頭一路跟著。
        /// 攀爬、跳落與縱躍用拋物線。step 是走一格平地所需的秒數。
        /// </summary>
        IEnumerator AnimWalk(UnitView v, int[] path, float step, bool follow)
        {
            int n = path.Length;
            float carry = 0;
            for (int i = 1; i < n; i++)
            {
                Vector3 a = Vis.Cell(world, path[i - 1]), b = Vis.Cell(world, path[i]);
                float flat = new Vector2(b.x - a.x, b.z - a.z).magnitude, dh = Mathf.Abs(b.y - a.y);
                bool ground = GroundSeg(path, i);
                bool leap = !battle.pf.Adjacent(path[i - 1], path[i]);
                bool climb = !ground && !leap && dh >= Vis.H * 2.5f; // 高過輕功的壁面：貼著爬，不是跳
                float dur = ground ? step * flat * (1 + dh * 0.6f) : climb ? step * (1.2f + dh * 1.5f) : step * (1.1f + flat * 0.5f + dh * 0.5f);
                float arc = ground ? 0 : 0.20f + dh * 0.28f + (leap ? 0.20f * flat : 0);
                // 樣條的前後控制點：相鄰的一段也是平地才用它，否則沿本段方向延伸（保持直線）
                Vector3 p0 = GroundSeg(path, i - 1) ? Vis.Cell(world, path[i - 2]) : a - (b - a);
                Vector3 p3 = GroundSeg(path, i + 1) ? Vis.Cell(world, path[i + 1]) : b + (b - a);

                float t = carry / dur;
                for (; t < 1; t += Time.deltaTime / dur)
                {
                    Vector3 p;
                    if (ground) { p = CatmullRom(p0, a, b, p3, t); p.y = terrain.HeightAt(p.x, p.z); }
                    else if (climb)
                    {
                        // 往上：先貼著壁面升上去，最後才翻上崖頂；往下：先探出去，再貼著壁面降下
                        bool up = b.y > a.y;
                        float side = up ? 0.3f * Mathf.Clamp01(t / 0.8f) + 0.7f * Mathf.Clamp01((t - 0.8f) / 0.2f)
                                        : 0.7f * Mathf.Clamp01(t / 0.2f) + 0.3f * Mathf.Clamp01((t - 0.2f) / 0.8f);
                        float rise = up ? Mathf.Clamp01(t / 0.85f) : Mathf.Clamp01((t - 0.15f) / 0.85f);
                        p = new Vector3(Mathf.Lerp(a.x, b.x, side), Mathf.Lerp(a.y, b.y, rise), Mathf.Lerp(a.z, b.z, side));
                    }
                    else { p = Vector3.Lerp(a, b, t); p.y += Mathf.Sin(t * Mathf.PI) * arc; }

                    Vector3 d = p - v.root.position;
                    d.y = 0;
                    float moved = d.magnitude;
                    if (moved > 1e-5f)
                        v.root.rotation = Quaternion.RotateTowards(v.root.rotation, Quaternion.LookRotation(d), 720f * Time.deltaTime);
                    v.root.position = p;
                    // 腳步跟著實際走的距離；騰空時收著腿不擺動
                    if (ground) { walkPhase += moved * 4.6f; v.PoseWalk(walkPhase); }
                    else if (climb) { walkPhase += Time.deltaTime * 9f; v.PoseWalk(walkPhase); } // 手腳並用
                    else v.PoseWalk(1.35f);
                    if (follow) rig.Focus(p + Vector3.up);
                    yield return null;
                }
                carry = Mathf.Max(0, (t - 1) * dur); // 這一段多走的時間算進下一段，銜接處才不會頓一下
                sfx.Step();
                UpdateCover(); // 走進或走出樹下、亭內時即時掀頂／還原
                UpdateFog();   // 每走一步視野跟著推進
            }
            v.root.position = Vis.Cell(world, path[n - 1]);
        }

        IEnumerator AnimPath(UnitView v, int[] path, float step, bool walk)
        {
            float phase = 0;
            for (int i = 1; i < path.Length; i++)
            {
                Vector3 a = Vis.Cell(world, path[i - 1]), b = Vis.Cell(world, path[i]);
                float dh = Mathf.Abs(b.y - a.y);
                bool slope = dh <= Vis.H + 0.01f && terrain.IsGroundCell(path[i - 1]) && terrain.IsGroundCell(path[i]);
                float dur = slope ? step * (1 + dh) : step * (1.5f + dh * 0.5f);
                float arc = slope ? 0 : 0.22f + dh * 0.28f;
                v.FaceTo(b);
                for (float t = 0; t < 1; t += Time.deltaTime / dur)
                {
                    var p = Vector3.Lerp(a, b, t);
                    if (slope) p.y = terrain.HeightAt(p.x, p.z);
                    else p.y += Mathf.Sin(t * Mathf.PI) * arc;
                    v.root.position = p;
                    if (walk) { phase += Time.deltaTime * 15f; v.PoseWalk(phase); }
                    yield return null;
                }
                v.root.position = b;
                if (walk) sfx.Step();
                UpdateCover(); // 走進或走出樹下、亭內時即時掀頂／還原
                UpdateFog();   // 每走一步視野跟著推進
            }
            v.PoseIdle();
        }

        IEnumerator AnimAttack(UnitView a, UnitView b, Skill sk)
        {
            a.FaceTo(b.root.position);
            Vector3 home = a.root.position;
            if (sk.melee)
            {
                Vector3 dir = b.root.position - home;
                dir.y = 0;
                dir.Normalize();
                for (float t = 0; t < 1; t += Time.deltaTime / 0.36f)
                {
                    a.PoseAttack(t, true);
                    // 高舉蓄力時微微後收，揮下時前踏，最後收回
                    float f = t < 0.38f ? -0.10f * (t / 0.38f)
                        : Mathf.Lerp(-0.10f, 0.32f, Mathf.Clamp01((t - 0.38f) / 0.22f)) * (1 - Mathf.Clamp01((t - 0.72f) / 0.28f));
                    a.root.position = home + dir * f;
                    yield return null;
                }
                a.root.position = home;
            }
            else
            {
                // 引弓或甩手，出手後飛行物飛向目標
                for (float t = 0; t < 0.6f; t += Time.deltaTime / 0.30f) { a.PoseAttack(t, false); yield return null; }
                yield return fx.Shot(home + Vector3.up * UnitView.ChestHeight, b.root.position + Vector3.up * (UnitView.ChestHeight - 0.08f), 0.2f);
            }
            a.PoseIdle();
        }

        /// <summary>受擊：閃白、後仰並縮一下。</summary>
        static IEnumerator Hurt(UnitView v)
        {
            for (float t = 0; t < 1; t += Time.deltaTime / 0.20f)
            {
                float k = Mathf.Sin(t * Mathf.PI);
                v.root.localScale = Vector3.one * (1 - 0.12f * k);
                v.SetFlash(k);
                v.PoseHurt(k);
                yield return null;
            }
            v.root.localScale = Vector3.one;
            v.SetFlash(0);
            v.PoseIdle();
        }
    }
}
