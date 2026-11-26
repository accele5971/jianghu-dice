using System;

namespace Jianghu
{
    // ───────── 角色外觀（捏模資料）─────────
    // 純資料。畫面層（UnitView）依此組裝模型；創角畫面直接編輯這份資料。
    // 每一項都是獨立的插槽，可以任意混搭。

    /// <summary>體型預設：一鍵套用一組身高、身圍、肩寬、頭部比例。</summary>
    public enum BodyType { Standard, Burly, Slim }

    /// <summary>體態：決定軀幹的基礎身形。</summary>
    public enum Gender { Male, Female }

    /// <summary>髮型。戴會蓋住頭頂的帽子時，髮髻會自動收起。</summary>
    public enum HairStyle { Topknot, HighKnot, TwinBuns, LongLoose, Ponytail, Short, Bald }

    /// <summary>頭飾。</summary>
    public enum Headwear
    {
        None,
        ConeHat,     // 斗笠
        VeilHat,     // 帷帽：寬簷帽垂薄紗
        ScholarCap,  // 書生巾
        Headwrap,    // 頭巾
        Helmet,      // 鐵盔紅纓
        Hood,        // 兜帽
        Crown,       // 玉冠髮簪
        Headband,    // 額帶
        GoldCirclet, // 金箍
    }

    public enum EyeStyle { Round, Phoenix, Narrow, Big }  // 眼型：圓眼、鳳眼、細眼、大眼
    public enum NoseStyle { Small, High, Wide, None }     // 鼻型：小鼻、挺鼻、寬鼻、無
    public enum MouthStyle { Flat, Smile, Frown, Small }  // 嘴型：平、微笑、下撇、小口
    public enum Brow { Calm, Stern, Fierce }            // 眉型：平和、冷峻、倒豎
    public enum Beard { None, Moustache, Goatee, LongBeard } // 鬍鬚：無、八字鬍、山羊鬍、長鬚
    public enum FaceItem { None, Mask, Eyepatch }       // 臉部配件：無、蒙面巾、眼罩

    /// <summary>服裝。</summary>
    public enum Outfit
    {
        Robe,        // 長袍：下襬及踝，腰繫蔽膝
        ShortJacket, // 短打：短衣、長褲、綁腿
        Armor,       // 鎧甲：胸甲、裙甲
        MonkRobe,    // 僧袍：長袍外斜披袈裟，頸掛念珠
    }

    public enum Sleeve { Wide, Narrow, Bare }                    // 袖型：寬袖、箭袖、赤膊
    public enum Shoulder { None, Pads, FurCollar, FurAndPads }   // 肩部：無、肩甲、毛領、毛領加肩甲

    /// <summary>背部。Auto 依兵器決定：持劍背劍鞘、持弓背箭袋。</summary>
    public enum BackItem { Auto, None, Sheath, TwinSheaths, Quiver, Cape, HatOnBack, Pack, Gourd }

    /// <summary>兵器外觀（招式由角色的武學決定，這裡只管手上拿什麼）。</summary>
    public enum WeaponKind { None, Sword, Saber, Cleaver, Fists, Bow, Knives, Spear, Staff, TwinBlades, Fan, Mace, Flute }

    /// <summary>
    /// 一個角色的完整外觀。顏色為 0xRRGGBB；0 表示採用預設。
    /// model 有填時，畫面層改用 Resources/Units/ 下同名的正式模型（找不到則退回暫定模型）。
    /// </summary>
    [Serializable]
    public sealed class Look
    {
        // 體型：1 為標準比例
        public float height = 1f;   // 身高      0.85 ～ 1.15
        public float girth = 1f;    // 身圍      0.80 ～ 1.35
        public float shoulder = 1f; // 肩寬      0.85 ～ 1.30
        public float headSize = 1f; // 頭的大小  0.88 ～ 1.15

        public Gender gender;       // 體態：男、女
        public float waist = 1f;    // 腰圍      0.85 ～ 1.25
        public float chest = 1f;    // 胸圍      0.85 ～ 1.25
        public float hips = 1f;     // 臀寬      0.85 ～ 1.25
        public float legs = 1f;     // 腿長      0.90 ～ 1.12

        // 五官：每一項有款式、大小（1 為標準，約 0.6～1.6）與位移（-1～1）
        public float faceWidth = 1f;  // 臉寬 0.85 ～ 1.20
        public float faceLength = 1f; // 臉長 0.85 ～ 1.20
        public EyeStyle eyes;
        public float eyeSize = 1f, eyeSpacing, eyeHeight;   // 眼距：負為靠近、正為分開
        public Brow brow;
        public float browSize = 1f, browHeight;
        public NoseStyle nose;
        public float noseSize = 1f, noseHeight;
        public MouthStyle mouth;
        public float mouthSize = 1f, mouthHeight;

        public HairStyle hair;
        public Headwear headwear;
        public Beard beard;
        public FaceItem face;
        public Outfit outfit;
        public Sleeve sleeve;
        public Shoulder shoulders;
        public BackItem back;
        public WeaponKind weapon;

        public uint skin = 0xEDC9A6;   // 膚色
        public uint hairColor = 0x211C1A; // 髮色與鬚色
        public uint cloth = 0x808080;  // 衣服主色
        public uint trim;              // 滾邊、腰帶、褲色、披風；0 表示由主色自動加深
        public uint hat;               // 帽子顏色；0 表示草色

        public bool ribbons; // 髮帶
        public bool pendant; // 腰間玉佩
        public bool gourd;   // 腰間酒葫蘆
        public bool pouch;   // 腰間暗器囊

        public string model = ""; // 正式模型的資源名稱；空白表示用暫定模型

        public Look Clone() { return (Look)MemberwiseClone(); }

        /// <summary>套用體型預設。</summary>
        public Look Body(BodyType t)
        {
            switch (t)
            {
                case BodyType.Burly: height = 1.10f; girth = 1.22f; shoulder = 1.20f; headSize = 0.95f; break;
                case BodyType.Slim: height = 0.94f; girth = 0.90f; shoulder = 0.90f; headSize = 1.03f; break;
                default: height = 1f; girth = 1f; shoulder = 1f; headSize = 1f; break;
            }
            return this;
        }
    }

    /// <summary>預先組好的造型，可直接用在角色範本上，也是混搭的範例。</summary>
    public static class Looks
    {
        // ── 目前登場的角色 ──

        /// <summary>白衣劍客：斗笠、髮帶、背劍、腰懸玉佩。</summary>
        public static readonly Look Swordsman = new Look
        {
            eyes = EyeStyle.Phoenix, nose = NoseStyle.High,
            hair = HairStyle.Ponytail, headwear = Headwear.ConeHat, outfit = Outfit.Robe, sleeve = Sleeve.Wide,
            weapon = WeaponKind.Sword, cloth = 0xE8EEEF, trim = 0x2E5E8E, ribbons = true, pendant = true,
        }.Body(BodyType.Standard);

        /// <summary>力士：赤膊短打、八字鬍、鐵護腕、腰掛酒葫蘆。</summary>
        public static readonly Look Brawler = new Look
        {
            nose = NoseStyle.Wide, faceWidth = 1.10f, browSize = 1.3f,
            hair = HairStyle.Topknot, headwear = Headwear.Headband, brow = Brow.Stern, beard = Beard.Moustache,
            outfit = Outfit.ShortJacket, sleeve = Sleeve.Bare, weapon = WeaponKind.Fists,
            skin = 0xD9A77C, cloth = 0xA8703A, trim = 0x4A2E18, gourd = true,
        }.Body(BodyType.Burly);

        /// <summary>暗器女俠：雙髻、箭袖長袍、飛刀、暗器囊。</summary>
        public static readonly Look KnifeMaiden = new Look
        {
            gender = Gender.Female, eyes = EyeStyle.Big, mouth = MouthStyle.Smile,
            hair = HairStyle.TwinBuns, outfit = Outfit.Robe, sleeve = Sleeve.Narrow, weapon = WeaponKind.Knives,
            skin = 0xF3D6BC, cloth = 0x8E5AA6, trim = 0x3D2352, ribbons = true, pouch = true,
        }.Body(BodyType.Slim);

        /// <summary>山賊：頭巾蒙面、短打、單刀。</summary>
        public static readonly Look Bandit = new Look
        {
            hair = HairStyle.Short, headwear = Headwear.Headwrap, brow = Brow.Fierce, face = FaceItem.Mask,
            outfit = Outfit.ShortJacket, sleeve = Sleeve.Narrow, weapon = WeaponKind.Saber, cloth = 0x5B564F, trim = 0x27231F,
        }.Body(BodyType.Standard);

        /// <summary>弓手：深色斗笠、短打、背箭袋。</summary>
        public static readonly Look Archer = new Look
        {
            hair = HairStyle.Short, headwear = Headwear.ConeHat, brow = Brow.Fierce, outfit = Outfit.ShortJacket, sleeve = Sleeve.Narrow,
            weapon = WeaponKind.Bow, cloth = 0x4E5C46, trim = 0x23291F, hat = 0x3A3632,
        }.Body(BodyType.Slim);

        /// <summary>寨主：高髻金箍、八字鬍、毛領肩甲、披風、大刀。</summary>
        public static readonly Look Warlord = new Look
        {
            eyes = EyeStyle.Narrow, nose = NoseStyle.Wide, mouth = MouthStyle.Frown, faceWidth = 1.08f, browSize = 1.4f,
            hair = HairStyle.HighKnot, headwear = Headwear.GoldCirclet, brow = Brow.Fierce, beard = Beard.Moustache,
            outfit = Outfit.Robe, sleeve = Sleeve.Wide, shoulders = Shoulder.FurAndPads, back = BackItem.Cape,
            weapon = WeaponKind.Cleaver, skin = 0xD9A77C, cloth = 0x701F1C, trim = 0x1B1414,
        }.Body(BodyType.Burly);

        // ── 備用造型（尚未登場，可直接取用）──

        /// <summary>書生：書生巾、寬袖長衫、摺扇、背書箱。</summary>
        public static readonly Look Scholar = new Look
        {
            hair = HairStyle.Topknot, headwear = Headwear.ScholarCap, outfit = Outfit.Robe, sleeve = Sleeve.Wide,
            back = BackItem.Pack, weapon = WeaponKind.Fan, cloth = 0xC9D6C3, trim = 0x4F6B58, pendant = true,
        }.Body(BodyType.Slim);

        /// <summary>武僧：光頭、僧袍袈裟、齊眉棍。</summary>
        public static readonly Look Monk = new Look
        {
            hair = HairStyle.Bald, brow = Brow.Stern, outfit = Outfit.MonkRobe, sleeve = Sleeve.Wide,
            weapon = WeaponKind.Staff, skin = 0xD9A77C, cloth = 0xB8862E, trim = 0x8C3B1E,
        }.Body(BodyType.Burly);

        /// <summary>道長：玉冠、白髮長鬚、背雙劍。</summary>
        public static readonly Look Taoist = new Look
        {
            hair = HairStyle.Topknot, headwear = Headwear.Crown, beard = Beard.LongBeard, outfit = Outfit.Robe, sleeve = Sleeve.Wide,
            back = BackItem.TwinSheaths, weapon = WeaponKind.Sword, cloth = 0x5F6C7A, trim = 0xD9DCD6, hairColor = 0xE6E6E0, ribbons = true,
        }.Body(BodyType.Standard);

        /// <summary>官兵：鐵盔紅纓、鎧甲、長槍。</summary>
        public static readonly Look Soldier = new Look
        {
            hair = HairStyle.Short, headwear = Headwear.Helmet, brow = Brow.Stern, outfit = Outfit.Armor, sleeve = Sleeve.Narrow,
            shoulders = Shoulder.Pads, weapon = WeaponKind.Spear, cloth = 0x8B2F2A, trim = 0x2A2A2E,
        }.Body(BodyType.Standard);

        /// <summary>刺客：兜帽蒙面、黑色短打、雙刃。</summary>
        public static readonly Look Assassin = new Look
        {
            hair = HairStyle.Short, headwear = Headwear.Hood, brow = Brow.Stern, face = FaceItem.Mask,
            outfit = Outfit.ShortJacket, sleeve = Sleeve.Narrow, weapon = WeaponKind.TwinBlades, cloth = 0x2B2D33, trim = 0x15161A, pouch = true,
        }.Body(BodyType.Slim);

        /// <summary>丐俠：披髮、破舊短打、竹棍、背大葫蘆。</summary>
        public static readonly Look Beggar = new Look
        {
            hair = HairStyle.LongLoose, beard = Beard.Goatee, outfit = Outfit.ShortJacket, sleeve = Sleeve.Bare,
            back = BackItem.Gourd, weapon = WeaponKind.Staff, skin = 0xD9A77C, cloth = 0x7B6A4E, trim = 0x4B3D2A,
        }.Body(BodyType.Standard);

        /// <summary>俠女：帷帽垂紗、淺色長裙、長劍。</summary>
        public static readonly Look Swordswoman = new Look
        {
            gender = Gender.Female, eyes = EyeStyle.Phoenix, mouth = MouthStyle.Small,
            hair = HairStyle.LongLoose, headwear = Headwear.VeilHat, outfit = Outfit.Robe, sleeve = Sleeve.Wide,
            weapon = WeaponKind.Sword, skin = 0xF3D6BC, cloth = 0xF0D9DC, trim = 0xA8475A, hat = 0xEFEDE6, pendant = true,
        }.Body(BodyType.Slim);

        /// <summary>鏢師：頭巾、眼罩、輕甲、披風、單刀。</summary>
        public static readonly Look Escort = new Look
        {
            hair = HairStyle.Short, headwear = Headwear.Headwrap, brow = Brow.Stern, beard = Beard.Goatee, face = FaceItem.Eyepatch,
            outfit = Outfit.Armor, sleeve = Sleeve.Narrow, back = BackItem.Cape, weapon = WeaponKind.Saber, cloth = 0x3F5A6B, trim = 0x7A2A22,
        }.Body(BodyType.Burly);

        /// <summary>隱士老者：白髮長鬚、灰袍、洞簫、背著斗笠。</summary>
        public static readonly Look Hermit = new Look
        {
            hair = HairStyle.Topknot, beard = Beard.LongBeard, outfit = Outfit.Robe, sleeve = Sleeve.Wide,
            back = BackItem.HatOnBack, weapon = WeaponKind.Flute, cloth = 0x9A9C97, trim = 0x55585A, hairColor = 0xECECE6, gourd = true,
        }.Body(BodyType.Slim);

        /// <summary>敵將：鐵盔、長鬚、鎧甲、銅錘。</summary>
        public static readonly Look Bruiser = new Look
        {
            hair = HairStyle.Short, headwear = Headwear.Helmet, brow = Brow.Fierce, beard = Beard.LongBeard,
            outfit = Outfit.Armor, sleeve = Sleeve.Bare, shoulders = Shoulder.Pads, weapon = WeaponKind.Mace,
            skin = 0xD9A77C, cloth = 0x4A3B34, trim = 0x1E1A18,
        }.Body(BodyType.Burly);

        /// <summary>全部預設造型，供創角畫面套用與測試逐一檢視。</summary>
        public static readonly Look[] All =
        {
            Swordsman, Brawler, KnifeMaiden, Bandit, Archer, Warlord, Scholar, Monk,
            Taoist, Soldier, Assassin, Beggar, Swordswoman, Escort, Hermit, Bruiser,
        };

        public static readonly string[] Names =
        {
            "白衣劍客", "力士", "暗器女俠", "山賊", "弓手", "寨主", "書生", "武僧",
            "道長", "官兵", "刺客", "丐俠", "俠女", "鏢師", "隱士", "敵將",
        };
    }
}
