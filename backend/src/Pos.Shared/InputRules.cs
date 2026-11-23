using System.Text.RegularExpressions;

namespace Pos.Shared;

// Input validation shared by the API and the browser (RNF-09: validation of
// inputs). The browser uses it for immediate feedback; the API applies it
// again because the browser is never trusted.
public static partial class InputRules
{
    public const int MaxSearchLength = 50;
    public const int MaxUsernameLength = 50;
    public const int MaxPercentDecimals = 2;

    // Letters (with accents and ñ), digits, spaces and the symbols that
    // appear in product names and codes: - . _ /
    [GeneratedRegex(@"^[\p{L}\p{N} .\-_/]*$")]
    private static partial Regex SearchPattern();

    // Odoo logins: letters, digits and . _ - @ (also e-mail logins).
    [GeneratedRegex(@"^[\p{L}\p{N}._\-@]+$")]
    private static partial Regex UsernamePattern();

    public const int MaxQuantityPerLine = 999;
    public const int MaxLines = 50;
    public const int MinNameLength = 3;
    public const int MaxNameLength = 80;

    // Person names: letters (with accents), spaces and . ' -
    [GeneratedRegex(@"^[\p{L} .'\-]+$")]
    private static partial Regex PersonNamePattern();

    public static string? ValidatePersonName(string? text)
    {
        string value = string.Join(' ', (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (value.Length < MinNameLength)
            return $"Ingrese el nombre del cliente (mínimo {MinNameLength} letras).";
        if (value.Length > MaxNameLength)
            return $"El nombre del cliente no puede superar {MaxNameLength} caracteres.";
        return PersonNamePattern().IsMatch(value) ? null : "El nombre del cliente solo puede contener letras.";
    }

    public const string SearchCharactersMessage =
        "La búsqueda solo admite letras, números, espacios y los símbolos - . _ /";
    public const string UsernameCharactersMessage =
        "El usuario solo admite letras, números y los símbolos . _ - @";

    // Returns an error message or null when the text is acceptable.
    public static string? ValidateSearch(string? text)
    {
        string value = text?.Trim() ?? "";
        if (value.Length > MaxSearchLength)
            return $"La búsqueda admite como máximo {MaxSearchLength} caracteres.";
        return SearchPattern().IsMatch(value) ? null : SearchCharactersMessage;
    }

    public static string? ValidateUsername(string? text)
    {
        string value = text?.Trim() ?? "";
        if (value.Length == 0)
            return "Ingrese el usuario.";
        if (value.Length > MaxUsernameLength)
            return $"El usuario admite como máximo {MaxUsernameLength} caracteres.";
        return UsernamePattern().IsMatch(value) ? null : UsernameCharactersMessage;
    }

    public static bool HasValidDecimals(decimal value, int decimals = MaxPercentDecimals) =>
        decimal.Round(value, decimals) == value;
}
