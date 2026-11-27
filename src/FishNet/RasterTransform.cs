namespace FishNet;

public sealed record PhotoEdit(int QuarterTurns = 0, int X = 0, int Y = 0, int Width = 0, int Height = 0, int Brightness = 0);
public sealed record RasterPixels(int Width, int Height, byte[] Pixels);

public static class RasterTransform
{
    public const int MaximumPixels = 16 * 1024 * 1024;
    public static RasterPixels Apply(byte[] bgra, int width, int height, PhotoEdit edit)
    {
        if (width <= 0 || height <= 0 || (long)width * height > MaximumPixels || bgra.LongLength != (long)width * height * 4)
            throw new InvalidDataException("The image exceeds the supported pixel limit or has invalid dimensions.");
        int cropWidth = edit.Width == 0 ? width : edit.Width, cropHeight = edit.Height == 0 ? height : edit.Height;
        if (edit.QuarterTurns is < 0 or > 3 || edit.Brightness is < -100 or > 100 || cropWidth <= 0 || cropHeight <= 0 || edit.X < 0 || edit.Y < 0 || edit.X > width - cropWidth || edit.Y > height - cropHeight)
            throw new InvalidDataException("Choose a crop fully inside the image and a rotation of 0, 90, 180 or 270 degrees.");
        int outputWidth = edit.QuarterTurns % 2 == 0 ? cropWidth : cropHeight;
        int outputHeight = edit.QuarterTurns % 2 == 0 ? cropHeight : cropWidth;
        var output = new byte[checked(outputWidth * outputHeight * 4)];
        for (int y = 0; y < cropHeight; y++) for (int x = 0; x < cropWidth; x++)
        {
            var (dx, dy) = edit.QuarterTurns switch
            { 1 => (cropHeight - 1 - y, x), 2 => (cropWidth - 1 - x, cropHeight - 1 - y), 3 => (y, cropWidth - 1 - x), _ => (x, y) };
            Buffer.BlockCopy(bgra, ((edit.Y + y) * width + edit.X + x) * 4, output, (dy * outputWidth + dx) * 4, 4);
            if (edit.Brightness != 0) for (int channel = 0; channel < 3; channel++)
            {
                int index = (dy * outputWidth + dx) * 4 + channel;
                output[index] = (byte)Math.Clamp(output[index] + edit.Brightness * 255 / 100, 0, 255);
            }
        }
        return new(outputWidth, outputHeight, output);
    }
}
