using Auxilia.SharedKernel.Identifiers;

namespace Auxilia.Domain.Tests.SharedKernel;

public sealed class IdGeneratorTests
{
    [Fact]
    public void New_CreatesVersion7Guid()
    {
        IdGenerator.New().Version.ShouldBe(7);
    }

    [Fact]
    public void New_WithTimeProvider_EncodesThatTimestamp()
    {
        var instant = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

        var id = IdGenerator.New(new FixedTimeProvider(instant));

        ReadUnixMilliseconds(id).ShouldBe(instant.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void New_LaterTimestamp_SortsAfterEarlierOne()
    {
        var earlier = IdGenerator.New(new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        var later = IdGenerator.New(new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero)));

        string.CompareOrdinal(earlier.ToString(), later.ToString()).ShouldBeLessThan(0);
    }

    // The first 48 bits of a version 7 UUID are the Unix timestamp in milliseconds (RFC 9562).
    private static long ReadUnixMilliseconds(Guid id) => Convert.ToInt64(id.ToString("N")[..12], 16);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
