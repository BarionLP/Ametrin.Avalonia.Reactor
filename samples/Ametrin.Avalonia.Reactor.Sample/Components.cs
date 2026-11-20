using System.Collections.Immutable;
using Ametrin.Avalonia.Reactor;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using static Ametrin.Avalonia.Reactor.UI;

namespace ReactorSample;

internal sealed class Gallery : Component
{
    protected override Element Render() => DockPanel(
        Component<Counter>().WithKey("counter"),
        Component<TodoList>().WithKey("todos"),
        Component<NumberList>().WithKey("numbers")
    ).VerticalSpacing(24).Margin(new Thickness(24));
}

internal sealed class Counter : Component
{
    protected override Element Render()
    {
        var (count, setCount) = UseState(0);
        return StackPanel(
            Button("Add").OnClick(() => setCount(count + 1)),
            TextBlock($"Clicked {count} times").VerticalAlignment(VerticalAlignment.Center)
        ).Orientation(Orientation.Horizontal).Spacing(12).DockPanelDock(Dock.Top);
    }
}

internal sealed record Todo(int Id, string Title, bool Done);

internal sealed class TodoList : Component
{
    protected override Element Render()
    {
        var (todos, setTodos) = UseState(ImmutableList<Todo>.Empty);
        var (draft, setDraft) = UseState("");
        var (nextId, setNextId) = UseState(1);

        void Add()
        {
            if (string.IsNullOrWhiteSpace(draft)) return;
            setTodos(todos.Add(new(nextId, draft.Trim(), false)));
            setNextId(nextId + 1);
            setDraft("");
        }

        void Toggle(int id) => setTodos(todos.ConvertAll(todo => todo.Id == id ? todo with { Done = !todo.Done } : todo));
        void Remove(int id) => setTodos(todos.RemoveAll(todo => todo.Id == id));

        return StackPanel(
            StackPanel(
                TextBox().Text(draft, text => setDraft(text ?? "")).PlaceholderText("What needs doing?").Width(240),
                Button("Add").OnClick(Add),
                TextBlock($"{todos.Count(todo => !todo.Done)} open").VerticalAlignment(VerticalAlignment.Center)
            ).Orientation(Orientation.Horizontal).Spacing(8),
            StackPanel([.. todos.Select(todo => Component<TodoRow, TodoRowProps>(new(todo, Toggle, Remove)).WithKey(todo.Id))]).Spacing(4)
        ).Spacing(8).DockPanelDock(Dock.Top);
    }
}

internal sealed record TodoRowProps(Todo Todo, Action<int> Toggle, Action<int> Remove);

internal sealed class TodoRow : Component<TodoRowProps>
{
    protected override Element Render() => StackPanel(
        CheckBox(Props.Todo.Title).IsChecked(Props.Todo.Done, _ => Props.Toggle(Props.Todo.Id)),
        Button("Remove").OnClick(() => Props.Remove(Props.Todo.Id))
    ).Orientation(Orientation.Horizontal).Spacing(8);
}

internal sealed class NumberList : Component
{
    protected override Element Render()
    {
        var numbers = UseMemo(() => Enumerable.Range(1, 10_000).ToArray());
        return ListBox().ItemsSource(numbers).ItemTemplate(ItemTemplate<NumberRow, int>());
    }
}

internal sealed class NumberRow : Component<int>
{
    protected override Element Render() => TextBlock($"{Props} squared is {Props * Props}");
}
