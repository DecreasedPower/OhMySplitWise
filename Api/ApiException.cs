namespace SplitMoneyTg.Api;

public sealed class ApiException(int statusCode, string title, string detail, string? code = null,
    IReadOnlyDictionary<string, object?>? extensions = null) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
    public string Title { get; } = title;
    public string? Code { get; } = code;
    public IReadOnlyDictionary<string, object?>? Extensions { get; } = extensions;
}
