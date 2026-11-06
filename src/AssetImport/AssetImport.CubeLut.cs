using System;
using System.Globalization;
using System.IO;

namespace AssetImport
{
    // Adobe/Resolve .cube order: red changes fastest, then green, then blue.
    // Work in encoded RGB as Photoshop Color Lookup does; alpha is untouched.
    internal sealed class CubeLut
    {
        private int size;
        private float[] values;
        private readonly int[][] offsets = new int[3][];
        private readonly float[][] fractions = new float[3][];
        private readonly float[] min = { 0, 0, 0 }, max = { 1, 1, 1 };

        internal static CubeLut Read(TextReader reader)
        {
            var lut = new CubeLut();
            string line;
            int count = 0;
            bool hasMin = false, hasMax = false;
            while ((line = reader.ReadLine()) != null)
            {
                int comment = line.IndexOf('#');
                if (comment >= 0) line = line.Substring(0, comment);
                var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || parts[0] == "TITLE") continue;
                if (parts[0] == "LUT_3D_SIZE")
                {
                    if (parts.Length != 2 || lut.values != null || !int.TryParse(parts[1], out lut.size) || lut.size < 2 || lut.size > 128)
                        throw new FormatException("LUT_3D_SIZE must be 2..128 and appear once.");
                    lut.values = new float[lut.size * lut.size * lut.size * 3];
                }
                else if (parts[0] == "DOMAIN_MIN" || parts[0] == "DOMAIN_MAX")
                {
                    bool isMin = parts[0] == "DOMAIN_MIN";
                    if (parts.Length != 4 || count != 0 || (isMin ? hasMin : hasMax)) throw new FormatException("Invalid LUT domain.");
                    if (isMin) hasMin = true; else hasMax = true;
                    for (int i = 0; i < 3; i++) (isMin ? lut.min : lut.max)[i] = Number(parts[i + 1]);
                }
                else
                {
                    if (parts.Length != 3 || lut.values == null || count + 3 > lut.values.Length)
                        throw new FormatException("Expected a 3D .cube LUT with RGB rows.");
                    for (int i = 0; i < 3; i++) lut.values[count++] = Number(parts[i]);
                }
            }
            if (lut.values == null || count != lut.values.Length) throw new FormatException("Incomplete 3D LUT.");
            for (int i = 0; i < 3; i++)
                if (lut.max[i] <= lut.min[i] || float.IsInfinity(lut.max[i] - lut.min[i]))
                    throw new FormatException("LUT domain must have finite positive width.");
            for (int axis = 0; axis < 3; axis++)
            {
                lut.offsets[axis] = new int[256];
                lut.fractions[axis] = new float[256];
                int stride = axis == 0 ? 3 : axis == 1 ? lut.size * 3 : lut.size * lut.size * 3;
                for (int value = 0; value < 256; value++)
                {
                    float coordinate = lut.Coordinate((byte)value, axis);
                    int cell = Math.Min((int)coordinate, lut.size - 2);
                    lut.offsets[axis][value] = cell * stride;
                    lut.fractions[axis][value] = coordinate - cell;
                }
            }
            return lut;
        }

        private static float Number(string text)
        {
            float value;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || float.IsNaN(value) || float.IsInfinity(value))
                throw new FormatException("Non-finite or invalid LUT number.");
            return value;
        }

        private float Coordinate(byte value, int axis)
        {
            return Math.Max(0, Math.Min(1, (value / 255f - min[axis]) / (max[axis] - min[axis]))) * (size - 1);
        }

        internal void Apply(byte[] rgba, Action checkCancellation = null)
        {
            if (rgba == null || rgba.Length % 4 != 0) throw new ArgumentException("Expected RGBA pixels.");
            int greenStep = size * 3, blueStep = size * size * 3;
            for (int start = 0; start < rgba.Length;)
            {
                if (checkCancellation != null) checkCancellation();
                int end = start + Math.Min(65536, rgba.Length - start);
                for (int p = start; p < end; p += 4)
                {
                    byte red = rgba[p], green = rgba[p + 1], blue = rgba[p + 2];
                    float r = fractions[0][red], g = fractions[1][green], b = fractions[2][blue];
                    float nr = 1 - r, ng = 1 - g, nb = 1 - b;
                    int cell = offsets[0][red] + offsets[1][green] + offsets[2][blue];
                    for (int channel = 0; channel < 3; channel++)
                    {
                        int index = cell + channel;
                        // Preserve the previous float-product and double-sum order so
                        // optimization does not move byte rounding boundaries.
                        double result = values[index] * nr * ng * nb;
                        result += values[index + 3] * r * ng * nb;
                        result += values[index + greenStep] * nr * g * nb;
                        result += values[index + greenStep + 3] * r * g * nb;
                        result += values[index + blueStep] * nr * ng * b;
                        result += values[index + blueStep + 3] * r * ng * b;
                        result += values[index + blueStep + greenStep] * nr * g * b;
                        result += values[index + blueStep + greenStep + 3] * r * g * b;
                        rgba[p + channel] = (byte)Math.Max(0, Math.Min(255, Math.Round(result * 255)));
                    }
                }
                start = end;
            }
        }
    }
}
