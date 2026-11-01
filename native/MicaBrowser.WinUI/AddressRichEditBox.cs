using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;

namespace MicaBrowser;

public sealed class AddressRichEditBox : RichEditBox
{
    private bool _updating;

    public string Text
    {
        get
        {
            Document.GetText(TextGetOptions.None, out var text);
            return (text ?? string.Empty).TrimEnd('\r');
        }
        set
        {
            if (_updating) return;
            _updating = true;
            try
            {
                Document.SetText(TextSetOptions.None, value ?? string.Empty);
            }
            finally
            {
                _updating = false;
            }
        }
    }

    public void SelectAll()
    {
        Document.Selection.SetRange(0, Text.Length);
    }
}
