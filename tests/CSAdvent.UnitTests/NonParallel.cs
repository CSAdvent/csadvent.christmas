namespace CSAdvent.UnitTests;

/// <summary>
/// Tests that run on their own, after all the others: those that need a timer to fire during synchronous work, such as a
/// time limit. A timer's callback needs a free thread-pool thread, and the tests running in parallel can take every one.
/// </summary>
[CollectionDefinition(nameof(NonParallel), DisableParallelization = true)]
public sealed class NonParallel;
