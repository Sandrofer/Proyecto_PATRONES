namespace Pos.Domain;

// A business rule was violated. The message is meant for the operator.
public sealed class DomainException(string message) : Exception(message);
