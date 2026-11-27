using System.Linq.Expressions;
using System.Reflection;
using Autofac;
using Autofac.Core;

namespace Nordstein.Core.Domain;

/// <summary>
/// Autofac registration helper that binds a hand-written factory delegate to exactly one
/// constructor of its implementation, by parameter types and position.
/// </summary>
/// <remarks>
/// Autofac's implicit delegate factories pick a constructor by parameter count and match arguments
/// by type, so two constructors of the same arity, two parameters of one type or an optional
/// parameter whose type is registered bind wrongly or not at all. The domain foundation keeps the
/// implicit factories for discovered <c>CreateNew</c>/<c>CreateExisting</c> delegates; this helper
/// is the opt-in for a union whose creation paths need their own constructor.
/// </remarks>
public static class ContainerBuilderFactoryExtensions
{
    private static readonly MethodInfo ResolveMethod =
        typeof(ContainerBuilderFactoryExtensions).GetMethod(nameof(Resolve), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ContainerBuilderFactoryExtensions.Resolve is missing.");

    /// <summary>
    /// Registers <typeparamref name="TDelegate"/> as a factory that activates
    /// <typeparamref name="TImplementation"/> through the one constructor its parameters select.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A constructor matches when its first parameters are exactly the delegate's parameter types
    /// (nullable reference annotations aside; <c>Guid?</c> is not <c>Guid</c>), followed by the
    /// types of <paramref name="fixedArguments"/> in order. Its remaining parameters come from the
    /// container. Among matching constructors the one with the fewest parameters wins. The check
    /// runs here, when the module loads, not on the first resolve.
    /// </para>
    /// <para>
    /// The delegate's arguments, then the fixed arguments' values, are passed by position, so an
    /// optional parameter always receives the passed value, even when its type is registered.
    /// <typeparamref name="TImplementation"/> is registered under a key private to this call: it is
    /// not resolvable as itself, only through the delegate. The container activates every instance
    /// and validates it (<see cref="RegistrationBuilderValidationExtensions.ValidateOnActivation{TLimit, TActivatorData, TStyle}"/>),
    /// so an invalid object surfaces as a <see cref="DependencyResolutionException"/> wrapping the
    /// <see cref="System.ComponentModel.DataAnnotations.ValidationException"/>. The delegate resolves
    /// from the lifetime scope it was resolved from. Registering the same delegate twice follows
    /// Autofac's rule: the later registration wins.
    /// </para>
    /// </remarks>
    /// <typeparam name="TDelegate">The factory delegate; its return type is assignable from <typeparamref name="TImplementation"/>.</typeparam>
    /// <typeparam name="TImplementation">The concrete type the delegate creates.</typeparam>
    /// <param name="builder">The container builder.</param>
    /// <param name="fixedArguments">
    /// <see cref="TypedParameter"/>s that follow the delegate's arguments, such as the kind two
    /// creation paths of one constructor are told apart by.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="fixedArguments"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A fixed argument is <see langword="null"/> or not a <see cref="TypedParameter"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The delegate's return type is not assignable from <typeparamref name="TImplementation"/>, or
    /// no public constructor matches, or two match with the fewest parameters.
    /// </exception>
    public static void RegisterFactory<TDelegate, TImplementation>(this ContainerBuilder builder, params Parameter[] fixedArguments)
        where TDelegate : Delegate
        where TImplementation : notnull
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(fixedArguments);
        TypedParameter[] fixedTyped = fixedArguments
            .Select(argument => argument as TypedParameter
                ?? throw new ArgumentException("Every fixed argument is a non-null TypedParameter.", nameof(fixedArguments)))
            .ToArray();

        MethodInfo invoke = typeof(TDelegate).GetMethod("Invoke")
            ?? throw new InvalidOperationException($"{typeof(TDelegate).Name} has no Invoke method.");
        if (!invoke.ReturnType.IsAssignableFrom(typeof(TImplementation)))
        {
            throw new InvalidOperationException(
                $"{typeof(TDelegate).Name} returns {invoke.ReturnType.Name}, which {typeof(TImplementation).Name} is not.");
        }

        Type[] delegateTypes = invoke.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        Type[] leading = [.. delegateTypes, .. fixedTyped.Select(argument => argument.Type)];
        ConstructorInfo constructor = SelectConstructor<TDelegate, TImplementation>(leading);

        var key = new object();
        builder.RegisterType<TImplementation>()
            .Keyed<TImplementation>(key)
            .UsingConstructor(constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray())
            .ValidateOnActivation();

        Func<IComponentContext, TDelegate> factory = CompileFactory<TDelegate, TImplementation>(
            invoke, delegateTypes, key, fixedTyped.Select(argument => argument.Value).ToArray());
        builder.Register(context => factory(context.Resolve<IComponentContext>())).As<TDelegate>();
    }

    private static ConstructorInfo SelectConstructor<TDelegate, TImplementation>(Type[] leading)
    {
        ConstructorInfo[] all = typeof(TImplementation).GetConstructors();
        ConstructorInfo[] matching = all
            .Where(constructor =>
            {
                ParameterInfo[] parameters = constructor.GetParameters();
                return parameters.Length >= leading.Length
                    && leading.Select((type, index) => parameters[index].ParameterType == type).All(same => same);
            })
            .ToArray();
        if (matching.Length == 0)
        {
            throw new InvalidOperationException(
                $"No public constructor of {typeof(TImplementation).Name} starts with ({Describe(leading)}) for "
                + $"{typeof(TDelegate).Name}; candidates: {DescribeAll(all)}.");
        }

        int fewest = matching.Min(constructor => constructor.GetParameters().Length);
        ConstructorInfo[] best = matching.Where(constructor => constructor.GetParameters().Length == fewest).ToArray();
        return best.Length == 1
            ? best[0]
            : throw new InvalidOperationException(
                $"Two or more constructors of {typeof(TImplementation).Name} match {typeof(TDelegate).Name} with "
                + $"{fewest} parameters: {DescribeAll(best)}.");
    }

    // (scope) => (a0, ..., an-1) => (TReturn)Resolve<TImplementation>(scope, key, [a0, ..., an-1], fixedValues),
    // compiled once per registration
    private static Func<IComponentContext, TDelegate> CompileFactory<TDelegate, TImplementation>(
        MethodInfo invoke, Type[] delegateTypes, object key, object?[] fixedValues)
        where TDelegate : Delegate
        where TImplementation : notnull
    {
        ParameterExpression scope = Expression.Parameter(typeof(IComponentContext), "scope");
        ParameterExpression[] arguments = delegateTypes.Select((type, index) => Expression.Parameter(type, $"a{index}")).ToArray();
        Expression call = Expression.Call(
            ResolveMethod.MakeGenericMethod(typeof(TImplementation)),
            scope,
            Expression.Constant(key),
            Expression.NewArrayInit(typeof(object), arguments.Select(argument => Expression.Convert(argument, typeof(object)))),
            Expression.Constant(fixedValues));
        Expression<TDelegate> inner = Expression.Lambda<TDelegate>(Expression.Convert(call, invoke.ReturnType), arguments);
        return Expression.Lambda<Func<IComponentContext, TDelegate>>(inner, scope).Compile();
    }

    private static TImplementation Resolve<TImplementation>(IComponentContext scope, object key, object?[] arguments, object?[] fixedValues)
        where TImplementation : notnull
    {
        var positional = new Parameter[arguments.Length + fixedValues.Length];
        for (int index = 0; index < arguments.Length; index++)
        {
            positional[index] = new PositionalParameter(index, arguments[index]);
        }

        for (int index = 0; index < fixedValues.Length; index++)
        {
            positional[arguments.Length + index] = new PositionalParameter(arguments.Length + index, fixedValues[index]);
        }

        return scope.ResolveKeyed<TImplementation>(key, positional);
    }

    private static string Describe(IEnumerable<Type> types) => string.Join(", ", types.Select(type => type.Name));

    private static string DescribeAll(IEnumerable<ConstructorInfo> constructors)
        => string.Join("; ", constructors.Select(constructor => $"({Describe(constructor.GetParameters().Select(parameter => parameter.ParameterType))})"));
}
