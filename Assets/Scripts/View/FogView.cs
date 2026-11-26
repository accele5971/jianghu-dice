using UnityEngine;

namespace Jianghu
{
    /// <summary>
    /// 戰爭迷霧的畫面端。把視野資料寫進一張「每格一個像素」的小貼圖，交給著色器取樣：
    /// R = 此刻看得到、G = 曾經看過。貼圖用雙線性過濾，所以邊界是柔和的；
    /// 數值逐幀往目標靠近，所以揭開與變暗都是漸變的。
    /// </summary>
    public sealed class FogView
    {
        const float LitSpeed = 4.5f, SeenSpeed = 2.8f;

        static readonly int FogId = Shader.PropertyToID("_JhFog"), SizeId = Shader.PropertyToID("_JhFogSize");
        static readonly int MistId = Shader.PropertyToID("_JhMist"), OnId = Shader.PropertyToID("_JhFogOn");

        readonly Texture2D tex;
        readonly Color32[] px;
        readonly float[] lit, seen;       // 目前顯示的值
        readonly byte[] litTo, seenTo;    // 目標值
        bool animating;

        public FogView(VoxelWorld w, Color mist)
        {
            int n = w.W * w.D;
            px = new Color32[n];
            lit = new float[n]; seen = new float[n];
            litTo = new byte[n]; seenTo = new byte[n];
            tex = new Texture2D(w.W, w.D, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            Upload();
            Shader.SetGlobalTexture(FogId, tex);
            Shader.SetGlobalVector(SizeId, new Vector4(w.W, w.D, 0, 0));
            Shader.SetGlobalColor(MistId, mist);
            SetEnabled(false);
        }

        public void SetEnabled(bool on) { Shader.SetGlobalFloat(OnId, on ? 1 : 0); }

        /// <summary>設定新的目標狀態；實際變化由 Tick 漸進。</summary>
        public void Apply(Vision v)
        {
            System.Array.Copy(v.lit, litTo, litTo.Length);
            System.Array.Copy(v.seen, seenTo, seenTo.Length);
            animating = true;
        }

        /// <summary>推進漸變。回傳是否仍在變化（呼叫端據此維持全速渲染）。</summary>
        public bool Tick(float dt)
        {
            if (!animating) return false;
            bool moving = false;
            for (int i = 0; i < lit.Length; i++)
            {
                if (lit[i] != litTo[i]) { lit[i] = Mathf.MoveTowards(lit[i], litTo[i], dt * LitSpeed); moving = true; }
                if (seen[i] != seenTo[i]) { seen[i] = Mathf.MoveTowards(seen[i], seenTo[i], dt * SeenSpeed); moving = true; }
            }
            Upload();
            animating = moving;
            return true;
        }

        void Upload()
        {
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32((byte)(lit[i] * 255), (byte)(seen[i] * 255), 0, 255);
            tex.SetPixels32(px);
            tex.Apply(false);
        }

        /// <summary>結束時關掉全域開關，避免影響編輯器裡其他使用同一著色器的物件。</summary>
        public void Dispose()
        {
            SetEnabled(false);
            if (tex != null) Object.Destroy(tex);
        }
    }
}
