using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace Nordstein.Core.Testing.Performance;

/// <summary>
/// A product's committed performance budgets: one JSON file in version control, nested groups
/// flattened to dotted keys (<c>{"listing": {"p95Ms": {"firstPage": 12}}}</c> is the budget
/// <c>listing.p95Ms.firstPage</c>), and the assertions that turn a measured number into a pass or a
/// defect against it.
/// </summary>
/// <remarks>
/// <para>
/// Absolute numbers, never "no slower than the last run". Comments are allowed and expected: each
/// budget carries its calibration (range, machine, configuration) and the regression signature that
/// explains a red. A key with no committed budget measures and never fails, which lets a new
/// scenario land, print its numbers and get its budget in a later change instead of being born with
/// a guessed one.
/// </para>
/// <para>
/// The file is read and validated once, at construction, and every malformed shape is refused
/// loudly rather than read as measure-only: a budget written as a string, a boolean, null, an array,
/// a negative or non-finite number, a name that is empty or holds a dot (it would collide with a
/// nested key), or a key declared twice. Immutable afterwards; safe to use from any thread.
/// </para>
/// </remarks>
public sealed class PerfBudgets
{
    private readonly FrozenDictionary<string, double> budgets;

    /// <summary>
    /// Reads and validates the budgets in <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The budget file, conventionally <c>perf-budgets.json</c> copied beside the test assembly.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">
    /// The file is not JSON (comments allowed), its root is not an object, or a budget or a name is
    /// malformed as the remarks list; the message names the file and the key.
    /// </exception>
    public PerfBudgets(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        string content = File.ReadAllText(path);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{path} is not valid JSON: {exception.Message}", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{path}: the root must be an object of budgets, not {document.RootElement.ValueKind}.");
            }

            Dictionary<string, double> flattened = new(StringComparer.Ordinal);
            Flatten(document.RootElement, prefix: string.Empty, flattened);
            budgets = flattened.ToFrozenDictionary(StringComparer.Ordinal);
        }

        Keys = [.. budgets.Keys.Order(StringComparer.Ordinal)];
    }

    /// <summary>The path the budgets were read from, as given.</summary>
    public string Path { get; }

    /// <summary>Every committed budget's dotted key, sorted ordinally.</summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>
    /// The budget at a dotted key, or null when none is committed (the measure-only case). Keys
    /// compare ordinally; a group's key is not a budget.
    /// </summary>
    /// <param name="key">The dotted key, such as <c>listing.p95Ms.firstPage</c>.</param>
    /// <returns>The budget, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> is blank.</exception>
    public double? Find(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return budgets.TryGetValue(key, out double budget) ? budget : null;
    }

    /// <summary>
    /// Asserts that <paramref name="measured"/> is at or below the budget at
    /// <paramref name="key"/> (a latency), writing the comparison to <paramref name="log"/> first;
    /// with no committed budget, writes the number as <c>MEASURE-ONLY</c> and passes.
    /// </summary>
    /// <param name="key">The budget's dotted key.</param>
    /// <param name="measured">The measured value, finite.</param>
    /// <param name="unit">The unit printed after each number, such as <c>ms</c>; may be empty.</param>
    /// <param name="log">Receives one line, such as <c>TestContext.WriteLine</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/>, <paramref name="unit"/> or <paramref name="log"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="measured"/> is NaN or infinite.</exception>
    /// <exception cref="AssertFailedException">The measured value is above the budget; the message names the key, both numbers and the budget file.</exception>
    public void ShouldBeAtMost(string key, double measured, string unit, Action<string> log)
        => Check(key, measured, unit, log, atMost: true);

    /// <summary>
    /// Asserts that <paramref name="measured"/> is at or above the floor at <paramref name="key"/>
    /// (a throughput), writing the comparison to <paramref name="log"/> first; with no committed
    /// floor, writes the number as <c>MEASURE-ONLY</c> and passes.
    /// </summary>
    /// <param name="key">The floor's dotted key.</param>
    /// <param name="measured">The measured value, finite.</param>
    /// <param name="unit">The unit printed after each number, such as <c>/s</c>; may be empty.</param>
    /// <param name="log">Receives one line, such as <c>TestContext.WriteLine</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/>, <paramref name="unit"/> or <paramref name="log"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="measured"/> is NaN or infinite.</exception>
    /// <exception cref="AssertFailedException">The measured value is below the floor; the message names the key, both numbers and the budget file.</exception>
    public void ShouldBeAtLeast(string key, double measured, string unit, Action<string> log)
        => Check(key, measured, unit, log, atMost: false);

    private void Check(string key, double measured, string unit, Action<string> log, bool atMost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(log);
        if (!double.IsFinite(measured))
        {
            throw new ArgumentOutOfRangeException(nameof(measured), measured, "A measured value is a finite number.");
        }

        if (Find(key) is not { } limit)
        {
            log(string.Create(CultureInfo.InvariantCulture, $"MEASURE-ONLY [{key}] {measured:F2}{unit} — no budget committed yet."));
            return;
        }

        string comparison = atMost ? "<=" : ">=";
        log(string.Create(CultureInfo.InvariantCulture, $"BUDGET [{key}] measured {measured:F2}{unit} {comparison} budget {limit:F2}{unit}?"));
        if (atMost ? measured <= limit : measured >= limit)
        {
            return;
        }

        string kind = atMost ? "budget" : "floor";
        throw new AssertFailedException(string.Create(
            CultureInfo.InvariantCulture,
            $"{key} has a committed {kind} of {limit:F2}{unit} and measured {measured:F2}{unit}; read its calibration and "
            + $"regression signature in {System.IO.Path.GetFileName(Path)} before assuming the {kind} is wrong."));
    }

    private void Flatten(JsonElement group, string prefix, Dictionary<string, double> into)
    {
        foreach (JsonProperty property in group.EnumerateObject())
        {
            string key = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
            if (property.Name.Length == 0)
            {
                throw new InvalidDataException($"{Path}: the budget or group '{key}' has an empty name.");
            }

            if (property.Name.Contains('.', StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{Path}: the name '{property.Name}' may not contain a dot; a budget's key is the names joined with dots.");
            }

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    Flatten(property.Value, key, into);
                    break;

                case JsonValueKind.Number:
                    if (!property.Value.TryGetDouble(out double budget) || !double.IsFinite(budget))
                    {
                        throw new InvalidDataException($"Budget '{key}' in {Path} is not a finite number.");
                    }

                    if (budget < 0)
                    {
                        throw new InvalidDataException(string.Create(
                            CultureInfo.InvariantCulture, $"Budget '{key}' in {Path} is {budget}; budgets are zero or positive."));
                    }

                    if (!into.TryAdd(key, budget))
                    {
                        throw new InvalidDataException($"Budget '{key}' is declared twice in {Path}.");
                    }

                    break;

                default:
                    // Loud rather than measure-only: a budget meant to be enforced that is a string or a
                    // boolean would otherwise pass every run forever.
                    throw new InvalidDataException(
                        $"Budget '{key}' in {Path} is {property.Value.ValueKind}; budgets must be numbers, and groups of budgets must be objects.");
            }
        }
    }
}
