using System.Collections.Generic;

namespace Jianghu
{
    /// <summary>
    /// 遮蔽物分組：把相連的樹葉、屋瓦各自編成一個「結構」（一棵樹冠、一片屋頂）。
    /// 畫面層可以整個結構淡出（掀頂），滑鼠拾取會略過已掀開的結構。
    /// 純顯示與操作用途：視線判定不受影響，樹葉與屋頂在規則上照樣擋暗器。
    /// </summary>
    public static class CoverMap
    {
        public static bool IsCover(byte b) { return b == Block.Leaf || b == Block.Tile || b == Block.Floor; }

        public static void Build(VoxelWorld w)
        {
            int[] off = { 1, -1, w.W, -w.W, w.WD, -w.WD };
            var stack = new Stack<int>();
            short count = 0;

            for (int i = 0; i < w.v.Length; i++)
            {
                if (!IsCover(w.v[i]) || w.cover[i] >= 0) continue;
                w.cover[i] = count;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    int x = w.X(c), y = w.Y(c), z = w.Z(c);
                    for (int d = 0; d < 6; d++)
                    {
                        // 邊界檢查：避免 x 方向 ±1 跨到下一列
                        int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
                        int ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        int nz = z + (d == 4 ? 1 : d == 5 ? -1 : 0);
                        if (!w.InB(nx, ny, nz)) continue;
                        int n = c + off[d];
                        if (!IsCover(w.v[n]) || w.cover[n] >= 0) continue;
                        w.cover[n] = count;
                        stack.Push(n);
                    }
                }
                count++;
            }

            w.coverCount = count;
            w.coverHidden = new bool[count];
        }
    }
}
