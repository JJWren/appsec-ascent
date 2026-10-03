namespace Ascent.Core.Interaction;

/// <summary>
/// Asks the Learner for input. The CLI provides a rich and a plain (screen-reader friendly) implementation;
/// tests use a scripted one.
/// </summary>
public interface IPrompter
{
    /// <summary>Asks a yes/no question.</summary>
    bool Confirm(string question, bool defaultValue = false);

    /// <summary>Asks for text until <paramref name="validate"/> returns null (it returns an error message otherwise).</summary>
    string Ask(string question, Func<string, string?>? validate = null);

    /// <summary>Asks the Learner to pick one option; returns its index.</summary>
    int Choose(string question, IReadOnlyList<string> options);

    /// <summary>Asks for a secret without echoing it. Only the maintainer tool uses this.</summary>
    string AskSecret(string question);

    /// <summary>
    /// Asks the Learner to type <paramref name="expected"/> exactly, for actions that can't be undone or that cost money
    /// (REL-02, CLD-02).
    /// </summary>
    bool ConfirmTyped(string question, string expected);
}
