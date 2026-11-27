using System.Reflection;
using System.Reflection.Emit;
using Autofac;
using AwesomeAssertions;

namespace Nordstein.Core.Domain.Tests;

/// <summary>
/// The domain module's discovery refuses a misconfigured product assembly when the container is
/// built, naming the types: an implementation of two equally specific domain interfaces, a domain
/// interface with no generator or with two, and two types claiming one service. Each case is its
/// own assembly, emitted at run time, because planting one in this test assembly would break every
/// other test's container.
/// </summary>
[TestClass]
public sealed class ModuleMisconfigurationTests
{
    [TestMethod]
    public void Build_AnImplementationOfTwoEquallySpecificDomainInterfaces_ThrowsNamingThem()
    {
        var product = new EmittedProduct();
        Type thing = product.Interface("IThing", typeof(IDomainObject));
        Type left = product.Interface("ILeftThing", thing);
        Type right = product.Interface("IRightThing", thing);
        product.Class("AmbiguousThing", left, right);

        Action act = () => Build(product.Assembly);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Multiple equally specific domain interfaces for Emitted.AmbiguousThing: *Emitted.ILeftThing*")
            .And.Message.Should().Contain("Emitted.IRightThing");
    }

    [TestMethod]
    public void Build_ADomainObjectWithoutAGenerator_ThrowsNamingTheGeneratorInterface()
    {
        var product = new EmittedProduct();
        Type thing = product.Interface("IThing", typeof(IDomainObject));
        product.Class("Thing", thing);

        Action act = () => Build(product.Assembly);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("No implementation of Nordstein.Core.Domain.IDomainObjectGenerator`1[[Emitted.IThing*found");
    }

    [TestMethod]
    public void Build_ADomainObjectWithTwoGenerators_ThrowsNamingBoth()
    {
        var product = new EmittedProduct();
        Type thing = product.Interface("IThing", typeof(IDomainObject));
        product.Class("Thing", thing);
        Type generator = typeof(IDomainObjectGenerator<>).MakeGenericType(thing);
        product.Class("FirstThingGenerator", generator);
        product.Class("SecondThingGenerator", generator);

        Action act = () => Build(product.Assembly);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Multiple implementations of Nordstein.Core.Domain.IDomainObjectGenerator`1[[Emitted.IThing*found: Emitted.FirstThingGenerator, Emitted.SecondThingGenerator");
    }

    [TestMethod]
    public void Build_TwoGeneratorsSharingAnotherService_ThrowsNamingTheService()
    {
        var product = new EmittedProduct();
        Type shared = product.Interface("ISharedService");
        Type first = product.Interface("IFirst", typeof(IDomainObject));
        Type second = product.Interface("ISecond", typeof(IDomainObject));
        product.Class("First", first);
        product.Class("Second", second);
        product.Class("FirstGenerator", typeof(IDomainObjectGenerator<>).MakeGenericType(first), shared);
        product.Class("SecondGenerator", typeof(IDomainObjectGenerator<>).MakeGenericType(second), shared);

        Action act = () => Build(product.Assembly);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Multiple implementations of Emitted.ISharedService found: Emitted.FirstGenerator, Emitted.SecondGenerator");
    }

    [TestMethod]
    public void Build_AbstractImplementationsAndGenerators_AreSkipped()
    {
        // An abstract base of a domain object or of a generator is no candidate: only the concrete
        // types are registered, so neither counts as a second implementation.
        var product = new EmittedProduct();
        Type thing = product.Interface("IThing", typeof(IDomainObject));
        Type generator = typeof(IDomainObjectGenerator<>).MakeGenericType(thing);
        Type marker = product.Interface("IGeneratorMarker");
        product.AbstractClass("ThingBase", thing);
        Type concrete = product.Class("Thing", thing);
        product.AbstractClass("ThingGeneratorBase", generator);
        Type thingGenerator = product.Class("ThingGenerator", generator, marker);

        using IContainer container = Build(product.Assembly);

        container.Resolve(thing).Should().BeOfType(concrete);
        container.Resolve(generator).Should().BeOfType(thingGenerator);
        container.Resolve(marker).Should().BeOfType(thingGenerator, "a generator is registered under every interface it implements");
    }

    private static IContainer Build(Assembly product)
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule(new Domain.Module(product));
        return builder.Build();
    }

    /// <summary>
    /// A product assembly built at run time: interfaces, and classes whose every interface method
    /// returns its default value (an empty sequence for a sequence).
    /// </summary>
    private sealed class EmittedProduct
    {
        private readonly AssemblyBuilder assembly;
        private readonly ModuleBuilder module;

        public EmittedProduct()
        {
            assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("Nordstein.Core.Domain.Tests.Emitted." + Guid.NewGuid().ToString("N")),
                AssemblyBuilderAccess.Run);
            module = assembly.DefineDynamicModule("Emitted");
        }

        public Assembly Assembly => assembly;

        public Type Interface(string name, params Type[] bases)
        {
            TypeBuilder type = module.DefineType(
                "Emitted." + name, TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
            foreach (Type baseInterface in bases)
            {
                type.AddInterfaceImplementation(baseInterface);
            }

            return type.CreateType();
        }

        public Type Class(string name, params Type[] interfaces) => Define(name, TypeAttributes.Sealed, interfaces);

        public Type AbstractClass(string name, params Type[] interfaces) => Define(name, TypeAttributes.Abstract, interfaces);

        private Type Define(string name, TypeAttributes kind, Type[] interfaces)
        {
            TypeBuilder type = module.DefineType("Emitted." + name, TypeAttributes.Public | TypeAttributes.Class | kind, typeof(object));
            type.DefineDefaultConstructor(MethodAttributes.Public);
            IEnumerable<Type> implemented = interfaces.Concat(interfaces.SelectMany(candidate => candidate.GetInterfaces())).Distinct();
            foreach (Type contract in implemented)
            {
                type.AddInterfaceImplementation(contract);
                foreach (MethodInfo method in contract.GetMethods())
                {
                    Implement(type, method);
                }
            }

            return type.CreateType();
        }

        private static void Implement(TypeBuilder type, MethodInfo method)
        {
            MethodBuilder implementation = type.DefineMethod(
                method.DeclaringType?.Name + "." + method.Name,
                MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                method.ReturnType,
                [.. method.GetParameters().Select(parameter => parameter.ParameterType)]);
            ILGenerator il = implementation.GetILGenerator();
            if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                // Validate returns no results: an empty sequence, which validation enumerates.
                MethodInfo empty = typeof(Array).GetMethod(nameof(Array.Empty))?.MakeGenericMethod(method.ReturnType.GetGenericArguments())
                    ?? throw new InvalidOperationException("Array.Empty is missing.");
                il.Emit(OpCodes.Call, empty);
            }
            else if (method.ReturnType != typeof(void))
            {
                il.Emit(OpCodes.Ldnull);
            }

            il.Emit(OpCodes.Ret);
            type.DefineMethodOverride(implementation, method);
        }
    }
}
