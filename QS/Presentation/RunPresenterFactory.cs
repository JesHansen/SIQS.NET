namespace QS.Presentation;

/// <summary>Selects the presentation style from the CLI flags: <c>--quiet</c>, <c>--debug</c>, or the default rich TUI.</summary>
internal static class RunPresenterFactory
{
    public static IRunPresenter Create(CliArguments cli)
        => cli.GetFlag("quiet") ? new QuietPresenter()
            : cli.GetFlag("debug") ? new DebugPresenter()
            : new NormalPresenter();
}
