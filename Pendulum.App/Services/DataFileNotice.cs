using Pendulum.Core.Persistence;

namespace Pendulum.App.Services;

/// A data file that couldn't be read at startup: the file, what Pendulum did, and where the
/// damaged copy was set aside (null when it was missing and the save before was used).
public sealed record DataFileNotice(string File, StoreOutcome Outcome, string? SetAsidePath);
