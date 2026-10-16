using System;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// توزيع عناصر ملصق الباركود داخل مقاس الملصق (كل الأبعاد بوحدة 1/100 بوصة زي PrintDocument،
    /// وارتفاع الباركود بالبكسل 96 DPI زي الصورة اللي بيولّدها Code128BarcodeGenerator).
    /// التوزيع القديم كان بيحط السطور بأبعاد ثابتة (ملصق 25 مم = 98 وحدة: السطر الأخير بيبدأ عند 92) فسطر
    /// السعر والصلاحية كان بيقع خارج الملصق؛ هنا الباركود هو اللي بياخد المساحة المتبقية بعد السطور الثابتة.
    /// </summary>
    public sealed class LabelLayout
    {
        public const int Margin = 2;
        public const int TitleLine = 12;     // خط 7pt
        public const int NameLine = 13;      // خط 8pt
        public const int FooterLine = 15;    // خط 9pt
        public const int Gap = 1;
        public const int BarcodeTextAreaPx = 20;   // مساحة الأرقام تحت الأعمدة داخل صورة الباركود
        public const int MinBarsPx = 16;
        public const int MaxBarcodeHeightParam = 60;
        public const int BarcodeQuietZonePx = 15;

        public int PageWidth { get; private set; }
        public int PageHeight { get; private set; }
        public bool HasTitle { get; private set; }
        public int TitleY { get; private set; }
        public int NameY { get; private set; }
        public int BarcodeY { get; private set; }
        /// <summary>قيمة بارامتر height في GenerateCode128Bitmap (الارتفاع بدون مساحة الأرقام)</summary>
        public int BarcodeHeightParam { get; private set; }
        public bool BarcodeIncludeText { get; private set; }
        /// <summary>ارتفاع الأعمدة السوداء الفعلي بالبكسل (المولّد بيحجز 5 بكسل فوق وتحت)</summary>
        public int BarsHeightPx => BarcodeHeightParam - 10;
        /// <summary>ارتفاع صورة الباركود الكلي بوحدات الصفحة (1/100 بوصة)</summary>
        public int BarcodeBitmapHeightUnits { get; private set; }
        public int FooterY { get; private set; }
        /// <summary>false = المساحة المتبقية للباركود أقل من حد القراءة (الصفحة بتتحط على الأقل 90 وحدة فمش بتحصل عملياً)</summary>
        public bool Fits { get; private set; }

        public static int MmToHundredths(double mm) => (int)(mm / 25.4 * 100);

        public static int PixelsToUnits(int px96) => (int)Math.Ceiling(px96 * 100.0 / 96.0);

        public static int UnitsToPixels(int units) => (int)Math.Floor(units * 96.0 / 100.0);

        public static LabelLayout Compute(double widthMm, double heightMm, bool wantTitle)
            => Build(widthMm, heightMm, wantTitle);

        private static LabelLayout Build(double widthMm, double heightMm, bool title)
        {
            var l = new LabelLayout
            {
                PageWidth = Math.Max(150, MmToHundredths(widthMm)),
                PageHeight = Math.Max(90, MmToHundredths(heightMm)),
                HasTitle = title
            };

            int y = Margin;
            if (title) { l.TitleY = y; y += TitleLine; }
            l.NameY = y; y += NameLine;
            l.BarcodeY = y;
            l.FooterY = l.PageHeight - Margin - FooterLine;

            int availUnits = l.FooterY - l.BarcodeY - Gap;
            int availPx = UnitsToPixels(Math.Max(0, availUnits));

            int heightParam = availPx - BarcodeTextAreaPx;
            if (heightParam >= MinBarsPx + 10)
            {
                l.BarcodeIncludeText = true;
                l.BarcodeHeightParam = Math.Min(heightParam, MaxBarcodeHeightParam);
            }
            else
            {
                l.BarcodeIncludeText = false;
                l.BarcodeHeightParam = Math.Min(Math.Max(availPx, 0), MaxBarcodeHeightParam);
            }

            int bitmapPx = l.BarcodeHeightParam + (l.BarcodeIncludeText ? BarcodeTextAreaPx : 0);
            l.BarcodeBitmapHeightUnits = PixelsToUnits(bitmapPx);
            l.Fits = l.BarsHeightPx >= MinBarsPx
                     && l.BarcodeY + l.BarcodeBitmapHeightUnits + Gap <= l.FooterY;
            return l;
        }

        /// <summary>عرض صورة الباركود بوحدات الصفحة لعدد وحدات (modules) معين بعرض 1 بكسل للوحدة</summary>
        public static int BarcodeWidthUnits(int totalModules, int moduleWidthPx = 1, int quietZonePx = BarcodeQuietZonePx)
            => PixelsToUnits(totalModules * moduleWidthPx + quietZonePx * 2);

        public bool BarcodeFitsWidth(int totalModules, int moduleWidthPx = 1, int quietZonePx = BarcodeQuietZonePx)
            => BarcodeWidthUnits(totalModules, moduleWidthPx, quietZonePx) <= PageWidth - Margin * 2;
    }
}
