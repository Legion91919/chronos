using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Chronos.Architecture.Tests;

/// <summary>Write side, read side and the event store keep their boundaries.</summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly EventStore = typeof(Chronos.EventStore.IEventStore).Assembly;
    private static readonly Assembly Ledger = typeof(Chronos.Ledger.ILedgerCommands).Assembly;
    private static readonly Assembly Statements = typeof(Chronos.Statements.ILoanQueries).Assembly;
    private static readonly Assembly[] Modules = [Ledger, Statements];

    private static string[] References(Assembly a) => a.GetReferencedAssemblies().Select(r => r.Name!).ToArray();

    [Fact]
    public void The_event_store_knows_no_module()
    {
        Assert.DoesNotContain(References(EventStore), n => n.StartsWith("Chronos.", StringComparison.Ordinal));
    }

    [Fact]
    public void The_write_side_does_not_depend_on_the_read_side()
    {
        Assert.DoesNotContain(References(Ledger), n => n == Statements.GetName().Name);
    }

    [Fact]
    public void No_module_opens_its_internals_to_another_module()
    {
        foreach (var module in Modules.Append(EventStore))
        {
            var friends = module.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName).ToArray();
            Assert.All(friends, f => Assert.True(f == "Chronos.Tests", $"{module.GetName().Name} exposes internals to {f}"));
        }
    }

    [Fact]
    public void The_read_side_uses_only_public_types_of_the_write_side()
    {
        // Every type Statements touches from Ledger (fields, properties, method signatures) must be public.
        var used = Statements.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Select(f => f.FieldType)
                .Concat(t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))))
            .SelectMany(Flatten)
            .Where(t => t.Assembly == Ledger)
            .Distinct()
            .ToArray();
        Assert.NotEmpty(used);
        Assert.All(used, t => Assert.True(t.IsPublic || t.IsNestedPublic, $"Statements uses internal Ledger type {t.FullName}"));
    }

    [Fact]
    public void Ledger_domain_types_are_internal()
    {
        Assert.False(typeof(Chronos.Ledger.Domain.Loan).IsPublic);
    }

    private static IEnumerable<Type> Flatten(Type t) =>
        t.IsGenericType ? t.GetGenericArguments().SelectMany(Flatten).Append(t.GetGenericTypeDefinition())
        : t.HasElementType ? Flatten(t.GetElementType()!) : [t];
}
