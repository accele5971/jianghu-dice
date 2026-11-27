using System;
using System.Collections.Generic;

namespace Jianghu
{
    /// <summary>行李的分類。</summary>
    public enum BagFilter { All, Weapon, Armor, Trinket, Item }

    /// <summary>行李的排序方式。</summary>
    public enum BagSort { Acquired, Name, Type }

    /// <summary>行李清單裡的一列：一件備用裝備，或某人行囊裡的一疊道具。</summary>
    public sealed class BagEntry
    {
        public Gear gear;
        public Item item;
        public int n = 1;       // 道具的數量
        public Unit holder;     // 道具在誰的行囊裡（裝備是全隊共用，為 null）
        public int seq;         // 取得的先後（愈小愈早）
        public string Name { get { return gear != null ? gear.name : item.name; } }
        /// <summary>類型排序用的鍵：先依欄位（兵器再依種類），道具排最後。</summary>
        public int TypeKey { get { return gear != null ? (int)gear.slot * 100 + (int)gear.cls : 10000 + (int)item.kind; } }
    }

    /// <summary>店裡的一樣貨。</summary>
    public sealed class Good
    {
        public Gear gear;
        public Item item;
        public int price;
        public string Name { get { return gear != null ? gear.name : item.name; } }
    }

    /// <summary>行李整理（分類、排序）與商店買賣。</summary>
    public sealed partial class Battle
    {
        public static readonly string[] FilterNames = { "全部", "兵器", "護具", "配件", "道具" };
        public static readonly string[] SortNames = { "取得順序", "名稱", "類型" };

        int itemSeq; // 道具取得的流水號

        /// <summary>給新的一疊道具編上取得順序（Give 與讀檔時呼叫）。</summary>
        void StampSeq(ItemStack s) { s.seq = ++itemSeq; }

        public static BagFilter FilterOf(Gear g)
        {
            if (g.IsWeapon) return BagFilter.Weapon;
            switch (g.slot)
            {
                case Slot.OffHand: case Slot.Head: case Slot.Upper: case Slot.Lower: case Slot.Hands: case Slot.Feet: return BagFilter.Armor;
            }
            return BagFilter.Trinket; // 腰、背、飾品
        }

        static int Compare(BagEntry a, BagEntry b, BagSort sort)
        {
            int c = 0;
            if (sort == BagSort.Name) c = string.CompareOrdinal(a.Name, b.Name);
            else if (sort == BagSort.Type)
            {
                c = a.TypeKey.CompareTo(b.TypeKey);
                if (c == 0) c = string.CompareOrdinal(a.Name, b.Name);
            }
            return c != 0 ? c : a.seq.CompareTo(b.seq);
        }

        /// <summary>
        /// 行李清單：共用的備用裝備，加上行囊裡的道具（only 不為 null 時只列那個人的，否則列全隊的）。
        /// 依 filter 篩選、依 sort 排序。取得順序：裝備照放進行李的先後，道具照放進行囊的先後，裝備排在道具前面。
        /// </summary>
        public List<BagEntry> BagEntries(BagFilter filter, BagSort sort, Unit only = null)
        {
            var list = new List<BagEntry>();
            if (filter != BagFilter.Item)
                for (int i = 0; i < stash.Count; i++)
                    if (filter == BagFilter.All || FilterOf(stash[i]) == filter) list.Add(new BagEntry { gear = stash[i], seq = i });
            if (filter == BagFilter.All || filter == BagFilter.Item)
                foreach (var u in units)
                {
                    if (u.team != 0 || !u.alive || (only != null && u != only)) continue;
                    foreach (var s in u.bag) list.Add(new BagEntry { item = s.item, n = s.n, holder = u, seq = 1000000 + s.seq });
                }
            list.Sort((a, b) => Compare(a, b, sort));
            return list;
        }

        // ───────── 價格 ─────────

        /// <summary>裝備的定價（店裡買的價錢）；賣出是一半。</summary>
        public static int Price(Gear g)
        {
            int p = 6;
            if (g.IsWeapon)
            {
                var c = Gears.Info(g.cls);
                var d = g.dmg.n > 0 ? g.dmg : c.dmg;
                p += (int)(d.Avg * 5) + Math.Max(0, g.reachMod) * 8 + (g.range > 0 ? 10 : 0);
                if (g.Named) p += 60;
            }
            p += g.armor * 14 + (g.move + g.jump + g.bag + g.stealth + g.perception + g.skillBonus) * 10;
            return Math.Max(4, p);
        }

        public static int Price(Item it) { return it.price; }
        public static int SellPrice(Gear g) { return Math.Max(1, Price(g) / 2); }
        public static int SellPrice(Item it) { return Math.Max(1, Price(it) / 2); }

        // ───────── 商店 ─────────

        static Good G(Gear g) { return new Good { gear = g, price = Price(g) }; }
        static Good G(Item it) { return new Good { item = it, price = Price(it) }; }

        /// <summary>這家店賣什麼（民宅不做買賣，回傳空清單）。貨源不限量。</summary>
        public List<Good> Goods(Prop shop)
        {
            var list = new List<Good>();
            if (shop == null || shop.kind != PropKind.Building) return list;
            switch (shop.shop)
            {
                case ShopKind.Smithy:
                    foreach (var g in new[] { Gears.LongSword, Gears.Saber, Gears.Spear, Gears.Mace, Gears.DaggerG, Gears.LongBow, Gears.Pebbles,
                                              Gears.IronShield, Gears.Helmet, Gears.Chain, Gears.Vambraces, Gears.IronBoots }) list.Add(G(g));
                    list.Add(G(Item.Caltrops)); list.Add(G(Item.Thunder)); list.Add(G(Item.Grapnel));
                    break;
                case ShopKind.TeaStall:
                    list.Add(G(Item.QiPill)); list.Add(G(Item.Antidote));
                    break;
                case ShopKind.Inn:
                    list.Add(G(Item.Salve)); list.Add(G(Item.Antidote)); list.Add(G(Item.Venom)); list.Add(G(Item.Lime)); list.Add(G(Item.SmokeBomb));
                    break;
                case ShopKind.Weaver:
                    foreach (var g in new[] { Gears.Robe, Gears.NightSuit, Gears.Leather, Gears.Greaves, Gears.Hood, Gears.Headwrap, Gears.Wraps,
                                              Gears.Sash, Gears.Satchel, Gears.SwiftBoots, Gears.Cloak, Gears.Quiver }) list.Add(G(g));
                    break;
            }
            return list;
        }

        /// <summary>現在能不能買這樣東西；可以回傳 null。</summary>
        public string WhyNotBuy(Unit u, Prop shop, Good g)
        {
            string why = WhyNot(u, shop);
            if (why != null) return why;
            if (silver < g.price) return "銀兩不夠";
            if (g.item != null && u.bag.Count >= u.BagSize && BagCount(u, g.item) == 0) return "行囊滿了";
            return null;
        }

        /// <summary>買一樣東西：裝備進共用行李，道具進 u 的行囊。</summary>
        public bool Buy(Unit u, Prop shop, Good g)
        {
            if (g == null || WhyNotBuy(u, shop, g) != null) return false;
            silver -= g.price;
            if (g.gear != null) stash.Add(g.gear); else Give(u, g.item);
            Log(N(u) + " 在「" + shop.name + "」買了「" + g.Name + "」，花了 " + g.price + " 兩銀子（剩 " + silver + " 兩）。");
            return true;
        }

        /// <summary>把共用行李裡的一件裝備賣掉（半價）。</summary>
        public bool SellGear(Unit u, Prop shop, Gear g)
        {
            if (g == null || shop == null || shop.kind != PropKind.Building || shop.shop == ShopKind.Home || WhyNot(u, shop) != null || !stash.Remove(g)) return false;
            silver += SellPrice(g);
            Log(N(u) + " 把「" + g.name + "」賣給「" + shop.name + "」，得 " + SellPrice(g) + " 兩銀子（共 " + silver + " 兩）。");
            return true;
        }

        /// <summary>把 u 行囊裡的一個道具賣掉（半價）。</summary>
        public bool SellItem(Unit u, Prop shop, Item it)
        {
            if (it == null || shop == null || shop.kind != PropKind.Building || shop.shop == ShopKind.Home || WhyNot(u, shop) != null || BagCount(u, it) <= 0) return false;
            for (int i = 0; i < u.bag.Count; i++)
                if (u.bag[i].item == it) { if (--u.bag[i].n <= 0) u.bag.RemoveAt(i); break; }
            silver += SellPrice(it);
            Log(N(u) + " 把「" + it.name + "」賣給「" + shop.name + "」，得 " + SellPrice(it) + " 兩銀子（共 " + silver + " 兩）。");
            return true;
        }
    }
}
