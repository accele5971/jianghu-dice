using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jianghu
{
    /// <summary>畫面層的比例設定。</summary>
    public static class Vis
    {
        /// <summary>每一層高度對應的世界單位。半格：高度差 1 層是緩坡，2 層以上才是岩壁。</summary>
        public const float H = 0.5f;

        /// <summary>格子 → 腳底的世界座標。遊戲 (x, y, z高) → Unity (x, z高 × H, y)。</summary>
        public static Vector3 Cell(VoxelWorld w, int cell)
        {
            return new Vector3(w.X(cell) + 0.5f, w.Z(cell) * H, w.Y(cell) + 0.5f);
        }

        /// <summary>穩定的雜湊亂數（0～1），讓植被與顏色變化每次都一樣。</summary>
        public static float Hash(int a, int b, int c = 0)
        {
            return Hashing.Hash(a, b, c);
        }
    }

    /// <summary>
    /// 平面著色（每個三角形各自的法線）＋頂點色的網格建構器，用於地形與場景物件。
    /// 三角形的正反面由「朝外提示向量」自動決定，呼叫端不必管頂點順序。
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> norms = new List<Vector3>();
        readonly List<Color32> cols = new List<Color32>();
        readonly List<int> tris = new List<int>();

        public int VertexCount { get { return verts.Count; } }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color32 col, Vector3 outward)
        {
            // Unity 以順時針為正面，此時 Cross(b-a, c-a) 指向觀看者
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(n, outward) < 0) { var t = b; b = c; c = t; n = -n; }
            n.Normalize();
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            norms.Add(n); norms.Add(n); norms.Add(n);
            cols.Add(col); cols.Add(col); cols.Add(col);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        /// <summary>三個頂點各自有顏色的三角形，用於不受光的漸層（遠山、霧面）。</summary>
        public void TriGradient(Vector3 a, Vector3 b, Vector3 c, Color32 ca, Color32 cb, Color32 cc)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            norms.Add(Vector3.up); norms.Add(Vector3.up); norms.Add(Vector3.up);
            cols.Add(ca); cols.Add(cb); cols.Add(cc);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        /// <summary>四邊形，頂點須依環繞順序給。</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col, Vector3 outward)
        {
            Tri(a, b, c, col, outward);
            Tri(a, c, d, col, outward);
        }

        /// <summary>長方體；yaw、pitch 為繞 Y、X 軸的旋轉角度。</summary>
        public void Box(Vector3 center, Vector3 size, Color32 col, float yaw = 0, float pitch = 0)
        {
            var rot = Quaternion.Euler(pitch, yaw, 0);
            Vector3 h = size * 0.5f;
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++)
                p[i] = center + rot * new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
            Quad(p[0], p[2], p[6], p[4], col, rot * Vector3.left);
            Quad(p[1], p[3], p[7], p[5], col, rot * Vector3.right);
            Quad(p[0], p[1], p[5], p[4], col, rot * Vector3.down);
            Quad(p[2], p[3], p[7], p[6], col, rot * Vector3.up);
            Quad(p[0], p[1], p[3], p[2], col, rot * Vector3.back);
            Quad(p[4], p[5], p[7], p[6], col, rot * Vector3.forward);
        }

        /// <summary>
        /// 旋轉體：由下而上的一串（半徑、高度）輪廓繞 Y 軸旋轉而成。
        /// 用來做樹幹、柱子等。capTop 會把頂端封起來。
        /// </summary>
        public void Lathe(Vector3 origin, float[] radius, float[] height, int sides, Color32 col, bool capTop, float spin = 0)
        {
            for (int k = 0; k + 1 < radius.Length; k++)
            {
                float slope = radius[k + 1] < radius[k] ? 0.6f : radius[k + 1] > radius[k] ? -0.6f : 0f;
                for (int i = 0; i < sides; i++)
                {
                    float a0 = spin + i * Mathf.PI * 2 / sides, a1 = spin + (i + 1) * Mathf.PI * 2 / sides, am = (a0 + a1) * 0.5f;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                    Vector3 up0 = Vector3.up * height[k], up1 = Vector3.up * height[k + 1];
                    var outward = new Vector3(Mathf.Cos(am), slope, Mathf.Sin(am));
                    Quad(origin + d0 * radius[k] + up0, origin + d1 * radius[k] + up0,
                         origin + d1 * radius[k + 1] + up1, origin + d0 * radius[k + 1] + up1, col, outward);
                }
            }
            if (!capTop) return;
            int last = radius.Length - 1;
            if (radius[last] <= 1e-4f) return;
            Vector3 top = origin + Vector3.up * height[last];
            for (int i = 0; i < sides; i++)
            {
                float a0 = spin + i * Mathf.PI * 2 / sides, a1 = spin + (i + 1) * Mathf.PI * 2 / sides;
                Tri(top, top + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius[last],
                    top + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius[last], col, Vector3.up);
            }
        }

        /// <summary>不規則的低面數球體（樹冠、石塊用）。jitter 是半徑的隨機擾動比例。</summary>
        public void Blob(Vector3 center, Vector3 radii, Color32 col, int seed, float jitter, int rings = 4, int segs = 7, float shadeVar = 0.08f)
        {
            var pts = new Vector3[(rings + 1) * segs];
            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings, phi = (v - 0.5f) * Mathf.PI;
                for (int s = 0; s < segs; s++)
                {
                    float th = (s + (r & 1) * 0.5f) * Mathf.PI * 2 / segs;
                    float j = 1 + (Vis.Hash(seed, r * 31 + s, 7) - 0.5f) * 2 * jitter;
                    var dir = new Vector3(Mathf.Cos(phi) * Mathf.Cos(th), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Sin(th));
                    if (r == 0 || r == rings) { dir = new Vector3(0, Mathf.Sin(phi), 0); j = 1; }
                    pts[r * segs + s] = center + Vector3.Scale(dir, radii) * j;
                }
            }
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segs; s++)
                {
                    int s1 = (s + 1) % segs;
                    Vector3 a = pts[r * segs + s], b = pts[r * segs + s1], c = pts[(r + 1) * segs + s1], d = pts[(r + 1) * segs + s];
                    // 每個面的明暗略有不同，讓低面數的塊面感更明顯
                    float k = 1 + (Vis.Hash(seed, r * 17 + s, 3) - 0.5f) * 2 * shadeVar;
                    var cc = new Color32((byte)Mathf.Clamp(col.r * k, 0, 255), (byte)Mathf.Clamp(col.g * k, 0, 255), (byte)Mathf.Clamp(col.b * k, 0, 255), 255);
                    if (r == 0) Tri(a, c, d, cc, (c + d) * 0.5f - center);
                    else if (r == rings - 1) Tri(a, b, c, cc, (a + b) * 0.5f - center);
                    else
                    {
                        Tri(a, b, c, cc, (a + b + c) / 3 - center);
                        Tri(a, c, d, cc, (a + c + d) / 3 - center);
                    }
                }
            }
        }

        /// <summary>目前所有頂點的範圍。</summary>
        public Bounds Bounds()
        {
            if (verts.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
            var b = new Bounds(verts[0], Vector3.zero);
            for (int i = 1; i < verts.Count; i++) b.Encapsulate(verts[i]);
            return b;
        }

        /// <summary>
        /// 圖示用的網格：先整個轉成 rot 的角度，再把光照直接烘進頂點色
        /// （之後用不受光的著色器畫，才不會受場景的燈光與迷霧影響）。不會改動這個建構器。
        /// </summary>
        public Mesh ToMeshBaked(string name, Quaternion rot, Vector3 lightDir)
        {
            lightDir.Normalize();
            var v = new List<Vector3>(verts.Count);
            var c = new List<Color32>(verts.Count);
            for (int i = 0; i < verts.Count; i++)
            {
                v.Add(rot * verts[i]);
                float k = 0.52f + 0.48f * Mathf.Max(0, Vector3.Dot(rot * norms[i], lightDir));
                var col = cols[i];
                c.Add(new Color32((byte)(col.r * k), (byte)(col.g * k), (byte)(col.b * k), 255));
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            // 大網格（地形）上傳後釋放 CPU 端副本；小網格留著，角色的零件之後要讀出來合併
            mesh.UploadMeshData(verts.Count > 8000);
            return mesh;
        }

        public MeshRenderer ToObject(string name, Transform parent, Material mat, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = ToMesh(name);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return mr;
        }
    }

    /// <summary>角色用的共用網格：平滑著色、有索引、不帶顏色（顏色由材質決定）。</summary>
    public static class Shapes
    {
        /// <summary>
        /// 平滑的旋轉體。squashZ 把前後壓扁（軀幹、袍子不是正圓）。
        /// 高度可以遞減（袖子、腿由上往下長）。
        /// </summary>
        public static Mesh Lathe(string name, float[] radius, float[] height, int sides, bool capStart, bool capEnd, float squashZ = 1f)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            int rings = radius.Length;
            for (int k = 0; k < rings; k++)
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2 / sides;
                    v.Add(new Vector3(Mathf.Cos(a) * radius[k], height[k], Mathf.Sin(a) * radius[k] * squashZ));
                }

            for (int k = 0; k + 1 < rings; k++)
                for (int i = 0; i < sides; i++)
                {
                    int i1 = (i + 1) % sides;
                    int a = k * sides + i, b = k * sides + i1, c = (k + 1) * sides + i1, d = (k + 1) * sides + i;
                    float am = (i + 0.5f) * Mathf.PI * 2 / sides;
                    var outward = new Vector3(Mathf.Cos(am), 0, Mathf.Sin(am));
                    Add(v, t, a, b, c, outward);
                    Add(v, t, a, c, d, outward);
                }

            if (capStart) Cap(v, t, 0, sides, height[0], height[0] < height[rings - 1] ? Vector3.down : Vector3.up);
            if (capEnd) Cap(v, t, (rings - 1) * sides, sides, height[rings - 1], height[0] < height[rings - 1] ? Vector3.up : Vector3.down);

            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false); // 保留可讀：角色的零件之後要合併
            return mesh;
        }

        static void Cap(List<Vector3> v, List<int> t, int ring0, int sides, float y, Vector3 outward)
        {
            int c = v.Count;
            v.Add(new Vector3(0, y, 0));
            for (int i = 0; i < sides; i++) Add(v, t, c, ring0 + i, ring0 + (i + 1) % sides, outward);
        }

        static void Add(List<Vector3> v, List<int> t, int a, int b, int c, Vector3 outward)
        {
            var n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (n.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(n, outward) < 0) { int s = b; b = c; c = s; }
            t.Add(a); t.Add(b); t.Add(c);
        }
    }
}
