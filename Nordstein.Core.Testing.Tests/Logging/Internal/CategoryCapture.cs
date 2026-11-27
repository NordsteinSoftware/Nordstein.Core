using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nordstein.Core.Testing.Tests.Logging.Internal;

/// <summary>
/// A provider that keeps the category names a <see cref="LoggerFactory"/> asks it for, so a test
/// learns which category Microsoft's own <c>Logger&lt;T&gt;</c> uses for a type.
/// </summary>
internal sealed class CategoryCapture : ILoggerProvider
{
    public List<string> Categories { get; } = [];

    public ILogger CreateLogger(string categoryName)
    {
        Categories.Add(categoryName);
        return NullLogger.Instance;
    }

    public void Dispose()
    {
    }
}
