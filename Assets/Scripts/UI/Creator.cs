using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Jianghu
{
    /// <summary>
    /// 創角（捏人）畫面：開戰前編輯我方角色的外觀。
    /// 直接修改 Look 資料，每次變更都通知控制器重建該角色的模型，所以所見即所得。
    /// 項目分成六個分頁；滑桿調連續數值，左右箭頭切換款式與顏色。
    /// </summary>
    public sealed class Creator
    {
        public Action onChanged;        // 目前這份外觀被改動了
        public Action onStart;          // 按下「出戰」
        public Action<int> onHero;      // 切換要編輯的角色
        public Action<Look> onReplace;  // 整份外觀換掉（套用預設造型、隨機）

        const float W = 396, H = 604, RowH = 33, RowsTop = -124;

        static readonly Color Bg = new Color(0.09f, 0.075f, 0.06f, 0.92f);
        static readonly Color Ink = new Color(0.93f, 0.87f, 0.76f);
        static readonly Color Dim = new Color(0.66f, 0.61f, 0.52f);
        static readonly Color Gold = new Color(0.86f, 0.70f, 0.38f);
        static readonly Color BtnNormal = new Color(0.22f, 0.17f, 0.12f, 0.95f);
        static readonly Color BtnHover = new Color(0.34f, 0.26f, 0.17f, 0.98f);
        static readonly Color BtnOn = new Color(0.56f, 0.20f, 0.14f, 0.98f);

        // 色盤
        static readonly uint[] SkinPal = { 0xF6DCC4, 0xEDC9A6, 0xE2B88F, 0xD9A77C, 0xC48E63, 0xA9744E };
        static readonly uint[] HairPal = { 0x211C1A, 0x3A2A20, 0x5A3D28, 0x7A7A7A, 0xE6E6E0, 0x6B2A22, 0x1C2433 };
        static readonly uint[] ClothPal =
        {
            0xE8EEEF, 0xC9D6C3, 0xF0D9DC, 0x9A9C97, 0x5F6C7A, 0x3F5A6B, 0x2E5E8E, 0x4E5C46, 0x4F6B58,
            0xB8862E, 0xA8703A, 0x7B6A4E, 0x8B2F2A, 0x701F1C, 0x8E5AA6, 0x5B564F, 0x2B2D33,
        };
        static readonly uint[] TrimPal = { 0, 0x2E5E8E, 0x4A2E18, 0x3D2352, 0x27231F, 0x8C3B1E, 0xD9DCD6, 0xA8475A, 0x7A2A22, 0x4F6B58, 0x15161A, 0xC9A45C };
        static readonly uint[] HatPal = { 0, 0x3A3632, 0xEFEDE6, 0x7A2A22, 0x2E5E8E, 0x4F6B58 };

        // 各選項的顯示名稱（順序須與列舉一致）
        static readonly string[] GenderN = { "男", "女" };
        static readonly string[] BodyN = { "標準", "魁梧", "纖細" };
        static readonly string[] EyeN = { "圓眼", "鳳眼", "細眼", "大眼" };
        static readonly string[] BrowN = { "平和", "冷峻", "倒豎" };
        static readonly string[] NoseN = { "小鼻", "挺鼻", "寬鼻", "無" };
        static readonly string[] MouthN = { "平", "微笑", "下撇", "小口" };
        static readonly string[] BeardN = { "無", "八字鬍", "山羊鬍", "長鬚" };
        static readonly string[] FaceN = { "無", "蒙面巾", "眼罩" };
        static readonly string[] HairN = { "束髻", "高髻", "雙髻", "披髮", "馬尾", "短髮", "光頭" };
        static readonly string[] HeadwearN = { "無", "斗笠", "帷帽", "書生巾", "頭巾", "鐵盔", "兜帽", "玉冠", "額帶", "金箍" };
        static readonly string[] OutfitN = { "長袍", "短打", "鎧甲", "僧袍" };
        static readonly string[] SleeveN = { "寬袖", "箭袖", "赤膊" };
        static readonly string[] ShoulderN = { "無", "肩甲", "毛領", "毛領加肩甲" };
        static readonly string[] BackN = { "依兵器", "無", "劍鞘", "雙劍鞘", "箭袋", "披風", "背斗笠", "書箱", "大葫蘆" };
        static readonly string[] WeaponN = { "空手", "長劍", "單刀", "大刀", "鐵護腕", "弓", "飛刀", "長槍", "齊眉棍", "雙刃", "摺扇", "銅錘", "洞簫" };
        static readonly string[] OnOffN = { "無", "有" };
        static readonly string[] TabN = { "體型", "眼眉", "鼻口", "頭髮", "服裝", "配件" };

        readonly Font font, kai;
        readonly RectTransform root;
        readonly List<GameObject> pages = new List<GameObject>();
        readonly List<Button> tabBtns = new List<Button>(), heroBtns = new List<Button>();
        readonly List<Action> syncs = new List<Action>();
        readonly Text presetText;
        readonly System.Random rnd = new System.Random();

        Look look;
        int tab, bodyPreset, presetIndex;
        bool syncing;

        public bool Visible { get { return root.gameObject.activeSelf; } }

        public Creator(Hud hud, string[] heroNames)
        {
            font = hud.BodyFont;
            kai = hud.KaiFont;

            root = R("Creator", hud.Canvas, 10, -56, W, H);
            root.gameObject.AddComponent<Image>().color = Bg;
            var edge = root.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.78f, 0.63f, 0.34f, 0.6f);
            edge.effectDistance = new Vector2(1.2f, -1.2f);

            T(root, 14, -8, 200, 32, "捏人 · 創角", 24, TextAnchor.MiddleLeft, Gold, true);
            T(root, 190, -12, 196, 26, "Q / E 轉動角色　滾輪縮放", 13, TextAnchor.MiddleRight, Dim, false);

            // 角色切換
            float hw = (W - 24 - (heroNames.Length - 1) * 6) / heroNames.Length;
            for (int i = 0; i < heroNames.Length; i++)
            {
                int index = i;
                heroBtns.Add(B(root, 12 + i * (hw + 6), -46, hw, 32, heroNames[i], () => { if (onHero != null) onHero(index); }, 19, true));
            }

            // 分頁
            float tw = (W - 24 - (TabN.Length - 1) * 4) / TabN.Length;
            for (int i = 0; i < TabN.Length; i++)
            {
                int index = i;
                tabBtns.Add(B(root, 12 + i * (tw + 4), -84, tw, 30, TabN[i], () => SelectTab(index), 17, true));
                var page = R("Page " + TabN[i], root, 0, RowsTop, W, RowH * 11);
                pages.Add(page.gameObject);
            }

            BuildPages();

            // 預設造型、隨機、出戰
            float by = RowsTop - RowH * 11 - 8;
            T(root, 14, by, 70, 30, "預設造型", 15, TextAnchor.MiddleLeft, Dim, false);
            B(root, 88, by, 30, 30, "◀", () => StepPreset(-1), 16, false);
            presetText = T(root, 120, by, 132, 30, "", 17, TextAnchor.MiddleCenter, Ink, true);
            B(root, 254, by, 30, 30, "▶", () => StepPreset(1), 16, false);
            B(root, 292, by, 92, 30, "套用", ApplyPreset, 17, true);

            B(root, 12, by - 42, 120, 42, "隨機", Randomize, 20, true);
            var go = B(root, 140, by - 42, W - 152, 42, "出　戰", () => { if (onStart != null) onStart(); }, 24, true);
            SetColors(go, BtnOn);

            root.gameObject.SetActive(false);
        }

        // ───────── 分頁內容 ─────────

        void BuildPages()
        {
            Transform p = pages[0].transform;
            int r = 0;
            Option(p, r++, "體態", GenderN, l => (int)l.gender, (l, v) => l.gender = (Gender)v);
            Option(p, r++, "體型預設", BodyN, l => bodyPreset, (l, v) => { bodyPreset = v; l.Body((BodyType)v); });
            Slide(p, r++, "身高", 0.85f, 1.15f, l => l.height, (l, v) => l.height = v);
            Slide(p, r++, "身圍", 0.80f, 1.35f, l => l.girth, (l, v) => l.girth = v);
            Slide(p, r++, "肩寬", 0.85f, 1.30f, l => l.shoulder, (l, v) => l.shoulder = v);
            Slide(p, r++, "腰圍", 0.85f, 1.25f, l => l.waist, (l, v) => l.waist = v);
            Slide(p, r++, "胸圍", 0.85f, 1.25f, l => l.chest, (l, v) => l.chest = v);
            Slide(p, r++, "臀寬", 0.85f, 1.25f, l => l.hips, (l, v) => l.hips = v);
            Slide(p, r++, "腿長", 0.90f, 1.12f, l => l.legs, (l, v) => l.legs = v);
            Slide(p, r++, "頭大小", 0.88f, 1.15f, l => l.headSize, (l, v) => l.headSize = v);
            Swatch(p, r++, "膚色", SkinPal, l => l.skin, (l, v) => l.skin = v, null);

            p = pages[1].transform; r = 0;
            Slide(p, r++, "臉寬", 0.85f, 1.20f, l => l.faceWidth, (l, v) => l.faceWidth = v);
            Slide(p, r++, "臉長", 0.85f, 1.20f, l => l.faceLength, (l, v) => l.faceLength = v);
            Option(p, r++, "眼型", EyeN, l => (int)l.eyes, (l, v) => l.eyes = (EyeStyle)v);
            Slide(p, r++, "眼睛大小", 0.6f, 1.4f, l => l.eyeSize, (l, v) => l.eyeSize = v);
            Slide(p, r++, "眼距", -1f, 1f, l => l.eyeSpacing, (l, v) => l.eyeSpacing = v);
            Slide(p, r++, "眼睛高低", -1f, 1f, l => l.eyeHeight, (l, v) => l.eyeHeight = v);
            Option(p, r++, "眉型", BrowN, l => (int)l.brow, (l, v) => l.brow = (Brow)v);
            Slide(p, r++, "眉毛大小", 0.6f, 1.6f, l => l.browSize, (l, v) => l.browSize = v);
            Slide(p, r++, "眉毛高低", -1f, 1f, l => l.browHeight, (l, v) => l.browHeight = v);

            p = pages[2].transform; r = 0;
            Option(p, r++, "鼻型", NoseN, l => (int)l.nose, (l, v) => l.nose = (NoseStyle)v);
            Slide(p, r++, "鼻子大小", 0.6f, 1.6f, l => l.noseSize, (l, v) => l.noseSize = v);
            Slide(p, r++, "鼻子高低", -1f, 1f, l => l.noseHeight, (l, v) => l.noseHeight = v);
            Option(p, r++, "嘴型", MouthN, l => (int)l.mouth, (l, v) => l.mouth = (MouthStyle)v);
            Slide(p, r++, "嘴巴大小", 0.6f, 1.6f, l => l.mouthSize, (l, v) => l.mouthSize = v);
            Slide(p, r++, "嘴巴高低", -1f, 1f, l => l.mouthHeight, (l, v) => l.mouthHeight = v);
            Option(p, r++, "鬍鬚", BeardN, l => (int)l.beard, (l, v) => l.beard = (Beard)v);
            Option(p, r++, "臉部配件", FaceN, l => (int)l.face, (l, v) => l.face = (FaceItem)v);

            p = pages[3].transform; r = 0;
            Option(p, r++, "髮型", HairN, l => (int)l.hair, (l, v) => l.hair = (HairStyle)v);
            Swatch(p, r++, "髮色", HairPal, l => l.hairColor, (l, v) => l.hairColor = v, null);
            Option(p, r++, "頭飾", HeadwearN, l => (int)l.headwear, (l, v) => l.headwear = (Headwear)v);
            Swatch(p, r++, "帽色", HatPal, l => l.hat, (l, v) => l.hat = v, "草色");
            Option(p, r++, "髮帶", OnOffN, l => l.ribbons ? 1 : 0, (l, v) => l.ribbons = v == 1);

            p = pages[4].transform; r = 0;
            Option(p, r++, "服裝", OutfitN, l => (int)l.outfit, (l, v) => l.outfit = (Outfit)v);
            Option(p, r++, "袖型", SleeveN, l => (int)l.sleeve, (l, v) => l.sleeve = (Sleeve)v);
            Option(p, r++, "肩部", ShoulderN, l => (int)l.shoulders, (l, v) => l.shoulders = (Shoulder)v);
            Swatch(p, r++, "主色", ClothPal, l => l.cloth, (l, v) => l.cloth = v, null);
            Swatch(p, r++, "滾邊色", TrimPal, l => l.trim, (l, v) => l.trim = v, "自動");

            p = pages[5].transform; r = 0;
            Option(p, r++, "背部", BackN, l => (int)l.back, (l, v) => l.back = (BackItem)v);
            Option(p, r++, "玉佩", OnOffN, l => l.pendant ? 1 : 0, (l, v) => l.pendant = v == 1);
            Option(p, r++, "酒葫蘆", OnOffN, l => l.gourd ? 1 : 0, (l, v) => l.gourd = v == 1);
            Option(p, r++, "暗器囊", OnOffN, l => l.pouch ? 1 : 0, (l, v) => l.pouch = v == 1);
            T(p, 14, -r * RowH - 6, W - 28, 44, "手上拿什麼由裝備決定：按 I 開啟「裝備與行囊」更換兵器與護具。", 13, TextAnchor.UpperLeft, Dim, false);
        }

        // ───────── 對外操作 ─────────

        public void Show(Look target, int heroIndex)
        {
            look = target;
            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            for (int i = 0; i < heroBtns.Count; i++) SetColors(heroBtns[i], i == heroIndex ? BtnOn : BtnNormal);
            SelectTab(tab);
            Refresh();
        }

        public void Hide() { root.gameObject.SetActive(false); }

        public void SelectTab(int index)
        {
            tab = index;
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].SetActive(i == index);
                SetColors(tabBtns[i], i == index ? BtnOn : BtnNormal);
            }
        }

        /// <summary>把畫面上的數值與目前的外觀資料對齊。</summary>
        public void Refresh()
        {
            if (look == null) return;
            syncing = true;
            foreach (var s in syncs) s();
            presetText.text = Looks.Names[presetIndex];
            syncing = false;
        }

        void Changed()
        {
            Refresh();
            if (onChanged != null) onChanged();
        }

        void StepPreset(int d)
        {
            presetIndex = (presetIndex + d + Looks.All.Length) % Looks.All.Length;
            presetText.text = Looks.Names[presetIndex];
        }

        void ApplyPreset()
        {
            if (onReplace != null) onReplace(Looks.All[presetIndex].Clone());
        }

        static TEnum Pick<TEnum>(System.Random r) where TEnum : struct
        {
            var values = Enum.GetValues(typeof(TEnum));
            return (TEnum)values.GetValue(r.Next(values.Length));
        }

        float Range(float a, float b) { return a + (float)rnd.NextDouble() * (b - a); }

        /// <summary>隨機產生一份外觀（數值取中間偏保守的範圍，避免太誇張）。</summary>
        void Randomize()
        {
            var l = new Look
            {
                gender = Pick<Gender>(rnd),
                height = Range(0.92f, 1.10f), girth = Range(0.88f, 1.22f), shoulder = Range(0.90f, 1.20f), headSize = Range(0.94f, 1.08f),
                waist = Range(0.90f, 1.15f), chest = Range(0.92f, 1.15f), hips = Range(0.92f, 1.12f), legs = Range(0.95f, 1.08f),
                faceWidth = Range(0.92f, 1.12f), faceLength = Range(0.92f, 1.12f),
                eyes = Pick<EyeStyle>(rnd), eyeSize = Range(0.8f, 1.3f), eyeSpacing = Range(-0.5f, 0.5f), eyeHeight = Range(-0.4f, 0.4f),
                brow = Pick<Brow>(rnd), browSize = Range(0.8f, 1.4f), browHeight = Range(-0.4f, 0.4f),
                nose = Pick<NoseStyle>(rnd), noseSize = Range(0.8f, 1.3f), noseHeight = Range(-0.4f, 0.4f),
                mouth = Pick<MouthStyle>(rnd), mouthSize = Range(0.8f, 1.3f), mouthHeight = Range(-0.4f, 0.4f),
                hair = Pick<HairStyle>(rnd), headwear = Pick<Headwear>(rnd), face = rnd.Next(5) == 0 ? Pick<FaceItem>(rnd) : FaceItem.None,
                outfit = Pick<Outfit>(rnd), sleeve = Pick<Sleeve>(rnd), shoulders = rnd.Next(3) == 0 ? Pick<Shoulder>(rnd) : Shoulder.None,
                back = Pick<BackItem>(rnd), weapon = Pick<WeaponKind>(rnd),
                skin = SkinPal[rnd.Next(SkinPal.Length)], hairColor = HairPal[rnd.Next(HairPal.Length)],
                cloth = ClothPal[rnd.Next(ClothPal.Length)], trim = TrimPal[rnd.Next(TrimPal.Length)], hat = HatPal[rnd.Next(HatPal.Length)],
                ribbons = rnd.Next(2) == 0, pendant = rnd.Next(3) == 0, gourd = rnd.Next(4) == 0, pouch = rnd.Next(4) == 0,
            };
            l.beard = l.gender == Gender.Male && rnd.Next(2) == 0 ? Pick<Beard>(rnd) : Beard.None;
            if (onReplace != null) onReplace(l);
        }

        // ───────── 三種列：款式、滑桿、顏色 ─────────

        void Option(Transform page, int row, string label, string[] names, Func<Look, int> get, Action<Look, int> set)
        {
            float y = -row * RowH;
            T(page, 14, y, 92, RowH - 3, label, 16, TextAnchor.MiddleLeft, Ink, false);
            var value = T(page, 144, y, 196, RowH - 3, "", 18, TextAnchor.MiddleCenter, Gold, true);
            B(page, 110, y - 1, 30, RowH - 5, "◀", () => { set(look, (get(look) + names.Length - 1) % names.Length); Changed(); }, 15, false);
            B(page, 344, y - 1, 30, RowH - 5, "▶", () => { set(look, (get(look) + 1) % names.Length); Changed(); }, 15, false);
            syncs.Add(() => value.text = names[Mathf.Clamp(get(look), 0, names.Length - 1)]);
        }

        void Slide(Transform page, int row, string label, float min, float max, Func<Look, float> get, Action<Look, float> set)
        {
            float y = -row * RowH;
            T(page, 14, y, 92, RowH - 3, label, 16, TextAnchor.MiddleLeft, Ink, false);
            var value = T(page, 330, y, 52, RowH - 3, "", 14, TextAnchor.MiddleRight, Dim, false);
            var slider = MakeSlider(page, 112, y - 5, 212, RowH - 13);
            slider.minValue = min;
            slider.maxValue = max;
            bool offset = min < 0; // 位移類顯示正負，比例類顯示百分比
            Action<float> show = v => value.text = offset ? (v >= 0 ? "+" : "") + Mathf.RoundToInt(v * 100) : Mathf.RoundToInt(v * 100) + "%";
            slider.onValueChanged.AddListener(v =>
            {
                if (syncing) return;
                set(look, v);
                show(v);
                if (onChanged != null) onChanged();
            });
            syncs.Add(() => { slider.value = Mathf.Clamp(get(look), min, max); show(slider.value); });
        }

        void Swatch(Transform page, int row, string label, uint[] palette, Func<Look, uint> get, Action<Look, uint> set, string zeroName)
        {
            float y = -row * RowH;
            T(page, 14, y, 92, RowH - 3, label, 16, TextAnchor.MiddleLeft, Ink, false);
            var chip = R("Chip", page, 150, y - 5, 184, RowH - 13);
            var img = chip.gameObject.AddComponent<Image>();
            var chipEdge = chip.gameObject.AddComponent<Outline>();
            chipEdge.effectColor = new Color(0, 0, 0, 0.8f);
            var name = T(chip, 0, 0, 184, RowH - 13, "", 14, TextAnchor.MiddleCenter, Ink, false);
            Func<int> index = () => Mathf.Max(0, Array.IndexOf(palette, get(look)));
            B(page, 110, y - 1, 30, RowH - 5, "◀", () => { set(look, palette[(index() + palette.Length - 1) % palette.Length]); Changed(); }, 15, false);
            B(page, 344, y - 1, 30, RowH - 5, "▶", () => { set(look, palette[(index() + 1) % palette.Length]); Changed(); }, 15, false);
            syncs.Add(() =>
            {
                uint v = get(look);
                bool auto = v == 0 && zeroName != null;
                img.color = auto ? new Color(0.25f, 0.22f, 0.19f) : (Color)new Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
                name.text = auto ? zeroName : "";
            });
        }

        // ───────── 介面小工具（一律以左上角定位）─────────

        static RectTransform R(string name, Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        Text T(Transform parent, float x, float y, float w, float h, string text, int size, TextAnchor anchor, Color color, bool useKai)
        {
            var r = R("Text", parent, x, y, w, h);
            var t = r.gameObject.AddComponent<Text>();
            t.font = useKai ? kai : font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        Button B(Transform parent, float x, float y, float w, float h, string label, Action onClick, int size, bool useKai)
        {
            var r = R("Button", parent, x, y, w, h);
            var img = r.gameObject.AddComponent<Image>();
            img.color = Color.white;
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            SetColors(b, BtnNormal);
            img.CrossFadeColor(BtnNormal, 0, true, true);
            b.onClick.AddListener(() => onClick());
            var t = T(r, 0, 0, w, h, label, size, TextAnchor.MiddleCenter, Ink, useKai);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return b;
        }

        static void SetColors(Button b, Color normal)
        {
            b.colors = new ColorBlock
            {
                normalColor = normal, selectedColor = normal,
                highlightedColor = normal == BtnOn ? BtnOn : BtnHover,
                pressedColor = BtnOn, disabledColor = new Color(0.13f, 0.12f, 0.11f, 0.75f),
                colorMultiplier = 1, fadeDuration = 0.06f,
            };
        }

        /// <summary>從零組一個滑桿：底條、填充、握把。</summary>
        static Slider MakeSlider(Transform parent, float x, float y, float w, float h)
        {
            var root = R("Slider", parent, x, y, w, h);
            var slider = root.gameObject.AddComponent<Slider>();

            var back = Child("Background", root, new Vector2(0, 0.32f), new Vector2(1, 0.68f), Vector2.zero, Vector2.zero);
            back.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);

            var fillArea = Child("Fill Area", root, new Vector2(0, 0.32f), new Vector2(1, 0.68f), new Vector2(-5, 0), new Vector2(-20, 0));
            var fill = Child("Fill", fillArea, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(10, 0));
            fill.gameObject.AddComponent<Image>().color = new Color(0.70f, 0.56f, 0.30f, 0.95f);

            var handleArea = Child("Handle Slide Area", root, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-20, 0));
            var handle = Child("Handle", handleArea, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(14, 0));
            var himg = handle.gameObject.AddComponent<Image>();
            himg.color = new Color(0.95f, 0.88f, 0.70f);

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = himg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            return slider;
        }

        static RectTransform Child(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = anchorMin;
            r.anchorMax = anchorMax;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            return r;
        }
    }
}
