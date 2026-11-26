using UnityEngine;

namespace Jianghu
{
    /// <summary>啟動點。不需要場景檔：在任何場景按 Play，遊戲都會由程式生成。</summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { NewGame(); }

        public static void NewGame()
        {
            new GameObject("Jianghu").AddComponent<GameController>();
        }
    }

    /// <summary>共用材質。只用 Unity 內建且預設會打包的著色器，不需自訂 shader。</summary>
    public static class Mats
    {
        static Shader lit, unlit;
        static readonly System.Collections.Generic.Dictionary<Color32, Material> solids =
            new System.Collections.Generic.Dictionary<Color32, Material>();

        static Shader Find(params string[] names)
        {
            foreach (var n in names)
            {
                var s = Shader.Find(n);
                if (s != null) return s;
            }
            return null;
        }

        public static Shader Lit
        {
            get { return lit != null ? lit : (lit = Find("Legacy Shaders/Diffuse", "Standard", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Lit")); }
        }

        /// <summary>地形：調色盤 × 頂點色（環境光遮蔽）。自訂著色器不存在時退回內建的，只是沒有遮蔽效果。</summary>
        public static Shader Terrain
        {
            get { var s = Shader.Find("Jianghu/Terrain"); return s != null ? s : Lit; }
        }

        /// <summary>不受光、半透明、吃頂點色。</summary>
        public static Shader Unlit
        {
            get { return unlit != null ? unlit : (unlit = Find("Sprites/Default", "UI/Default", "Unlit/Transparent")); }
        }

        public static Color FromHex(uint rgb)
        {
            return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        }

        /// <summary>同色共用同一材質。</summary>
        public static Material Solid(Color c)
        {
            Color32 key = c;
            Material m;
            if (!solids.TryGetValue(key, out m) || m == null)
            {
                m = new Material(Lit) { color = c };
                solids[key] = m;
            }
            return m;
        }

        static readonly System.Collections.Generic.Dictionary<Color32, Material> units =
            new System.Collections.Generic.Dictionary<Color32, Material>();
        static readonly Material[] silhouettes = new Material[2];

        /// <summary>角色本體材質（會寫模板，供透視剪影使用）。找不到自訂著色器時退回一般受光材質。</summary>
        public static Material Unit(Color c)
        {
            var sh = Shader.Find("Jianghu/UnitLit");
            if (sh == null) return Solid(c);
            Color32 key = c;
            Material m;
            if (!units.TryGetValue(key, out m) || m == null)
            {
                m = new Material(sh) { color = c };
                units[key] = m;
            }
            return m;
        }

        /// <summary>透視剪影材質（依陣營上色）。著色器不存在時回傳 null，角色就不畫剪影。</summary>
        public static Material Silhouette(int team)
        {
            if (silhouettes[team] == null)
            {
                var sh = Shader.Find("Jianghu/Silhouette");
                if (sh == null || Shader.Find("Jianghu/UnitLit") == null) return null;
                silhouettes[team] = new Material(sh)
                {
                    color = team == 0 ? new Color(0.30f, 0.62f, 1f, 0.55f) : new Color(1f, 0.32f, 0.26f, 0.55f),
                };
            }
            return silhouettes[team];
        }

        /// <summary>水面：半透明並套用迷霧。自訂著色器不存在時退回一般的半透明材質。</summary>
        public static Material Water(Color c)
        {
            var sh = Shader.Find("Jianghu/Water");
            return sh != null ? new Material(sh) { color = c } : Glow(c);
        }

        public static Material Glow(Color c)
        {
            var m = new Material(Unlit) { color = c };
            // Sprites/Default 會用這兩個參數做翻轉與染色；用在一般 Mesh 上時明確給定，不依賴預設值
            if (m.HasProperty("_Flip")) m.SetVector("_Flip", Vector4.one);
            if (m.HasProperty("_RendererColor")) m.SetColor("_RendererColor", Color.white);
            return m;
        }
    }
}
