using UnityEngine;

namespace Jianghu
{
    /// <summary>
    /// 環繞鏡頭。右鍵拖曳或 Q/E 旋轉、滾輪縮放、方向鍵或中鍵拖曳平移。
    /// 左鍵完全留給選格子，不會和鏡頭操作衝突。
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Vector3 target = new Vector3(12, 3, 12);
        public float yaw = 35, pitch = 48, dist = 32;
        public Vector3 boundsMax = new Vector3(24, 16, 24);

        /// <summary>這一幀鏡頭是否有變動（供按需渲染與重新拾取使用）。</summary>
        public bool Changed { get; private set; }

        bool focusing;
        Vector3 focusTo;
        float shake;

        /// <summary>鏡頭震動（受擊、會心時用），強度會自行衰減。</summary>
        public void Shake(float amount) { shake = Mathf.Max(shake, amount); }

        /// <summary>直接對準某一點，不往地圖中央偏移（創角特寫用）。</summary>
        public void FocusExact(Vector3 worldPoint)
        {
            focusTo = worldPoint;
            focusing = true;
        }

        public void Focus(Vector3 worldPoint)
        {
            // 焦點略偏向地圖中央：角色站在角落時，畫面才不會有一半是空的
            var centre = new Vector3(boundsMax.x * 0.5f, worldPoint.y, boundsMax.z * 0.5f);
            focusTo = Vector3.Lerp(worldPoint, centre, 0.25f);
            focusing = true;
        }

        void LateUpdate()
        {
            Vector3 t0 = target;
            float y0 = yaw, p0 = pitch, d0 = dist;
            float dt = Time.unscaledDeltaTime;

            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * 4f;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3f, 12f, 85f);
            }
            if (Input.GetKey(KeyCode.Q)) yaw += 90 * dt;
            if (Input.GetKey(KeyCode.E)) yaw -= 90 * dt;

            var flat = Quaternion.Euler(0, yaw, 0);
            Vector3 pan = Vector3.zero;
            if (Input.GetKey(KeyCode.UpArrow)) pan += Vector3.forward;
            if (Input.GetKey(KeyCode.DownArrow)) pan += Vector3.back;
            if (Input.GetKey(KeyCode.LeftArrow)) pan += Vector3.left;
            if (Input.GetKey(KeyCode.RightArrow)) pan += Vector3.right;
            if (pan != Vector3.zero) { target += flat * pan.normalized * (dist * 0.6f * dt); focusing = false; }
            if (Input.GetMouseButton(2))
            {
                target -= flat * new Vector3(Input.GetAxis("Mouse X"), 0, Input.GetAxis("Mouse Y")) * (dist * 0.03f);
                focusing = false;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0) dist = Mathf.Clamp(dist * (1 - scroll * 0.1f), 2.2f, 60f);

            if (focusing)
            {
                target = Vector3.Lerp(target, focusTo, 1 - Mathf.Exp(-8 * dt));
                if ((target - focusTo).sqrMagnitude < 0.0004f) { target = focusTo; focusing = false; }
            }

            target.x = Mathf.Clamp(target.x, 0, boundsMax.x);
            target.y = Mathf.Clamp(target.y, 0, boundsMax.y);
            target.z = Mathf.Clamp(target.z, 0, boundsMax.z);

            var rot = Quaternion.Euler(pitch, yaw, 0);
            var pos = target - rot * Vector3.forward * dist;
            bool shaking = shake > 0.002f;
            if (shaking)
            {
                pos += rot * new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0) * shake;
                shake *= Mathf.Exp(-14 * dt);
            }
            else shake = 0;
            transform.SetPositionAndRotation(pos, rot);

            Changed = shaking || target != t0 || yaw != y0 || pitch != p0 || dist != d0;
        }
    }
}
