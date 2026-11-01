namespace Pendulum.App.ViewModels;

/// A Piper voice Settings offers to download: its name, licence, the credit its licence asks for,
/// and the page its files are on.
public sealed record VoiceOffer(string Name, string Licence, string Credit, string Url);
