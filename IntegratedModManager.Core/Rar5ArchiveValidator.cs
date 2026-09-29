namespace IntegratedModManager.Core;

/// <summary>
/// Supplements tar's listing with RAR5 redirection checks. Some libarchive
/// versions omit junction and file-copy records from their public listing.
/// </summary>
public static class Rar5ArchiveValidator
{
    private static ReadOnlySpan<byte> Signature => [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x01, 0x00];
    private const long MaximumTotalHeaderBytes = 64 * 1024 * 1024;
    private const int MaximumHeaderCount = 262144;

    public static void Validate(string archivePath)
    {
        using var stream = File.OpenRead(archivePath);
        Span<byte> prefix = stackalloc byte[8];
        int prefixLength = stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
        ReadOnlySpan<byte> actualPrefix = prefix[..prefixLength];
        if (actualPrefix.StartsWith("MZ"u8) || actualPrefix.StartsWith("\u007fELF"u8))
        {
            // libarchive also recognizes embedded RAR5 in PE/ELF executables.
            // Reject that route instead of letting it bypass offset-zero inspection.
            throw Unsupported("Self-extracting archives cannot be inspected safely for Bandizip.");
        }
        if (!actualPrefix.SequenceEqual(Signature))
        {
            return;
        }

        bool sawMainHeader = false;
        long totalHeaderBytes = 0;
        for (int headerCount = 0; headerCount < MaximumHeaderCount; headerCount++)
        {
            // RAR5 layout: CRC32, header-size vint, header bytes, then packed data.
            // CRC/data integrity remains the archive tools' responsibility. Bounds
            // are checked here before every read/seek, without decompressing data.
            Skip(stream, 4);
            int headerSize = ReadHeaderSize(stream);
            totalHeaderBytes += headerSize;
            if (headerSize == 0 || totalHeaderBytes > MaximumTotalHeaderBytes)
            {
                throw Unsupported("RAR5 inspection exceeds the supported header limits.");
            }
            if (headerSize > stream.Length - stream.Position)
            {
                throw Malformed();
            }
            byte[] header = new byte[headerSize];
            stream.ReadExactly(header);
            int offset = 0;
            ulong type = ReadVInt(header, ref offset);
            ulong flags = ReadVInt(header, ref offset);
            if ((flags & ~0x7fUL) != 0 || (flags & 0x18) != 0)
            {
                throw Unsupported("Unknown or split RAR5 headers cannot be inspected safely.");
            }
            ulong extraSize = (flags & 1) != 0 ? ReadVInt(header, ref offset) : 0;
            ulong dataSize = (flags & 2) != 0 ? ReadVInt(header, ref offset) : 0;
            if (extraSize > (ulong)(header.Length - offset))
            {
                throw Malformed();
            }
            int extraOffset = header.Length - (int)extraSize;
            ReadOnlySpan<byte> fields = header.AsSpan(0, extraOffset);
            if (type == 4)
            {
                throw Unsupported("Encrypted RAR5 headers cannot be inspected safely.");
            }
            if (!sawMainHeader && type != 1)
            {
                throw Malformed();
            }

            switch (type)
            {
                case 1:
                    if (sawMainHeader || dataSize != 0)
                    {
                        throw Malformed();
                    }
                    ulong archiveFlags = ReadVInt(fields, ref offset);
                    if ((archiveFlags & ~0x1fUL) != 0 || (archiveFlags & 3) != 0)
                    {
                        // A later volume could introduce a link not present in this file.
                        throw Unsupported("Multivolume or unknown RAR5 archives require 7-Zip.");
                    }
                    sawMainHeader = true;
                    break;
                case 2:
                case 3:
                    ReadFileFields(fields, ref offset, isService: type == 3);
                    break;
                case 5:
                    if (dataSize != 0 || extraSize != 0 || ReadVInt(fields, ref offset) != 0)
                    {
                        throw Unsupported("Unsupported RAR5 end-of-archive header.");
                    }
                    break;
                default:
                    throw Unsupported("Unknown RAR5 header type cannot be inspected safely.");
            }
            if (offset != extraOffset)
            {
                throw Malformed();
            }
            ValidateExtraRecords(header.AsSpan(extraOffset), type);
            Skip(stream, dataSize);
            if (type == 5)
            {
                // The format explicitly permits appended signatures after this marker.
                return;
            }
        }
        throw Unsupported("RAR5 inspection exceeds the supported header count.");
    }

    private static void ReadFileFields(ReadOnlySpan<byte> fields, ref int offset, bool isService)
    {
        ulong flags = ReadVInt(fields, ref offset);
        if ((flags & ~0xfUL) != 0)
        {
            throw Unsupported("Unknown RAR5 file flags cannot be inspected safely.");
        }
        ReadVInt(fields, ref offset); // Unpacked size; never used to allocate memory.
        ReadVInt(fields, ref offset); // Host-specific attributes.
        if ((flags & 2) != 0)
        {
            Advance(fields, ref offset, 4); // Modification time.
        }
        if ((flags & 4) != 0)
        {
            Advance(fields, ref offset, 4); // Data CRC32.
        }
        ReadVInt(fields, ref offset); // Compression information.
        ReadVInt(fields, ref offset); // Host OS.
        ulong nameLength = ReadVInt(fields, ref offset);
        int nameOffset = offset;
        Advance(fields, ref offset, nameLength);
        if (nameLength == 0)
        {
            throw Malformed();
        }
        if (isService && fields[nameOffset..offset].SequenceEqual("QO"u8))
        {
            // Quick-open data caches a second representation of file headers. An
            // extractor may use that copy while tar lists the sequential headers.
            throw Unsupported("RAR5 quick-open headers require 7-Zip for consistent inspection.");
        }
    }

    private static void ValidateExtraRecords(ReadOnlySpan<byte> extra, ulong headerType)
    {
        int offset = 0;
        while (offset < extra.Length)
        {
            ulong size = ReadVInt(extra, ref offset);
            int recordStart = offset;
            Advance(extra, ref offset, size);
            ReadOnlySpan<byte> record = extra[recordStart..offset];
            int recordOffset = 0;
            ulong type = ReadVInt(record, ref recordOffset);
            if (headerType is 2 or 3 && type == 5)
            {
                // Reject the whole record class, including future redirection kinds.
                throw Unsupported("The RAR5 archive contains an unsupported link or copy link.");
            }
            if (headerType == 1 && type == 1)
            {
                // A locator could point to cached headers hidden inside a data area,
                // so checking only sequential service-header names is insufficient.
                ulong flags = ReadVInt(record, ref recordOffset);
                if ((flags & ~3UL) != 0
                    || ((flags & 1) != 0 && ReadVInt(record, ref recordOffset) != 0))
                {
                    throw Unsupported("RAR5 quick-open or unknown locator records require 7-Zip.");
                }
                if ((flags & 2) != 0)
                {
                    ReadVInt(record, ref recordOffset); // Recovery-data offset.
                }
                if (recordOffset != record.Length)
                {
                    throw Malformed();
                }
            }
        }
    }

    private static int ReadHeaderSize(Stream stream)
    {
        int size = 0;
        for (int index = 0; index < 3; index++)
        {
            int value = stream.ReadByte();
            if (value < 0)
            {
                throw Malformed();
            }
            size |= (value & 0x7f) << (index * 7);
            if ((value & 0x80) == 0)
            {
                return size;
            }
        }
        throw Malformed();
    }

    private static ulong ReadVInt(ReadOnlySpan<byte> data, ref int offset)
    {
        ulong result = 0;
        for (int index = 0; index < 10; index++)
        {
            if (offset >= data.Length)
            {
                throw Malformed();
            }
            byte value = data[offset++];
            if (index == 9 && value > 1)
            {
                throw Malformed();
            }
            result |= (ulong)(value & 0x7f) << (index * 7);
            if ((value & 0x80) == 0)
            {
                return result;
            }
        }
        throw Malformed();
    }

    private static void Advance(ReadOnlySpan<byte> data, ref int offset, ulong size)
    {
        if (size > (ulong)(data.Length - offset))
        {
            throw Malformed();
        }
        offset += (int)size;
    }

    private static void Skip(Stream stream, ulong size)
    {
        if (size > (ulong)(stream.Length - stream.Position))
        {
            throw Malformed();
        }
        stream.Position += (long)size;
    }

    private static InvalidDataException Malformed() => new("The RAR5 archive has a malformed or truncated header.");
    private static InvalidDataException Unsupported(string message) => new(message);
}
