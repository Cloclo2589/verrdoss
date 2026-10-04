using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Verrdoss.App;

public static class LockIcon
{
    private static readonly Color Body = Color.FromArgb(31, 75, 115);

    public static void Write(string path)
    {
        var sizes = new[] { 16, 24, 32, 48, 64, 256 };
        var images = new byte[sizes.Length][];
        for (var i = 0; i < sizes.Length; i++)
            images[i] = DrawPng(sizes[i]);
        WriteIco(path, sizes, images);
    }

    private static byte[] DrawPng(int size)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            var unit = size / 16f;
            using var pen = new Pen(Body, Math.Max(1.4f, unit * 1.35f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            var shackle = new RectangleF(4.2f * unit, 1.6f * unit, 7.6f * unit, 8.2f * unit);
            graphics.DrawArc(pen, shackle, 180, 180);
            var mid = shackle.Top + shackle.Height / 2f;
            var bodyTop = 6.7f * unit;
            graphics.DrawLine(pen, shackle.Left, mid, shackle.Left, bodyTop);
            graphics.DrawLine(pen, shackle.Right, mid, shackle.Right, bodyTop);
            using var brush = new SolidBrush(Body);
            FillRound(graphics, brush, 2.4f * unit, bodyTop, 11.2f * unit, 7.4f * unit, 1.5f * unit);
            using var hole = new SolidBrush(Color.White);
            var cx = size / 2f;
            var cy = bodyTop + 2.5f * unit;
            var radius = Math.Max(1f, 1.15f * unit);
            graphics.FillEllipse(hole, cx - radius, cy - radius, radius * 2f, radius * 2f);
            graphics.FillRectangle(hole, cx - radius * 0.42f, cy + radius * 0.4f, radius * 0.84f, 2.3f * unit);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static void FillRound(Graphics graphics, Brush brush, float x, float y, float width, float height, float radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2f;
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }

    private static void WriteIco(string path, int[] sizes, byte[][] images)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Length);
        var offset = 6 + (16 * images.Length);
        for (var i = 0; i < images.Length; i++)
        {
            var size = sizes[i] >= 256 ? 0 : sizes[i];
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }

        foreach (var image in images)
            writer.Write(image);
    }
}
