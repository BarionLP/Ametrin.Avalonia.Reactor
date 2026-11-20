using Avalonia.Controls;

namespace Ametrin.Avalonia.Reactor;

public static class ClassesExtensions
{
    // Replace and Clear only touch normal classes, so pseudo-classes survive.
    private static readonly PlainProperty<Control, string> ClassNames = new((control, classes) => control.Classes.Replace(global::Avalonia.Controls.Classes.Parse(classes)), control => control.Classes.Clear());

    public static ControlElement<T> Classes<T>(this ControlElement<T> element, string classes) where T : Control, new() => element.Set(ClassNames, classes);
}
