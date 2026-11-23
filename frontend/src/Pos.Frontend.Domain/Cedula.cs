namespace Pos.Frontend.Domain;

// Ecuadorian cedula check (same algorithm as the API) for immediate feedback.
// The API validates it again: the browser is never trusted.
public static class Cedula
{
    public static bool IsValid(string? value)
    {
        if (value is null || value.Length != 10 || !value.All(char.IsAsciiDigit))
            return false;

        int province = (value[0] - '0') * 10 + (value[1] - '0');
        if (province is < 1 or > 24 && province != 30)
            return false;
        if (value[2] - '0' > 5)
            return false;

        int total = 0;
        for (int i = 0; i < 9; i++)
        {
            int digit = (value[i] - '0') * (i % 2 == 0 ? 2 : 1);
            total += digit > 9 ? digit - 9 : digit;
        }
        return (10 - total % 10) % 10 == value[9] - '0';
    }
}
