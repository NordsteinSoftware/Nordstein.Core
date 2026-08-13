using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Autofac;
using Nordstein.Core.Common.DependencyInjection;
using Nordstein.Core.Domain.Events;
using Nordstein.Core.Domain.Events.Internal;

namespace Nordstein.Core.Domain;

/// <summary>
/// Discovers domain implementations and generators in explicitly supplied consumer assemblies.
/// </summary>
public sealed class Module : Autofac.Module
{
    private const string FoundationRegisteredKey = "Nordstein.Core.Domain.FoundationRegistered";
    private const string RegistrationsKey = "Nordstein.Core.Domain.Registrations";

    private readonly IReadOnlyCollection<Assembly> domainAssemblies;

    public Module(params Assembly[] domainAssemblies)
    {
        ArgumentNullException.ThrowIfNull(domainAssemblies);
        if (domainAssemblies.Length == 0)
        {
            throw new ArgumentException("At least one domain assembly is required.", nameof(domainAssemblies));
        }

        this.domainAssemblies = domainAssemblies.Distinct().ToArray();
    }

    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);
        if (!builder.Properties.ContainsKey(FoundationRegisteredKey))
        {
            builder.Properties[FoundationRegisteredKey] = true;
            builder.RegisterModule<Common.Module>();
            builder.RegisterType<EntityEventService>()
                .As<IEntityEventService>()
                .SingleInstance();
        }

        if (!builder.Properties.TryGetValue(RegistrationsKey, out object? stored)
            || stored is not Dictionary<Type, Type> registered)
        {
            registered = new Dictionary<Type, Type>();
            builder.Properties[RegistrationsKey] = registered;
        }

        var directBases = new HashSet<Type> { typeof(IDomainEntity), typeof(IDomainObject), typeof(IArchivable) };
        Type[] productTypes = domainAssemblies.SelectMany(assembly => assembly.GetTypes()).Distinct().ToArray();
        var domainInterfaceTypes = productTypes
            .Where(type => type is { IsInterface: true }
                && type != typeof(IDomainEntity)
                && type != typeof(IDomainObject)
                && type != typeof(IArchivable)
                && !(type.IsGenericTypeDefinition && type.GetGenericTypeDefinition() == typeof(IDomainEntity<>)))
            .Where(type =>
            {
                Type[] all = type.GetInterfaces();
                var transitive = all.SelectMany(candidate => candidate.GetInterfaces()).ToHashSet();
                return all.Where(candidate => !transitive.Contains(candidate)).Any(candidate =>
                    directBases.Contains(candidate)
                    || candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IDomainEntity<>));
            })
            .ToArray();

        var registrations = new HashSet<(Type InterfaceType, Type ImplementationType)>();
        foreach (Type domainInterfaceType in domainInterfaceTypes)
        {
            CollectEntityRegistrations(domainInterfaceType, productTypes, registrations);
        }

        foreach ((Type interfaceType, Type implementationType) in registrations)
        {
            ConfigureEntity(builder, interfaceType, implementationType, productTypes, registered);
        }

        Type[] objectGeneratorTypes = productTypes
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => type.GetInterfaces().Any(candidate =>
                candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IDomainObjectGenerator<>)))
            .Where(type => !type.GetInterfaces().Any(candidate =>
                candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IDomainEntityGenerator<>)))
            .ToArray();
        foreach (Type generatorType in objectGeneratorTypes)
        {
            RegisterInterfaces(builder, generatorType, registered);
        }
    }

    private static void CollectEntityRegistrations(
        Type domainInterfaceType,
        IReadOnlyCollection<Type> productTypes,
        ISet<(Type InterfaceType, Type ImplementationType)> registrations)
    {
        foreach (Type domainObjectType in productTypes.Where(type =>
                     domainInterfaceType.IsAssignableFrom(type)
                     && type is { IsInterface: false, IsAbstract: false }))
        {
            Type[] candidates = domainObjectType.GetInterfaces()
                .Where(domainInterfaceType.IsAssignableFrom)
                .ToArray();
            Type[] mostDerived = candidates
                .Where(candidate => !candidates.Any(other =>
                    candidate != other && candidate.IsAssignableFrom(other)))
                .ToArray();
            if (mostDerived.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Multiple equally specific domain interfaces for {domainObjectType.FullName}: "
                    + string.Join(", ", mostDerived.Select(candidate => candidate.FullName)));
            }

            Type interfaceType = mostDerived[0];
            registrations.Add((interfaceType, domainObjectType));
        }
    }

    private static void ConfigureEntity(
        ContainerBuilder builder,
        Type domainInterfaceType,
        Type domainObjectType,
        IReadOnlyCollection<Type> productTypes,
        IDictionary<Type, Type> registered)
    {
        if (TryRegister(domainInterfaceType, domainObjectType, registered))
        {
            builder.RegisterType(domainObjectType)
                .As(domainInterfaceType)
                .OnActivated(context =>
                {
                    if (context.Instance is IValidatableObject validatable)
                    {
                        Validator.ValidateObject(validatable, new ValidationContext(context.Instance), true);
                    }
                });
        }

        Type generatorInterfaceType = typeof(IDomainObjectGenerator<>).MakeGenericType(domainInterfaceType);
        Type[] generatorImplementationTypes = productTypes.Where(type =>
                generatorInterfaceType.IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false })
            .ToArray();
        if (generatorImplementationTypes.Length != 1)
        {
            throw new InvalidOperationException(generatorImplementationTypes.Length == 0
                ? $"No implementation of {generatorInterfaceType.FullName} found"
                : $"Multiple implementations of {generatorInterfaceType.FullName} found: "
                  + string.Join(", ", generatorImplementationTypes.Select(type => type.FullName)));
        }

        Type generatorImplementationType = generatorImplementationTypes[0];

        RegisterInterfaces(builder, generatorImplementationType, registered);
    }

    private static void RegisterInterfaces(
        ContainerBuilder builder,
        Type implementationType,
        IDictionary<Type, Type> registered)
    {
        foreach (Type serviceType in implementationType.GetInterfaces())
        {
            if (TryRegister(serviceType, implementationType, registered))
            {
                builder.RegisterType(implementationType).As(serviceType);
            }
        }
    }

    private static bool TryRegister(
        Type serviceType,
        Type implementationType,
        IDictionary<Type, Type> registered)
    {
        if (!registered.TryGetValue(serviceType, out Type? existing))
        {
            registered[serviceType] = implementationType;
            return true;
        }

        if (existing != implementationType)
        {
            throw new InvalidOperationException(
                $"Multiple implementations of {serviceType.FullName} found: "
                + $"{existing.FullName}, {implementationType.FullName}");
        }

        return false;
    }
}
