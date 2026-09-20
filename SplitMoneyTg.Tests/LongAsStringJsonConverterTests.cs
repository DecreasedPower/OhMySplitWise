using System.Text.Json;
using SplitMoneyTg.Api;
using Xunit;

namespace SplitMoneyTg.Tests;

public sealed class LongAsStringJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new LongAsStringJsonConverter() }
    };

    [Fact]
    public void Read_AcceptsTelegramNumericIds()
    {
        var value = JsonSerializer.Deserialize<Payload>("{\"Id\":123456789}", Options);

        Assert.Equal(123456789, value!.Id);
    }

    [Fact]
    public void Write_UsesStringForJavascriptSafety()
    {
        var json = JsonSerializer.Serialize(new Payload(long.MinValue), Options);

        Assert.Equal("{\"Id\":\"-9223372036854775808\"}", json);
    }

    private sealed record Payload(long Id);
}
