using System.Text;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeJwtUtilitiesTests
{
    [Fact]
    public void DecodeJwtPayload_Reads_Base64Url_Payload()
    {
        var token = CreateJwt("""{"exp":123,"sub":"worker"}""");

        var payload = BridgeJwtUtilities.DecodeJwtPayload(token);

        Assert.NotNull(payload);
        Assert.Equal(123, payload!["exp"]!.GetValue<int>());
        Assert.Equal("worker", payload["sub"]!.GetValue<string>());
    }

    [Fact]
    public void DecodeJwtPayload_Strips_SessionIngress_Prefix()
    {
        var token = "sk-ant-si-" + CreateJwt("""{"exp":456}""");

        var payload = BridgeJwtUtilities.DecodeJwtPayload(token);

        Assert.NotNull(payload);
        Assert.Equal(456, payload!["exp"]!.GetValue<int>());
    }

    [Fact]
    public void DecodeJwtPayload_Returns_Null_For_Malformed_Token()
    {
        Assert.Null(BridgeJwtUtilities.DecodeJwtPayload("not-a-jwt"));
        Assert.Null(BridgeJwtUtilities.DecodeJwtPayload("a.b"));
        Assert.Null(BridgeJwtUtilities.DecodeJwtPayload("a.b.c"));
    }

    [Fact]
    public void DecodeJwtExpiry_Returns_Exp_Claim_Or_Null()
    {
        var tokenWithExp = CreateJwt("""{"exp":789}""");
        var tokenWithoutExp = CreateJwt("""{"sub":"worker"}""");

        Assert.Equal(789, BridgeJwtUtilities.DecodeJwtExpiry(tokenWithExp));
        Assert.Null(BridgeJwtUtilities.DecodeJwtExpiry(tokenWithoutExp));
    }

    private static string CreateJwt(string payloadJson)
    {
        const string headerJson = """{"alg":"none","typ":"JWT"}""";
        return $"{ToBase64Url(headerJson)}.{ToBase64Url(payloadJson)}.sig";
    }

    private static string ToBase64Url(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
