using Ascent.Core.Errors;
using Ascent.Maintainer.Commands;
using Spectre.Console.Cli;

namespace Ascent.Maintainer.Hosting;

/// <summary>Builds the <c>ascent-maint</c> command app.</summary>
public static class MaintainerApp
{
    /// <summary>Creates the app over a context.</summary>
    public static CommandApp Create(MaintainerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var app = new CommandApp(new MaintainerRegistrar(context));
        app.Configure(config =>
        {
            config.SetApplicationName("ascent-maint");
            config.SetExceptionHandler((exception, _) => Handle(exception, context));
            config.AddCommand<InitShieldCommand>("init-shield")
                .WithDescription("Create sealed/shield.json (public by design). Refuses to replace an existing key.");
            config.AddCommand<KeygenCommand>("keygen")
                .WithDescription("Create the maintainer signing key (DPAPI), its encrypted backup, and the Engine's public key.");
            config.AddBranch("key", branch =>
            {
                branch.SetDescription("Manage the signing key.");
                branch.AddCommand<KeyImportCommand>("import")
                    .WithDescription("Restore the signing key from its encrypted backup.");
            });
            config.AddCommand<SealCommand>("seal")
                .WithDescription("Encrypt changed items from the sealed repository's sources into unsigned bundles.");
            config.AddCommand<SignCommand>("sign")
                .WithDescription("Check and sign every unsigned or invalidly signed bundle; record each signature in the ledger.");
            config.AddCommand<VerifyCommand>("verify")
                .WithDescription("Verify every bundle's signature and that it decrypts.");
        });
        return app;
    }

    internal static int Handle(Exception exception, MaintainerContext context)
    {
        switch (exception)
        {
            case AscentException ascent:
                context.Out.WriteLine("FAIL: " + ascent.Message);
                if (ascent.NextStep is { } next)
                {
                    context.Out.WriteLine("Next: " + next);
                }

                return ascent.ExitCode;
            case CommandAppException usage:
                context.Out.WriteLine("FAIL: " + usage.Message);
                return ExitCodes.Usage;
            default:
                context.Out.WriteLine("FAIL: Unexpected error (" + exception.GetType().Name + "): " + exception.Message);
                return ExitCodes.CheckFailed;
        }
    }
}

/// <summary>A minimal registrar: commands receive the <see cref="MaintainerContext"/> through their constructor.</summary>
internal sealed class MaintainerRegistrar(MaintainerContext context) : ITypeRegistrar, ITypeResolver
{
    private readonly Dictionary<Type, List<Func<object?>>> registrations = [];

    public void Register(Type service, Type implementation) => Add(service, () => Create(implementation));

    public void RegisterInstance(Type service, object implementation) => Add(service, () => implementation);

    public void RegisterLazy(Type service, Func<object> factory)
    {
        var lazy = new Lazy<object>(factory);
        Add(service, () => lazy.Value);
    }

    public ITypeResolver Build() => this;

    public object? Resolve(Type? type)
    {
        if (type is null)
        {
            return null;
        }

        if (type == typeof(MaintainerContext))
        {
            return context;
        }

        if (registrations.TryGetValue(type, out var factories) && factories.Count > 0)
        {
            return factories[^1]();
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var element = type.GetGenericArguments()[0];
            var all = registrations.TryGetValue(element, out var elementFactories) ? elementFactories.Select(f => f()).OfType<object>().ToList() : [];
            var array = Array.CreateInstance(element, all.Count);
            for (var i = 0; i < all.Count; i++)
            {
                array.SetValue(all[i], i);
            }

            return array;
        }

        return type.IsClass && !type.IsAbstract ? Create(type) : null;
    }

    private object? Create(Type implementation)
    {
        foreach (var constructor in implementation.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
        {
            var parameters = constructor.GetParameters();
            var arguments = parameters
                .Select(p => p.ParameterType == typeof(MaintainerContext)
                    ? context
                    : registrations.TryGetValue(p.ParameterType, out var factories) && factories.Count > 0 ? factories[^1]() : null)
                .ToArray();
            if (arguments.All(argument => argument is not null))
            {
                return constructor.Invoke(arguments);
            }
        }

        return null;
    }

    private void Add(Type service, Func<object?> factory)
    {
        if (!registrations.TryGetValue(service, out var list))
        {
            registrations[service] = list = [];
        }

        list.Add(factory);
    }
}
