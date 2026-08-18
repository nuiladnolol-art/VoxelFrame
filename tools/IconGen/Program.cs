using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class Program {
    static void Main() {
        int[] sizes = new[] { 16, 32, 48, 64, 128, 256 };
        var bitmaps = new List<Bitmap>();
        foreach (int s in sizes) {
            bitmaps.Add(RenderVoxelBlockIcon(s));
        }

        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string assetsDir = Path.Combine(root, "assets");
        Directory.CreateDirectory(assetsDir);

        // Save assets/icon.png
        bitmaps[^1].Save(Path.Combine(assetsDir, "icon.png"), ImageFormat.Png);

        // Save multi-resolution .ico files
        string[] targetIcos = new[] {
            Path.Combine(assetsDir, "icon.ico"),
            Path.Combine(root, "src", "VoxelFrame.Game", "app.ico"),
            Path.Combine(root, "src", "VoxelFrame.Launcher", "launcher.ico"),
            Path.Combine(root, "src", "VoxelFrame.Installer", "app.ico"),
        };

        foreach (var path in targetIcos) {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SaveMultiSizeIco(bitmaps, path);
            Console.WriteLine("Generated: " + path);
        }

        foreach (var b in bitmaps) b.Dispose();
        Console.WriteLine("All icons successfully generated!");
    }

    static Bitmap RenderVoxelBlockIcon(int size) {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float cx = size / 2f;
        float cy = size / 2f - size * 0.03f;
        float r = size * 0.44f;
        float rx = r * 0.866f;
        float ry = r * 0.5f;

        // Top face (Grass)
        PointF[] topFace = new[] {
            new PointF(cx, cy - r),
            new PointF(cx + rx, cy - ry),
            new PointF(cx, cy),
            new PointF(cx - rx, cy - ry)
        };
        using (var topBrush = new SolidBrush(Color.FromArgb(105, 185, 60))) {
            g.FillPolygon(topBrush, topFace);
        }

        // Left face (Dirt with grass overhang)
        PointF[] leftFace = new[] {
            new PointF(cx - rx, cy - ry),
            new PointF(cx, cy),
            new PointF(cx, cy + r),
            new PointF(cx - rx, cy + ry)
        };
        using (var leftBrush = new SolidBrush(Color.FromArgb(135, 95, 55))) {
            g.FillPolygon(leftBrush, leftFace);
        }
        float overhangH = r * 0.32f;
        PointF[] leftGrass = new[] {
            new PointF(cx - rx, cy - ry),
            new PointF(cx, cy),
            new PointF(cx, cy + overhangH),
            new PointF(cx - rx * 0.45f, cy - ry + overhangH * 1.3f),
            new PointF(cx - rx, cy - ry + overhangH)
        };
        using (var leftGrassBrush = new SolidBrush(Color.FromArgb(88, 162, 48))) {
            g.FillPolygon(leftGrassBrush, leftGrass);
        }

        // Right face (Dirt shaded with grass overhang)
        PointF[] rightFace = new[] {
            new PointF(cx, cy),
            new PointF(cx + rx, cy - ry),
            new PointF(cx + rx, cy + ry),
            new PointF(cx, cy + r)
        };
        using (var rightBrush = new SolidBrush(Color.FromArgb(98, 68, 38))) {
            g.FillPolygon(rightBrush, rightFace);
        }
        PointF[] rightGrass = new[] {
            new PointF(cx, cy),
            new PointF(cx + rx, cy - ry),
            new PointF(cx + rx, cy - ry + overhangH),
            new PointF(cx + rx * 0.45f, cy - ry + overhangH * 1.3f),
            new PointF(cx, cy + overhangH)
        };
        using (var rightGrassBrush = new SolidBrush(Color.FromArgb(68, 132, 36))) {
            g.FillPolygon(rightGrassBrush, rightGrass);
        }

        // 3D Isometric Outline
        using (var pen = new Pen(Color.FromArgb(35, 22, 14), Math.Max(1.2f, size / 48f))) {
            g.DrawPolygon(pen, topFace);
            g.DrawPolygon(pen, leftFace);
            g.DrawPolygon(pen, rightFace);
        }

        return bmp;
    }

    static void SaveMultiSizeIco(List<Bitmap> bitmaps, string filePath) {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write((ushort)0); // idReserved
        bw.Write((ushort)1); // idType (1 = Icon)
        bw.Write((ushort)bitmaps.Count); // idCount

        int offset = 6 + 16 * bitmaps.Count;
        List<byte[]> pngs = new();
        foreach (var bmp in bitmaps) {
            using var pngMs = new MemoryStream();
            bmp.Save(pngMs, ImageFormat.Png);
            byte[] pngData = pngMs.ToArray();
            pngs.Add(pngData);

            bw.Write((byte)(bmp.Width >= 256 ? 0 : bmp.Width));
            bw.Write((byte)(bmp.Height >= 256 ? 0 : bmp.Height));
            bw.Write((byte)0); // Color count
            bw.Write((byte)0); // Reserved
            bw.Write((ushort)1); // Color planes
            bw.Write((ushort)32); // Bits per pixel
            bw.Write((uint)pngData.Length); // Size of image data
            bw.Write((uint)offset); // Offset of image data
            offset += pngData.Length;
        }

        foreach (var png in pngs) {
            bw.Write(png);
        }

        bw.Flush();
        File.WriteAllBytes(filePath, ms.ToArray());
    }
}
