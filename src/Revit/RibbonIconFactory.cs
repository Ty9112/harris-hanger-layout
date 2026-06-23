using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HangerLayout.Revit
{
    /// <summary>
    /// Runtime icon generator. Draws the ring-hanger icon into a 16/32 px
    /// <see cref="ImageSource"/> at startup via
    /// <see cref="DrawingVisual"/> + <see cref="RenderTargetBitmap"/> —
    /// avoids shipping bitmap files alongside the DLL and keeps the icon
    /// crisp at both ribbon image sizes (small / large).
    /// </summary>
    internal static class RibbonIconFactory
    {
        private static readonly Color MetalGray = Color.FromRgb(0x55, 0x55, 0x55);

        /// <summary>Ring-hanger icon: vertical drop rod from the top of the
        /// canvas terminating at a stroked circle (the hanger band).
        /// Symmetric and centred — reads cleanly at 16px ribbon scale.</summary>
        public static ImageSource Hanger(int size) => Render(size, DrawHanger);

        // ── Render plumbing ────────────────────────────────────────────────

        private static ImageSource Render(int size, Action<DrawingContext, int> draw)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                draw(dc, size);
            }
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        private static void DrawHanger(DrawingContext dc, int size)
        {
            double w = size, h = size;
            double strokeWidth = Math.Max(1.5, size * 0.10);
            var pen = new Pen(new SolidColorBrush(MetalGray), strokeWidth)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap   = PenLineCap.Round,
                LineJoin     = PenLineJoin.Round,
            };
            pen.Brush.Freeze();
            pen.Freeze();

            double cx          = w * 0.50;
            double rodTopY     = h * 0.05;
            double ringRadius  = w * 0.32;
            double ringCenterY = h * 0.62;
            double ringTopY    = ringCenterY - ringRadius;

            // Drop rod (top to top-of-ring).
            var rodGeom = new StreamGeometry();
            using (var ctx = rodGeom.Open())
            {
                ctx.BeginFigure(new Point(cx, rodTopY), isFilled: false, isClosed: false);
                ctx.LineTo(new Point(cx, ringTopY), isStroked: true, isSmoothJoin: false);
            }
            rodGeom.Freeze();
            dc.DrawGeometry(null, pen, rodGeom);

            // Hanger band — stroked circle, no fill keeps the silhouette
            // crisp at small ribbon scales.
            var ring = new EllipseGeometry(new Point(cx, ringCenterY), ringRadius, ringRadius);
            ring.Freeze();
            dc.DrawGeometry(null, pen, ring);
        }
    }
}
