using Pos.Domain.Discounts;
using Pos.Shared;

namespace Pos.Application.Discounts;

public interface IDiscountPolicyCatalog
{
    IReadOnlyList<IDiscountPolicy> All { get; }
    IDiscountPolicy? Find(string? code);
}

// Receives every registered policy from the DI container. A new policy only
// needs to be registered; this class does not change (Open/Closed).
public sealed class DiscountPolicyCatalog(IEnumerable<IDiscountPolicy> policies) : IDiscountPolicyCatalog
{
    readonly IReadOnlyList<IDiscountPolicy> _policies = policies.ToList();

    public IReadOnlyList<IDiscountPolicy> All => _policies;

    public IDiscountPolicy? Find(string? code) =>
        _policies.FirstOrDefault(p => string.Equals(
            p.Code, string.IsNullOrWhiteSpace(code) ? NoDiscountPolicy.PolicyCode : code,
            StringComparison.OrdinalIgnoreCase));
}

public interface IGetDiscountPoliciesInputPort
{
    IReadOnlyList<DiscountPolicyDto> Handle();
}

public sealed class GetDiscountPoliciesInteractor(IDiscountPolicyCatalog catalog) : IGetDiscountPoliciesInputPort
{
    public IReadOnlyList<DiscountPolicyDto> Handle() =>
        catalog.All.Select(p => new DiscountPolicyDto(
            p.Code, p.Description, p.RequiresPercent, p.MaxPercent, p.MinimumAmount, p.RequiresIdentifiedCustomer)).ToList();
}
