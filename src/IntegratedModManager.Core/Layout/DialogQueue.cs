namespace IntegratedModManager.Core.Layout;

/// <summary>
/// Shows dialogs one at a time (stage J8): a dialog asked for while another is open waits its turn, and one that is
/// closed before its turn (a progress dialog whose work already finished) is skipped. The window's DialogService
/// uses it for every ContentDialog.
/// </summary>
public sealed class DialogQueue<TDialog> where TDialog : class
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<TDialog> _withdrawn = new(ReferenceEqualityComparer.Instance);

    /// <summary>The dialog on screen, if any.</summary>
    public TDialog? Current { get; private set; }

    /// <summary>A dialog is open or about to open.</summary>
    public bool IsBusy => _gate.CurrentCount == 0;

    /// <summary>
    /// Shows <paramref name="dialog"/> with <paramref name="show"/> once no other dialog is open, and returns its
    /// result; returns <paramref name="skipped"/> when it was closed before its turn.
    /// </summary>
    public async Task<TResult> ShowAsync<TResult>(TDialog dialog, Func<TDialog, Task<TResult>> show, TResult skipped)
    {
        await _gate.WaitAsync();
        try
        {
            if (_withdrawn.Remove(dialog)) return skipped;
            Current = dialog;
            return await show(dialog);
        }
        finally
        {
            if (ReferenceEquals(Current, dialog)) Current = null;
            _gate.Release();
        }
    }

    /// <summary>
    /// Closes a dialog: true when it is the one on screen (the caller hides it); otherwise it will not be shown.
    /// </summary>
    public bool Close(TDialog dialog)
    {
        if (ReferenceEquals(Current, dialog)) return true;
        _withdrawn.Add(dialog);
        return false;
    }
}
