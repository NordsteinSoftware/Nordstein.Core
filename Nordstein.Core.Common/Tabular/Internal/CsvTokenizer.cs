using System.Text;

namespace Nordstein.Core.Common.Tabular.Internal;

/// <summary>
/// The lenient RFC 4180 state machine behind <see cref="CsvReader"/>: fed text chunk by chunk, it
/// completes records into a list. Its state survives chunk boundaries; one instance reads one input.
/// </summary>
internal sealed class CsvTokenizer
{
    private readonly char delimiter;
    private readonly StringBuilder field = new();
    private List<string> fields = [];
    private State state = State.FieldStart;
    private bool skipLineFeed;
    private bool started;

    public CsvTokenizer(char delimiter)
    {
        this.delimiter = delimiter;
    }

    private enum State
    {
        FieldStart,
        Unquoted,
        Quoted,
        QuoteInQuoted,
    }

    /// <summary>
    /// Consumes <paramref name="chunk"/>, adding each record it completes to <paramref name="completed"/>.
    /// </summary>
    public void Consume(ReadOnlySpan<char> chunk, List<IReadOnlyList<string>> completed)
    {
        foreach (char c in chunk)
        {
            if (!started)
            {
                started = true;
                if (c == '﻿')
                {
                    continue;
                }
            }

            if (skipLineFeed)
            {
                skipLineFeed = false;
                if (c == '\n')
                {
                    continue;
                }
            }

            switch (state)
            {
                case State.Quoted:
                    if (c == '"')
                    {
                        state = State.QuoteInQuoted;
                    }
                    else
                    {
                        field.Append(c);
                    }

                    break;
                case State.QuoteInQuoted when c == '"':
                    field.Append('"');
                    state = State.Quoted;
                    break;
                case State.FieldStart when c == '"':
                    state = State.Quoted;
                    break;
                default:
                    if (c == delimiter)
                    {
                        EndField();
                    }
                    else if (c is '\r' or '\n')
                    {
                        skipLineFeed = c == '\r';
                        EndRecord(completed);
                    }
                    else
                    {
                        field.Append(c);
                        state = State.Unquoted;
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Ends the input: a record in progress (an unterminated quote included) is completed.
    /// </summary>
    public void Finish(List<IReadOnlyList<string>> completed)
    {
        if (state != State.FieldStart || fields.Count > 0)
        {
            EndRecord(completed);
        }
    }

    private void EndField()
    {
        fields.Add(field.ToString());
        field.Clear();
        state = State.FieldStart;
    }

    private void EndRecord(List<IReadOnlyList<string>> completed)
    {
        EndField();
        completed.Add(fields);
        fields = [];
    }
}
