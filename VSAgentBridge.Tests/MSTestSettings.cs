[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

// Test classes are internal: their [DataRow] methods take internal enums from VSAgentBridge.Logic.
[assembly: DiscoverInternals]
