using FishNet;
using System.Buffers.Binary;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

internal static class VisualNativeChecks
{
    internal static async Task Run(string root)
    {
        var photo = Path.Combine(root, "generated-photo.png");
        using (var memory = new InMemoryRandomAccessStream())
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memory);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 2, 3, 96, 96,
                Enumerable.Range(0, 6).SelectMany(i => new byte[] { (byte)i, (byte)(30 + i), (byte)(60 + i), 255 }).ToArray());
            await encoder.FlushAsync(); memory.Seek(0);
            using var read = memory.AsStreamForRead(); using var output = File.Create(photo); await read.CopyToAsync(output);
        }
        // Add a real PNG metadata chunk containing made-up private metadata.
        const string marker = "private-gps-fixture-not-real";
        var bytes = File.ReadAllBytes(photo); var text = Encoding.ASCII.GetBytes("Comment\0" + marker);
        var chunk = new byte[text.Length + 12]; BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)text.Length);
        "tEXt"u8.CopyTo(chunk.AsSpan(4)); text.CopyTo(chunk, 8);
        uint crc = 0xffffffff;
        foreach (var b in chunk.AsSpan(4, text.Length + 4)) { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1; }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), ~crc);
        await File.WriteAllBytesAsync(photo, [.. bytes[..^12], .. chunk, .. bytes[^12..]]);
        var original = File.ReadAllBytes(photo);
        var metadata = await VisualFiles.ReadPhotoAsync(photo, default);
        if (metadata.Width != 2 || metadata.Height != 3 || metadata.Frames != 1) throw new Exception("Generated photo metadata decode failed");
        var copy = Path.Combine(root, "edited-copy.png");
        await VisualFiles.ExportPhotoAsync(photo, copy, new PhotoEdit(1), default);
        if (!File.ReadAllBytes(photo).SequenceEqual(original)) throw new Exception("Editing changed the original photo");
        var edited = await VisualFiles.ReadPhotoAsync(copy, default);
        if (edited.Width != 3 || edited.Height != 2) throw new Exception("Edited output did not rotate the actual image");
        if (Encoding.ASCII.GetString(File.ReadAllBytes(copy)).Contains(marker)) throw new Exception("Private source metadata was copied");
        try { await VisualFiles.ExportPhotoAsync(photo, photo, new PhotoEdit(), default); throw new Exception("Photo source overwrite accepted"); }
        catch (InvalidDataException) { }
        var before = File.ReadAllBytes(copy);
        try { await VisualFiles.ExportPhotoAsync(photo, copy, new PhotoEdit(X: 99), default); throw new Exception("Invalid crop accepted"); }
        catch (InvalidDataException) { }
        if (!File.ReadAllBytes(copy).SequenceEqual(before)) throw new Exception("Failed edit destroyed prior exported file");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await VisualFiles.ExportPhotoAsync(photo, copy, new PhotoEdit(), cancel.Token); throw new Exception("Canceled export accepted"); }
        catch (OperationCanceledException) { }

        var pdf = Path.Combine(root, "generated-document.pdf");
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 144 144] /Resources << >> /Contents 4 0 R >>",
            StreamObject("1 0 0 rg 0 0 144 144 re f\n"),
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 144 144] /Resources << >> /Contents 6 0 R >>",
            StreamObject("0 0 1 rg 0 0 144 144 re f\n")
        ];
        var documentText = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int> { 0 };
        for (int i = 0; i < objects.Length; i++) { offsets.Add(Encoding.ASCII.GetByteCount(documentText.ToString())); documentText.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        int xref = Encoding.ASCII.GetByteCount(documentText.ToString());
        documentText.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) documentText.Append(offset.ToString("D10") + " 00000 n \n");
        documentText.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        await File.WriteAllTextAsync(pdf, documentText.ToString(), Encoding.ASCII);
        using var document = await VisualFiles.LoadPdfAsync(pdf, "", default);
        if (document.PageCount != 2) throw new Exception("PDF page count incorrect");
        for (uint page = 0; page < 2; page++)
        {
            using var rendered = await VisualFiles.RenderPdfAsync(document, page, default);
            var decoder = await BitmapDecoder.CreateAsync(rendered);
            if (decoder.PixelWidth > 2048 || decoder.PixelHeight > 2048 || decoder.PixelWidth == 0) throw new Exception("PDF rendering exceeded bounds");
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
            var data = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)]; bitmap.CopyToBuffer(data.AsBuffer());
            int index = (bitmap.PixelWidth * (bitmap.PixelHeight / 2) + bitmap.PixelWidth / 2) * 4;
            if (page == 0 ? data[index + 2] < 240 || data[index] > 20 : data[index] < 240 || data[index + 2] > 20)
                throw new Exception("PDF renderer displayed the wrong page pixels");
        }
        try { using var invalid = await VisualFiles.RenderPdfAsync(document, 2, default); throw new Exception("Out-of-range PDF page accepted"); }
        catch (ArgumentOutOfRangeException) { }
        Console.WriteLine("PASS: actual image decode/rotation, metadata-free copy, original preservation, edit failure/cancellation, and bounded two-page PDF rendering.");
    }
    private static string StreamObject(string contents) => $"<< /Length {Encoding.ASCII.GetByteCount(contents)} >>\nstream\n{contents}endstream";
}
