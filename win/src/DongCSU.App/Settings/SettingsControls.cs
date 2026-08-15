using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DongCSU.App.Settings;

/// <summary>
/// 설정 창을 이루는 부품.
///
/// **기본 WPF 컨트롤을 거의 쓰지 않는다.** ComboBox·CheckBox 는 크롬이 늘 밝은 색이라
/// 어두운 테마에서 혼자 하얗게 뜨고, 통째로 다시 그리려면 ControlTemplate 을 코드로
/// 짜야 해서 읽기 어려워진다. 대신 **경계선과 글자만으로 만든 얇은 부품**을 쓴다 —
/// 고를 것이 두세 개뿐인 설정에는 분절 컨트롤이 콤보보다 낫기도 하다.
///
/// XAML 을 쓰지 않는 이유는 <c>CLAUDE.md</c> 에 있다.
/// </summary>
internal static class Ui
{
    public const double Radius = 8;

    // ── 글자 ────────────────────────────────────────────────────────

    public static TextBlock Title(SettingsPalette palette, string text) => new()
    {
        Text = text,
        FontSize = 19,
        FontWeight = FontWeights.SemiBold,
        Foreground = palette.Brush(palette.Primary),
        Margin = new Thickness(0, 0, 0, 14),
    };

    /// <summary>구역 소제목. 무엇을 묶어 놓은 것인지 한눈에 갈린다.</summary>
    public static TextBlock Section(SettingsPalette palette, string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = palette.Brush(palette.Faint),
        Margin = new Thickness(2, 18, 0, 6),
    };

    public static TextBlock Label(SettingsPalette palette, string text, double size = 13) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = palette.Brush(palette.Primary),
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Hint(SettingsPalette palette, string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        Foreground = palette.Brush(palette.Tertiary),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 6, 0, 0),
        LineHeight = 17,
    };

    // ── 판 ──────────────────────────────────────────────────────────

    /// <summary>설정 몇 줄을 담는 판.</summary>
    public static Border Card(SettingsPalette palette, params UIElement[] children)
    {
        var stack = new StackPanel();
        foreach (var child in children) stack.Children.Add(child);

        return new Border
        {
            Background = palette.Brush(palette.Card),
            BorderBrush = palette.Brush(palette.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radius + 2),
            Padding = new Thickness(14, 6, 14, 6),
            Child = stack,
        };
    }

    /// <summary>왼쪽에 이름, 오른쪽에 조작부. 줄 사이에는 옅은 선을 깐다.</summary>
    public static UIElement Row(
        SettingsPalette palette, string label, FrameworkElement control, string? hint = null, bool enabled = true)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Label(palette, label));
        if (hint is not null)
        {
            var note = Hint(palette, hint);
            note.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(note);
        }

        // **`Grid` 의 별(*) + 자동 열을 쓰지 않는다.**
        //
        // 그러면 줄바꿈하는 글이 조작부 밑으로 깔린다. 별 열의 자식을 잴 때는 아직
        // 자동 열의 너비가 안 정해져서 **넓은 폭으로 줄을 나눠 놓고**, 나중에 열만
        // 좁아지기 때문이다. 칸은 제대로 좁아지는데 글자만 밖으로 넘친다.
        //
        // `DockPanel` 은 한 번에 잰다 — 오른쪽에 붙인 것을 먼저 재고, 남은 폭을 마지막
        // 자식에게 준다. 그래서 글이 조작부 자리를 침범할 수 없다.
        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 10), LastChildFill = true };

        control.VerticalAlignment = VerticalAlignment.Center;
        // 글과 맞닿으면 읽기 힘들다.
        control.Margin = new Thickness(14, 0, 0, 0);
        DockPanel.SetDock(control, Dock.Right);
        row.Children.Add(control);

        // **마지막에 넣어야** 남은 자리를 채운다.
        row.Children.Add(text);

        // **못 누르는 것은 흐리게.** 눌러도 화면에 아무 변화가 없는 항목이 멀쩡히
        // 열려 있으면, 사용자는 앱이 고장 났다고 본다.
        if (!enabled)
        {
            row.IsEnabled = false;
            row.Opacity = 0.45;
        }
        return row;
    }

    public static Border Divider(SettingsPalette palette) => new()
    {
        Height = 1,
        Background = palette.Brush(palette.Line),
        Margin = new Thickness(0, 0, 0, 0),
    };

    // ── 토글 ────────────────────────────────────────────────────────

    /// <summary>켜고 끄는 스위치. 기본 CheckBox 보다 상태가 멀리서도 보인다.</summary>
    public static FrameworkElement Toggle(SettingsPalette palette, bool value, Action<bool> onSet)
    {
        const double width = 40;
        const double height = 22;
        const double knob = 16;

        var track = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(height / 2),
            Background = palette.Brush(value ? palette.Accent : palette.TrackOff),
            Cursor = Cursors.Hand,
        };

        var dot = new Border
        {
            Width = knob,
            Height = knob,
            CornerRadius = new CornerRadius(knob / 2),
            Background = palette.Brush(value ? palette.OnAccent : palette.Secondary),
            HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(3, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        track.Child = dot;

        var current = value;
        track.MouseLeftButtonUp += (_, _) =>
        {
            current = !current;
            track.Background = palette.Brush(current ? palette.Accent : palette.TrackOff);
            dot.Background = palette.Brush(current ? palette.OnAccent : palette.Secondary);
            dot.HorizontalAlignment = current ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            onSet(current);
        };
        return track;
    }

    // ── 분절 컨트롤 ─────────────────────────────────────────────────

    /// <summary>
    /// 고를 것이 몇 개 안 될 때. 눌러 보기 전에 **선택지가 다 보인다** —
    /// 콤보는 열어 봐야 무엇이 있는지 알 수 있다.
    /// </summary>
    public static FrameworkElement Segmented(
        SettingsPalette palette, IReadOnlyList<string> titles, int selected, Action<int> onSet)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        var cells = new List<Border>();

        void Paint(int index)
        {
            for (var i = 0; i < cells.Count; i++)
            {
                var chosen = i == index;
                cells[i].Background = palette.Brush(chosen ? palette.Accent : Colors.Transparent);
                ((TextBlock)cells[i].Child).Foreground =
                    palette.Brush(chosen ? palette.OnAccent : palette.Secondary);
                ((TextBlock)cells[i].Child).FontWeight = chosen ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        for (var i = 0; i < titles.Count; i++)
        {
            var index = i;
            var cell = new Border
            {
                CornerRadius = new CornerRadius(Radius - 2),
                Padding = new Thickness(11, 5, 11, 5),
                Margin = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = titles[i], FontSize = 12 },
            };
            cell.MouseLeftButtonUp += (_, _) => { Paint(index); onSet(index); };
            cells.Add(cell);
            strip.Children.Add(cell);
        }
        Paint(selected);

        return new Border
        {
            Background = palette.Brush(palette.TrackOff),
            CornerRadius = new CornerRadius(Radius),
            Padding = new Thickness(2),
            Child = strip,
        };
    }

    // ── 미끄럼 막대 ─────────────────────────────────────────────────

    /// <summary>
    /// 0~1 사이 값을 끌어서 정한다.
    ///
    /// 기본 <c>Slider</c> 를 쓰지 않는 이유는 크롬이 테마를 안 따라서다. 직접 그리면
    /// **값이 바뀌는 동안 다시 그리지 않아도 된다**는 이점도 있다 — 탭을 통째로 다시
    /// 만드는 방식이라, 끌 때마다 다시 그리면 드래그가 끊긴다.
    /// </summary>
    public static FrameworkElement Slider(
        SettingsPalette palette, double value, double minimum, double maximum, Action<double> onSet)
    {
        const double width = 132;
        const double height = 4;
        const double knob = 14;

        var track = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(height / 2),
            Background = palette.Brush(palette.TrackOff),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var filled = new Border
        {
            Height = height,
            CornerRadius = new CornerRadius(height / 2),
            Background = palette.Brush(palette.Accent),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var dot = new Border
        {
            Width = knob,
            Height = knob,
            CornerRadius = new CornerRadius(knob / 2),
            Background = palette.Brush(palette.OnAccent),
            BorderBrush = palette.Brush(palette.Accent),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var readout = new TextBlock
        {
            FontSize = 12,
            Width = 38,
            TextAlignment = TextAlignment.Right,
            Foreground = palette.Brush(palette.Secondary),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };

        var canvas = new Grid { Width = width, Height = knob + 6, Cursor = Cursors.Hand };
        canvas.Children.Add(track);
        canvas.Children.Add(filled);
        canvas.Children.Add(dot);

        void Paint(double ratio)
        {
            filled.Width = Math.Max(0, width * ratio);
            dot.Margin = new Thickness(Math.Clamp(width * ratio - knob / 2, 0, width - knob), 0, 0, 0);
            readout.Text = $"{Math.Round(minimum + (maximum - minimum) * ratio, 2) * 100:0}%";
        }

        var span = maximum - minimum;
        Paint(span > 0 ? (value - minimum) / span : 0);

        void Move(double x)
        {
            var ratio = Math.Clamp(x / width, 0, 1);
            Paint(ratio);
            onSet(minimum + span * ratio);
        }

        canvas.MouseLeftButtonDown += (_, e) =>
        {
            canvas.CaptureMouse();
            Move(e.GetPosition(canvas).X);
        };
        canvas.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed) Move(e.GetPosition(canvas).X);
        };
        canvas.MouseLeftButtonUp += (_, _) => canvas.ReleaseMouseCapture();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(canvas);
        row.Children.Add(readout);
        return row;
    }

    // ── 스크롤 ──────────────────────────────────────────────────────

    /// <summary>
    /// 스크롤 되는 영역. **막대를 직접 그린다.**
    ///
    /// WPF 기본 <c>ScrollBar</c> 는 테마 브러시가 박혀 있어서 어두운 배경에서 혼자
    /// 밝은 띠로 뜬다. 통째로 다시 그리려면 <c>Track</c> 이 들어간 ControlTemplate 이
    /// 필요한데, 그건 코드로 짜기가 마땅치 않다(XAML 을 쓰지 않기로 한 이유는 별도).
    /// 그래서 기본 막대는 숨기고 얇은 띠를 얹는다 — 휠과 드래그 둘 다 먹는다.
    /// </summary>
    public static (Grid Host, ScrollViewer Scroll) Scroller(SettingsPalette palette, UIElement content)
    {
        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var thumb = new Border
        {
            Width = 6,
            CornerRadius = new CornerRadius(3),
            Background = palette.Brush(palette.TrackOff),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 4, 0),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
        };

        void Sync()
        {
            var extent = scroll.ExtentHeight;
            var viewport = scroll.ViewportHeight;
            if (extent <= viewport || viewport <= 0)
            {
                thumb.Visibility = Visibility.Collapsed;
                return;
            }

            thumb.Visibility = Visibility.Visible;
            var height = Math.Max(28, viewport * viewport / extent);
            thumb.Height = height;
            var travel = viewport - height;
            var ratio = scroll.ScrollableHeight > 0 ? scroll.VerticalOffset / scroll.ScrollableHeight : 0;
            thumb.Margin = new Thickness(0, travel * ratio, 4, 0);
        }

        scroll.ScrollChanged += (_, _) => Sync();
        scroll.SizeChanged += (_, _) => Sync();

        var dragging = false;
        var grabbedAt = 0.0;
        var startOffset = 0.0;

        thumb.MouseLeftButtonDown += (_, e) =>
        {
            dragging = true;
            grabbedAt = e.GetPosition(scroll).Y;
            startOffset = scroll.VerticalOffset;
            thumb.CaptureMouse();
            thumb.Background = palette.Brush(palette.Secondary);
        };
        thumb.MouseMove += (_, e) =>
        {
            if (!dragging) return;

            var travel = scroll.ViewportHeight - thumb.ActualHeight;
            if (travel <= 0) return;

            var moved = e.GetPosition(scroll).Y - grabbedAt;
            scroll.ScrollToVerticalOffset(startOffset + moved / travel * scroll.ScrollableHeight);
        };
        thumb.MouseLeftButtonUp += (_, _) =>
        {
            dragging = false;
            thumb.ReleaseMouseCapture();
            thumb.Background = palette.Brush(palette.TrackOff);
        };

        var host = new Grid();
        host.Children.Add(scroll);
        host.Children.Add(thumb);
        return (host, scroll);
    }

    // ── 단추 ────────────────────────────────────────────────────────

    public enum ButtonKind { Normal, Accent, Danger }

    public static Border Button(
        SettingsPalette palette,
        string text,
        Action onClick,
        ButtonKind kind = ButtonKind.Normal,
        bool enabled = true)
    {
        var isAccent = kind == ButtonKind.Accent;
        var foreground = kind switch
        {
            ButtonKind.Accent => palette.OnAccent,
            ButtonKind.Danger => palette.Danger,
            _ => palette.Primary,
        };

        var border = new Border
        {
            Background = palette.Brush(isAccent ? palette.Accent : Colors.Transparent),
            BorderBrush = palette.Brush(isAccent ? palette.Accent : palette.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radius - 2),
            Padding = new Thickness(14, 6, 14, 6),
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
            Opacity = enabled ? 1 : 0.45,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                Foreground = palette.Brush(foreground),
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };

        if (!enabled) return border;

        border.MouseEnter += (_, _) =>
        {
            if (!isAccent) border.Background = palette.Brush(palette.Hover);
        };
        border.MouseLeave += (_, _) =>
        {
            if (!isAccent) border.Background = palette.Brush(Colors.Transparent);
        };
        border.MouseLeftButtonUp += (_, _) => onClick();
        return border;
    }

    /// <summary>단추 여러 개를 한 줄에.</summary>
    public static UIElement ButtonRow(params UIElement[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var button in buttons)
        {
            button.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            row.Children.Add(button);
        }
        return row;
    }

    /// <summary>
    /// 글자가 실제로 차지하는 폭. **자리를 못 박아야 할 때만 쓴다** — 갈래 딱지처럼
    /// 여러 줄의 시작점을 맞춰야 하는 자리다.
    /// </summary>
    public static double TextWidth(string text, double size, FontWeight weight)
    {
        var typeface = new Typeface(
            new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal);
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            size,
            Brushes.Black,
            VisualTreeHelper.GetDpi(new Border()).PixelsPerDip);
        return formatted.Width;
    }

    /// <summary>상태를 한마디로 알리는 알약.</summary>
    public static Border Pill(SettingsPalette palette, string text, Color color) => new()
    {
        Background = palette.Brush(Color.FromArgb(0x2E, color.R, color.G, color.B)),
        CornerRadius = new CornerRadius(999),
        Padding = new Thickness(8, 2, 8, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.Brush(color),
        },
    };
}
