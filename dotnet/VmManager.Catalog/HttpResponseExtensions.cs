namespace VmManager.Catalog;

public static class HttpResponseExtensions
{
    public static async Task EnsureSuccessWithContextAsync(
        this HttpResponseMessage response,
        string operation
    )
    {
        await HttpErrorHelper.EnsureSuccessOrThrowAsync(response, operation);
    }
}
