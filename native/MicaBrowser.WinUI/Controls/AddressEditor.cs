using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Text;

namespace MicaBrowser.Controls;

/// <summary>RichEdit-based address editor: native text layout and cheap drag selection.</summary>
public sealed class AddressEditor : RichEditBox
{
    public string Text
    {
        get
        {
            Document.GetText(TextGetOptions.None, out var value);
            return value.TrimEnd('\r');
        }
        set
        {
            if (string.Equals(Text, value, StringComparison.Ordinal))
            {
                return;
            }
            Document.SetText(TextSetOptions.None, value ?? string.Empty);
            Document.Selection.SetRange((value ?? string.Empty).Length, (value ?? string.Empty).Length);
        }
    }

    public void SelectAll()
    {
        Document.Selection.SetRange(0, Text.Length);
    }
}
