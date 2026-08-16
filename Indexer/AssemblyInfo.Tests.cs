using System.Runtime.CompilerServices;

// Exposes internal members (RuleMatches, etc.) to Indexer.Tests, so
// rules can be checked directly without running the whole of Main.
[assembly: InternalsVisibleTo("Indexer.Tests")]
