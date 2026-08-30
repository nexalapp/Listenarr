using Listenarr.Api.Middleware;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Middleware;

/// <summary>
/// Request bodies are logged before anything else can redact them, so this is the last
/// line between a secret and the log file.
/// </summary>
[Trait("Name", "RequestBodyRedactionTests")]
[Trait("Category", "Middleware")]
public sealed class RequestBodyRedactionTests : BaseTests
{
    [Fact]
    public void SecretsNestedInsideAnEscapedSettingsBlobAreRedacted()
    {
        // The shape that leaked a real forum password: a JSON string inside JSON, with a
        // key an exact-name pattern does not match.
        const string body =
            """{"name":"","additionalSettings":"{\"abook_username\":\"nexal\",\"abook_password\":\"hunter2\"}"}""";

        var redacted = RequestBodyLoggingMiddleware.RedactSensitiveJsonFields(body);

        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
        Assert.Contains("nexal", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mam_id", "MAMSECRET")]
    [InlineData("nzbking_api_key", "KEYVALUE")]
    [InlineData("abook_session_cookie", "COOKIEVALUE")]
    [InlineData("abook_password", "PASSVALUE")]
    public void SecretsAreMatchedOnKeySubstringNotExactName(string key, string secret)
    {
        var body = $$"""{"additionalSettings":"{\"{{key}}\":\"{{secret}}\"}"}""";

        var redacted = RequestBodyLoggingMiddleware.RedactSensitiveJsonFields(body);

        Assert.DoesNotContain(secret, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void PlainTopLevelSecretsAreStillRedacted()
    {
        const string body = """{"password":"hunter2","apiKey":"abc123","username":"nexal"}""";

        var redacted = RequestBodyLoggingMiddleware.RedactSensitiveJsonFields(body);

        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", redacted, StringComparison.Ordinal);
        Assert.Contains("nexal", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void NonSecretFieldsAreLeftReadable()
    {
        // Redacting everything would make the log useless for diagnosing anything.
        const string body = """{"name":"abook.link","implementation":"AbookLink","url":"https://abook.link"}""";

        var redacted = RequestBodyLoggingMiddleware.RedactSensitiveJsonFields(body);

        Assert.Contains("AbookLink", redacted, StringComparison.Ordinal);
        Assert.Contains("abook.link", redacted, StringComparison.Ordinal);
    }
}
