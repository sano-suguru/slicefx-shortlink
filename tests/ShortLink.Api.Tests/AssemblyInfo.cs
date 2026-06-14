// DB integration tests must not run in parallel to avoid isolation issues
[assembly: CollectionBehavior(DisableTestParallelization = true)]
