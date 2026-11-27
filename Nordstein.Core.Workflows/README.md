# Nordstein.Core.Workflows

Product-agnostic workflow kernel for Nordstein applications.

- **Definition** — `WorkflowGraph`: a directed acyclic graph of typed nodes and edges, with data
  references `{{trigger.<path>}}` and `{{steps.<nodeId>.output.<path>}}` between them; no
  expression language.
- **Node contract** — `INodeType` (name and major, input and output JSON schemas, branches, side
  effects, `ExecuteAsync`) and `ISuspendingNodeType` (`ResumeAsync`). The product owns its catalogue;
  a graph names a type as `name@major` and keeps running on that major after a newer one ships.
- **Validation** — `IWorkflowValidator`: structure, cycles, reachability, configurations against
  input schemas, and every reference against the upstream node's output schema; warnings for a
  node on a superseded major.
- **Execution** — `IWorkflowInterpreter`: deterministic, sequential, step by step through the
  product's `IStepJournal`, with a step budget, cancellation, dry runs, and suspend and resume.
- **Trial** — `INodeTrial`: one node against supplied upstream outputs, without a run, its output
  checked against its output schema.
- **Kernel node types** — `IConditionNodeType` (`condition@1`: rules over data, branches `true` and
  `false`) and `IForEachNodeType` (`for-each@1`: a loop over an array), for a product to put in its
  catalogue.

Register `Nordstein.Core.Workflows.Module`; see [`docs/workflows.md`](../docs/workflows.md).
