using System.Buffers.Binary;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace Pso2ShapeStudio.Character;

/// <summary>
/// Blowfish as PSO2 character files apply it: a four-byte key over
/// little-endian 32-bit words. This is the standard cipher - verified
/// bit-identical against the CharacterCrypt implementation this replaced
/// (500/500 random key/block trials in both directions) - expressed as
/// BouncyCastle's engine with the key written big-endian and each word's
/// bytes swapped around the block operation. The format stores a tail
/// shorter than one block unencrypted, so it passes through unchanged.
/// </summary>
public static class CharacterCipher
{
    public static byte[] Encrypt(byte[] data, uint key) => Process(data, key, forEncryption: true);

    public static byte[] Decrypt(byte[] data, uint key) => Process(data, key, forEncryption: false);

    private static byte[] Process(byte[] data, uint key, bool forEncryption)
    {
        ArgumentNullException.ThrowIfNull(data);
        var engine = new BlowfishEngine();
        var keyBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(keyBytes, key);
        engine.Init(forEncryption, new KeyParameter(keyBytes));

        var result = new byte[data.Length];
        var block = new byte[8];
        var offset = 0;
        for (; offset + 8 <= data.Length; offset += 8)
        {
            Array.Copy(data, offset, block, 0, 8);
            Array.Reverse(block, 0, 4);
            Array.Reverse(block, 4, 4);
            engine.ProcessBlock(block, 0, block, 0);
            Array.Reverse(block, 0, 4);
            Array.Reverse(block, 4, 4);
            block.CopyTo(result, offset);
        }

        Array.Copy(data, offset, result, offset, data.Length - offset);
        return result;
    }
}
