// Tests share one PostgreSQL server and own one database each, so classes and methods run in
// parallel.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
