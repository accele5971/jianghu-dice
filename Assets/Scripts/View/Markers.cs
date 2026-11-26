using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jianghu
{
    /// <summary>
    /// 格子高亮（移動範圍、路徑、目標、範圍招式）。平時地面不畫格線，只有這裡會把格子顯示出來。
    /// 全部畫在同一張動態 Mesh 上，以頂點色區分：不論幾格都是 1 次繪製呼叫，
    /// 而且只在高亮內容改變時才重建。地面上的格子會細分並貼合坡面。
    /// </summary>
    public sealed class Markers
    {
        const int Sub = 4;          // 地面格的細分數
        const float Inset = 0.05f;  // 格與格之間留縫，看得出一格一格

        readonly VoxelWorld w;
        readonly TerrainView terrain;
        readonly Mesh mesh;
        readonly List<Vector3> verts = new List<Vector3>(4096);
        readonly List<Color32> cols = new List<Color32>(4096);
        readonly List<int> tris = new List<int>(6144);

        public Markers(VoxelWorld world, TerrainView terrain, Transform parent)
        {
            w = world;
            this.terrain = terrain;
            mesh = new Mesh { name = "Markers", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            var go = new GameObject("Markers");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mats.Glow(Color.white);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        public void Clear()
        {
            verts.Clear(); cols.Clear(); tris.Clear();
        }

        public void Add(int cell, Color32 color, float lift = 0)
        {
            float x0 = w.X(cell) + Inset, z0 = w.Y(cell) + Inset, size = 1 - Inset * 2;

            if (!terrain.IsGroundCell(cell))
            {
                // 橋面、屋頂、樹冠：平的一片
                float y = w.Z(cell) * Vis.H + 0.04f + lift;
                int n = verts.Count;
                verts.Add(new Vector3(x0, y, z0));
                verts.Add(new Vector3(x0, y, z0 + size));
                verts.Add(new Vector3(x0 + size, y, z0 + size));
                verts.Add(new Vector3(x0 + size, y, z0));
                for (int k = 0; k < 4; k++) cols.Add(color);
                tris.Add(n); tris.Add(n + 1); tris.Add(n + 2);
                tris.Add(n); tris.Add(n + 2); tris.Add(n + 3);
                return;
            }

            // 地面：細分後逐點取地表高度，貼在坡面上
            int b = verts.Count, row = Sub + 1;
            for (int j = 0; j <= Sub; j++)
                for (int i = 0; i <= Sub; i++)
                {
                    float px = x0 + size * i / Sub, pz = z0 + size * j / Sub;
                    verts.Add(new Vector3(px, terrain.HeightAt(px, pz) + 0.045f + lift, pz));
                    cols.Add(color);
                }
            for (int j = 0; j < Sub; j++)
                for (int i = 0; i < Sub; i++)
                {
                    int a = b + i + j * row;
                    tris.Add(a); tris.Add(a + row); tris.Add(a + row + 1);
                    tris.Add(a); tris.Add(a + row + 1); tris.Add(a + 1);
                }
        }

        public void Commit()
        {
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0, false);
            mesh.bounds = new Bounds(new Vector3(w.W * 0.5f, w.H * Vis.H * 0.5f, w.D * 0.5f), new Vector3(w.W, w.H * Vis.H, w.D));
        }
    }
}
