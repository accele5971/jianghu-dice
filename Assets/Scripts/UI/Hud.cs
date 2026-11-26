using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Jianghu
{
    /// <summary>
    /// 介面：先攻列、角色面板、招式列、戰報、提示框、血條、飄字、結算畫面。
    /// 全部由程式建立，不需要預製物或場景設定。
    /// </summary>
    public sealed class Hud
    {
        public Action<int> onSkill;   // 點了第 i 個招式
        public Action onEnd, onAgain;

        static readonly Color Bg = new Color(0.09f, 0.075f, 0.06f, 0.86f);
        static readonly Color Ink = new Color(0.93f, 0.87f, 0.76f);
        static readonly Color Dim = new Color(0.66f, 0.61f, 0.52f);
        static readonly Color Gold = new Color(0.86f, 0.70f, 0.38f);
        static readonly Color BtnNormal = new Color(0.22f, 0.17f, 0.12f, 0.95f);
        static readonly Color BtnHover = new Color(0.34f, 0.26f, 0.17f, 0.98f);
        static readonly Color BtnSelected = new Color(0.56f, 0.20f, 0.14f, 0.98f);
        static readonly Color BtnDisabled = new Color(0.13f, 0.12f, 0.11f, 0.75f);

        const int BtnW = 150, BtnH = 62, Gap = 8;

        readonly Font font, kai; // 內文用黑體保持小字清楚；標題、名字、按鈕用楷體
        public Font BodyFont { get { return font; } }
        public Font KaiFont { get { return kai; } }
        public Transform Canvas { get { return canvas; } }
        readonly Transform canvas;
        readonly Text turnText, panelText, logText, hintText, tipText, overTitle, overText;
        readonly Text bannerText;
        int bannerGen;
        readonly RectTransform tipRoot, overRoot, barLayer, floatLayer, actionRoot;
        readonly List<Button> skillBtns = new List<Button>();
        readonly List<Text> skillLabels = new List<Text>();
        readonly Button endBtn;
        readonly List<string> lines = new List<string>();
        readonly List<Bar> bars = new List<Bar>();
        readonly Stack<Text> floatPool = new Stack<Text>();
        readonly StringBuilder sb = new StringBuilder(512);
        string defaultHint = "";

        sealed class Bar
        {
            public RectTransform root, fill, qi;
            public Text name;
            public bool shown = true;
        }

        public Hud(Transform parent)
        {
            font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft JhengHei UI", "Microsoft JhengHei", "PingFang TC", "Noto Sans CJK TC", "Arial Unicode MS", "Arial" }, 18);
            kai = Font.CreateDynamicFontFromOSFont(
                new[] { "DFKai-SB", "BiauKai", "KaiTi", "Kaiti TC", "STKaiti", "Microsoft JhengHei" }, 22);

            var cgo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(parent, false);
            cgo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            canvas = cgo.transform;

            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.transform.SetParent(parent, false);

            // 世界座標跟隨層（放最底，不擋介面）
            barLayer = Layer("Bars");
            floatLayer = Layer("Floats");

            // 先攻列
            var top = Panel("Turn", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(1000, 38));
            turnText = MakeText(top, 21, TextAnchor.MiddleCenter, Ink, true);
            Stretch(turnText.rectTransform, 8);

            // 戰報
            var log = Panel("Log", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -58), new Vector2(390, 350));
            log.gameObject.AddComponent<RectMask2D>();
            logText = MakeText(log, 15, TextAnchor.LowerLeft, Ink);
            Stretch(logText.rectTransform, 10);
            logText.lineSpacing = 1.15f;

            // 角色面板
            var panel = Panel("Unit", new Vector2(0, 0), new Vector2(0, 0), new Vector2(10, 10), new Vector2(300, 240));
            panelText = MakeText(panel, 16, TextAnchor.UpperLeft, Ink);
            Stretch(panelText.rectTransform, 12);
            panelText.lineSpacing = 1.2f;

            // 招式列
            actionRoot = MakeRect("Actions", canvas, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(10, BtnH));
            Text endLabel;
            endBtn = MakeButton(actionRoot, out endLabel);
            endLabel.text = "<b>結束回合</b>\n<size=13>空白鍵</size>";
            endText = endLabel;
            endBtn.onClick.AddListener(() => { if (onEnd != null) onEnd(); });

            var hint = MakeRect("Hint", canvas, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 104), new Vector2(820, 28));
            hintText = MakeText(hint, 19, TextAnchor.MiddleCenter, Gold, true);
            Stretch(hintText.rectTransform, 0);
            hintText.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.85f);

            var help = MakeRect("Help", canvas, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(1000, 20));
            var helpText = MakeText(help, 13, TextAnchor.MiddleCenter, Dim);
            Stretch(helpText.rectTransform, 0);
            helpText.text = "右鍵拖曳或 Q/E 轉視角 · 滾輪縮放 · 方向鍵或中鍵平移 · WASD 或左鍵移動（兩鍵同按斜走）· Shift 跑 · Alt＋方向 跳 · 右鍵點地面轉向 · 數字鍵選招 · F 互動 · I 行囊 · X 掀頂 · F3 畫質 · 空白鍵 結束回合／互動";
            helpText.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);

            // 滑鼠提示
            tipRoot = Panel("Tip", new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(250, 60));
            tipRoot.GetComponent<Image>().raycastTarget = false;
            tipText = MakeText(tipRoot, 15, TextAnchor.UpperLeft, Ink);
            Stretch(tipText.rectTransform, 8);
            tipRoot.gameObject.SetActive(false);

            // 回合橫幅
            var banner = MakeRect("Banner", canvas, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -66), new Vector2(800, 56));
            bannerText = MakeText(banner, 44, TextAnchor.MiddleCenter, Color.white, true);
            Stretch(bannerText.rectTransform, 0);
            bannerText.fontStyle = FontStyle.Bold;
            var bo = bannerText.gameObject.AddComponent<Outline>();
            bo.effectColor = new Color(0, 0, 0, 0.85f);
            bo.effectDistance = new Vector2(2, -2);
            banner.gameObject.SetActive(false);

            // 對話框（與路人交談）
            dialogRoot = Panel("Dialog", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 112), new Vector2(780, 236));
            dialogTitle = MakeText(dialogRoot, 25, TextAnchor.UpperLeft, Gold, true);
            Stretch(dialogTitle.rectTransform, 14);
            dialogBody = MakeText(dialogRoot, 17, TextAnchor.UpperLeft, Ink);
            Stretch(dialogBody.rectTransform, 16);
            dialogBody.rectTransform.offsetMax = new Vector2(-16, -52);
            dialogBody.rectTransform.offsetMin = new Vector2(16, 62);
            dialogBody.lineSpacing = 1.2f;
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                Text label;
                var btn = MakeButton(dialogRoot, out label);
                var br = (RectTransform)btn.transform;
                br.sizeDelta = new Vector2(180, 46);
                br.anchoredPosition = new Vector2((i - 1.5f) * 188, 10);
                label.fontSize = 18;
                btn.onClick.AddListener(() => { if (onDialog != null) onDialog(index); });
                dialogBtns.Add(btn);
                dialogLabels.Add(label);
            }
            dialogRoot.gameObject.SetActive(false);

            // 裝備與行囊面板
            gearRoot = Panel("Gear", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(900, 560));
            gearTitle = MakeText(gearRoot, 26, TextAnchor.UpperLeft, Gold, true);
            Stretch(gearTitle.rectTransform, 16);
            gearRight = MakeText(gearRoot, 18, TextAnchor.UpperLeft, Dim, true);
            Stretch(gearRight.rectTransform, 16);
            gearRight.rectTransform.offsetMin = new Vector2(466, 16);
            gearRight.rectTransform.offsetMax = new Vector2(-16, -22);
            gearDetail = MakeText(gearRoot, 15, TextAnchor.LowerLeft, Ink);
            Stretch(gearDetail.rectTransform, 16);
            gearDetail.rectTransform.offsetMax = new Vector2(-150, -470);
            for (int i = 0; i < 11 + 11; i++)
            {
                int index = i;
                bool left = i < 11;
                Text label;
                var btn = MakeButton(gearRoot, out label);
                var br = (RectTransform)btn.transform;
                br.anchorMin = br.anchorMax = new Vector2(0, 1);
                br.pivot = new Vector2(0, 1);
                br.sizeDelta = new Vector2(left ? 434 : 418, 34);
                br.anchoredPosition = left ? new Vector2(16, -54 - i * 37) : new Vector2(466, -54 - (i - 11) * 37);
                label.fontSize = 17;
                label.alignment = TextAnchor.MiddleLeft;
                label.rectTransform.offsetMin = new Vector2(44, 2);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                // 列首的小圖示：直接用這件裝備或道具的模型畫出來
                var ig = new GameObject("Icon", typeof(RectTransform));
                var ir = (RectTransform)ig.transform;
                ir.SetParent(br, false);
                ir.anchorMin = ir.anchorMax = new Vector2(0, 0.5f);
                ir.pivot = new Vector2(0, 0.5f);
                ir.anchoredPosition = new Vector2(6, 0);
                ir.sizeDelta = new Vector2(32, 32);
                var raw = ig.AddComponent<RawImage>();
                raw.raycastTarget = false;
                raw.enabled = false;
                if (left) gearSlotIcons.Add(raw); else gearOptIcons.Add(raw);
                btn.onClick.AddListener(() =>
                {
                    if (index < 11) { if (onGearSlot != null) onGearSlot(index); }
                    else if (onGearOption != null) onGearOption(index - 11);
                });
                if (left) { gearSlotBtns.Add(btn); gearSlotLabels.Add(label); }
                else { gearOptBtns.Add(btn); gearOptLabels.Add(label); }
            }
            {
                Text label;
                var close = MakeButton(gearRoot, out label);
                var cr = (RectTransform)close.transform;
                cr.anchorMin = cr.anchorMax = new Vector2(1, 0);
                cr.pivot = new Vector2(1, 0);
                cr.sizeDelta = new Vector2(124, 40);
                cr.anchoredPosition = new Vector2(-16, 14);
                label.text = "<b>關閉</b> <size=13>I</size>";
                close.onClick.AddListener(() => { if (onGearClose != null) onGearClose(); });
            }
            // 戰前整備時切換三位俠客的分頁
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                Text label;
                var tab = MakeButton(gearRoot, out label);
                var tr = (RectTransform)tab.transform;
                tr.anchorMin = tr.anchorMax = new Vector2(0, 1);
                tr.pivot = new Vector2(0, 1);
                tr.sizeDelta = new Vector2(84, 30);
                tr.anchoredPosition = new Vector2(196 + i * 88, -14);
                label.fontSize = 16;
                tab.onClick.AddListener(() => { if (onGearTab != null) onGearTab(index); });
                gearTabs.Add(tab);
                gearTabLabels.Add(label);
            }
            gearRoot.gameObject.SetActive(false);

            // 戰前整備的入口（創角畫面時顯示在畫面下方）
            {
                Text label;
                prepBtn = MakeButton(canvas, out label);
                prepBtn.gameObject.name = "PrepBtn";
                var r = (RectTransform)prepBtn.transform;
                r.sizeDelta = new Vector2(250, 52);
                r.anchoredPosition = new Vector2(120, 26);
                label.fontSize = 22;
                label.text = "<b>戰前整備</b> <size=14>I　兵器、護具、行囊</size>";
                prepBtn.onClick.AddListener(() => { if (onBag != null) onBag(); });
                prepBtn.gameObject.SetActive(false);
            }

            // 開啟行囊的按鈕（角色面板上方）
            {
                Text label;
                var bag = MakeButton(canvas, out label);
                bag.gameObject.name = "BagBtn";
                var r = (RectTransform)bag.transform;
                r.anchorMin = r.anchorMax = new Vector2(0, 0);
                r.pivot = new Vector2(0, 0);
                r.sizeDelta = new Vector2(150, 36);
                r.anchoredPosition = new Vector2(10, 256);
                label.fontSize = 18;
                label.text = "<b>裝備與行囊</b> <size=13>I</size>";
                bag.onClick.AddListener(() => { if (onBag != null) onBag(); });
            }

            // 結算
            overRoot = MakeRect("Over", canvas, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            overRoot.anchorMin = Vector2.zero; overRoot.anchorMax = Vector2.one;
            overRoot.offsetMin = overRoot.offsetMax = Vector2.zero;
            overRoot.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);
            var box = Panel("Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460, 250));
            box.SetParent(overRoot, false);
            overTitle = MakeText(box, 62, TextAnchor.UpperCenter, Gold, true);
            Stretch(overTitle.rectTransform, 22);
            overText = MakeText(box, 18, TextAnchor.MiddleCenter, Ink);
            Stretch(overText.rectTransform, 22);
            Text againLabel;
            var again = MakeButton(box, out againLabel);
            var ar = (RectTransform)again.transform;
            ar.anchorMin = ar.anchorMax = new Vector2(0.5f, 0);
            ar.pivot = new Vector2(0.5f, 0);
            ar.anchoredPosition = new Vector2(0, 24);
            ar.sizeDelta = new Vector2(180, 50);
            againLabel.text = "<b>再戰一場</b>";
            again.onClick.AddListener(() => { if (onAgain != null) onAgain(); });
            overRoot.gameObject.SetActive(false);
        }

        /// <summary>顯示或隱藏戰鬥用的面板（創角時先收起來，把畫面留給角色）。</summary>
        public void SetBattleUi(bool on)
        {
            foreach (var name in new[] { "Turn", "Log", "Unit", "Actions", "Hint", "Help" })
            {
                var t = canvas.Find(name);
                if (t != null) t.gameObject.SetActive(on);
            }
        }

        // ───────── 對話框 ─────────

        readonly RectTransform dialogRoot;
        readonly Text dialogTitle, dialogBody;
        readonly List<Button> dialogBtns = new List<Button>();
        readonly List<Text> dialogLabels = new List<Text>();
        Action<int> onDialog;

        public bool DialogOpen { get { return dialogRoot.gameObject.activeSelf; } }

        /// <summary>顯示對話框：標題、內文、最多四個選項（enabled 為 false 的選項顯示但不能按）。</summary>
        public void ShowDialog(string title, string body, string[] labels, bool[] enabled, Action<int> onPick)
        {
            dialogTitle.text = title;
            dialogBody.text = body;
            onDialog = onPick;
            for (int i = 0; i < dialogBtns.Count; i++)
            {
                bool on = i < labels.Length;
                dialogBtns[i].gameObject.SetActive(on);
                if (!on) continue;
                dialogLabels[i].text = labels[i];
                dialogBtns[i].interactable = enabled[i];
                ((RectTransform)dialogBtns[i].transform).anchoredPosition = new Vector2((i - (labels.Length - 1) * 0.5f) * 188, 10);
            }
            dialogRoot.gameObject.SetActive(true);
            dialogRoot.SetAsLastSibling();
        }

        public void HideDialog()
        {
            onDialog = null;
            dialogRoot.gameObject.SetActive(false);
        }

        // ───────── 裝備與行囊面板 ─────────

        public Action onBag;
        readonly Button prepBtn;
        readonly List<RawImage> gearSlotIcons = new List<RawImage>(), gearOptIcons = new List<RawImage>();
        readonly List<Button> gearTabs = new List<Button>();
        readonly List<Text> gearTabLabels = new List<Text>();
        Action<int> onGearTab;

        /// <summary>顯示或隱藏「戰前整備」的入口按鈕。</summary>
        public void SetPrep(bool on) { prepBtn.gameObject.SetActive(on); if (on) prepBtn.transform.SetAsLastSibling(); }

        /// <summary>裝備面板的位置：戰前靠左，把右邊留給角色；戰鬥中置中。</summary>
        public void SetGearSide(bool leftSide) { gearRoot.anchoredPosition = leftSide ? new Vector2(-180, 10) : new Vector2(0, 10); }

        /// <summary>面板上方的角色分頁（戰前整備用）；names 為 null 就不顯示。</summary>
        public void SetGearTabs(string[] names, int sel, Action<int> onTab)
        {
            onGearTab = onTab;
            for (int i = 0; i < gearTabs.Count; i++)
            {
                bool on = names != null && i < names.Length;
                gearTabs[i].gameObject.SetActive(on);
                if (!on) continue;
                gearTabLabels[i].text = names[i];
                gearTabs[i].colors = Colors(i == sel ? BtnSelected : BtnNormal);
            }
        }

        /// <summary>設定面板各列的圖示（null 表示該列沒有圖示）。要在 ShowGear 之後呼叫。</summary>
        public void SetGearIcons(Texture[] slots, Texture[] options)
        {
            for (int i = 0; i < gearSlotIcons.Count; i++)
            {
                var t = slots != null && i < slots.Length ? slots[i] : null;
                gearSlotIcons[i].texture = t;
                gearSlotIcons[i].enabled = t != null;
            }
            for (int i = 0; i < gearOptIcons.Count; i++)
            {
                var t = options != null && i < options.Length ? options[i] : null;
                gearOptIcons[i].texture = t;
                gearOptIcons[i].enabled = t != null;
            }
        }

        readonly RectTransform gearRoot;
        readonly Text gearTitle, gearRight, gearDetail;
        readonly List<Button> gearSlotBtns = new List<Button>(), gearOptBtns = new List<Button>();
        readonly List<Text> gearSlotLabels = new List<Text>(), gearOptLabels = new List<Text>();
        Action<int> onGearSlot, onGearOption;
        Action onGearClose;

        public bool GearOpen { get { return gearRoot.gameObject.activeSelf; } }

        /// <summary>
        /// 顯示裝備面板。左邊 11 個欄位（sel 是選中的欄位，-1 表示沒選），
        /// 右邊是一排選項（沒選欄位時是行囊裡的道具，選了欄位時是可換的裝備）。
        /// </summary>
        public void ShowGear(string title, string[] slots, int sel, string rightTitle, string[] options, bool[] enabled, string detail,
                             Action<int> onSlot, Action<int> onOption, Action onClose)
        {
            gearTitle.text = title;
            gearRight.text = rightTitle;
            gearDetail.text = detail;
            onGearSlot = onSlot; onGearOption = onOption; onGearClose = onClose;
            for (int i = 0; i < gearSlotBtns.Count; i++)
            {
                gearSlotLabels[i].text = i < slots.Length ? slots[i] : "";
                gearSlotBtns[i].colors = Colors(i == sel ? BtnSelected : BtnNormal);
            }
            for (int i = 0; i < gearOptBtns.Count; i++)
            {
                bool on = i < options.Length;
                gearOptBtns[i].gameObject.SetActive(on);
                if (!on) continue;
                gearOptLabels[i].text = options[i];
                gearOptBtns[i].interactable = enabled[i];
            }
            gearRoot.gameObject.SetActive(true);
            gearRoot.SetAsLastSibling();
        }

        public void HideGear()
        {
            onGearSlot = onGearOption = null;
            onGearClose = null;
            gearRoot.gameObject.SetActive(false);
        }

        // ───────── 建構小工具 ─────────

        RectTransform MakeRect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            return r;
        }

        RectTransform Layer(string name)
        {
            var r = MakeRect(name, canvas, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            return r;
        }

        RectTransform Panel(string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var r = MakeRect(name, canvas, anchor, pivot, pos, size);
            r.gameObject.AddComponent<Image>().color = Bg;
            // 細金邊
            var edge = r.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.78f, 0.63f, 0.34f, 0.55f);
            edge.effectDistance = new Vector2(1.2f, -1.2f);
            return r;
        }

        Text MakeText(Transform parent, int size, TextAnchor anchor, Color color, bool useKai = false)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = useKai ? kai : font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static void Stretch(RectTransform r, float pad)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(pad, pad);
            r.offsetMax = new Vector2(-pad, -pad);
        }

        Button MakeButton(Transform parent, out Text label)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0);
            r.pivot = new Vector2(0.5f, 0);
            r.sizeDelta = new Vector2(BtnW, BtnH);
            go.GetComponent<Image>().color = Color.white;
            var b = go.GetComponent<Button>();
            b.targetGraphic = go.GetComponent<Image>();
            b.colors = Colors(BtnNormal);
            go.GetComponent<Image>().CrossFadeColor(BtnNormal, 0, true, true); // 一出現就是底色，不要從白色漸變
            b.navigation = new Navigation { mode = Navigation.Mode.None }; // 避免空白鍵重複觸發按鈕
            label = MakeText(r, 20, TextAnchor.MiddleCenter, Ink, true);
            Stretch(label.rectTransform, 4);
            return b;
        }

        static ColorBlock Colors(Color normal)
        {
            return new ColorBlock
            {
                normalColor = normal, selectedColor = normal,
                highlightedColor = normal == BtnSelected ? BtnSelected : BtnHover,
                pressedColor = BtnSelected, disabledColor = BtnDisabled,
                colorMultiplier = 1, fadeDuration = 0.08f,
            };
        }

        // ───────── 先攻列、面板、招式列 ─────────

        public void SetTurn(Battle b, Unit active, int round, Func<Unit, bool> shown)
        {
            sb.Length = 0;
            sb.Append("<color=#DCB361>第 ").Append(round).Append(" 回合</color>　");
            foreach (var u in b.order)
            {
                // 規則層的結算比畫面播放超前，所以依「畫面上還站著」來列，而不是 u.alive
                if (!shown(u)) continue;
                string c = u.team == 0 ? "#8FC1EE" : "#F0958A";
                if (u == active) sb.Append("<b><color=#FFE08A>【").Append(u.name).Append("】</color></b> ");
                else sb.Append("<color=").Append(c).Append('>').Append(u.name).Append("</color> ");
            }
            turnText.text = sb.ToString();
        }

        /// <summary>live 為 true 時顯示本回合資源（輪到玩家操作時）。</summary>
        public void ShowUnit(Unit u, int hp, bool live)
        {
            sb.Length = 0;
            sb.Append("<size=24><b><color=").Append(u.team == 0 ? "#8FC1EE" : "#F0958A").Append('>').Append(u.name)
              .Append("</color></b></size>  <color=#A89C85>").Append(u.cls).Append("</color>\n");
            sb.Append("氣血 <b>").Append(hp).Append("</b>/").Append(u.maxHp);
            sb.Append("　內力 <b>").Append(u.qi).Append("</b>/").Append(u.maxQi).Append('\n');
            sb.Append("防禦 <b>").Append(u.Ac).Append("</b>　輕功 <b>").Append(u.Jump).Append("</b>");
            if (live) sb.Append("　移動 <b>").Append(u.moveLeft).Append("</b>");
            sb.Append('\n');
            sb.Append("兵器 <b>").Append(u.Weapon != null ? u.Weapon.name : "空手").Append("</b> <color=#A89C85>")
              .Append(Gears.Info(u.Weapon != null ? u.Weapon.cls : WeaponClass.None).name).Append("</color>\n");
            sb.Append("<color=#A89C85>");
            for (int i = 0; i < 6; i++)
            {
                int m = Dice.Mod(u.stats[i]);
                sb.Append(Db.StatNames[i]).Append(' ').Append(u.stats[i]).Append('(').Append(m >= 0 ? "+" : "").Append(m).Append(')');
                sb.Append(i == 2 ? "\n" : i < 5 ? "  " : "");
            }
            sb.Append("</color>\n");
            if (live)
            {
                sb.Append("動作 ").Append(u.actionLeft ? "<color=#FFE08A>●</color>" : "○");
                sb.Append("　附加動作 ").Append(u.bonusLeft ? "<color=#FFE08A>●</color>" : "○").Append('\n');
            }
            if (u.guard) sb.Append("<color=#DCB361>金鐘罩</color> ");
            if (u.light) sb.Append("<color=#9FE0C0>梯雲縱</color> ");
            if (u.stun) sb.Append("<color=#C9A0E8>定身</color> ");
            if (u.fright > 0) sb.Append("<color=#C9A0E8>膽寒</color> ");
            if (u.blind > 0) sb.Append("<color=#C9A0E8>目盲</color> ");
            if (u.tangle > 0) sb.Append("<color=#C9A0E8>纏繞</color> ");
            if (u.poison > 0) sb.Append("<color=#9FE0C0>中毒</color> ");
            if (u.bleed > 0) sb.Append("<color=#D9534F>流血</color> ");
            if (u.venom) sb.Append("<color=#9FE0C0>兵器淬毒</color> ");
            if (live && u.team == 0) sb.Append(u.knownCell >= 0 ? "<color=#F0958A>行蹤敗露</color>" : "<color=#9FE0C0>行蹤未露</color>");
            panelText.text = sb.ToString();
        }

        /// <summary>u 為 null 表示目前不可操作。</summary>
        public void SetActions(Unit u, Battle b, Skill selected)
        {
            int n = u != null ? u.skills.Length : 0;
            while (skillBtns.Count < n)
            {
                int index = skillBtns.Count;
                Text label;
                var btn = MakeButton(actionRoot, out label);
                btn.onClick.AddListener(() => { if (onSkill != null) onSkill(index); });
                var trig = btn.gameObject.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => HoverSkill(index));
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => hintText.text = defaultHint);
                trig.triggers.Add(enter);
                trig.triggers.Add(exit);
                skillBtns.Add(btn);
                skillLabels.Add(label);
            }

            shownSkills = u != null ? u.skills : null;
            float total = n * (BtnW + Gap) + BtnW;
            float x = -total * 0.5f + BtnW * 0.5f;
            for (int i = 0; i < skillBtns.Count; i++)
            {
                var btn = skillBtns[i];
                btn.gameObject.SetActive(i < n);
                if (i >= n) continue;
                var sk = u.skills[i];
                ((RectTransform)btn.transform).anchoredPosition = new Vector2(x, 0);
                x += BtnW + Gap;
                skillLabels[i].text = "<b>" + (i + 1) + " " + sk.name + "</b>\n<size=13>" + (sk.bonus ? "附加" : "動作")
                    + (sk.cost > 0 ? " · 內力 " + sk.cost : "") + "</size>";
                btn.interactable = b.CanUse(u, sk);
                btn.colors = Colors(sk == selected ? BtnSelected : BtnNormal);
            }
            ((RectTransform)endBtn.transform).anchoredPosition = new Vector2(x, 0);
            endBtn.interactable = u != null;
        }

        Skill[] shownSkills;

        void HoverSkill(int i)
        {
            if (shownSkills == null || i >= shownSkills.Length) return;
            var sk = shownSkills[i];
            string dice = sk.dmg.n > 0 ? "（" + sk.dmg + "）" : "";
            hintText.text = sk.name + dice + "：" + sk.desc;
        }

        Text endText;

        /// <summary>自由行動時，「結束回合」改成「等候片刻」，先攻列改成說明文字。</summary>
        public void SetExplore(bool on, string who)
        {
            endText.text = on ? "<b>等候片刻</b>\n<size=13>讓時間過一輪</size>" : "<b>結束回合</b>\n<size=13>空白鍵</size>";
            if (on) turnText.text = "<color=#9FE0C0>自由行動</color>　尚未被發現　<b><color=#FFE08A>【" + who + "】</color></b>　<color=#A89C85>Tab 換人 · 空白鍵 互動</color>";
        }

        public void SetHint(string s)
        {
            defaultHint = s;
            hintText.text = s;
        }

        // ───────── 戰報 ─────────

        public void Log(string line)
        {
            lines.Add(line);
            if (lines.Count > 60) lines.RemoveRange(0, 20);
            sb.Length = 0;
            for (int i = Math.Max(0, lines.Count - 22); i < lines.Count; i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(lines[i]);
            }
            logText.text = sb.ToString();
        }

        // ───────── 滑鼠提示 ─────────

        public void ShowTip(string text)
        {
            tipText.text = text;
            tipRoot.sizeDelta = new Vector2(250, tipText.preferredHeight + 18);
            tipRoot.gameObject.SetActive(true);
        }

        public void HideTip()
        {
            if (tipRoot.gameObject.activeSelf) tipRoot.gameObject.SetActive(false);
        }

        // ───────── 血條 ─────────

        public void AddBar(Unit u)
        {
            var root = MakeRect("Bar " + u.name, barLayer, Vector2.zero, new Vector2(0.5f, 0), Vector2.zero, new Vector2(140, 40));
            var name = MakeText(root, 18, TextAnchor.UpperCenter, u.team == 0 ? new Color(0.72f, 0.86f, 1f) : new Color(1f, 0.74f, 0.70f), true);
            Stretch(name.rectTransform, 0);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            name.text = u.name;
            name.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f);

            bool hasQi = u.maxQi > 0;
            var hp = MakeGauge(root, new Vector2(0, hasQi ? 6 : 2), new Vector2(64, 8),
                u.team == 0 ? new Color(0.36f, 0.78f, 0.42f) : new Color(0.86f, 0.30f, 0.24f));
            RectTransform qi = hasQi ? MakeGauge(root, new Vector2(0, 0), new Vector2(64, 5), new Color(0.38f, 0.66f, 0.98f)) : null;
            bars.Add(new Bar { root = root, fill = hp, qi = qi, name = name });
        }

        /// <summary>一條有底色的量條；回傳可縮放的填充部分。</summary>
        RectTransform MakeGauge(RectTransform parent, Vector2 pos, Vector2 size, Color color)
        {
            var back = MakeRect("Gauge", parent, new Vector2(0.5f, 0), new Vector2(0.5f, 0), pos, size);
            var bi = back.gameObject.AddComponent<Image>();
            bi.color = new Color(0, 0, 0, 0.75f);
            bi.raycastTarget = false;
            var fill = MakeRect("Fill", back, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(1, 1);
            fill.offsetMax = new Vector2(-1, -1);
            var fi = fill.gameObject.AddComponent<Image>();
            fi.color = color;
            fi.raycastTarget = false;
            return fill;
        }

        static void SetFill(RectTransform f, float ratio)
        {
            f.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1);
            f.offsetMax = new Vector2(-1, -1);
        }

        public void SetBar(Unit u, int hp) { SetFill(bars[u.id].fill, hp / (float)u.maxHp); }

        public void SetQi(Unit u, int qi)
        {
            var q = bars[u.id].qi;
            if (q != null) SetFill(q, qi / (float)u.maxQi);
        }

        /// <summary>名字旁的狀態標籤（定身、金鐘罩、梯雲縱）；傳 null 清除。</summary>
        public void SetTags(Unit u, string tags)
        {
            bars[u.id].name.text = string.IsNullOrEmpty(tags) ? u.name : u.name + " <size=12><color=#FFE08A>" + tags + "</color></size>";
        }

        /// <summary>回合開始的橫幅：淡入、停留、淡出。新的橫幅會直接取代舊的。</summary>
        public IEnumerator Banner(string text, Color color)
        {
            int gen = ++bannerGen;
            var go = bannerText.transform.parent.gameObject;
            go.SetActive(true);
            bannerText.text = text;
            for (float k = 0; k < 1; k += Time.deltaTime / 1.1f)
            {
                if (gen != bannerGen) yield break;
                color.a = k < 0.15f ? k / 0.15f : k > 0.6f ? (1 - k) / 0.4f : 1f;
                bannerText.color = color;
                yield return null;
            }
            if (gen == bannerGen) go.SetActive(false);
        }

        public void RemoveBar(Unit u)
        {
            bars[u.id].shown = false;
            bars[u.id].root.gameObject.SetActive(false);
        }

        /// <summary>每幀把血條與提示框對到螢幕位置。</summary>
        public void Follow(Camera cam, List<UnitView> views)
        {
            for (int i = 0; i < bars.Count; i++)
            {
                var bar = bars[i];
                if (!bar.shown) continue;
                var p = cam.WorldToScreenPoint(views[i].root.position + new Vector3(0, UnitView.BarHeight, 0));
                bool vis = p.z > 0 && views[i].root.gameObject.activeSelf;
                if (bar.root.gameObject.activeSelf != vis) bar.root.gameObject.SetActive(vis);
                if (vis) bar.root.position = new Vector3(p.x, p.y, 0);
            }
            if (tipRoot.gameObject.activeSelf)
            {
                // 提示框以左上角為基準放在游標右下；靠近螢幕邊緣時往回收，避免超出畫面
                var m = Input.mousePosition;
                float s = tipRoot.lossyScale.x;
                float tw = tipRoot.sizeDelta.x * s, th = tipRoot.sizeDelta.y * s;
                float x = Mathf.Min(m.x + 18, Screen.width - tw - 4);
                float y = Mathf.Max(m.y - 18, th + 4);
                tipRoot.position = new Vector3(x, y, 0);
            }
        }

        // ───────── 飄字 ─────────

        public IEnumerator Float(Camera cam, Vector3 world, string text, Color color, int size)
        {
            Text t;
            if (floatPool.Count > 0) t = floatPool.Pop();
            else
            {
                var r = MakeRect("Float", floatLayer, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240, 50));
                t = MakeText(r, 24, TextAnchor.MiddleCenter, Color.white, true);
                Stretch(t.rectTransform, 0);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.fontStyle = FontStyle.Bold;
                t.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f);
            }
            var holder = (RectTransform)t.transform.parent;
            holder.gameObject.SetActive(true);
            t.text = text;
            t.fontSize = size;

            for (float k = 0; k < 1; k += Time.deltaTime / 0.95f)
            {
                var p = cam.WorldToScreenPoint(world);
                holder.position = new Vector3(p.x, p.y + k * 70, 0);
                color.a = p.z > 0 ? Mathf.Clamp01((1 - k) * 2.5f) : 0;
                t.color = color;
                yield return null;
            }
            holder.gameObject.SetActive(false);
            floatPool.Push(t);
        }

        // ───────── 結算 ─────────

        public void ShowOver(bool win, int round)
        {
            overTitle.text = win ? "大獲全勝" : "飲恨江湖";
            overTitle.color = win ? Gold : new Color(0.85f, 0.33f, 0.29f);
            overText.text = win ? "黑風寨群匪伏誅，共歷 " + round + " 回合。" : "三俠力戰不敵，來日再雪此恨。";
            overRoot.gameObject.SetActive(true);
            overRoot.SetAsLastSibling();
        }
    }
}
