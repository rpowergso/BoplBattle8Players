using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MorePlayers;

internal static class LobbyPlatformAssets
{
    private const string NativePlatform = "LongRect";
    private const string LevelOneAssets = "sharedassets6.assets";
    private const int TextureClassId = 28;
    private const int SpriteClassId = 213;
    private static Sprite widePlatform;
    private static bool triedReading;

    internal static Sprite GetLongPlatform()
    {
        if (widePlatform != null) return widePlatform;
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            if (sprite.name == NativePlatform) return widePlatform = sprite;
        if (triedReading) return null;
        triedReading = true;

        try
        {
            widePlatform = ReadLongPlatform(Application.dataPath);
        }
        catch (Exception error) when (error is IOException || error is InvalidDataException ||
            error is UnauthorizedAccessException || error is UnityException || error is ArgumentException)
        {
            Main.Log.LogWarning($"The lobby could not read Bopl's {NativePlatform} artwork and will use SmallCapsule: {error.Message}");
        }
        return widePlatform;
    }

    // LongRect belongs to Level 1, so Unity does not always load it in the menu.
    // Read its named Sprite and streamed Texture2D from the installed player data.
    // This only supports the serialized asset format used by Bopl 2.5.1 on Windows.
    private static Sprite ReadLongPlatform(string gameData)
    {
        using var reader = new BinaryReader(File.OpenRead(Path.Combine(gameData, LevelOneAssets)));
        var file = reader.BaseStream;
        file.Position = 8;
        if (ReadBigEndian(reader, 4) != 22) throw new InvalidDataException("Unexpected Unity asset format.");
        file.Position = 16;
        if (reader.ReadByte() != 0) throw new InvalidDataException("Unexpected Unity asset byte order.");
        file.Position = 32;
        long dataStart = ReadBigEndian(reader, 8);
        file.Position = 48;
        var unityVersion = new StringBuilder();
        byte next;
        while (unityVersion.Length < 64 && (next = reader.ReadByte()) != 0)
            unityVersion.Append((char)next);
        if (!unityVersion.ToString().StartsWith("2022.3.", StringComparison.Ordinal) || reader.ReadInt32() != 19 ||
            reader.ReadBoolean() || dataStart > file.Length)
            throw new InvalidDataException("Unexpected Unity player metadata.");

        int typeCount = ReadCount(reader, 128);
        var types = new int[typeCount];
        for (int i = 0; i < types.Length; i++)
        {
            types[i] = reader.ReadInt32();
            reader.ReadBoolean(); // Stripped type flag
            reader.ReadInt16(); // Script type index
            file.Position += types[i] == 114 ? 32 : 16; // Type and optional script hashes
        }

        var textureObject = default(AssetObject);
        var spriteObject = default(AssetObject);
        int objectCount = ReadCount(reader, 1024);
        for (int i = 0; i < objectCount; i++)
        {
            Align(reader);
            reader.ReadInt64(); // Path ID
            long start = dataStart + reader.ReadInt64();
            uint size = reader.ReadUInt32();
            int type = reader.ReadInt32();
            if (start < dataStart || start > file.Length || size > file.Length - start || type < 0 || type >= types.Length)
                throw new InvalidDataException("Invalid Unity object entry.");
            if (types[type] != TextureClassId && types[type] != SpriteClassId) continue;

            long nextEntry = file.Position;
            file.Position = start;
            if (ReadString(reader) == NativePlatform)
            {
                var asset = new AssetObject(file.Position, start + size);
                if (types[type] == TextureClassId) textureObject = asset;
                else spriteObject = asset;
            }
            file.Position = nextEntry;
        }

        if (textureObject.End == 0 || spriteObject.End == 0)
            throw new InvalidDataException("The named platform sprite and texture were not found.");

        file.Position = spriteObject.Start;
        var rect = new Rect(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        file.Position += 24; // Render offset and border
        float units = reader.ReadSingle();
        var pivot = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        if (file.Position > spriteObject.End || !(units > 0) || !(pivot.x >= 0 && pivot.x <= 1) || !(pivot.y >= 0 && pivot.y <= 1))
            throw new InvalidDataException("Invalid platform sprite geometry.");

        // Unity's Texture2D stream record is at the end of this format's object.
        // Search only within that object and validate the relative file and byte range.
        file.Position = textureObject.Start + 8; // Fallback format and flags
        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        file.Position += 8; // Complete image size and mips stripped
        var format = (TextureFormat)reader.ReadInt32();
        if (width <= 0 || width > 4096 || height <= 0 || height > 4096 || format != TextureFormat.RGBA32 ||
            rect.x != 0 || rect.y != 0 || rect.width != width || rect.height != height)
            throw new InvalidDataException("Unexpected LongRect texture format or dimensions.");

        string expectedStream = LevelOneAssets + ".resS";
        int topMipSize = checked(width * height * 4);
        int pathRecordLength = (4 + Encoding.UTF8.GetByteCount(expectedStream) + 3) & ~3;
        long streamRecord = textureObject.End - pathRecordLength - 12;
        if (streamRecord < file.Position) throw new InvalidDataException("Missing LongRect texture stream record.");
        file.Position = streamRecord;
        long streamOffset = reader.ReadInt64();
        uint streamSize = reader.ReadUInt32();
        if (ReadString(reader) != expectedStream || streamOffset < 0 || streamSize < topMipSize)
            throw new InvalidDataException("Invalid LongRect texture stream record.");

        using var stream = new BinaryReader(File.OpenRead(Path.Combine(gameData, expectedStream)));
        if (streamOffset > stream.BaseStream.Length || streamSize > stream.BaseStream.Length - streamOffset)
            throw new InvalidDataException("LongRect texture stream exceeds the installed resource.");
        stream.BaseStream.Position = streamOffset;
        var pixels = stream.ReadBytes(topMipSize);
        if (pixels.Length != topMipSize) throw new EndOfStreamException("LongRect texture data was incomplete.");

        var texture = new Texture2D(width, height, format, false) { name = NativePlatform, wrapMode = TextureWrapMode.Clamp };
        try
        {
            texture.LoadRawTextureData(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, rect, pivot, units, 0, SpriteMeshType.FullRect);
            sprite.name = NativePlatform;
            return sprite;
        }
        catch
        {
            UnityEngine.Object.Destroy(texture);
            throw;
        }
    }

    private readonly struct AssetObject
    {
        internal readonly long Start;
        internal readonly long End;

        internal AssetObject(long start, long end)
        {
            Start = start;
            End = end;
        }
    }

    private static long ReadBigEndian(BinaryReader reader, int bytes)
    {
        long value = 0;
        for (int i = 0; i < bytes; i++) value = (value << 8) | reader.ReadByte();
        return value;
    }

    private static int ReadCount(BinaryReader reader, int max)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > max) throw new InvalidDataException("Invalid Unity metadata length.");
        return count;
    }

    private static string ReadString(BinaryReader reader)
    {
        int length = ReadCount(reader, 256);
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        Align(reader);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void Align(BinaryReader reader) => reader.BaseStream.Position = (reader.BaseStream.Position + 3) & ~3L;
}
