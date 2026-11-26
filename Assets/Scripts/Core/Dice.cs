using System;

namespace Jianghu
{
    /// <summary>mulberry32：快、可設種子、無配置。</summary>
    public sealed class Rng
    {
        uint s;
        public Rng(uint seed) { s = seed; }

        public double Next()
        {
            unchecked
            {
                s += 0x6D2B79F5u;
                uint t = (s ^ (s >> 15)) * (1u | s);
                t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        public int Range(int n) { return (int)(Next() * n); }
    }

    /// <summary>骰式，例如 2d6+3。</summary>
    public struct DiceExpr
    {
        public int n, sides, plus;
        public DiceExpr(int n, int sides, int plus = 0) { this.n = n; this.sides = sides; this.plus = plus; }
        public float Avg { get { return n * (sides + 1) * 0.5f + plus; } }
        public override string ToString()
        {
            return n + "d" + sides + (plus > 0 ? "+" + plus : plus < 0 ? plus.ToString() : "");
        }
    }

    public static class Dice
    {
        static Rng rng = new Rng(unchecked((uint)Environment.TickCount) ^ 0x9E3779B9u);

        public static void Seed(uint seed) { rng = new Rng(seed); }

        public static int Die(int sides) { return 1 + rng.Range(sides); }

        /// <summary>mode &gt; 0 優勢、&lt; 0 劣勢、0 一般。r2 在一般情況為 0。</summary>
        public static int D20(int mode, out int r1, out int r2)
        {
            r1 = Die(20);
            r2 = 0;
            if (mode == 0) return r1;
            r2 = Die(20);
            return mode > 0 ? Math.Max(r1, r2) : Math.Min(r1, r2);
        }

        /// <summary>crit 時骰數加倍。</summary>
        public static int Roll(DiceExpr e, bool crit = false)
        {
            int n = crit ? e.n * 2 : e.n, total = e.plus;
            for (int i = 0; i < n; i++) total += Die(e.sides);
            return total;
        }

        public static int Mod(int score) { return (int)Math.Floor((score - 10) / 2.0); }

        /// <summary>命中機率（含 1 必失、20 必中與優劣勢）。</summary>
        public static float HitChance(int bonus, int ac, int mode)
        {
            float p = Math.Min(0.95f, Math.Max(0.05f, (21 + bonus - ac) / 20f));
            return mode > 0 ? 1 - (1 - p) * (1 - p) : mode < 0 ? p * p : p;
        }
    }
}
