using AwesomeAssertions;
using Nordstein.Core.Domain.Exceptions;

namespace Nordstein.Core.Domain.Tests.Exceptions;

/// <summary>
/// Exercises both constructors of <see cref="EntityAlreadyExistsException"/>: the id clash and the
/// unique-key clash that carries the provider's exception.
/// </summary>
[TestClass]
public sealed class EntityAlreadyExistsExceptionTests
{
    [TestMethod]
    public void Ctor_IdAndType_NamesTheTypeAndTheId()
    {
        var id = Guid.NewGuid();

        var exception = new EntityAlreadyExistsException(id, typeof(IDisposable));

        exception.Message.Should().Be($"Entity of type 'IDisposable' with id '{id}' already exists.");
        exception.InnerException.Should().BeNull();
    }

    [TestMethod]
    public void Ctor_TypeAndInner_KeepsInnerAndNamesType()
    {
        var inner = new InvalidOperationException("23505");

        var exception = new EntityAlreadyExistsException(typeof(IDisposable), inner);

        exception.InnerException.Should().BeSameAs(inner);
        exception.Message.Should().Be("Entity of type 'IDisposable' with the same unique key already exists.");
    }

    [TestMethod]
    public void Ctor_TypeAndNullInner_HasNoInnerException()
    {
        var exception = new EntityAlreadyExistsException(typeof(IDisposable), null);

        exception.InnerException.Should().BeNull();
        exception.Message.Should().Contain("'IDisposable'");
    }

    [TestMethod]
    public void Ctor_NullType_Throws()
    {
        Func<EntityAlreadyExistsException> act = () => new EntityAlreadyExistsException(NullType(), null);

        act.Should().Throw<ArgumentNullException>().WithParameterName("entityType");
    }

    // An unassigned element of a Type array: a null the nullable analysis does not flag, which the
    // constructor must still reject.
    private static Type NullType() => (new Type[1])[0];
}
