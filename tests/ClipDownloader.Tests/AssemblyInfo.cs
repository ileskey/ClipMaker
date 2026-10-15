using Xunit.v3;
using Xunit.Sdk;

// WPF pack-resource caches and desktop focus are process-wide. Each UI test owns an STA Dispatcher,
// but simultaneous Window.InitializeComponent calls do not provide independent resource contexts.
[assembly: Parallelization(Mode = ParallelMode.None)]
