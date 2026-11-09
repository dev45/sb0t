using System;
using System.Globalization;
using SkiaSharp;

namespace core
{
    /// <summary>Comprueba que las piezas nativas y dinámicas funcionan en este sistema. Uso: roomhost --selftest</summary>
    public static class SelfTest
    {
        public static bool Run()
        {
            Console.WriteLine("Sistema: " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);

            bool ok = true;
            ok &= Check("SQLite nativo", Sqlite, "42");
            ok &= Check("SkiaSharp nativo (imagen JPEG)", Skia, null);
            ok &= Check("Corrector ortografico (Hunspell)", Spell, null);
            ok &= Check("Jurassic (JavaScript a IL)", Script, "42");

            Console.WriteLine(ok ? "AUTOPRUEBA: TODO CORRECTO" : "AUTOPRUEBA: HAY FALLOS");
            return ok;
        }

        private static bool Check(string name, Func<string> test, string expected)
        {
            try
            {
                string result = test();
                bool pass = expected == null || result == expected;
                Console.WriteLine((pass ? "OK    " : "FALLO ") + name + " -> " + result);
                return pass;
            }
            catch (Exception e)
            {
                Console.WriteLine("FALLO " + name + " -> " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        private static string Sqlite()
        {
            using (var c = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"))
            {
                c.Open();

                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "select 41 + 1";
                    return Convert.ToInt64(cmd.ExecuteScalar()).ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        private static string Skia()
        {
            using (SKBitmap bmp = new SKBitmap(100, 80))
            {
                using (SKCanvas canvas = new SKCanvas(bmp))
                    canvas.Clear(SKColors.Red);

                using (SKImage image = SKImage.FromBitmap(bmp))
                using (SKData jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 69))
                {
                    byte[] bytes = jpeg.ToArray();

                    using (SKBitmap back = SKBitmap.Decode(bytes))
                    {
                        if (back == null || bytes.Length == 0)
                            throw new Exception("no se pudo codificar o decodificar");

                        return bytes.Length + " bytes, " + back.Width + "x" + back.Height;
                    }
                }
            }
        }

        private static string Spell()
        {
            string corrected = SpellCheck.Correct("helo wrld");
            bool good = SpellCheck.Confirm("hello world");
            return "Correct('helo wrld')='" + corrected + "', Confirm('hello world')=" + good;
        }

        private static string Script()
        {
            Jurassic.ScriptEngine engine = new Jurassic.ScriptEngine();
            object r = engine.Evaluate("(function (a, b) { return a * b; })(6, 7)");
            return Convert.ToDouble(r, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }
    }
}