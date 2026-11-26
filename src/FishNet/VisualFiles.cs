using Aquarium.Core;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;

using Windows.Storage.Streams;

namespace FishNet;

public sealed record PhotoInformation(uint Width, uint Height, uint Frames, ulong Bytes, string DateTaken, string Title);
public static class VisualFiles
{
    public static async Task<PhotoInformation> ReadPhotoAsync(string path, CancellationToken token)
    {
        using var file = File.OpenRead(path);
        var size = (ulong)file.Length;
        if (size > 64UL * 1024 * 1024) throw new InvalidDataException("Photo files must be at most 64 MB.");
        using var input = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(input).AsTask(token);
        if (decoder.OrientedPixelWidth == 0 || decoder.OrientedPixelHeight == 0 || (ulong)decoder.OrientedPixelWidth * decoder.OrientedPixelHeight > 100UL * 1024 * 1024 ||
            decoder.FrameCount > 200 || decoder.FrameCount > 1 && (ulong)decoder.PixelWidth * decoder.PixelHeight > 4UL * 1024 * 1024)
            throw new InvalidDataException("This image exceeds the viewer's pixel or animation limits.");
        var properties = await decoder.BitmapProperties.GetPropertiesAsync(new[] { "System.Photo.DateTaken", "System.Title" }).AsTask(token);
        string date = properties.TryGetValue("System.Photo.DateTaken", out var taken) && taken.Value is DateTimeOffset recorded ? recorded.ToString("g") : "Not recorded";
        string title = properties.TryGetValue("System.Title", out var label) ? label.Value as string ?? "" : "";
        return new(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight, decoder.FrameCount, size,
            date, title);
    }

    public static async Task ExportPhotoAsync(string source, string destination, PhotoEdit edit, CancellationToken token)
    {
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a new file; the source photo is never overwritten.");
        var extension = Path.GetExtension(destination).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg")) throw new InvalidDataException("Export a PNG or JPEG copy.");
        _ = await ReadPhotoAsync(source, token);
        using var file = File.OpenRead(source);
        using var input = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(input).AsTask(token);
        if ((ulong)decoder.OrientedPixelWidth * decoder.OrientedPixelHeight > RasterTransform.MaximumPixels)
            throw new InvalidDataException("Editing supports up to 16 megapixels. The original is unchanged.");
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(token);
        var bytes = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)]; bitmap.CopyToBuffer(bytes.AsBuffer());
        var transformed = await Task.Run(() => RasterTransform.Apply(bytes, bitmap.PixelWidth, bitmap.PixelHeight, edit), token);
        using var encoded = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(extension == ".png" ? BitmapEncoder.PngEncoderId : BitmapEncoder.JpegEncoderId, encoded).AsTask(token);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, extension == ".png" ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore,
                (uint)transformed.Width, (uint)transformed.Height, 96, 96, transformed.Pixels);
            // New pixel-only encoding deliberately does not copy EXIF/GPS or other source metadata.
        await encoder.FlushAsync().AsTask(token);
        encoded.Seek(0);
        using var content = encoded.AsStreamForRead();
        await AtomicFile.WriteAsync(destination, (output, cancellation) => content.CopyToAsync(output, cancellation), token);
    }

    public sealed class OpenedPdf : IDisposable
    {
        private readonly FileStream file;
        private readonly IRandomAccessStream input;
        internal PdfDocument Document { get; }
        public uint PageCount => Document.PageCount;
        internal OpenedPdf(FileStream file, IRandomAccessStream input, PdfDocument document)
        { this.file = file; this.input = input; Document = document; }
        public void Dispose() { input.Dispose(); file.Dispose(); }
    }
    public static async Task<OpenedPdf> LoadPdfAsync(string path, string password, CancellationToken token)
    {
        var file = File.OpenRead(path);
        IRandomAccessStream? input = null;
        try
        {
            if (file.Length > 512L * 1024 * 1024) throw new InvalidDataException("PDF files must be at most 512 MB.");
            input = file.AsRandomAccessStream();
            var document = await PdfDocument.LoadFromStreamAsync(input, password).AsTask(token);
            if (document.PageCount is 0 or > 10000) throw new InvalidDataException("PDF page count is outside the viewer limit.");
            return new OpenedPdf(file, input, document);
        }
        catch { input?.Dispose(); file.Dispose(); throw; }
    }
    public static async Task<InMemoryRandomAccessStream> RenderPdfAsync(OpenedPdf document, uint index, CancellationToken token)
    {
        if (index >= document.PageCount) throw new ArgumentOutOfRangeException(nameof(index));
        using var page = document.Document.GetPage(index);
        if (!double.IsFinite(page.Size.Width) || !double.IsFinite(page.Size.Height) || page.Size.Width <= 0 || page.Size.Height <= 0)
            throw new InvalidDataException("This PDF page has invalid dimensions.");
        double scale = Math.Min(2048 / page.Size.Width, 2048 / page.Size.Height);
        var output = new InMemoryRandomAccessStream();
        try
        {
            await page.RenderToStreamAsync(output, new PdfPageRenderOptions
            { DestinationWidth = (uint)Math.Max(1, page.Size.Width * scale), DestinationHeight = (uint)Math.Max(1, page.Size.Height * scale) }).AsTask(token);
            output.Seek(0); return output;
        }
        catch { output.Dispose(); throw; }
    }
}
