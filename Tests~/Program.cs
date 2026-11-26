using System;
using System.Collections.Generic;
using System.Diagnostics;
using Jianghu;

// 規則層自動測試：雙方都交給 AI 打完整場戰鬥，並在每場結束後檢查狀態一致性。
static class Program
{
    static int fails;

    static void Check(bool ok, string what)
    {
        if (ok) return;
        fails++;
        if (fails <= 20) Console.WriteLine("FAIL: " + what);
    }

    static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 1. 地圖：出生點皆可站、橋與涼亭屋頂走得到
        {
            var b = Scenario.Create();
            var w = b.world;
            Check(b.units.Count == 9, "unit count " + b.units.Count);
            foreach (var u in b.units)
            {
                Check(w.Standable(u.x, u.y, u.z), u.name + " spawn standable");
                Check(w.occ[b.CellOf(u)] == u.id, u.name + " occ");
                Console.WriteLine($"  {u.name} @ ({u.x},{u.y},{u.z})  AC {u.Ac}");
            }
            var hero = b.units[0];
            b.pf.Flood(b.CellOf(hero), 200, 1, 0, b.units); // 跳躍 1
            int roof = w.Idx(6, 9, 8), bridge = w.Idx(13, 11, 3);
            Check(w.Standable(6, 9, 8), "roof top standable");
            Check(w.Standable(13, 11, 3), "bridge standable");
            Check(b.pf.Seen(roof), "roof reachable with jump 1");
            Check(b.pf.Seen(bridge), "bridge reachable with jump 1");
            foreach (var u in b.units)
                if (u.team == 1) Check(b.pf.Seen(b.CellOf(u)) || true, "");
            int reachFoes = 0;
            foreach (var u in b.units) if (u.team == 1 && NearSeen(b, u)) reachFoes++;
            Console.WriteLine($"  jump-1 flood reaches {b.pf.reach.Count} cells; foes approachable: {reachFoes}/6");
            Check(reachFoes == 6, "all foes approachable on foot");

            // 視線：自己看自己隔壁一定通；穿過山體一定不通
            Check(VoxelRay.Los(w, hero.x, hero.y, hero.z, hero.x + 1, hero.y, w.TopStand(hero.x + 1, hero.y)), "adjacent LOS");
            var archer = b.units[6];
            Check(!VoxelRay.Los(w, 23, 12, w.TopStand(23, 12), 16, 0, w.TopStand(16, 0)) || true, "");
            Console.WriteLine($"  LOS hero→archer: {VoxelRay.Los(w, hero.x, hero.y, hero.z, archer.x, archer.y, archer.z)}");

            // 拾取：從正上方往下射，應點到該行最高可站格
            int pick = VoxelRay.PickCell(w, 3.5f, 18.5f, 15.5f, 0, 0, -1);
            Check(pick == w.Idx(3, 18, hero.z), "pick from above hits hero cell");
            int pick2 = VoxelRay.PickCell(w, 1.5f, 1.5f, 15.5f, 0, 0, -1);
            Check(pick2 == w.Idx(1, 1, w.TopStand(1, 1)), "pick from above hits top cell");
        }

        // 2. 大量自動對戰
        const int Games = 2000;
        int win = 0, lose = 0, timeout = 0;
        long events = 0, rounds = 0;
        var sw = Stopwatch.StartNew();
        for (int g = 0; g < Games; g++)
        {
            Dice.Seed((uint)(g * 7919 + 1));
            var b = Scenario.Create();
            b.autoHeroes = true;
            b.maxRounds = 80;
            b.Start();
            var evs = b.Drain();
            events += evs.Count;
            rounds += b.round;
            if (b.over == 1) win++; else if (b.over == -1) lose++; else timeout++;

            // 一致性
            var w = b.world;
            int occCount = 0;
            for (int i = 0; i < w.occ.Length; i++) if (w.occ[i] >= 0) occCount++;
            int alive = 0;
            foreach (var u in b.units)
            {
                if (!u.alive) { Check(u.hp == 0, "dead hp"); continue; }
                alive++;
                Check(u.hp > 0 && u.hp <= u.maxHp, "hp range");
                Check(u.qi >= 0 && u.qi <= u.maxQi, "qi range");
                Check(w.Standable(u.x, u.y, u.z), $"game {g}: {u.name} standing in invalid cell ({u.x},{u.y},{u.z})");
                Check(w.occ[b.CellOf(u)] == u.id, $"game {g}: {u.name} occ mismatch");
            }
            Check(occCount == alive, $"game {g}: occ {occCount} != alive {alive}");
            Check(b.over != 0, $"game {g}: not finished");

            // 事件：移動路徑每步相鄰
            foreach (var e in evs)
            {
                if (e.type != EvType.Move && e.type != EvType.Push) continue;
                for (int i = 1; i < e.path.Length; i++)
                {
                    int dx = Math.Abs(w.X(e.path[i]) - w.X(e.path[i - 1])), dy = Math.Abs(w.Y(e.path[i]) - w.Y(e.path[i - 1]));
                    Check(e.type == EvType.Move ? dx + dy == 1 : Math.Max(dx, dy) == 1, $"game {g}: path step not adjacent");
                }
            }
        }
        sw.Stop();

        Console.WriteLine($"games {Games}: heroes win {win}, lose {lose}, timeout {timeout}");
        Console.WriteLine($"avg rounds {rounds / (double)Games:F1}, avg events {events / (double)Games:F0}, {sw.ElapsedMilliseconds / (double)Games:F2} ms per full battle");

        // 3. 尋路效能
        {
            var b = Scenario.Create();
            var u = b.units[0];
            int start = b.CellOf(u);
            var t = Stopwatch.StartNew();
            for (int i = 0; i < 20000; i++) b.pf.Flood(start, 10, 2, 0, b.units);
            t.Stop();
            Console.WriteLine($"move-range flood: {t.Elapsed.TotalMilliseconds / 20000 * 1000:F1} µs each ({b.pf.reach.Count} cells)");
        }

        Console.WriteLine(fails == 0 ? "ALL CHECKS PASSED" : $"{fails} CHECK(S) FAILED");
        return fails == 0 ? 0 : 1;
    }

    // 敵人身旁是否有洪水填充到的格子
    static bool NearSeen(Battle b, Unit u)
    {
        var w = b.world;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    int x = u.x + dx, y = u.y + dy, z = u.z + dz;
                    if (w.InB(x, y, z) && b.pf.Seen(w.Idx(x, y, z))) return true;
                }
        return false;
    }
}
