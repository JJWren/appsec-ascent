using Ascent.Cli.Hosting;
using Ascent.Cli.Tests.TestSupport;

namespace Ascent.Cli.Tests;

public sealed class TypeResolverTests
{
    public interface IGreeter
    {
        string Name { get; }
    }

    [Fact]
    public void Resolves_the_host_registrations_and_constructor_dependencies()
    {
        using var engine = TestEngine.Empty();
        using var host = engine.CreateHost(TextWriter.Null);
        var registrar = new TypeRegistrar(host);
        registrar.RegisterInstance(typeof(IGreeter), new Greeter("first"));
        registrar.RegisterInstance(typeof(IGreeter), new Greeter("second"));
        registrar.Register(typeof(Widget), typeof(Widget));
        var calls = 0;
        registrar.RegisterLazy(typeof(Counter), () => new Counter(++calls));
        var resolver = (TypeResolver)registrar.Build();

        resolver.Resolve(null).ShouldBeNull();
        resolver.Resolve(typeof(EngineHost)).ShouldBe(host);
        ((IGreeter)resolver.Resolve(typeof(IGreeter))!).Name.ShouldBe("second");
        ((IEnumerable<IGreeter>)resolver.Resolve(typeof(IEnumerable<IGreeter>))!).Select(g => g.Name).ShouldBe(["first", "second"]);
        ((IEnumerable<Widget>)resolver.Resolve(typeof(IEnumerable<Widget>))!).Count().ShouldBe(1);
        ((IEnumerable<string>)resolver.Resolve(typeof(IEnumerable<string>))!).ShouldBeEmpty();

        var widget = (Widget)resolver.Resolve(typeof(Widget))!;
        widget.Host.ShouldBe(host);
        widget.Greeter.Name.ShouldBe("second");

        ((Counter)resolver.Resolve(typeof(Counter))!).Value.ShouldBe(1);
        ((Counter)resolver.Resolve(typeof(Counter))!).Value.ShouldBe(1);

        resolver.Resolve(typeof(Plain)).ShouldBeOfType<Plain>();
        resolver.Resolve(typeof(IDisposable)).ShouldBeNull();
        resolver.Resolve(typeof(NeedsUnregistered)).ShouldBeNull();
    }

    public sealed class Greeter(string name) : IGreeter
    {
        public string Name { get; } = name;
    }

    public sealed class Widget(EngineHost host, IGreeter greeter)
    {
        public EngineHost Host { get; } = host;

        public IGreeter Greeter { get; } = greeter;
    }

    public sealed class Counter(int value)
    {
        public int Value { get; } = value;
    }

    public sealed class Plain;

    public sealed class NeedsUnregistered(Uri address)
    {
        public Uri Address { get; } = address;
    }
}
