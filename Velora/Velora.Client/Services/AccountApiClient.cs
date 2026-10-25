using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Velora.Contracts;

namespace Velora.Client.Services;

public sealed class AccountApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class AccountApiClient(IHttpClientFactory clients, IHttpContextAccessor context)
{
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct,
        string? token = null, bool authenticate = true)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (authenticate)
        {
            token ??= context.HttpContext is null ? null : await context.HttpContext.GetTokenAsync("access_token");
            if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        try { return await clients.CreateClient("Products").SendAsync(request, ct); }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        { throw new AccountApiException(503, "Accounts are temporarily unavailable. Please try again later."); }
    }

    public async Task<T> RequestAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct,
        string? token = null, bool authenticate = true)
    {
        using var response = await SendAsync(method, path, body, ct, token, authenticate);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode == HttpStatusCode.Unauthorized ? "Your session has expired. Sign in again." :
                response.StatusCode == HttpStatusCode.Forbidden ? "You do not have access to this operation." :
                response.StatusCode == HttpStatusCode.TooManyRequests ? "Too many attempts. Please wait a minute and try again." : "The account request could not be completed.";
            try
            {
                var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                if (problem.TryGetProperty("detail", out var detail)) message = detail.GetString() ?? message;
                else if (problem.TryGetProperty("errors", out var errors))
                    message = string.Join(" ", errors.EnumerateObject().SelectMany(e => e.Value.EnumerateArray().Select(v => v.GetString())));
            }
            catch (JsonException) { }
            throw new AccountApiException((int)response.StatusCode, message);
        }
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
            ?? throw new AccountApiException(503, "The account response was incomplete. Please try again.");
    }
}
