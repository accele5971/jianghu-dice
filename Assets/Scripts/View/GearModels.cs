using UnityEngine;

namespace Jianghu
{
    /// <summary>兵器拿在手上的方式（決定待機與出招的姿勢）。</summary>
    public enum Hold { OneHand, TwoHand, Pole, BowLeft, Pair, Wrist }

    /// <summary>
    /// 每一件裝備與道具自己的模型。全部由程式生成，回傳建構器（MeshBuilder），
    /// 同一份既拿來掛在角色身上，也拿來烘成面板上的圖示、做成丟出去的飛行物與掉在地上的實體。
    /// 手持物的座標：握把在原點，+Z 朝前（刀劍的刃朝這邊）、+Y 朝上（長兵沿著這邊）。
    /// </summary>
    public static class GearModels
    {
        static Color32 C(int r, int g, int b) { return new Color32((byte)r, (byte)g, (byte)b, 255); }

        static readonly Color32 Steel = C(214, 219, 228), DarkSteel = C(120, 126, 138), Iron = C(62, 64, 72), Black = C(34, 34, 40);
        static readonly Color32 Wood = C(96, 62, 38), PaleWood = C(150, 108, 62), Bamboo = C(104, 78, 46);
        static readonly Color32 Gold = C(214, 170, 78), Bronze = C(186, 142, 62), Red = C(190, 40, 34), Blue = C(60, 96, 170);
        static readonly Color32 Paper = C(242, 237, 220), Cloth = C(196, 188, 168), Leather = C(120, 82, 50), DarkLeather = C(72, 50, 36);
        static readonly Color32 Jade = C(96, 176, 142), Tan = C(200, 158, 92), Stone = C(140, 142, 138), White = C(236, 234, 226);

        // ───────── 小工具 ─────────

        // 立著的圓盤（面朝 ±Z），用來做盾、鏡、玉佩
        static void Disc(MeshBuilder mb, Vector3 c, float r, float thick, Color32 col, int sides = 12)
        {
            Vector3 f = Vector3.forward * (thick * 0.5f);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0) * r, p1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0) * r;
                mb.Tri(c + f, c + p0 + f, c + p1 + f, col, Vector3.forward);
                mb.Tri(c - f, c + p0 - f, c + p1 - f, col, Vector3.back);
                mb.Quad(c + p0 - f, c + p1 - f, c + p1 + f, c + p0 + f, col, p0 + p1);
            }
        }

        // 沿 +Z 的刀劍。width 是左右（X）、tall 是上下（Y）。guard：0 無、1 十字護手、2 圓盤護手
        static void Blade(MeshBuilder mb, float len, float width, float tall, Color32 blade, int guard, Color32 guardCol, Color32 grip)
        {
            mb.Box(new Vector3(0, 0, 0.06f + len * 0.5f), new Vector3(width, tall, len), blade);
            // 尖端：一小段收窄
            mb.Box(new Vector3(0, 0, 0.06f + len + 0.025f), new Vector3(width * 0.55f, tall * 0.55f, 0.05f), blade);
            mb.Box(new Vector3(0, 0, -0.03f), new Vector3(0.032f, 0.032f, 0.16f), grip);
            mb.Box(new Vector3(0, 0, -0.118f), new Vector3(0.042f, 0.042f, 0.024f), guardCol); // 劍首
            if (guard == 1) mb.Box(new Vector3(0, 0, 0.055f), new Vector3(0.15f, 0.035f, 0.028f), guardCol);
            else if (guard == 2) mb.Box(new Vector3(0, 0, 0.055f), new Vector3(0.075f, 0.075f, 0.018f), guardCol);
            else mb.Box(new Vector3(0, 0, 0.055f), new Vector3(0.05f, tall + 0.03f, 0.03f), grip);
        }

        static void Tassel(MeshBuilder mb, Color32 col, float len = 0.16f)
        {
            mb.Box(new Vector3(0, -len * 0.5f, -0.12f), new Vector3(0.016f, len, 0.016f), col);
            mb.Box(new Vector3(0, -len, -0.12f), new Vector3(0.028f, 0.04f, 0.028f), col);
        }

        // 長兵的桿子：沿 +Y，握在中段偏下
        static void Shaft(MeshBuilder mb, float len, Color32 col, float thick = 0.028f)
        {
            mb.Box(new Vector3(0, len * 0.5f - 0.48f, 0.02f), new Vector3(thick, len, thick), col);
        }

        static void BowShape(MeshBuilder mb, float s, Color32 wood)
        {
            for (int k = -1; k <= 1; k += 2)
            {
                mb.Box(new Vector3(0, k * 0.13f * s, 0.055f * s), new Vector3(0.030f, 0.27f * s, 0.030f), wood, 0, k * 10);
                mb.Box(new Vector3(0, k * 0.34f * s, 0.015f * s), new Vector3(0.026f, 0.20f * s, 0.026f), wood, 0, k * 26);
                mb.Box(new Vector3(0, k * 0.47f * s, -0.050f * s), new Vector3(0.022f, 0.13f * s, 0.022f), wood, 0, k * 46);
            }
            mb.Box(new Vector3(0, 0, -0.075f * s), new Vector3(0.008f, 1.02f * s, 0.008f), Paper);
            mb.Box(new Vector3(0, 0, 0.06f * s), new Vector3(0.036f, 0.12f, 0.036f), DarkLeather); // 握把纏皮
        }

        static void FanShape(MeshBuilder mb, Color32 a, Color32 b, Color32 rib, Color32 edge)
        {
            const int ribs = 8;
            for (int i = 0; i < ribs; i++)
            {
                float a0 = Mathf.Deg2Rad * (-52 + i * 104f / ribs), a1 = Mathf.Deg2Rad * (-52 + (i + 1) * 104f / ribs);
                Vector3 d0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)), d1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1));
                Vector3 i0 = d0 * 0.09f, i1 = d1 * 0.09f, o0 = d0 * 0.29f, o1 = d1 * 0.29f;
                var c = (i & 1) == 0 ? a : b;
                mb.Quad(i0, o0, o1, i1, c, Vector3.up);
                mb.Quad(i0, o0, o1, i1, c, Vector3.down);
                mb.Box(o0 * 0.5f, new Vector3(0.008f, 0.006f, 0.29f), rib, a0 * Mathf.Rad2Deg, 0);
                mb.Box((o0 + o1) * 0.5f, new Vector3(0.075f, 0.008f, 0.012f), edge, (a0 + a1) * 0.5f * Mathf.Rad2Deg, 0);
            }
        }

        static void Flute(MeshBuilder mb, Color32 body, Color32 ring, Color32 tassel)
        {
            mb.Box(new Vector3(0, 0, 0.20f), new Vector3(0.022f, 0.022f, 0.52f), body);
            for (int i = 0; i < 3; i++) mb.Box(new Vector3(0, 0, 0.06f + i * 0.16f), new Vector3(0.027f, 0.027f, 0.012f), ring);
            mb.Box(new Vector3(0, -0.07f, -0.05f), new Vector3(0.014f, 0.14f, 0.014f), tassel);
        }

        // ───────── 兵器 ─────────

        public static MeshBuilder Weapon(Gear g, out Hold grip)
        {
            var mb = new MeshBuilder();
            grip = Hold.OneHand;

            if (g == Gears.QingFeng)
            {
                // 青鋒劍：劍身泛青，金護手，紅劍穗，護手嵌一粒碧玉
                Blade(mb, 0.70f, 0.034f, 0.012f, C(196, 226, 214), 1, Gold, C(52, 78, 70));
                mb.Box(new Vector3(0, 0.02f, 0.055f), new Vector3(0.03f, 0.02f, 0.03f), Jade);
                Tassel(mb, Red);
            }
            else if (g == Gears.LongSword) Blade(mb, 0.68f, 0.032f, 0.012f, Steel, 1, DarkSteel, Wood);
            else if (g == Gears.HeavySword)
            {
                Blade(mb, 0.74f, 0.085f, 0.022f, DarkSteel, 0, Iron, DarkLeather);
                grip = Hold.TwoHand;
            }
            else if (g == Gears.DarkIron)
            {
                // 玄鐵重劍：通體烏黑、無鋒無尖，握柄纏暗紅布
                mb.Box(new Vector3(0, 0, 0.47f), new Vector3(0.10f, 0.03f, 0.82f), Black);
                mb.Box(new Vector3(0, 0, 0.47f), new Vector3(0.03f, 0.036f, 0.70f), Iron);
                mb.Box(new Vector3(0, 0, 0.055f), new Vector3(0.16f, 0.05f, 0.04f), Iron);
                mb.Box(new Vector3(0, 0, -0.05f), new Vector3(0.036f, 0.036f, 0.20f), C(96, 30, 30));
                grip = Hold.TwoHand;
            }
            else if (g == Gears.SoftSword)
            {
                // 繞指柔：極薄的軟劍，劍身微微起伏，銀護手，藍穗
                for (int i = 0; i < 7; i++)
                    mb.Box(new Vector3(Mathf.Sin(i * 1.1f) * 0.014f, 0, 0.11f + i * 0.105f), new Vector3(0.024f, 0.006f, 0.12f), C(196, 214, 238), Mathf.Cos(i * 1.1f) * 7, 0);
                mb.Box(new Vector3(0, 0, 0.055f), new Vector3(0.09f, 0.03f, 0.022f), Steel);
                mb.Box(new Vector3(0, 0, -0.03f), new Vector3(0.028f, 0.028f, 0.15f), C(40, 60, 110));
                Tassel(mb, Blue, 0.22f);
            }
            else if (g == Gears.Saber)
            {
                // 單刀：刀背厚、刀尖上挑，圓盤護手，刀首繫紅綢
                Blade(mb, 0.56f, 0.014f, 0.075f, Steel, 2, Gold, Wood);
                mb.Box(new Vector3(0, 0.022f, 0.66f), new Vector3(0.014f, 0.05f, 0.08f), Steel, 0, -24);
                Tassel(mb, Red, 0.12f);
            }
            else if (g == Gears.Hatchet)
            {
                // 柴刀：短而寬的粗鐵刀，木柄
                mb.Box(new Vector3(0, 0.012f, 0.26f), new Vector3(0.012f, 0.095f, 0.34f), DarkSteel);
                mb.Box(new Vector3(0, -0.03f, 0.27f), new Vector3(0.014f, 0.012f, 0.34f), Steel); // 磨亮的刃口
                mb.Box(new Vector3(0, 0, -0.01f), new Vector3(0.034f, 0.034f, 0.20f), PaleWood);
            }
            else if (g == Gears.GhostBlade)
            {
                // 鬼頭刀：寬背大刀，刀背穿三個銅環，護手是鬼面
                mb.Box(new Vector3(0, 0.012f, 0.42f), new Vector3(0.018f, 0.12f, 0.66f), Steel);
                mb.Box(new Vector3(0, 0.03f, 0.78f), new Vector3(0.018f, 0.085f, 0.10f), Steel, 0, -28);
                for (int i = 0; i < 3; i++) mb.Box(new Vector3(0, 0.085f, 0.24f + i * 0.17f), new Vector3(0.012f, 0.045f, 0.045f), Gold);
                mb.Blob(new Vector3(0, 0, 0.06f), new Vector3(0.055f, 0.07f, 0.045f), C(120, 30, 28), 3, 0.12f, 3, 6);
                mb.Box(new Vector3(0, 0, -0.06f), new Vector3(0.036f, 0.036f, 0.22f), DarkLeather);
                mb.Box(new Vector3(0, 0, -0.18f), new Vector3(0.05f, 0.05f, 0.03f), Gold);
                grip = Hold.TwoHand;
            }
            else if (g == Gears.Spear)
            {
                Shaft(mb, 1.55f, Wood);
                mb.Lathe(new Vector3(0, 1.075f, 0.02f), new[] { 0.040f, 0.0f }, new[] { 0f, 0.20f }, 4, Steel, false);
                mb.Blob(new Vector3(0, 1.03f, 0.02f), new Vector3(0.05f, 0.06f, 0.05f), Red, 4, 0.10f, 3, 6, 0.08f);
                grip = Hold.Pole;
            }
            else if (g == Gears.Staff)
            {
                Shaft(mb, 1.45f, PaleWood, 0.034f);
                mb.Box(new Vector3(0, 0.93f, 0.02f), new Vector3(0.040f, 0.09f, 0.040f), Steel);
                mb.Box(new Vector3(0, -0.43f, 0.02f), new Vector3(0.040f, 0.09f, 0.040f), Steel);
                grip = Hold.Pole;
            }
            else if (g == Gears.SerpentSpear)
            {
                // 丈八蛇矛：桿特長，矛頭彎曲如蛇，黑纓
                Shaft(mb, 1.85f, C(70, 46, 30));
                for (int i = 0; i < 5; i++)
                    mb.Box(new Vector3(Mathf.Sin(i * 1.6f) * 0.022f, 1.40f + i * 0.075f, 0.02f), new Vector3(0.05f - i * 0.006f, 0.085f, 0.012f), Steel, 0, 0);
                mb.Lathe(new Vector3(0, 1.74f, 0.02f), new[] { 0.022f, 0f }, new[] { 0f, 0.09f }, 4, Steel, false);
                mb.Blob(new Vector3(0, 1.34f, 0.02f), new Vector3(0.05f, 0.06f, 0.05f), Black, 5, 0.10f, 3, 6, 0.08f);
                grip = Hold.Pole;
            }
            else if (g == Gears.Halberd)
            {
                // 方天畫戟：槍尖兩側各一彎月牙刃，金箍，朱桿
                Shaft(mb, 1.60f, C(128, 36, 30));
                mb.Lathe(new Vector3(0, 1.12f, 0.02f), new[] { 0.036f, 0.0f }, new[] { 0f, 0.22f }, 4, Steel, false);
                for (int k = -1; k <= 1; k += 2)
                {
                    mb.Box(new Vector3(k * 0.075f, 1.05f, 0.02f), new Vector3(0.012f, 0.20f, 0.012f), Steel);
                    mb.Box(new Vector3(k * 0.045f, 1.13f, 0.02f), new Vector3(0.07f, 0.012f, 0.012f), Steel);
                    mb.Box(new Vector3(k * 0.045f, 0.97f, 0.02f), new Vector3(0.07f, 0.012f, 0.012f), Steel);
                    mb.Box(new Vector3(k * 0.098f, 1.05f, 0.02f), new Vector3(0.04f, 0.13f, 0.010f), Steel);
                }
                mb.Box(new Vector3(0, 0.93f, 0.02f), new Vector3(0.042f, 0.05f, 0.042f), Gold);
                mb.Blob(new Vector3(0, 0.88f, 0.02f), new Vector3(0.045f, 0.05f, 0.045f), Gold, 6, 0.10f, 3, 6, 0.08f);
                grip = Hold.Pole;
            }
            else if (g == Gears.DaggerG)
            {
                Blade(mb, 0.20f, 0.026f, 0.010f, Steel, 0, Iron, DarkLeather);
            }
            else if (g == Gears.TwinBlades)
            {
                Blade(mb, 0.36f, 0.030f, 0.010f, Steel, 2, Bronze, Wood);
                grip = Hold.Pair;
            }
            else if (g == Gears.Mace)
            {
                mb.Box(new Vector3(0, 0, 0.16f), new Vector3(0.030f, 0.030f, 0.36f), Wood);
                mb.Blob(new Vector3(0, 0, 0.42f), new Vector3(0.105f, 0.105f, 0.125f), Bronze, 7, 0.03f, 4, 8, 0.08f);
                mb.Box(new Vector3(0, 0, 0.30f), new Vector3(0.05f, 0.05f, 0.03f), Gold);
            }
            else if (g == Gears.WolfClub)
            {
                // 狼牙棒：粗頭細柄的長棒，棒頭滿佈鐵刺
                mb.Box(new Vector3(0, 0, 0.14f), new Vector3(0.032f, 0.032f, 0.42f), Wood);
                mb.Blob(new Vector3(0, 0, 0.52f), new Vector3(0.085f, 0.085f, 0.20f), C(70, 56, 44), 11, 0.04f, 4, 8, 0.08f);
                for (int i = 0; i < 12; i++)
                {
                    float a = i * 2.4f, z = 0.38f + (i % 4) * 0.09f;
                    var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                    mb.Lathe(new Vector3(0, 0, z) + d * 0.07f, new[] { 0.016f, 0f }, new[] { 0f, 0.05f }, 4, Steel, false);
                    mb.Box(new Vector3(0, 0, z) + d * 0.095f, new Vector3(0.014f, 0.014f, 0.014f), Steel, a * Mathf.Rad2Deg, 0);
                }
            }
            else if (g == Gears.Bracers)
            {
                mb.Lathe(Vector3.zero, new[] { 0.085f, 0.095f, 0.085f }, new[] { 0f, 0.07f, 0.14f }, 7, Steel, false);
                for (int i = 0; i < 4; i++) mb.Box(new Vector3(Mathf.Cos(i * 1.57f) * 0.092f, 0.07f, Mathf.Sin(i * 1.57f) * 0.092f), new Vector3(0.02f, 0.02f, 0.02f), Gold);
                grip = Hold.Pair;
            }
            else if (g == Gears.NineWhip)
            {
                // 九節鞭：九段短鐵桿以環相連，自然垂下，末端是鏢頭
                mb.Box(new Vector3(0, 0, -0.02f), new Vector3(0.03f, 0.03f, 0.14f), DarkLeather);
                Vector3 p = new Vector3(0, 0, 0.06f);
                for (int i = 0; i < 9; i++)
                {
                    float pitch = 18 + i * 9f; // 愈往後垂得愈直
                    var dir = Quaternion.Euler(pitch, 0, 0) * Vector3.forward;
                    mb.Box(p + dir * 0.04f, new Vector3(0.014f, 0.014f, 0.07f), Steel, 0, pitch);
                    p += dir * 0.085f;
                    mb.Box(p - dir * 0.005f, new Vector3(0.022f, 0.022f, 0.012f), Gold, 0, pitch);
                }
                mb.Box(p + Vector3.down * 0.03f, new Vector3(0.022f, 0.06f, 0.012f), Steel);
            }
            else if (g == Gears.MeteorHammer)
            {
                // 流星錘：鐵鏈繫著一顆瓜稜鐵球
                mb.Box(new Vector3(0, 0, -0.02f), new Vector3(0.03f, 0.03f, 0.12f), DarkLeather);
                Vector3 p = new Vector3(0, 0, 0.05f);
                for (int i = 0; i < 10; i++)
                {
                    float pitch = 25 + i * 7f;
                    var dir = Quaternion.Euler(pitch, 0, 0) * Vector3.forward;
                    mb.Box(p + dir * 0.022f, new Vector3((i & 1) == 0 ? 0.02f : 0.008f, (i & 1) == 0 ? 0.008f : 0.02f, 0.04f), Iron, 0, pitch);
                    p += dir * 0.045f;
                }
                mb.Blob(p + Vector3.down * 0.07f, new Vector3(0.085f, 0.085f, 0.085f), DarkSteel, 13, 0.05f, 4, 8, 0.1f);
                mb.Box(p + Vector3.down * 0.07f, new Vector3(0.18f, 0.02f, 0.02f), Iron);
            }
            else if (g == Gears.WillowKnives)
            {
                // 柳葉飛刀：狹長如柳葉，尾繫紅綢
                mb.Box(new Vector3(0, 0, 0.10f), new Vector3(0.026f, 0.006f, 0.14f), Steel);
                mb.Box(new Vector3(0, 0, 0.185f), new Vector3(0.014f, 0.006f, 0.04f), Steel);
                mb.Box(new Vector3(0, 0, 0.0f), new Vector3(0.012f, 0.008f, 0.07f), Iron);
                mb.Box(new Vector3(0, -0.03f, -0.045f), new Vector3(0.012f, 0.07f, 0.004f), Red);
            }
            else if (g == Gears.Pebbles)
            {
                for (int i = 0; i < 3; i++)
                    mb.Blob(new Vector3((i - 1) * 0.035f, i == 1 ? 0.02f : 0, 0.03f + (i & 1) * 0.02f), Vector3.one * (0.03f + i * 0.004f), Stone, 20 + i, 0.2f, 3, 5, 0.12f);
            }
            else if (g == Gears.BoneNeedles)
            {
                // 透骨針：指間夾著五根細針，針尾一點藍
                for (int i = -2; i <= 2; i++)
                {
                    mb.Box(new Vector3(i * 0.016f, 0, 0.10f), new Vector3(0.004f, 0.004f, 0.18f), Steel, i * 6f, 0);
                    mb.Box(new Vector3(i * 0.010f, 0, 0.012f), new Vector3(0.008f, 0.008f, 0.012f), Blue, i * 6f, 0);
                }
            }
            else if (g == Gears.LongBow) { BowShape(mb, 1f, Wood); grip = Hold.BowLeft; }
            else if (g == Gears.ShortBow) { BowShape(mb, 0.72f, PaleWood); grip = Hold.BowLeft; }
            else if (g == Gears.SleeveBolt)
            {
                // 袖箭：綁在前臂上的銅筒，筒口露出箭鏃
                mb.Box(new Vector3(0, 0.06f, 0.05f), new Vector3(0.05f, 0.05f, 0.18f), Bronze);
                mb.Box(new Vector3(0, 0.06f, 0.15f), new Vector3(0.018f, 0.018f, 0.05f), Steel);
                mb.Box(new Vector3(0, 0.06f, -0.02f), new Vector3(0.062f, 0.062f, 0.02f), DarkLeather);
                mb.Box(new Vector3(0, 0.06f, 0.10f), new Vector3(0.062f, 0.062f, 0.02f), DarkLeather);
                grip = Hold.Wrist;
            }
            else if (g == Gears.Fan) FanShape(mb, Paper, C(228, 222, 204), Wood, C(120, 150, 130));
            else if (g == Gears.CosmosFan) FanShape(mb, C(58, 60, 70), C(74, 76, 88), Steel, Gold);
            else if (g == Gears.Flute) Flute(mb, Bamboo, C(70, 52, 30), Red);
            else if (g == Gears.TideFlute) Flute(mb, Jade, White, Blue);
            else if (g == Gears.JudgeBrush)
            {
                // 判官筆：精鐵筆桿，筆尖如錐，筆尾一環
                mb.Box(new Vector3(0, 0, 0.14f), new Vector3(0.022f, 0.022f, 0.32f), Iron);
                mb.Box(new Vector3(0, 0, 0.29f), new Vector3(0.030f, 0.030f, 0.03f), Gold);
                mb.Box(new Vector3(0, 0, 0.325f), new Vector3(0.020f, 0.020f, 0.05f), Black);
                mb.Box(new Vector3(0, 0, 0.365f), new Vector3(0.010f, 0.010f, 0.04f), Black);
                mb.Box(new Vector3(0, 0, -0.04f), new Vector3(0.036f, 0.008f, 0.036f), Gold);
            }
            else
            {
                // 沒有專屬模型的兵器：一根短棍，至少看得出手上有東西
                mb.Box(new Vector3(0, 0, 0.15f), new Vector3(0.03f, 0.03f, 0.4f), Wood);
            }
            return mb;
        }

        // ───────── 盾 ─────────

        public static MeshBuilder Shield(Gear g)
        {
            var mb = new MeshBuilder();
            if (g == Gears.IronShield)
            {
                Disc(mb, Vector3.zero, 0.21f, 0.02f, DarkSteel, 10);
                Disc(mb, new Vector3(0, 0, 0.014f), 0.07f, 0.02f, Steel, 8);
                for (int i = 0; i < 6; i++) mb.Box(new Vector3(Mathf.Cos(i * 1.047f) * 0.16f, Mathf.Sin(i * 1.047f) * 0.16f, 0.012f), Vector3.one * 0.022f, Gold);
            }
            else
            {
                // 藤牌：一圈圈編起來的圓牌
                Disc(mb, Vector3.zero, 0.23f, 0.02f, Tan, 12);
                Disc(mb, new Vector3(0, 0, 0.006f), 0.16f, 0.02f, C(176, 134, 74), 12);
                Disc(mb, new Vector3(0, 0, 0.012f), 0.09f, 0.02f, Tan, 10);
                Disc(mb, new Vector3(0, 0, 0.020f), 0.035f, 0.02f, DarkLeather, 8);
            }
            return mb;
        }

        // ───────── 圖示用：護具與飾品（穿在身上時由 UnitView 另外組裝） ─────────

        public static MeshBuilder Icon(Gear g)
        {
            Hold grip;
            if (g.IsWeapon) return Weapon(g, out grip);
            if (g.slot == Slot.OffHand) return Shield(g);
            var mb = new MeshBuilder();
            switch (g.slot)
            {
                case Slot.Head:
                    if (g == Gears.ConeHat) mb.Lathe(Vector3.zero, new[] { 0.37f, 0.21f, 0.012f }, new[] { 0f, 0.085f, 0.185f }, 12, C(214, 186, 115), false);
                    else if (g == Gears.Helmet)
                    {
                        mb.Lathe(Vector3.zero, new[] { 0.17f, 0.16f, 0.10f, 0.02f }, new[] { 0f, 0.10f, 0.18f, 0.22f }, 8, DarkSteel, true);
                        mb.Lathe(Vector3.zero, new[] { 0.20f, 0.17f }, new[] { -0.02f, 0.02f }, 8, Gold, false);
                        mb.Blob(new Vector3(0, 0.27f, 0), new Vector3(0.04f, 0.07f, 0.04f), Red, 3, 0.1f, 3, 6);
                    }
                    else
                    {
                        Color32 c = g == Gears.Hood ? C(52, 54, 66) : C(120, 60, 50);
                        mb.Blob(new Vector3(0, 0.10f, 0), new Vector3(0.17f, 0.15f, 0.17f), c, 4, 0.06f, 4, 8, 0.08f);
                        if (g == Gears.Hood) mb.Box(new Vector3(0, 0.06f, 0.13f), new Vector3(0.18f, 0.14f, 0.05f), Black);
                        else mb.Box(new Vector3(0.14f, 0.03f, -0.08f), new Vector3(0.05f, 0.14f, 0.03f), c, 0, 20);
                    }
                    break;

                case Slot.Upper:
                {
                    Color32 c = g == Gears.NightSuit ? Black : g == Gears.Leather ? Leather : g == Gears.Chain ? DarkSteel : g == Gears.Plate ? C(96, 100, 110) : Cloth;
                    mb.Box(new Vector3(0, 0, 0), new Vector3(0.34f, 0.44f, 0.16f), c);
                    for (int k = -1; k <= 1; k += 2) mb.Box(new Vector3(k * 0.25f, 0.10f, 0), new Vector3(0.20f, 0.14f, 0.13f), c, 0, 0);
                    mb.Box(new Vector3(0, 0.24f, 0), new Vector3(0.12f, 0.05f, 0.12f), g == Gears.Plate ? Gold : DarkLeather);
                    if (g == Gears.Robe) { mb.Box(new Vector3(0, 0, 0.082f), new Vector3(0.05f, 0.44f, 0.01f), C(120, 110, 96)); mb.Box(new Vector3(0, -0.06f, 0.084f), new Vector3(0.35f, 0.05f, 0.01f), Blue); }
                    if (g == Gears.NightSuit) mb.Box(new Vector3(0, -0.06f, 0.084f), new Vector3(0.35f, 0.04f, 0.01f), C(90, 30, 30));
                    if (g == Gears.Leather) for (int k = -1; k <= 1; k += 2) mb.Box(new Vector3(0, 0.02f, 0.084f), new Vector3(0.04f, 0.50f, 0.01f), DarkLeather, 0, 0);
                    if (g == Gears.Leather) mb.Box(new Vector3(0, -0.10f, 0.086f), new Vector3(0.35f, 0.05f, 0.01f), DarkLeather);
                    if (g == Gears.Chain) for (int r = 0; r < 5; r++) mb.Box(new Vector3(0, -0.17f + r * 0.085f, 0.083f), new Vector3(0.34f, 0.012f, 0.01f), Steel);
                    if (g == Gears.Plate) { Disc(mb, new Vector3(0, 0.05f, 0.09f), 0.075f, 0.02f, Gold, 10); for (int k = -1; k <= 1; k += 2) mb.Blob(new Vector3(k * 0.22f, 0.20f, 0), new Vector3(0.10f, 0.07f, 0.10f), DarkSteel, 9, 0.05f, 3, 6); }
                    break;
                }

                case Slot.Lower:
                {
                    Color32 c = g == Gears.Tassets ? DarkSteel : g == Gears.Greaves ? Leather : Cloth;
                    mb.Box(new Vector3(0, 0.20f, 0), new Vector3(0.30f, 0.08f, 0.14f), g == Gears.Trousers ? C(120, 110, 96) : DarkLeather);
                    for (int k = -1; k <= 1; k += 2)
                    {
                        mb.Box(new Vector3(k * 0.08f, -0.05f, 0), new Vector3(0.12f, 0.44f, 0.12f), g == Gears.Tassets ? C(70, 60, 54) : c);
                        if (g == Gears.Greaves) mb.Box(new Vector3(k * 0.08f, -0.02f, 0.066f), new Vector3(0.10f, 0.14f, 0.012f), DarkLeather);
                        if (g == Gears.Tassets) { mb.Box(new Vector3(k * 0.10f, 0.04f, 0.066f), new Vector3(0.13f, 0.26f, 0.012f), DarkSteel); mb.Box(new Vector3(k * 0.10f, -0.08f, 0.07f), new Vector3(0.13f, 0.015f, 0.012f), Gold); }
                    }
                    break;
                }

                case Slot.Hands:
                    for (int k = -1; k <= 1; k += 2)
                    {
                        mb.Lathe(new Vector3(k * 0.12f, -0.10f, 0), new[] { 0.07f, 0.085f }, new[] { 0f, 0.20f }, 7, g == Gears.Vambraces ? DarkSteel : Leather, false);
                        mb.Lathe(new Vector3(k * 0.12f, -0.02f, 0), new[] { 0.086f, 0.086f }, new[] { 0f, 0.03f }, 7, g == Gears.Vambraces ? Gold : DarkLeather, false);
                    }
                    break;

                case Slot.Waist:
                    if (g == Gears.Satchel) Pouch(mb);
                    else
                    {
                        mb.Lathe(Vector3.zero, new[] { 0.20f, 0.20f }, new[] { -0.05f, 0.05f }, 10, DarkLeather, false);
                        mb.Box(new Vector3(0, 0, 0.20f), new Vector3(0.09f, 0.09f, 0.02f), Gold);
                    }
                    break;

                case Slot.Feet:
                {
                    Color32 c = g == Gears.IronBoots ? DarkSteel : g == Gears.CloudBoots ? White : g == Gears.SwiftBoots ? DarkLeather : C(60, 54, 50);
                    for (int k = -1; k <= 1; k += 2)
                    {
                        mb.Box(new Vector3(k * 0.10f, 0, 0.04f), new Vector3(0.12f, 0.08f, 0.24f), c);
                        if (g != Gears.Shoes) mb.Box(new Vector3(k * 0.10f, 0.12f, -0.03f), new Vector3(0.11f, 0.18f, 0.11f), c);
                        if (g == Gears.SwiftBoots) mb.Box(new Vector3(k * 0.10f, 0.20f, -0.03f), new Vector3(0.12f, 0.03f, 0.12f), Red);
                        if (g == Gears.CloudBoots) mb.Box(new Vector3(k * 0.10f, 0.10f, 0.03f), new Vector3(0.125f, 0.04f, 0.06f), Gold);
                        if (g == Gears.IronBoots) mb.Box(new Vector3(k * 0.10f, 0.0f, 0.14f), new Vector3(0.125f, 0.085f, 0.05f), Steel);
                    }
                    break;
                }

                case Slot.Back:
                    if (g == Gears.Quiver)
                    {
                        mb.Lathe(new Vector3(0, -0.20f, 0), new[] { 0.045f, 0.055f }, new[] { 0f, 0.40f }, 6, Wood, false);
                        for (int i = -1; i <= 1; i++) mb.Box(new Vector3(i * 0.02f, 0.27f, 0), new Vector3(0.012f, 0.16f, 0.012f), Paper);
                    }
                    else if (g == Gears.Cloak)
                    {
                        mb.Quad(new Vector3(-0.14f, 0.3f, 0), new Vector3(0.14f, 0.3f, 0), new Vector3(0.26f, -0.3f, 0.05f), new Vector3(-0.26f, -0.3f, 0.05f), C(60, 40, 70), Vector3.back);
                        mb.Quad(new Vector3(-0.14f, 0.3f, 0), new Vector3(0.14f, 0.3f, 0), new Vector3(0.26f, -0.3f, 0.05f), new Vector3(-0.26f, -0.3f, 0.05f), C(60, 40, 70), Vector3.forward);
                        mb.Box(new Vector3(0, 0.30f, 0), new Vector3(0.06f, 0.05f, 0.03f), Gold);
                    }
                    else
                    {
                        mb.Box(Vector3.zero, new Vector3(0.05f, 0.62f, 0.04f), C(40, 44, 58));
                        mb.Box(new Vector3(0, -0.30f, 0), new Vector3(0.056f, 0.06f, 0.046f), Gold);
                        mb.Box(new Vector3(0, 0.30f, 0), new Vector3(0.056f, 0.04f, 0.046f), Gold);
                    }
                    break;

                default: // 飾品
                    Charm(mb, g);
                    break;
            }
            return mb;
        }

        /// <summary>腰間的百寶囊。</summary>
        public static void Pouch(MeshBuilder mb)
        {
            mb.Box(Vector3.zero, new Vector3(0.13f, 0.11f, 0.06f), DarkLeather);
            mb.Box(new Vector3(0, 0.035f, 0.032f), new Vector3(0.135f, 0.04f, 0.012f), C(50, 34, 24));
            mb.Box(new Vector3(0, 0.005f, 0.038f), new Vector3(0.022f, 0.022f, 0.008f), Gold);
        }

        /// <summary>飾品：玉佩、護心鏡、虎符。</summary>
        public static void Charm(MeshBuilder mb, Gear g)
        {
            if (g == Gears.Mirror)
            {
                Disc(mb, Vector3.zero, 0.075f, 0.018f, Gold, 12);
                Disc(mb, new Vector3(0, 0, 0.008f), 0.052f, 0.018f, C(236, 214, 150), 10);
            }
            else if (g == Gears.Tally)
            {
                // 虎符：伏虎形的銅牌
                mb.Box(Vector3.zero, new Vector3(0.11f, 0.05f, 0.022f), Bronze);
                mb.Box(new Vector3(0.05f, 0.025f, 0), new Vector3(0.04f, 0.04f, 0.022f), Bronze);
                for (int i = -1; i <= 1; i++) mb.Box(new Vector3(i * 0.028f - 0.01f, 0, 0.012f), new Vector3(0.008f, 0.05f, 0.004f), C(90, 60, 24));
                mb.Box(new Vector3(0, 0.05f, 0), new Vector3(0.008f, 0.06f, 0.008f), Red);
            }
            else
            {
                // 玉佩：碧玉環，紅繩與穗
                Disc(mb, Vector3.zero, 0.045f, 0.012f, Jade, 10);
                Disc(mb, new Vector3(0, 0, 0.004f), 0.016f, 0.014f, C(40, 90, 70), 8);
                mb.Box(new Vector3(0, 0.07f, 0), new Vector3(0.008f, 0.07f, 0.008f), Red);
                mb.Box(new Vector3(0, -0.085f, 0), new Vector3(0.014f, 0.08f, 0.014f), Red);
            }
        }

        // ───────── 道具 ─────────

        static void Bottle(MeshBuilder mb, Color32 body, Color32 stopper, float fat)
        {
            mb.Lathe(Vector3.zero, new[] { 0.04f * fat, 0.055f * fat, 0.05f * fat, 0.022f, 0.022f }, new[] { 0f, 0.03f, 0.10f, 0.13f, 0.16f }, 8, body, true);
            mb.Lathe(new Vector3(0, 0.16f, 0), new[] { 0.026f, 0.022f }, new[] { 0f, 0.03f }, 6, stopper, true);
        }

        public static MeshBuilder Item(Item it)
        {
            var mb = new MeshBuilder();
            if (it == Jianghu.Item.Salve)
            {
                // 金創藥：白瓷瓶，紅布塞，瓶身一道紅籤
                Bottle(mb, White, Red, 1f);
                mb.Box(new Vector3(0, 0.07f, 0.052f), new Vector3(0.04f, 0.05f, 0.006f), Red);
            }
            else if (it == Jianghu.Item.QiPill)
            {
                // 養氣丹：青花小葫蘆
                mb.Blob(new Vector3(0, 0.045f, 0), new Vector3(0.05f, 0.05f, 0.05f), C(120, 160, 210), 2, 0.03f, 4, 8, 0.05f);
                mb.Blob(new Vector3(0, 0.115f, 0), new Vector3(0.032f, 0.034f, 0.032f), C(120, 160, 210), 3, 0.03f, 4, 8, 0.05f);
                mb.Box(new Vector3(0, 0.08f, 0), new Vector3(0.05f, 0.012f, 0.05f), Gold);
                mb.Box(new Vector3(0, 0.155f, 0), new Vector3(0.016f, 0.03f, 0.016f), Wood);
            }
            else if (it == Jianghu.Item.Antidote) Bottle(mb, Jade, White, 0.85f);
            else if (it == Jianghu.Item.Venom)
            {
                Bottle(mb, C(70, 40, 96), Black, 0.8f);
                mb.Box(new Vector3(0, 0.07f, 0.045f), new Vector3(0.03f, 0.03f, 0.006f), C(170, 220, 120));
            }
            else if (it == Jianghu.Item.Lime)
            {
                // 石灰粉：紮口的白布包
                mb.Blob(new Vector3(0, 0.06f, 0), new Vector3(0.075f, 0.065f, 0.075f), White, 5, 0.10f, 4, 7, 0.06f);
                mb.Box(new Vector3(0, 0.125f, 0), new Vector3(0.04f, 0.02f, 0.04f), Wood);
                mb.Blob(new Vector3(0, 0.155f, 0), new Vector3(0.04f, 0.03f, 0.04f), White, 6, 0.15f, 3, 6);
            }
            else if (it == Jianghu.Item.SmokeBomb)
            {
                // 煙霧彈：一截竹筒，兩頭封蠟，露出引信
                mb.Lathe(Vector3.zero, new[] { 0.045f, 0.045f }, new[] { 0f, 0.17f }, 8, C(150, 160, 96), true);
                mb.Lathe(Vector3.zero, new[] { 0.048f, 0.048f }, new[] { 0.08f, 0.095f }, 8, C(110, 120, 70), false);
                mb.Lathe(new Vector3(0, 0.17f, 0), new[] { 0.047f, 0.03f }, new[] { 0f, 0.02f }, 8, C(170, 60, 50), true);
                mb.Box(new Vector3(0.01f, 0.22f, 0), new Vector3(0.008f, 0.07f, 0.008f), Paper, 0, 0);
            }
            else if (it == Jianghu.Item.Thunder)
            {
                // 霹靂彈：烏黑的鐵殼圓彈，紅紙封口，一截引信
                mb.Blob(new Vector3(0, 0.075f, 0), new Vector3(0.075f, 0.075f, 0.075f), Black, 8, 0.03f, 4, 8, 0.08f);
                mb.Box(new Vector3(0, 0.15f, 0), new Vector3(0.04f, 0.02f, 0.04f), Red);
                mb.Box(new Vector3(0.012f, 0.19f, 0), new Vector3(0.008f, 0.07f, 0.008f), Tan, 0, 0);
                mb.Box(new Vector3(0.016f, 0.23f, 0), new Vector3(0.016f, 0.016f, 0.016f), C(255, 190, 80));
            }
            else if (it == Jianghu.Item.Caltrops)
            {
                for (int k = 0; k < 3; k++)
                {
                    var o = new Vector3((k - 1) * 0.07f, 0.04f, (k & 1) * 0.05f);
                    mb.Box(o, new Vector3(0.11f, 0.016f, 0.016f), Iron, k * 40, 0);
                    mb.Box(o, new Vector3(0.016f, 0.016f, 0.11f), Iron, k * 40, 0);
                    mb.Box(o + Vector3.up * 0.03f, new Vector3(0.016f, 0.09f, 0.016f), Iron);
                }
            }
            else if (it == Jianghu.Item.Grapnel)
            {
                // 飛爪百鍊索：三爪鐵鉤，盤成一圈的繩索
                mb.Lathe(Vector3.zero, new[] { 0.085f, 0.11f, 0.085f }, new[] { 0f, 0.025f, 0.05f }, 10, Tan, false);
                mb.Lathe(Vector3.zero, new[] { 0.05f, 0.085f }, new[] { 0.025f, 0.025f }, 10, C(170, 130, 70), false);
                mb.Box(new Vector3(0, 0.11f, 0), new Vector3(0.018f, 0.14f, 0.018f), Iron);
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 2.094f;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    mb.Box(new Vector3(0, 0.17f, 0) + d * 0.03f, new Vector3(0.07f, 0.014f, 0.014f), Iron, -a * Mathf.Rad2Deg, 0);
                    mb.Box(new Vector3(0, 0.145f, 0) + d * 0.065f, new Vector3(0.014f, 0.06f, 0.014f), Steel);
                }
            }
            else mb.Box(new Vector3(0, 0.05f, 0), Vector3.one * 0.1f, Wood);
            return mb;
        }
    }
}
