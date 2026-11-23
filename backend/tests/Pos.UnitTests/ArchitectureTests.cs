using System.Reflection;

namespace Pos.UnitTests;

// RNF-01 evidence: the inner layers do not reference the outer ones.
public class ArchitectureTests
{
    static string[] References(Type typeInAssembly) =>
        typeInAssembly.Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

    [Fact]
    public void Domain_depends_on_no_other_project()
    {
        var refs = References(typeof(Pos.Domain.Sales.Sale));

        Assert.DoesNotContain(refs, r => r.StartsWith("Pos.", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_does_not_depend_on_Infrastructure_Api_or_web_frameworks()
    {
        var refs = References(typeof(Pos.Application.Sales.RegisterSaleInteractor));

        Assert.DoesNotContain("Pos.Infrastructure", refs);
        Assert.DoesNotContain("Pos.Api", refs);
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain("System.Net.Http", refs);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_Api()
    {
        Assert.DoesNotContain("Pos.Api", References(typeof(Pos.Infrastructure.DependencyContainer)));
    }
}
