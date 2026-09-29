using System.Text;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class Rar5ArchiveValidatorTests : IDisposable
{
    private static readonly byte[] Signature = [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x01, 0x00];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-rar5-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Validate_AcceptsOrdinaryFilesAndSkipsPackedData()
    {
        // Header-looking payload must remain data; inspection never decompresses it.
        byte[] misleadingPayload = Header(4, [0]);
        byte[] archive = Archive(
            Header(2, FileFields("first.ini"), data: misleadingPayload),
            Header(3, FileFields("CMT"), data: "comment"u8.ToArray()),
            Header(2, FileFields("目录/second.ini"), extra: Extra(3, [1, 0, 0, 0, 0])));

        Validate(archive);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(128)]
    public void Validate_RejectsEveryRedirectionKindInFilesAndServices(int redirectionType)
    {
        byte[] redirection = Extra(5, Join(VInt((ulong)redirectionType), [0, 6], "target"u8.ToArray()));
        foreach (ulong headerType in new ulong[] { 2, 3 })
        {
            var exception = Assert.Throws<InvalidDataException>(() => Validate(Archive(
                Header(headerType, FileFields("entry"), extra: redirection))));
            Assert.Contains("unsupported link or copy link", exception.Message);
        }
    }

    [Fact]
    public void Validate_FindsRedirectionAfterOtherRecordsAndPackedData()
    {
        byte[] extras = Join(Extra(99, [1, 2, 3]), Extra(5, [5, 0, 1, 120]));
        Assert.Throws<InvalidDataException>(() => Validate(Archive(
            Header(2, FileFields("first"), data: new byte[512]),
            Header(2, FileFields("second"), extra: extras))));
    }

    [Fact]
    public void Validate_DoesNotInterpretMainMetadataAsFileRedirection()
    {
        Validate(Join(Signature, Header(1, [0], extra: Extra(5, [0])), Header(5, [0])));
    }

    [Fact]
    public void Validate_RejectsEncryptedHeaders()
    {
        Assert.Throws<InvalidDataException>(() => Validate(Join(Signature, Header(4, [0]))));
    }

    [Theory]
    [InlineData("4D5A")]
    [InlineData("7F454C46")]
    public void Validate_RejectsExecutablePrefixesRegardlessOfExtension(string prefixHex)
    {
        Assert.Throws<InvalidDataException>(() => Validate(Join(Convert.FromHexString(prefixHex), new byte[32], Archive())));
    }

    [Theory]
    [InlineData("")]
    [InlineData("504B0304")]
    [InlineData("377ABCAF271C")]
    [InlineData("526172211A0700")]
    public void Validate_LeavesOtherFormatsForTarInspection(string prefixHex)
    {
        Validate(Convert.FromHexString(prefixHex));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Validate_RejectsMultivolumeMainHeaders(int flags)
    {
        Assert.Throws<InvalidDataException>(() => Validate(Join(Signature, Header(1, [(byte)flags]), Header(5, [0]))));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(128)]
    public void Validate_RejectsSplitOrUnknownHeaderFlags(int flags)
    {
        Assert.Throws<InvalidDataException>(() => Validate(Archive(Header(2, FileFields("entry"), flags: (ulong)flags))));
    }

    [Fact]
    public void Validate_RejectsQuickOpenServiceAndActiveLocator()
    {
        Assert.Throws<InvalidDataException>(() => Validate(Archive(Header(3, FileFields("QO")))));
        Assert.Throws<InvalidDataException>(() => Validate(Join(Signature,
            Header(1, [0], extra: Extra(1, [1, 42])), Header(5, [0]))));
        // A reserved zero locator is explicitly inactive in the RAR5 format.
        Validate(Join(Signature, Header(1, [0], extra: Extra(1, [3, 0, 42])), Header(5, [0])));
    }

    [Fact]
    public void Validate_ReadsOptionalFileFieldsAndFullWidthVInts()
    {
        byte[] fields = Join([6], VInt(ulong.MaxValue), [0], new byte[8], [0, 0, 1, 120]);
        Validate(Archive(Header(2, fields)));
        // Space-reserved integers are legal, so do not require shortest encoding.
        Validate(Join(Signature, Header(1, [0x80, 0]), Header(5, [0])));
    }

    [Fact]
    public void Validate_AllowsAppendedArchiveSignatureData()
    {
        Validate(Join(Archive(), "external digital signature"u8.ToArray()));
    }

    [Theory]
    [MemberData(nameof(MalformedArchives))]
    public void Validate_RejectsMalformedOrIncompleteStructure(byte[] archive)
    {
        Assert.Throws<InvalidDataException>(() => Validate(archive));
    }

    public static IEnumerable<object[]> MalformedArchives()
    {
        yield return [Signature]; // No main/end header.
        yield return [Join(Signature, new byte[3])]; // Incomplete CRC field.
        yield return [Join(Signature, new byte[4], [0x80])]; // Truncated size vint.
        yield return [Join(Signature, new byte[4], [0x80, 0x80, 0x80, 0])]; // Oversized size vint.
        yield return [Join(Signature, new byte[4], [0])]; // Empty header.
        yield return [Join(Signature, new byte[4], [100, 1, 0, 0])]; // Header exceeds file.
        yield return [Join(Signature, Header(1, [0]))]; // Missing end marker.
        yield return [Join(Signature, Header(5, [0]))]; // Missing main header.
        yield return [Archive(Header(1, [0]))]; // Duplicate main header.
        yield return [Archive(Header(99, []))]; // Unknown block interpretation.
        yield return [Archive(Header(2, []))]; // Missing file fields.
        yield return [Archive(Header(2, [0x80]))]; // Truncated field vint.
        yield return [Archive(Header(2, Enumerable.Repeat((byte)0xff, 10).ToArray()))]; // Overflowing vint.
        yield return [Archive(Header(2, [0x10, 0, 0, 0, 0, 1, 120]))]; // Unknown file flags.
        yield return [Archive(Header(2, [2, 0, 0]))]; // Truncated time field.
        yield return [Archive(Header(2, [4, 0, 0]))]; // Truncated CRC field.
        yield return [Archive(Header(2, [0, 0, 0, 0, 0, 10, 120]))]; // Truncated name.
        yield return [Archive(Header(2, FileFields("")))]; // Empty name.
        yield return [Archive(Header(2, Join(FileFields("x"), [0])))]; // Unexplained field bytes.
        yield return [Archive(Header(2, FileFields("x"), extra: [0]))]; // Zero-size extra record.
        yield return [Archive(Header(2, FileFields("x"), extra: [4, 3]))]; // Extra exceeds area.
        yield return [Archive(Header(2, FileFields("x"), extra: [1, 0x80]))]; // Truncated extra type.
        yield return [Archive(Header(2, FileFields("x"), declaredExtraSize: 100))];
        yield return [Archive(Header(2, FileFields("x"), declaredDataSize: ulong.MaxValue))];
        yield return [Join(Signature, Header(1, [0], data: [0]), Header(5, [0]))];
        yield return [Join(Signature, Header(1, [0x20]), Header(5, [0]))];
        yield return [Join(Signature, Header(1, [0]), Header(5, [1]))]; // Another volume follows.
        yield return [Join(Signature, Header(1, [0]), Header(5, [0], extra: [1, 3]))];
        yield return [Join(Signature, Header(1, [0]), Header(5, [0], data: [0]))];
        yield return [Join(Signature, Header(1, [0], extra: Extra(1, [1])), Header(5, [0]))];
        yield return [Join(Signature, Header(1, [0], extra: Extra(1, [4])), Header(5, [0]))];
        yield return [Join(Signature, Header(1, [0], extra: Extra(1, [0, 0])), Header(5, [0]))];
    }

    private void Validate(byte[] archive)
    {
        Directory.CreateDirectory(_root);
        // Deliberately misleading extension verifies content-based detection.
        string path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".7z");
        File.WriteAllBytes(path, archive);
        Rar5ArchiveValidator.Validate(path);
    }

    private static byte[] Archive(params byte[][] blocks) => Join(Signature, Header(1, [0]), Join(blocks), Header(5, [0]));

    private static byte[] Header(ulong type, byte[] fields, byte[]? extra = null, byte[]? data = null,
        ulong flags = 0, ulong? declaredExtraSize = null, ulong? declaredDataSize = null)
    {
        extra ??= [];
        data ??= [];
        bool hasExtra = extra.Length > 0 || declaredExtraSize.HasValue;
        bool hasData = data.Length > 0 || declaredDataSize.HasValue;
        flags |= (hasExtra ? 1UL : 0) | (hasData ? 2UL : 0);
        byte[] header = Join(VInt(type), VInt(flags),
            hasExtra ? VInt(declaredExtraSize ?? (ulong)extra.Length) : [],
            hasData ? VInt(declaredDataSize ?? (ulong)data.Length) : [], fields, extra);
        // This validator checks framing, not CRC. Real tools retain integrity checks.
        return Join(new byte[4], VInt((ulong)header.Length), header, data);
    }

    private static byte[] FileFields(string name)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(name);
        return Join([0, 0, 0, 0, 0], VInt((ulong)encoded.Length), encoded);
    }

    private static byte[] Extra(ulong type, byte[] data)
    {
        byte[] record = Join(VInt(type), data);
        return Join(VInt((ulong)record.Length), record);
    }

    private static byte[] VInt(ulong value)
    {
        var encoded = new List<byte>();
        do
        {
            byte current = (byte)(value & 0x7f);
            value >>= 7;
            encoded.Add(value == 0 ? current : (byte)(current | 0x80));
        } while (value != 0);
        return encoded.ToArray();
    }

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
