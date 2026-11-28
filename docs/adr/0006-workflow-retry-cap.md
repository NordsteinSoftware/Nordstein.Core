# ADR 0006: Kernel cap on workflow node retries

- Status: proposed
- Date: 2026-11-28
- Issue: [#81](https://github.com/NordsteinSoftware/Nordstein.Core/issues/81) (follow-ups of #54)
- Amends: ADR 0005 decision 5 (`attempts` and `backoff` get an upper bound)

## Context

A workflow node may carry a `RetryPolicy`: `Attempts` (2 to `int.MaxValue`) and a `Backoff`
(any non-negative `TimeSpan`). The interpreter runs a failed read-only node again up to
`Attempts` times and awaits the backoff between tries (`ExecuteWithRetryAsync`). A wait longer
than one timer can hold (about 49 days) is awaited in parts (`WaitAsync`).

`WorkflowExecution.MaxSteps` bounds the steps a run starts. A retry is not a new step: one step,
one `StepStarted`, however many tries. The replay after a suspension counts each completed step
once and does not know how many tries it took.

So a graph can hold one step, and the run, for `(Attempts - 1) × Backoff`: practically forever.
In a loop body the hold is multiplied by the number of iterations. The graph text comes from a
user or an agent, so the kernel cannot trust it to choose sane numbers. The run's cancellation
token bounds it only when the product sets a timeout, and Core cannot check that.

Core has published no release. ADR 0005 decision 2 says a change that refuses an accepted graph
needs an ADR; this is that ADR, and no major version is needed before the first publish.

## Decision

1. **The kernel caps the retry policy, not the product.** `RetryPolicy` gets
   `public const int MaxAttempts = 10` and `public static readonly TimeSpan MaxBackoff` (one hour). The constructor throws
   `ArgumentOutOfRangeException` above either. The worst hold of one step is 9 waits of one
   hour, and 10 executions of a read-only node.
2. **The graph reader reports the cap with the JSON path.** `attempts` above 10 and a
   `backoff` above `PT1H` are format errors at `$.nodes[i].retry.attempts` / `.backoff`, with a
   message naming the bound (`a whole number from 2 to 10`, `at most PT1H`). The reader never
   constructs a `RetryPolicy` that would throw.
3. **Retries do not count against `MaxSteps`.** `MaxSteps` keeps counting journaled steps.
   Counting tries would make a resumed run count differently from the run that suspended,
   because `CompletedStep` does not carry tries. The cap in decision 1 bounds the tries instead.
4. **A longer wait is a suspension, not a backoff.** A product that must wait for hours or days
   (a rate-limit window, a partner system that is down) suspends the run and resumes it later.
   That wait survives a restart; an in-process backoff does not. `docs/workflows.md` says so.
5. **The split wait goes.** With `MaxBackoff` far below the timer limit, `WaitAsync` and
   `MaxTimerWait` are dead code. The backoff is one `Task.Delay` on the run's token.

## Alternatives considered

- **Count every try against `MaxSteps`.** Reuses the product's existing budget, but changes
  what a step is, breaks the resume count (decision 3), and still leaves the backoff unbounded.
- **A product-set limit on `WorkflowExecution` (`MaxAttempts`, `MaxRetryWait`).** More API for
  a number every product would set the same way. It can be added later without breaking: a
  limit below the kernel cap only refuses more.
- **Rely on the run's cancellation token.** Core cannot require a timeout, and a step stuck in a
  backoff looks like a hung run in the journal.
- **Higher caps (100 attempts, one day).** A day-long in-process wait is the case decision 4
  moves to suspension. 10 tries cover transient faults; more tries hide a broken dependency.

## Consequences

- A graph with `attempts` above 10 or a `backoff` above one hour is refused by the reader and
  by the `RetryPolicy` constructor. No released product depends on it. Products porting graphs
  from nordstein-dms check their stored graphs before they switch to Core's reader.
- Raising a cap later is additive (more graphs accepted, ADR 0005 decision 2). Lowering it is
  breaking.
- `docs/workflows.md` ("Retries", "Graph format") and the XML docs of `RetryPolicy` and
  `IWorkflowGraphReader` name the bounds. ADR 0005 decision 5 is read with this amendment.
- Tests: the constructor and the reader accept exactly 10 and `PT1H` and refuse 11 and
  `PT1H0.001S` (or the next tick). The split-wait tests in `RetryBackoffTests` go.
