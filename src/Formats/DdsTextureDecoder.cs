using BCnEncoder.Decoder;
using BCnEncoder.Shared.ImageFiles;
using Pfim;

namespace Pso2ShapeStudio.Formats;

public static class DdsTextureDecoder
{
    public static RenderTexture Decode(string name, ReadOnlyMemory<byte> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var bytes = data.ToArray();
        using (var stream = new MemoryStream(bytes, writable: false))
        using (var image = Pfimage.FromStream(stream))
        {
            // Pfim handles the legacy fourCC formats (BC1/2/3...). Anything it
            // leaves compressed - BC7 and the other DX10-header formats NGS
            // uses - falls through to BCnEncoder below.
            if (!image.Compressed && TryConvert(name, image, out var texture))
            {
                return texture;
            }
        }

        return DecodeBlockCompressed(name, bytes);
    }

    private static RenderTexture DecodeBlockCompressed(string name, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var file = DdsFile.Load(stream);
        var decoder = new BcDecoder();
        var pixels = decoder.Decode(file);
        var width = checked((int)file.header.dwWidth);
        var height = checked((int)file.header.dwHeight);
        if (pixels.Length < width * height)
        {
            throw new InvalidDataException(
                $"DDS texture '{name}' decoded to {pixels.Length} pixels for {width}x{height}.");
        }

        var rgba = new byte[checked(width * height * 4)];
        for (var index = 0; index < width * height; index++)
        {
            var pixel = pixels[index];
            var target = index * 4;
            rgba[target] = pixel.r;
            rgba[target + 1] = pixel.g;
            rgba[target + 2] = pixel.b;
            rgba[target + 3] = pixel.a;
        }

        return new RenderTexture(name, width, height, rgba);
    }

    private static bool TryConvert(string name, IImage image, out RenderTexture texture)
    {
        texture = null!;
        if (image.Format is not (ImageFormat.Rgba32 or ImageFormat.Rgb24 or ImageFormat.Rgb8))
        {
            return false;
        }

        var rgba = new byte[checked(image.Width * image.Height * 4)];
        for (var y = 0; y < image.Height; y++)
        {
            var sourceRow = y * image.Stride;
            var targetRow = y * image.Width * 4;
            for (var x = 0; x < image.Width; x++)
            {
                var target = targetRow + x * 4;
                switch (image.Format)
                {
                    case ImageFormat.Rgba32:
                        {
                            var source = sourceRow + x * 4;
                            // Pfim follows Windows bitmap byte order (BGRA).
                            rgba[target] = image.Data[source + 2];
                            rgba[target + 1] = image.Data[source + 1];
                            rgba[target + 2] = image.Data[source];
                            rgba[target + 3] = image.Data[source + 3];
                            break;
                        }
                    case ImageFormat.Rgb24:
                        {
                            var source = sourceRow + x * 3;
                            rgba[target] = image.Data[source + 2];
                            rgba[target + 1] = image.Data[source + 1];
                            rgba[target + 2] = image.Data[source];
                            rgba[target + 3] = byte.MaxValue;
                            break;
                        }
                    case ImageFormat.Rgb8:
                        {
                            var value = image.Data[sourceRow + x];
                            rgba[target] = value;
                            rgba[target + 1] = value;
                            rgba[target + 2] = value;
                            rgba[target + 3] = byte.MaxValue;
                            break;
                        }
                }
            }
        }

        texture = new RenderTexture(name, image.Width, image.Height, rgba);
        return true;
    }
}
