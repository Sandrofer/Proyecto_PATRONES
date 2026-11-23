using System.Collections.Concurrent;
using Pos.Application.Ports;
using Pos.Shared;

namespace Pos.Infrastructure.Idempotency;

// Protects against concurrent duplicates while the API is running.
// Durability across restarts comes from Odoo itself: every order is sent with
// the Idempotency-Key as its uuid and the use case searches it before creating.
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    readonly ConcurrentDictionary<Guid, SaleRegistrationDto?> _entries = new();

    public IdempotencyClaim Claim(Guid key)
    {
        if (_entries.TryAdd(key, null))
            return new IdempotencyClaim(IdempotencyClaimStatus.Acquired);

        return _entries.TryGetValue(key, out var result) && result is not null
            ? new IdempotencyClaim(IdempotencyClaimStatus.Completed, result)
            : new IdempotencyClaim(IdempotencyClaimStatus.InProgress);
    }

    public void Complete(Guid key, SaleRegistrationDto result) => _entries[key] = result;

    public void Release(Guid key) => _entries.TryRemove(key, out _);
}
