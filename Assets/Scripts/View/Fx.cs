using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jianghu
{
    /// <summary>飛行物與範圍圈。物件只建立一次，重複使用。</summary>
    public sealed class Fx
    {
        readonly Transform shot, ring;
        readonly Material ringMat;

        public Fx(Transform parent)
        {
            shot = Make(PrimitiveType.Sphere, parent, Mats.Solid(new Color(0.92f, 0.92f, 0.95f)));
            shot.localScale = Vector3.one * 0.14f;
            ringMat = Mats.Glow(new Color(1f, 0.55f, 0.15f, 0.6f));
            ring = Make(PrimitiveType.Cylinder, parent, ringMat);
        }

        static Transform Make(PrimitiveType type, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
            return go.transform;
        }

        public IEnumerator Shot(Vector3 from, Vector3 to, float dur)
        {
            shot.gameObject.SetActive(true);
            float arc = Vector3.Distance(from, to) * 0.06f;
            for (float t = 0; t < 1; t += Time.deltaTime / dur)
            {
                var p = Vector3.Lerp(from, to, t);
                p.y += Mathf.Sin(t * Mathf.PI) * arc;
                shot.position = p;
                yield return null;
            }
            shot.gameObject.SetActive(false);
        }

        Transform tossed;
        MeshFilter tossedMesh;

        /// <summary>把一個有模型的東西丟出去：拋物線飛行，邊飛邊翻滾。</summary>
        public IEnumerator Toss(Mesh mesh, Material mat, Vector3 from, Vector3 to, float dur)
        {
            if (tossed == null)
            {
                var go = new GameObject("Tossed");
                go.transform.SetParent(shot.parent, false);
                tossedMesh = go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.On;
                tossed = go.transform;
            }
            tossed.GetComponent<MeshRenderer>().sharedMaterial = mat;
            tossedMesh.sharedMesh = mesh;
            tossed.localScale = Vector3.one * 1.5f; // 飛在空中時放大一點才看得清楚
            tossed.gameObject.SetActive(true);
            float arc = 0.5f + Vector3.Distance(from, to) * 0.16f;
            for (float t = 0; t < 1; t += Time.deltaTime / dur)
            {
                var p = Vector3.Lerp(from, to, t);
                p.y += Mathf.Sin(t * Mathf.PI) * arc;
                tossed.position = p;
                tossed.rotation = Quaternion.Euler(t * 540f, t * 200f, 0);
                yield return null;
            }
            tossed.gameObject.SetActive(false);
        }

        public IEnumerator Ring(Vector3 center, float radius)
        {
            ring.gameObject.SetActive(true);
            ring.position = center + Vector3.up * 0.12f;
            var c = ringMat.color;
            for (float t = 0; t < 1; t += Time.deltaTime / 0.4f)
            {
                float d = Mathf.Lerp(0.3f, radius * 2 + 1, t);
                ring.localScale = new Vector3(d, 0.03f, d);
                c.a = 0.6f * (1 - t);
                ringMat.color = c;
                yield return null;
            }
            ring.gameObject.SetActive(false);
        }
    }
}
