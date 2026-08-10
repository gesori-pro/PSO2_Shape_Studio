using System.Buffers.Binary;
using Pso2ShapeStudio.Formats;

namespace Pso2ShapeStudio.Core.Tests.Formats;

public sealed class DdsTextureDecoderTests
{
    [Fact]
    public void Decode_ConvertsPfimBgraToRgba()
    {
        var dds = CreateOnePixelDds(blue: 0x33, green: 0x22, red: 0x11, alpha: 0x44);

        var texture = DdsTextureDecoder.Decode("pixel.dds", dds);

        Assert.Equal(1, texture.Width);
        Assert.Equal(1, texture.Height);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, texture.RgbaPixels);
    }

    private static byte[] CreateOnePixelDds(byte blue, byte green, byte red, byte alpha)
    {
        var data = new byte[132];
        "DDS "u8.CopyTo(data);
        Write(4, 124);
        Write(8, 0x100F);
        Write(12, 1);
        Write(16, 1);
        Write(20, 4);
        Write(76, 32);
        Write(80, 0x41);
        Write(88, 32);
        Write(92, 0x00FF0000);
        Write(96, 0x0000FF00);
        Write(100, 0x000000FF);
        Write(104, unchecked((int)0xFF000000));
        Write(108, 0x1000);
        data[128] = blue;
        data[129] = green;
        data[130] = red;
        data[131] = alpha;
        return data;

        void Write(int offset, int value) =>
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);
    }
}
