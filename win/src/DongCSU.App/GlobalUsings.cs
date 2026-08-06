// WPF 와 WinForms 를 함께 켜면 같은 이름이 양쪽에 있어서 전부 모호해진다
// (Color · Brush · Point · Size · Button · ListBox …).
//
// WinForms 는 **트레이 아이콘 하나 때문에** 켰을 뿐이라, 이름은 전부 WPF 쪽으로 못 박는다.
// System.Drawing 것이 필요한 자리(TrayIcon.cs)에서는 거기서 전체 이름으로 적는다.
global using Application = System.Windows.Application;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Button = System.Windows.Controls.Button;
global using CheckBox = System.Windows.Controls.CheckBox;
global using Color = System.Windows.Media.Color;
global using ComboBox = System.Windows.Controls.ComboBox;
global using Cursors = System.Windows.Input.Cursors;
global using FontFamily = System.Windows.Media.FontFamily;
global using FontStyle = System.Windows.FontStyle;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using ListBox = System.Windows.Controls.ListBox;
global using MessageBox = System.Windows.MessageBox;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using Orientation = System.Windows.Controls.Orientation;
global using Pen = System.Windows.Media.Pen;
global using Point = System.Windows.Point;
global using Size = System.Windows.Size;
global using SystemFonts = System.Windows.SystemFonts;
global using VerticalAlignment = System.Windows.VerticalAlignment;
