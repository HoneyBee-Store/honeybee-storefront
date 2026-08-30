using HoneyBee.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HoneyBee.Web.Data;

/// <summary>
/// Imports the catalogue from the v1 static site. Runs once on an empty
/// database and does nothing afterwards, so it is safe to leave wired up.
///
/// Stock flags mirror the Google Sheet as it stood at import. After this, the
/// database is the source of truth and the sheet is retired.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        await SeedPickupLocationsAsync(db);
        await SeedProductsAsync(db);
        await db.SaveChangesAsync();
    }

    private static async Task SeedPickupLocationsAsync(AppDbContext db)
    {
        if (await db.PickupLocations.AnyAsync()) return;

        db.PickupLocations.AddRange(
            new PickupLocation
            {
                NameAr = "الزرقاء",
                NameEn = "Zarqa",
                MapUrl = "https://maps.app.goo.gl/UuvNrKQxXjKx1o1f6",
                Latitude = 32.134651,
                Longitude = 36.074959,
                SortOrder = 1
            },
            new PickupLocation
            {
                NameAr = "عمّان",
                NameEn = "Amman",
                MapUrl = "https://maps.app.goo.gl/xeYEDQJXoHxU7vnY8",
                Latitude = 32.037898,
                Longitude = 35.833588,
                SortOrder = 2
            });
    }

    private static async Task SeedProductsAsync(AppDbContext db)
    {
        if (await db.Products.AnyAsync()) return;

        // FocalY values were measured from each photo by scanning for where the
        // product actually sits in frame — the jars sit high, so a default
        // centre crop shows carpet. Carried over from v1 rather than re-derived.
        var seed = new (string Slug, string NameAr, string NameEn, string DescAr, string DescEn,
                        string Image, int FocalY, bool InStock)[]
        {
            ("sidr-honey", "عسل السدر", "Sidr Honey",
             "عسل غني برائحة عطرية عميقة وقوام كثيف ومذاق قوي يدوم طويلًا. من أثمن ما نحصده.",
             "A rich, deeply aromatic honey with a thick texture and a bold, lasting flavour. One of our most prized harvests.",
             "img/products/honey-sidr.jpg", 24, true),

            ("jabali-honey", "عسل جبلي", "Jabali Honey (Mountain)",
             "عسل جبلي بري من أزهار المرتفعات النائية. خفيف وزهري وناعم.",
             "Wild mountain honey collected from remote highland blooms. Light, floral, and smooth.",
             "img/products/honey-jabali.jpg", 23, true),

            ("marrar-honey", "عسل مرار", "Marrar Honey",
             "عسل ذهبي مميّز بلمسة مرارة خفيفة، معروف برائحته القوية.",
             "A distinctive golden honey with a slightly bitter edge, prized for its intense aroma.",
             "img/products/honey-marrar.jpg", 42, true),

            ("kina-honey", "عسل كينا", "Kina Honey (Eucalyptus)",
             "عسل أزهار الكينا بنكهة عشبية منعشة ولون كهرماني صافٍ.",
             "Eucalyptus blossom honey with a fresh, herbal note and a clear amber tone.",
             "img/products/honey-kina.jpg", 28, true),

            // v1 called this and the beeswax below both "شمع عسل" in the sheet,
            // which customers could not tell apart. Restored to the distinct
            // names the product cards originally used.
            ("raw-honeycomb", "شمع عسل طبيعي", "Raw Honeycomb",
             "مباشرة من الخلية — شمع عسل طبيعي ما زال على الإطار، دون معالجة وبكامل نكهته.",
             "Straight from the hive — natural honeycomb still on the frame, unprocessed and full of flavour.",
             "img/products/honeycomb-raw.jpg", 50, true),

            ("cut-comb-honey", "قطع شمع العسل", "Cut Comb Honey",
             "قطع مرتّبة من شمع العسل جاهزة للتقديم — امضغ الشمع للاستمتاع بحلاوة طبيعية.",
             "Neatly cut comb honey squares, ready to serve — chew the wax for a naturally sweet treat.",
             "img/products/honeycomb-cut.jpg", 60, true),

            ("bee-pollen", "حبوب لقاح النحل", "Bee Pollen",
             "حبيبات غنية بالعناصر الغذائية يجمعها النحل بنفسه، معبّأة طازجة بكميات صغيرة.",
             "Nutrient-dense granules collected by the bees themselves, packed fresh in small portions.",
             "img/products/bee-pollen.jpg", 49, true),

            ("propolis", "بروبوليس", "Propolis",
             "بروبوليس نحل خام، صمغ الخلية الطبيعي، معروف تقليديًا بخصائصه.",
             "Raw bee propolis, the hive's natural resin, traditionally valued for its properties.",
             "img/products/propolis.jpg", 47, true),

            ("royal-jelly", "غذاء ملكات النحل", "Royal Jelly",
             "غذاء ملكي طازج، مادة غنية بالعناصر ينتجها النحل العامل لتغذية الملكة.",
             "Fresh royal jelly, the nutrient-rich substance produced by worker bees to feed the queen.",
             "img/products/royal-jelly.jpg", 40, true),

            ("pure-beeswax", "شمع نحل نقي", "Pure Beeswax",
             "شمع نحل نقي غير مكرر مباشرة من الخلية.",
             "Pure, unrefined beeswax straight from the hive.",
             "img/products/beeswax.jpg", 53, false)
        };

        var order = 1;
        foreach (var s in seed)
        {
            db.Products.Add(new Product
            {
                Slug = s.Slug,
                NameAr = s.NameAr,
                NameEn = s.NameEn,
                DescriptionAr = s.DescAr,
                DescriptionEn = s.DescEn,
                Price = null,           // no prices yet — confirmed by phone
                InStock = s.InStock,
                SortOrder = order++,
                Images =
                {
                    new ProductImage
                    {
                        Path = s.Image,
                        AltAr = s.NameAr,
                        AltEn = s.NameEn,
                        FocalY = s.FocalY,
                        IsPrimary = true,
                        SortOrder = 1
                    }
                }
            });
        }
    }
}
