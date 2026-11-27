using TerrariaTas.Core.Studio;
using Xunit;

namespace TerrariaTas.Core.Tests;

/// <summary>Sends a Studio message through the codec and back.</summary>
internal static class Codec
{
    public static T RoundTrip<T>(T message)
        where T : StudioMessage =>
        Assert.IsType<T>(StudioCodec.Decode(StudioCodec.Encode(message)));
}
