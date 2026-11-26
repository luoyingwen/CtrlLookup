namespace CtrlLookup.Core;

public enum WritingTask { Spelling, Polish }

public readonly record struct WritingQuery(WritingTask Task, string Text)
{
    public static WritingQuery? FromSelection(string text)
    {
        var selection = text.Trim();
        if (LlmSelectionText.Limit(selection) is not { } input) return null;
        // Classify the original selection, not the truncated request, and keep its capitalization.
        var task = EnglishHeadword.FromSelection(selection) is not null ? WritingTask.Spelling : WritingTask.Polish;
        return new(task, input);
    }
}
