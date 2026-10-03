using Spectre.Console.Cli;

namespace Ascent.Cli.Hosting;

/// <summary>
/// A minimal registrar for Spectre.Console.Cli (no DI container, P19). Commands receive the <see cref="EngineHost"/>
/// through their constructor; Spectre's own registrations are honored, including requests for all registrations of
/// a service as <see cref="IEnumerable{T}"/>.
/// </summary>
internal sealed class TypeRegistrar(EngineHost host) : ITypeRegistrar
{
    private readonly Dictionary<Type, List<Func<TypeResolver, object?>>> registrations = [];

    public void Register(Type service, Type implementation) => Add(service, resolver => resolver.Create(implementation));

    public void RegisterInstance(Type service, object implementation) => Add(service, _ => implementation);

    public void RegisterLazy(Type service, Func<object> factory)
    {
        var lazy = new Lazy<object>(factory);
        Add(service, _ => lazy.Value);
    }

    public ITypeResolver Build() => new TypeResolver(host, registrations);

    private void Add(Type service, Func<TypeResolver, object?> factory)
    {
        if (!registrations.TryGetValue(service, out var list))
        {
            registrations[service] = list = [];
        }

        list.Add(factory);
    }
}

/// <summary>Resolves the host, registered services, and classes whose constructor dependencies are registered.</summary>
internal sealed class TypeResolver(EngineHost host, IReadOnlyDictionary<Type, List<Func<TypeResolver, object?>>> registrations) : ITypeResolver
{
    public object? Resolve(Type? type)
    {
        if (type is null)
        {
            return null;
        }

        if (type == typeof(EngineHost))
        {
            return host;
        }

        if (registrations.TryGetValue(type, out var factories) && factories.Count > 0)
        {
            return factories[^1](this);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var element = type.GetGenericArguments()[0];
            var all = registrations.TryGetValue(element, out var elementFactories)
                ? elementFactories.Select(factory => factory(this)).Where(instance => instance is not null).ToList()
                : [];
            var array = Array.CreateInstance(element, all.Count);
            for (var i = 0; i < all.Count; i++)
            {
                array.SetValue(all[i], i);
            }

            return array;
        }

        return type.IsClass && !type.IsAbstract ? Create(type) : null;
    }

    /// <summary>Creates a class using the widest constructor whose parameters are all the host or registered services.</summary>
    internal object? Create(Type implementation)
    {
        foreach (var constructor in implementation.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
        {
            var parameters = constructor.GetParameters();
            var arguments = new object?[parameters.Length];
            var resolvable = true;
            for (var i = 0; i < parameters.Length && resolvable; i++)
            {
                var parameterType = parameters[i].ParameterType;
                arguments[i] = parameterType == typeof(EngineHost)
                    ? host
                    : registrations.TryGetValue(parameterType, out var factories) && factories.Count > 0 ? factories[^1](this) : null;
                resolvable = arguments[i] is not null;
            }

            if (resolvable)
            {
                return constructor.Invoke(arguments);
            }
        }

        return null;
    }
}
