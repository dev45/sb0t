using System;
using System.IO;
using SkiaSharp;

namespace scripting
{
    internal static class ImageHelper
    {
        private const int JpegQuality = 75; // mismo valor por defecto que usaba GDI+

        /// <summary>Redimensiona a 48x48 y devuelve JPEG.</summary>
        public static byte[] Avatar(byte[] data)
        {
            using (SKBitmap raw = Decode(data))
                return ResizeToJpeg(raw, 48, 48);
        }

        /// <summary>Limita a 384x384 (misma fórmula original) y devuelve JPEG.</summary>
        public static byte[] Scribble(byte[] data, out int height)
        {
            using (SKBitmap raw = Decode(data))
            {
                int img_x = raw.Width;
                int img_y = raw.Height;

                if (img_x > 384)
                {
                    img_x = 384;
                    img_y = raw.Height - (int)Math.Floor(Math.Floor((double)raw.Height / 100) * Math.Floor(((double)(raw.Width - 384) / raw.Width) * 100));
                }

                if (img_y > 384)
                {
                    img_x -= (int)Math.Floor(Math.Floor((double)img_x / 100) * Math.Floor(((double)(img_y - 384) / img_y) * 100));
                    img_y = 384;
                }

                img_x = Math.Max(1, img_x);
                img_y = Math.Max(1, img_y);

                height = img_y;
                return ResizeToJpeg(raw, img_x, img_y);
            }
        }

        private static SKBitmap Decode(byte[] data)
        {
            SKBitmap bmp = SKBitmap.Decode(data);

            if (bmp == null)
                throw new InvalidDataException("Invalid image data");

            return bmp;
        }

        private static byte[] ResizeToJpeg(SKBitmap raw, int w, int h)
        {
            using (SKBitmap sized = new SKBitmap(w, h))
            using (SKCanvas canvas = new SKCanvas(sized))
            using (SKPaint paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High })
            {
                canvas.Clear(SKColors.White); // JPEG no tiene transparencia
                canvas.DrawBitmap(raw, new SKRect(0, 0, w, h), paint);

                using (SKImage image = SKImage.FromBitmap(sized))
                using (SKData encoded = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality))
                    return encoded.ToArray();
            }
        }
    }
}