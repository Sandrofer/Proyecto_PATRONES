namespace Pos.Domain.Customers;

// Value object: a valid Ecuadorian identity card number (cedula).
// Rules: 10 digits, province 01-24 (or 30 for Ecuadorians abroad), third
// digit lower than 6 (natural person) and the modulo 10 check digit.
public sealed record Cedula
{
    public string Value { get; }

    Cedula(string value) => Value = value;

    public static Cedula Create(string? value)
    {
        string text = (value ?? "").Trim();
        if (!IsValid(text))
            throw new DomainException($"La cédula '{text}' no es válida.");
        return new Cedula(text);
    }

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

        int check = (10 - total % 10) % 10;
        return check == value[9] - '0';
    }

    public override string ToString() => Value;
}

// Customer of a sale. A sale without customer is "consumidor final".
public sealed record Customer
{
    public const int MaxNameLength = 80;

    public Cedula Identification { get; }
    public string Name { get; }

    Customer(Cedula identification, string name)
    {
        Identification = identification;
        Name = name;
    }

    public static Customer Create(string? identification, string? name)
    {
        var cedula = Cedula.Create(identification);
        string cleanName = string.Join(' ', (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (cleanName.Length < 3)
            throw new DomainException("El nombre del cliente debe tener al menos 3 caracteres.");
        if (cleanName.Length > MaxNameLength)
            throw new DomainException($"El nombre del cliente no puede superar {MaxNameLength} caracteres.");
        if (!cleanName.All(c => char.IsLetter(c) || c is ' ' or '.' or '\'' or '-'))
            throw new DomainException("El nombre del cliente solo puede contener letras.");

        return new Customer(cedula, cleanName);
    }
}
