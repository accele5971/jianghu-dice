using System;
using UnityEngine;

namespace Jianghu
{
    /// <summary>
    /// 音效。全部在啟動時用數學函式合成（正弦波、雜訊、包絡線），不需要任何音檔。
    /// </summary>
    public sealed class Sfx
    {
        const int Rate = 44100;
        const float Tau = Mathf.PI * 2;

        readonly AudioSource src;
        readonly AudioClip click, step, swing, hit, crit, miss, heal, buff, fall, turn;

        public Sfx(GameObject host)
        {
            src = host.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0;
            src.volume = 0.55f;

            var rnd = new System.Random(7);
            Func<float> noise = () => (float)(rnd.NextDouble() * 2 - 1);

            click = Make("click", 0.06f, t => Mathf.Sin(Tau * 1200 * t) * Mathf.Exp(-t * 70) * 0.35f);
            step = Make("step", 0.08f, t => noise() * Mathf.Exp(-t * 90) * 0.22f + Mathf.Sin(Tau * 150 * t) * Mathf.Exp(-t * 60) * 0.3f);
            swing = Make("swing", 0.16f, t => noise() * Mathf.Sin(Mathf.PI * t / 0.16f) * 0.22f);
            miss = Make("miss", 0.22f, t => noise() * Mathf.Sin(Mathf.PI * t / 0.22f) * 0.12f);
            hit = Make("hit", 0.24f, t => Mathf.Sin(Tau * (95 * t - 70 * t * t)) * Mathf.Exp(-t * 16) * 0.8f + noise() * Mathf.Exp(-t * 45) * 0.35f);
            crit = Make("crit", 0.5f, t => Mathf.Sin(Tau * (95 * t - 60 * t * t)) * Mathf.Exp(-t * 12) * 0.8f + noise() * Mathf.Exp(-t * 35) * 0.4f
                + (Mathf.Sin(Tau * 1320 * t) + Mathf.Sin(Tau * 1980 * t) * 0.5f) * Mathf.Exp(-t * 7) * 0.2f);
            heal = Make("heal", 0.45f, t => Mathf.Sin(Tau * (520 * t + 500 * t * t)) * Mathf.Sin(Mathf.PI * t / 0.45f) * 0.3f);
            buff = Make("buff", 0.36f, t => (t < 0.14f ? Mathf.Sin(Tau * 523 * t) : Mathf.Sin(Tau * 784 * t)) * Mathf.Exp(-(t < 0.14f ? t : t - 0.14f) * 9) * 0.3f);
            fall = Make("fall", 0.5f, t => Mathf.Sin(Tau * (210 * t - 150 * t * t)) * Mathf.Exp(-t * 6) * 0.5f);
            turn = Make("turn", 0.4f, t => Mathf.Sin(Tau * 587 * t) * Mathf.Exp(-t * 9) * 0.25f + (t > 0.09f ? Mathf.Sin(Tau * 880 * t) * Mathf.Exp(-(t - 0.09f) * 8) * 0.25f : 0));
        }

        static AudioClip Make(string name, float seconds, Func<float, float> wave)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                // 尾端 5 毫秒淡出，避免截斷造成爆音
                float tail = Mathf.Clamp01((seconds - t) / 0.005f);
                data[i] = Mathf.Clamp(wave(t), -1f, 1f) * tail;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Play(AudioClip c, float volume = 1f) { if (src != null && c != null) src.PlayOneShot(c, volume); }

        public void Click() { Play(click); }
        public void Step() { Play(step, 0.8f); }
        public void Swing() { Play(swing); }
        public void Miss() { Play(miss); }
        public void Hit(bool isCrit) { Play(isCrit ? crit : hit); }
        public void Heal() { Play(heal); }
        public void Buff() { Play(buff); }
        public void Fall() { Play(fall); }
        public void Turn() { Play(turn); }
    }
}
