using System.Collections.Generic;
using UnityEngine;

namespace Jianghu
{
    /// <summary>
    /// 把裝備與道具的模型畫成小圖示（面板用）。每個模型只畫一次，之後取快取。
    /// 做法：把模型轉到好看的角度、把光照烘進頂點色，用一台只看得到它的正交相機畫進小貼圖。
    /// </summary>
    public static class Icons
    {
        const int Size = 96, Layer = 31;
        static readonly Dictionary<object, Texture2D> cache = new Dictionary<object, Texture2D>();
        static Material mat;
        static Camera cam;

        public static Texture2D Of(Gear g)
        {
            if (g == null) return null;
            Texture2D t;
            if (cache.TryGetValue(g, out t) && t != null) return t;
            return cache[g] = Bake(GearModels.Icon(g), g.name);
        }

        public static Texture2D Of(Item it)
        {
            if (it == null) return null;
            Texture2D t;
            if (cache.TryGetValue(it, out t) && t != null) return t;
            return cache[it] = Bake(GearModels.Item(it), it.name);
        }

        /// <summary>目前快取了幾張圖示。</summary>
        public static int Count { get { return cache.Count; } }

        static Texture2D Bake(MeshBuilder mb, string name)
        {
            if (mb.VertexCount == 0) return null;
            if (mat == null)
            {
                var sh = Shader.Find("Jianghu/Icon");
                if (sh == null) return null; // 著色器不在就不顯示圖示，面板照常運作
                mat = new Material(sh);
            }

            // 細長的東西斜著擺才放得下：沿 Z 的刀劍轉成斜向右上，沿 Y 的長兵向右傾
            Vector3 s = mb.Bounds().size;
            Quaternion rot;
            if (s.z > Mathf.Max(s.x, s.y) * 1.3f) rot = Quaternion.Euler(0, 0, 45) * Quaternion.Euler(0, 90, 0) * Quaternion.Euler(0, 0, 20);
            else if (s.y > Mathf.Max(s.x, s.z) * 2.2f) rot = Quaternion.Euler(0, 0, -42);
            // 平躺的薄片（摺扇）：立起來正對鏡頭
            else if (s.y < Mathf.Min(s.x, s.z) * 0.3f) rot = Quaternion.Euler(-68, 0, 0);
            // 其餘：模型的正面是 +Z，鏡頭在 -Z 那一側，所以轉半圈讓正面朝鏡頭，再略為側轉、俯視
            else rot = Quaternion.Euler(14, 0, 0) * Quaternion.Euler(0, 180 + 24, 0);

            var mesh = mb.ToMeshBaked(name, rot, new Vector3(-0.45f, 0.75f, -0.5f));
            var go = new GameObject("IconModel");
            go.layer = Layer;
            go.transform.position = new Vector3(0, -2000, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;

            if (cam == null)
            {
                var cg = new GameObject("IconCamera");
                cg.hideFlags = HideFlags.HideAndDontSave;
                cam = cg.AddComponent<Camera>();
                cam.enabled = false; // 只在需要時手動畫一次
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.cullingMask = 1 << Layer;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 20;
                cam.allowHDR = false;
                cam.allowMSAA = false;
            }
            Bounds b = mesh.bounds;
            Vector3 centre = go.transform.position + b.center;
            cam.orthographicSize = Mathf.Max(0.03f, Mathf.Max(b.extents.x, b.extents.y) * 1.12f);
            cam.transform.SetPositionAndRotation(centre + Vector3.back * 8, Quaternion.identity);

            var rt = RenderTexture.GetTemporary(Size, Size, 16, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Icon " + name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(mesh);
            return tex;
        }
    }
}
