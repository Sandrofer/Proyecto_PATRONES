using System.Text.Json;
using Microsoft.JSInterop;
using Pos.Frontend.Application;
using Pos.Shared;

namespace Pos.Frontend.Infrastructure;

// Keeps the operator's JWT in the browser's sessionStorage (cleared when the
// tab is closed). The Odoo API key never reaches the browser.
public sealed class BrowserAuthSession(IJSRuntime js) : IAuthSession
{
    const string StorageKey = "pos.auth";
    LoginResponse? _current;

    public event Action? Changed;

    public string? AccessToken => IsAuthenticated ? _current!.AccessToken : null;
    public string? OperatorName => _current?.OperatorName;
    public bool IsAuthenticated => _current is not null && _current.ExpiresAtUtc > DateTime.UtcNow;

    public async Task InitializeAsync()
    {
        try
        {
            string? json = await js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey);
            _current = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<LoginResponse>(json);
        }
        catch (Exception ex) when (ex is JSException or JsonException)
        {
            _current = null;
        }
    }

    public async Task SignInAsync(LoginResponse response)
    {
        _current = response;
        await js.InvokeVoidAsync("sessionStorage.setItem", StorageKey, JsonSerializer.Serialize(response));
        Changed?.Invoke();
    }

    public async Task SignOutAsync()
    {
        _current = null;
        await js.InvokeVoidAsync("sessionStorage.removeItem", StorageKey);
        Changed?.Invoke();
    }
}
