using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Win32;

namespace WpfApp1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<TableData> Tables { get; } = new();
        private readonly Brush[] brushes = { Brushes.Firebrick, Brushes.SeaGreen, Brushes.RoyalBlue, Brushes.DarkOrange, Brushes.Purple, Brushes.Teal };
        private double zoom = 1.0;
        private Vector offset = new();
        private Point lastMouse;
        private bool isDragging;

        public MainWindow()
        {
            InitializeComponent();
            TablesList.ItemsSource = Tables;
            SizeChanged += MainWindow_SizeChanged;
            AddTable("Default", new[] { new PointData { X = 0, Y = 0 }, new PointData { X = 1, Y = 1 }, new PointData { X = 2, Y = 4 } });
            DrawGraph();
        }

        private TableData? SelectedTable => TablesList.SelectedItem as TableData;

        private void AddTable(string name, IEnumerable<PointData> points)
        {
            TableData table = new() { Name = name };
            foreach (PointData point in points)
            {
                table.Points.Add(point);
            }
            table.Points.CollectionChanged += Points_CollectionChanged;
            AttachPointHandlers(table);
            Tables.Add(table);
            TablesList.SelectedItem = table;
            DrawGraph();
        }

        private void TablesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PointsGrid.ItemsSource = SelectedTable?.Points;
        }

        private void Points_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (SelectedTable != null)
            {
                AttachPointHandlers(SelectedTable);
            }
            DrawGraph();
        }

        private void Point_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            DrawGraph();
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawGraph();
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            TableData? table = SelectedTable;
            if (table == null)
            {
                return;
            }

            double x = table.Points.Count;
            table.Points.Add(new PointData { X = x, Y = x * x });
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            TableData? table = SelectedTable;
            if (table != null && PointsGrid.SelectedItem is PointData point)
            {
                table.Points.Remove(point);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            TableData? table = SelectedTable;
            if (table == null)
            {
                return;
            }

            SaveFileDialog dialog = new() { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*", FileName = table.Name + ".csv" };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            File.WriteAllLines(dialog.FileName, table.Points.Select(point => $"{point.X};{point.Y}"));
            table.Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            TablesList.Items.Refresh();
            DrawGraph();
        }

        private void Load_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new() { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*", Multiselect = true };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            foreach (string fileName in dialog.FileNames)
            {
                List<PointData> points = new();
                foreach (string line in File.ReadAllLines(fileName))
                {
                    string[] parts = line.Split(';', ',');
                    if (parts.Length >= 2 && double.TryParse(parts[0], out double x) && double.TryParse(parts[1], out double y))
                    {
                        points.Add(new PointData { X = x, Y = y });
                    }
                }
                AddTable(System.IO.Path.GetFileNameWithoutExtension(fileName), points);
            }
        }

        private void AttachPointHandlers(TableData table)
        {
            foreach (PointData point in table.Points)
            {
                point.PropertyChanged -= Point_PropertyChanged;
                point.PropertyChanged += Point_PropertyChanged;
            }
        }

        private void DrawGraph()
        {
            double width = Math.Max(PlotImage.ActualWidth, 1);
            double height = Math.Max(PlotImage.ActualHeight, 1);
            DrawingGroup drawing = new()
            {
                ClipGeometry = new RectangleGeometry(new Rect(0, 0, width, height))
            };
            using DrawingContext context = drawing.Open();
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

            Rect area = GetPlotArea(width, height);
            context.DrawRectangle(null, new Pen(Brushes.Black, 1), area);

            if (!TryGetViewport(out double minX, out double maxX, out double minY, out double maxY))
            {
                PlotImage.Source = new DrawingImage(drawing);
                return;
            }

            DrawAxes(context, area, minX, maxX, minY, maxY);

            context.PushClip(new RectangleGeometry(area));
            for (int i = 0; i < Tables.Count; ++i)
            {
                TableData table = Tables[i];
                List<PointData> points = table.Points.OrderBy(point => point.X).ToList();
                if (points.Count < 2)
                {
                    continue;
                }

                StreamGeometry geometry = new();
                using StreamGeometryContext stream = geometry.Open();
                bool started = false;
                foreach (PointData point in points)
                {
                    Point mapped = Map(point, minX, maxX, minY, maxY, area);
                    if (!started)
                    {
                        stream.BeginFigure(mapped, false, false);
                        started = true;
                    }
                    else
                    {
                        stream.LineTo(mapped, true, false);
                    }
                }
                geometry.Freeze();
                Brush brush = brushes[i % brushes.Length];
                context.DrawGeometry(null, new Pen(brush, 2), geometry);
            }
            context.Pop();

            for (int i = 0; i < Tables.Count; ++i)
            {
                if (Tables[i].Points.Count >= 2)
                    DrawLegendItem(context, area.Right + 25, area.Top + i * 24, brushes[i % brushes.Length], Tables[i].Name);
            }

            PlotImage.Source = new DrawingImage(drawing);
        }

        private static Rect GetPlotArea(double width, double height) =>
            new(55, 25, Math.Max(width - 180, 1), Math.Max(height - 85, 1));

        private bool TryGetViewport(out double minX, out double maxX, out double minY, out double maxY)
        {
            List<PointData> points = Tables.SelectMany(table => table.Points).ToList();
            minX = maxX = minY = maxY = 0;
            if (points.Count == 0)
                return false;

            double dataMinX = points.Min(point => point.X);
            double dataMaxX = points.Max(point => point.X);
            double dataMinY = points.Min(point => point.Y);
            double dataMaxY = points.Max(point => point.Y);
            Rect area = GetPlotArea(Math.Max(PlotImage.ActualWidth, 1), Math.Max(PlotImage.ActualHeight, 1));
            double dataSpanX = Math.Abs(dataMaxX - dataMinX) < 1e-6 ? 1 : dataMaxX - dataMinX;
            double dataSpanY = Math.Abs(dataMaxY - dataMinY) < 1e-6 ? 1 : dataMaxY - dataMinY;
            double unitsPerPixel = Math.Max(dataSpanX / area.Width, dataSpanY / area.Height) / zoom;
            double spanX = unitsPerPixel * area.Width;
            double spanY = unitsPerPixel * area.Height;
            double middleX = (dataMinX + dataMaxX) / 2 + offset.X;
            double middleY = (dataMinY + dataMaxY) / 2 + offset.Y;
            minX = middleX - spanX / 2;
            maxX = middleX + spanX / 2;
            minY = middleY - spanY / 2;
            maxY = middleY + spanY / 2;
            return true;
        }

        private static void DrawAxes(DrawingContext context, Rect area, double minX, double maxX, double minY, double maxY)
        {
            Pen gridPen = new(Brushes.Gainsboro, 1);
            Typeface typeface = new("Segoe UI");
            double dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;
            double step = GetTickStep(Math.Max(maxX - minX, maxY - minY) / 5);
            var culture = System.Globalization.CultureInfo.CurrentCulture;

            double firstX = Math.Ceiling(minX / step) * step;
            for (double value = firstX; value <= maxX + step * 1e-9; value += step)
            {
                double x = area.Left + (value - minX) / (maxX - minX) * area.Width;
                context.DrawLine(gridPen, new Point(x, area.Top), new Point(x, area.Bottom));
                FormattedText label = new(FormatTick(value, step, culture), culture, FlowDirection.LeftToRight, typeface, 11, Brushes.Black, dpi);
                context.DrawText(label, new Point(x - label.Width / 2, area.Bottom + 5));
            }

            double firstY = Math.Ceiling(minY / step) * step;
            for (double value = firstY; value <= maxY + step * 1e-9; value += step)
            {
                double y = area.Bottom - (value - minY) / (maxY - minY) * area.Height;
                context.DrawLine(gridPen, new Point(area.Left, y), new Point(area.Right, y));
                FormattedText label = new(FormatTick(value, step, culture), culture, FlowDirection.LeftToRight, typeface, 11, Brushes.Black, dpi);
                context.DrawText(label, new Point(area.Left - label.Width - 6, y - label.Height / 2));
            }

            context.DrawText(new FormattedText("X", culture, FlowDirection.LeftToRight, typeface, 12, Brushes.Black, dpi), new Point(area.Right + 20, area.Bottom + 6));
            context.DrawText(new FormattedText("Y", culture, FlowDirection.LeftToRight, typeface, 12, Brushes.Black, dpi), new Point(area.Left - 25, area.Top - 20));
        }

        private static double GetTickStep(double desiredStep)
        {
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(desiredStep)));
            double normalized = desiredStep / magnitude;
            double factor = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
            return factor * magnitude;
        }

        private static string FormatTick(double value, double step, System.Globalization.CultureInfo culture)
        {
            int decimals = Math.Clamp((int)Math.Ceiling(-Math.Log10(step)), 0, 12);
            double rounded = Math.Round(value, decimals);
            return rounded.ToString($"F{decimals}", culture);
        }

        private static void DrawLegendItem(DrawingContext context, double x, double y, Brush brush, string text)
        {
            Typeface typeface = new("Segoe UI");
            double dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;
            context.DrawRectangle(brush, null, new Rect(x, y + 4, 16, 10));
            context.DrawText(new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 12, Brushes.Black, dpi), new Point(x + 24, y));
        }

        private static Point Map(PointData point, double minX, double maxX, double minY, double maxY, Rect area)
        {
            double x = area.Left + (point.X - minX) / (maxX - minX) * area.Width;
            double y = area.Bottom - (point.Y - minY) / (maxY - minY) * area.Height;
            return new Point(x, y);
        }

        private void PlotImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!TryGetViewport(out double minX, out double maxX, out double minY, out double maxY))
                return;

            Rect area = GetPlotArea(PlotImage.ActualWidth, PlotImage.ActualHeight);
            Point mouse = e.GetPosition(PlotImage);
            double fractionX = Math.Clamp((mouse.X - area.Left) / area.Width, 0, 1);
            double fractionY = Math.Clamp((area.Bottom - mouse.Y) / area.Height, 0, 1);
            double anchorX = minX + fractionX * (maxX - minX);
            double anchorY = minY + fractionY * (maxY - minY);

            double newZoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), 1e-10, 1e10);
            double scale = zoom / newZoom;
            offset.X += (anchorX - (minX + maxX) / 2) * (1 - scale);
            offset.Y += (anchorY - (minY + maxY) / 2) * (1 - scale);
            zoom = newZoom;
            DrawGraph();
            e.Handled = true;
        }

        private void PlotImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                isDragging = true;
                lastMouse = e.GetPosition(PlotImage);
                PlotImage.CaptureMouse();
            }
        }

        private void PlotImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDragging)
            {
                return;
            }

            Point currentMouse = e.GetPosition(PlotImage);
            if (TryGetViewport(out double minX, out double maxX, out double minY, out double maxY))
            {
                Rect area = GetPlotArea(PlotImage.ActualWidth, PlotImage.ActualHeight);
                offset.X -= (currentMouse.X - lastMouse.X) * (maxX - minX) / area.Width;
                offset.Y += (currentMouse.Y - lastMouse.Y) * (maxY - minY) / area.Height;
            }
            lastMouse = currentMouse;
            DrawGraph();
        }

        private void PlotImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            isDragging = false;
            PlotImage.ReleaseMouseCapture();
        }

        private void PlotImage_LostMouseCapture(object sender, MouseEventArgs e)
        {
            isDragging = false;
        }
    }

    public class TableData
    {
        public string Name { get; set; } = "";
        public ObservableCollection<PointData> Points { get; } = new();
    }

    public class PointData : INotifyPropertyChanged
    {
        private double x;
        private double y;

        public double X
        {
            get => x;
            set
            {
                x = value;
                OnPropertyChanged();
            }
        }

        public double Y
        {
            get => y;
            set
            {
                y = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
