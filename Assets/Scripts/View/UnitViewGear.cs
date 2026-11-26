using UnityEngine;

namespace Jianghu
{
    /// <summary>
    /// 角色模型的裝備部分：每一件裝備穿上身都有自己的模型。
    /// 兵器與盾拿在手上；衣甲、護腿、護腕、腰帶、靴子、飾品是疊在身體對應部位上的零件。
    /// （頭上戴的與背上背的沿用外觀資料的款式，由規則層在換裝時一併改掉。）
    /// </summary>
    public sealed partial class UnitView
    {
        static readonly Color LeatherCol = new Color(0.47f, 0.32f, 0.20f), DarkLeatherCol = new Color(0.28f, 0.20f, 0.14f);
        static readonly Color MailCol = new Color(0.62f, 0.64f, 0.68f), NightCol = new Color(0.12f, 0.12f, 0.15f), SashRed = new Color(0.50f, 0.14f, 0.13f);
        static readonly Color CloudWhite = new Color(0.92f, 0.91f, 0.87f);

        Gear G(Unit u, Slot s) { return u.gear[(int)s]; }

        // 會改掉衣服顏色的裝備（要在組裝身體之前決定）
        void GearColors(Unit u)
        {
            if (G(u, Slot.Upper) == Gears.NightSuit)
            {
                cloth = NightCol;
                trim = new Color(0.08f, 0.08f, 0.10f);
                accent = SashRed;
            }
        }

        // 手上的兵器：每一件都有自己的模型，握法決定姿勢
        void BuildGearWeapon(Gear w)
        {
            Hold grip;
            var mesh = GearModels.Weapon(w, out grip).ToMesh(w.name);
            switch (grip)
            {
                case Hold.TwoHand:
                    Prop(socketHandR, mesh);
                    twoHanded = true;
                    break;
                case Hold.Pole:
                    Prop(socketHandR, mesh);
                    poleWeapon = true;
                    break;
                case Hold.BowLeft:
                    Prop(socketHandL, mesh);
                    bowInLeftHand = true;
                    break;
                case Hold.Pair:
                    Prop(socketHandR, mesh);
                    Prop(socketHandL, GearModels.Weapon(w, out grip).ToMesh(w.name));
                    break;
                default:
                    Prop(socketHandR, mesh);
                    break;
            }

            // 暗器：腰間另外插著備用的
            if (w.cls == WeaponClass.Thrown)
            {
                var belt = new MeshBuilder();
                if (w == Gears.Pebbles) GearModels.Pouch(belt);
                else for (int i = -1; i <= 1; i++) belt.Box(new Vector3(i * 0.075f, 0, 0), new Vector3(w == Gears.BoneNeedles ? 0.008f : 0.022f, 0.12f, 0.010f), Steel);
                Prop(hips, belt.ToMesh("Spare")).transform.localPosition = w == Gears.Pebbles
                    ? new Vector3(0.12f * g, 0.545f + dL, waistZ * 0.5f) : new Vector3(0, 0.56f + dL, waistZ + 0.014f);
            }
        }

        void BuildGear(Unit u)
        {
            // ── 副手：盾 ──
            var off = G(u, Slot.OffHand);
            if (off != null && !off.IsWeapon)
            {
                var t = Prop(socketHandL, GearModels.Shield(off).ToMesh(off.name)).transform;
                t.localPosition = new Vector3(-0.07f, 0.06f, 0.03f);
                t.localRotation = Quaternion.Euler(0, -38, 0);
            }

            // ── 上身 ──
            var up = G(u, Slot.Upper);
            if (up == Gears.Leather)
            {
                // 皮甲：胸前交叉的皮帶與護胸
                Part(cubeMesh, upper, Up + new Vector3(0, 0.80f, chestZ * 0.96f), new Vector3(0.20f * g, 0.20f, 0.03f), LeatherCol, false);
                for (int k = -1; k <= 1; k += 2)
                    Part(cubeMesh, upper, Up + new Vector3(k * 0.07f * g, 0.86f, chestZ * 0.99f), new Vector3(0.04f, 0.26f, 0.03f), DarkLeatherCol, false, new Vector3(0, 0, k * 24));
            }
            else if (up == Gears.Chain)
            {
                // 鎖子甲：一圈圈的鐵環
                for (int r = 0; r < 4; r++)
                    Part(bandMesh, upper, Up + new Vector3(0, 0.66f + r * 0.075f, 0), new Vector3(0.232f * g, 0.016f, 0.184f * g), MailCol, false);
            }
            else if (up == Gears.Plate)
            {
                // 鐵甲：兩片護肩，金邊
                foreach (var sh in new[] { shoulderL, shoulderR_ })
                {
                    Part(sphereMesh, sh, new Vector3(0, 0.0f, 0), new Vector3(0.21f, 0.13f, 0.21f), ArmorCol, false);
                    Part(bandMesh, sh, new Vector3(0, -0.045f, 0), new Vector3(0.10f, 0.018f, 0.10f), GoldCol, false);
                }
            }
            else if (up == Gears.NightSuit)
            {
                // 夜行衣：腰間一道暗紅腰帶，頸間的蒙面巾垂在胸前
                Part(bandMesh, hips, new Vector3(0, 0.615f + dL, 0), new Vector3((waistZ / 0.86f + 0.006f), 0.075f, waistZ + 0.006f), SashRed, false);
                Part(cubeMesh, upper, Up + new Vector3(0, 0.965f, chestZ * 0.55f), new Vector3(0.15f, 0.06f, 0.05f), NightCol, false);
            }

            // ── 下身 ──
            var low = G(u, Slot.Lower);
            if (low == Gears.Greaves)
            {
                foreach (var hip in new[] { hipL, hipR })
                    Part(cubeMesh, hip, new Vector3(0, -0.20f - dL * 0.4f, 0.052f), new Vector3(0.105f, 0.13f, 0.035f), LeatherCol, false);
            }
            else if (low == Gears.Tassets)
            {
                // 鐵裙甲：腰下垂著的甲片
                for (int k = -1; k <= 1; k++)
                {
                    Part(cubeMesh, hips, new Vector3(k * 0.115f * g, 0.50f + dL, waistZ * (k == 0 ? 1.12f : 0.95f) + 0.02f), new Vector3(0.105f, 0.20f, 0.022f), ArmorCol, false);
                    Part(cubeMesh, hips, new Vector3(k * 0.115f * g, 0.405f + dL, waistZ * (k == 0 ? 1.12f : 0.95f) + 0.024f), new Vector3(0.107f, 0.018f, 0.022f), GoldCol, false);
                }
            }

            // ── 手 ──
            var hands = G(u, Slot.Hands);
            if (hands != null)
            {
                bool iron = hands == Gears.Vambraces;
                foreach (var sh in new[] { shoulderL, shoulderR_ })
                {
                    Part(bandMesh, sh, new Vector3(0, -0.34f, 0), new Vector3(iron ? 0.068f : 0.062f, iron ? 0.15f : 0.10f, iron ? 0.068f : 0.062f), iron ? ArmorCol : LeatherCol, false);
                    if (iron) Part(bandMesh, sh, new Vector3(0, -0.27f, 0), new Vector3(0.072f, 0.02f, 0.072f), GoldCol, false);
                }
            }

            // ── 腰 ──
            var waist = G(u, Slot.Waist);
            if (waist == Gears.Sash)
            {
                Part(bandMesh, hips, new Vector3(0, 0.615f + dL, 0), new Vector3(waistZ / 0.86f + 0.012f, 0.12f, waistZ + 0.012f), DarkLeatherCol, false);
                Part(cubeMesh, hips, new Vector3(0, 0.615f + dL, waistZ + 0.016f), new Vector3(0.075f, 0.075f, 0.02f), GoldCol, false);
            }
            else if (waist == Gears.Satchel)
            {
                var mb = new MeshBuilder();
                GearModels.Pouch(mb);
                var t = Prop(hips, mb.ToMesh("Satchel")).transform;
                t.localPosition = new Vector3(-0.13f * g, 0.54f + dL, waistZ * 0.55f);
                t.localRotation = Quaternion.Euler(0, -40, 0);
            }

            // ── 足 ──
            var feet = G(u, Slot.Feet);
            if (feet != null && feet != Gears.Shoes)
            {
                Color c = feet == Gears.IronBoots ? ArmorCol : feet == Gears.CloudBoots ? CloudWhite : DarkLeatherCol;
                foreach (var hip in new[] { hipL, hipR })
                {
                    Part(cubeMesh, hip, new Vector3(0, -0.462f - dL, 0.037f), new Vector3(0.128f, 0.085f, 0.222f), c, false);
                    Part(bandMesh, hip, new Vector3(0, -0.385f - dL, 0), new Vector3(0.074f, 0.12f, 0.074f), c, false); // 靴筒
                    Part(bandMesh, hip, new Vector3(0, -0.33f - dL, 0), new Vector3(0.078f, 0.022f, 0.078f),
                        feet == Gears.SwiftBoots ? SashRed : GoldCol, false);                                              // 靴口的滾邊
                }
            }

            // ── 飾品（兩欄）──
            for (int s = (int)Slot.Charm1; s <= (int)Slot.Charm2; s++)
            {
                var c = u.gear[s];
                if (c == null) continue;
                var mb = new MeshBuilder();
                GearModels.Charm(mb, c);
                if (c == Gears.Mirror)
                    Prop(upper, mb.ToMesh(c.name)).transform.localPosition = Up + new Vector3(0, 0.83f, chestZ + 0.012f);
                else
                {
                    // 玉佩與虎符掛在腰間，一左一右
                    bool left = c == Gears.Tally;
                    Prop(hips, mb.ToMesh(c.name)).transform.localPosition = new Vector3((left ? -0.085f : 0.085f) * g, c == Gears.Tally ? 0.56f + dL : 0.50f + dL, waistZ + 0.022f);
                }
            }
        }
    }
}
