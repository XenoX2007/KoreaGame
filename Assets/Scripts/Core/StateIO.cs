using System;
using System.IO;
using UnityEngine;

// Helpers for the models' WriteState/ReadState, which online the host uses to send a game's state to
// the other phones. Plain System.IO, so the models stay free of networking.
public static class StateIO
{
    public static void Write(this BinaryWriter writer, Vector2 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
    }

    public static Vector2 ReadVector2(this BinaryReader reader) => new Vector2(reader.ReadSingle(), reader.ReadSingle());

    // -1 for none.
    public static void WriteOptional(this BinaryWriter writer, int? value) => writer.Write((sbyte)(value ?? -1));

    public static int? ReadOptional(this BinaryReader reader)
    {
        int value = reader.ReadSByte();
        return value < 0 ? (int?)null : value;
    }

    // Packed 8 to a byte.
    public static void WriteBits(this BinaryWriter writer, bool[] bits)
    {
        for (int i = 0; i < bits.Length; i += 8)
        {
            int packed = 0;
            for (int bit = 0; bit < 8 && i + bit < bits.Length; bit++)
                if (bits[i + bit]) packed |= 1 << bit;
            writer.Write((byte)packed);
        }
    }

    public static void ReadBits(this BinaryReader reader, bool[] bits)
    {
        for (int i = 0; i < bits.Length; i += 8)
        {
            int packed = reader.ReadByte();
            for (int bit = 0; bit < 8 && i + bit < bits.Length; bit++)
                bits[i + bit] = (packed & (1 << bit)) != 0;
        }
    }

    public static byte[] Pack(Action<BinaryWriter> write)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            write(writer);
            writer.Flush();
            return stream.ToArray();
        }
    }

    public static void Unpack(byte[] bytes, Action<BinaryReader> read)
    {
        using (var stream = new MemoryStream(bytes))
        using (var reader = new BinaryReader(stream))
            read(reader);
    }
}
