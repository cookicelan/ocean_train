using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Controls.Shapes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace BRC.Marker.Pages;

// ============================================================
// 交互句柄类型枚举
// ============================================================
public enum HandleType
{
    None,
    Move,           // 框内部拖移
    ResizeN,        // 上边
    ResizeS,        // 下边
    ResizeW,        // 左边
    ResizeE,        // 右边
    ResizeNW,       // 左上角
    ResizeNE,       // 右上角
    ResizeSW,       // 左下角
    ResizeSE        // 右下角
}

// ============================================================
// 运行时标注框数据（与 Canvas 控件绑定）
// ============================================================
public class AnnotationBox
{
    public string Label { get; set; } = string.Empty;

    // Canvas 坐标（像素）
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    // 对应 Canvas 上的主矩形
    public global::Avalonia.Controls.Shapes.Rectangle? RectShape { get; set; }
    // 标签文字
    public TextBlock? LabelBlock { get; set; }
    // 8个顶点/边缘句柄（仅选中时显示）
    public List<global::Avalonia.Controls.Shapes.Ellipse> Handles { get; } = new();

    public bool IsSelected { get; set; }

    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

// ============================================================
// 主控件
// ============================================================
public partial class Marker : UserControl
{
    // ---- 基础变量 ----
    private WindowNotificationManager? _manager;
    private ObservableCollection<ImageFileItem> _imageList = new();

    // ---- 新建标注模式 ----
    private bool _isAnnotationMode = false;
    private Point? _startPoint = null;
    private List<string> _labelHistory = new();

    // ---- 运行时标注框列表 ----
    private List<AnnotationBox> _boxes = new();

    // ---- 交互状态 ----
    private AnnotationBox? _activeBox = null;
    private HandleType _dragHandle = HandleType.None;
    private Point _dragStartMouse;
    private double _dragStartLeft, _dragStartTop, _dragStartW, _dragStartH;

    // ---- 布局缓存 ----
    private AnnotationFile? _pendingAnnotations = null;
    private Bitmap? _pendingBitmap = null;
    private bool _layoutHandlerAttached = false;

    // 句柄大小与边缘检测区域
    private const double HandleSize = 8;
    private const double EdgeHitZone = 6;

    public Marker()
    {
        InitializeComponent();
        ImageListBox.ItemsSource = _imageList;

        AnnotationCanvas.PointerPressed += OnCanvasPointerPressed;
        AnnotationCanvas.PointerMoved += OnCanvasPointerMoved;
        AnnotationCanvas.PointerReleased += OnCanvasPointerReleased;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        _manager = new WindowNotificationManager(topLevel) { MaxItems = 3 };
    }

    // ============================================================
    // 矩形标注模式切换
    // ============================================================
    private void ButtonClick_Rectangle(object? sender, RoutedEventArgs e)
    {
        if (MarkImage.Source == null || _imageList.Count == 0)
        {
            _manager?.Show(new Notification("警告", "请先加载并选择一张图片", NotificationType.Warning));
            return;
        }

        _isAnnotationMode = !_isAnnotationMode;

        if (_isAnnotationMode)
        {
            DeselectAll();
            _manager?.Show(new Notification("模式切换", "进入标注模式：在图上点击两点画框", NotificationType.Information));
            AnnotationCanvas.IsVisible = true;
            AnnotationCanvas.Cursor = new Cursor(StandardCursorType.Cross);
        }
        else
        {
            _manager?.Show(new Notification("模式切换", "退出标注模式，进入编辑模式", NotificationType.Information));
            _startPoint = null;
            AnnotationCanvas.Cursor = Cursor.Default;
        }
    }

    // ============================================================
    // Canvas 鼠标按下
    // ============================================================
    private async void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(AnnotationCanvas);
        var props = e.GetCurrentPoint(AnnotationCanvas).Properties;

        // ---------- 新建标注模式 ----------
        if (_isAnnotationMode)
        {
            if (!props.IsLeftButtonPressed) return;
            if (MarkImage.Source is not Bitmap) return;

            if (_startPoint == null)
            {
                _startPoint = pos;
                var dot = new global::Avalonia.Controls.Shapes.Ellipse
                {
                    Fill = Brushes.Red,
                    Width = 6,
                    Height = 6,
                    [Canvas.LeftProperty] = pos.X - 3,
                    [Canvas.TopProperty] = pos.Y - 3,
                    Tag = "dot"
                };
                AnnotationCanvas.Children.Add(dot);
            }
            else
            {
                var start = _startPoint.Value;
                var tempRect = new global::Avalonia.Controls.Shapes.Rectangle
                {
                    Stroke = Brushes.Red,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent,
                    Width = Math.Abs(pos.X - start.X),
                    Height = Math.Abs(pos.Y - start.Y),
                    [Canvas.LeftProperty] = Math.Min(start.X, pos.X),
                    [Canvas.TopProperty] = Math.Min(start.Y, pos.Y),
                    Tag = "temp"
                };
                AnnotationCanvas.Children.Add(tempRect);

                var label = await ShowLabelInputDialog();

                // 移除临时辅助图形
                var toRemove = AnnotationCanvas.Children
                    .OfType<Control>()
                    .Where(c => c.Tag is string t && (t == "dot" || t == "temp"))
                    .ToList();
                foreach (var c in toRemove) AnnotationCanvas.Children.Remove(c);

                _startPoint = null;

                if (!string.IsNullOrEmpty(label))
                {
                    if (!_labelHistory.Contains(label)) _labelHistory.Add(label);

                    var box = new AnnotationBox
                    {
                        Label = label,
                        Left = Math.Min(start.X, pos.X),
                        Top = Math.Min(start.Y, pos.Y),
                        Width = Math.Abs(pos.X - start.X),
                        Height = Math.Abs(pos.Y - start.Y)
                    };
                    AddBoxToCanvas(box);
                    _boxes.Add(box);
                    SaveAllAnnotationsToJson();
                }
            }
            return;
        }

        // ---------- 编辑模式 ----------

        // 右键 = 弹出菜单
        if (props.IsRightButtonPressed)
        {
            var hit = HitTestBox(pos);
            if (hit != null) { SelectBox(hit); ShowContextMenu(hit); }
            return;
        }

        if (!props.IsLeftButtonPressed) return;

        bool isCtrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var hitBox = HitTestBox(pos);

        // Ctrl + 左键 = 复制
        if (hitBox != null && isCtrl)
        {
            var copy = new AnnotationBox
            {
                Label = hitBox.Label,
                Left = hitBox.Left + 10,
                Top = hitBox.Top + 10,
                Width = hitBox.Width,
                Height = hitBox.Height
            };
            AddBoxToCanvas(copy);
            _boxes.Add(copy);
            SelectBox(copy);
            SaveAllAnnotationsToJson();
            _manager?.Show(new Notification("已复制", $"复制了标注框: {copy.Label}", NotificationType.Information));
            return;
        }

        if (hitBox != null)
        {
            SelectBox(hitBox);
            _dragHandle = GetHandleType(hitBox, pos);
            _dragStartMouse = pos;
            _dragStartLeft = hitBox.Left;
            _dragStartTop = hitBox.Top;
            _dragStartW = hitBox.Width;
            _dragStartH = hitBox.Height;
            _activeBox = hitBox;
            e.Pointer.Capture(AnnotationCanvas);
        }
        else
        {
            DeselectAll();
        }
    }

    // ============================================================
    // Canvas 鼠标移动
    // ============================================================
    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(AnnotationCanvas);
        if (_isAnnotationMode) return;

        // 拖拽中
        if (_activeBox != null && _dragHandle != HandleType.None &&
            e.GetCurrentPoint(AnnotationCanvas).Properties.IsLeftButtonPressed)
        {
            double dx = pos.X - _dragStartMouse.X;
            double dy = pos.Y - _dragStartMouse.Y;

            double newLeft = _dragStartLeft;
            double newTop = _dragStartTop;
            double newW = _dragStartW;
            double newH = _dragStartH;

            switch (_dragHandle)
            {
                case HandleType.Move:
                    newLeft = _dragStartLeft + dx;
                    newTop = _dragStartTop + dy;
                    break;
                case HandleType.ResizeE:
                    newW = Math.Max(10, _dragStartW + dx);
                    break;
                case HandleType.ResizeW:
                    newLeft = _dragStartLeft + dx;
                    newW = Math.Max(10, _dragStartW - dx);
                    break;
                case HandleType.ResizeS:
                    newH = Math.Max(10, _dragStartH + dy);
                    break;
                case HandleType.ResizeN:
                    newTop = _dragStartTop + dy;
                    newH = Math.Max(10, _dragStartH - dy);
                    break;
                case HandleType.ResizeNW:
                    newLeft = _dragStartLeft + dx; newTop = _dragStartTop + dy;
                    newW = Math.Max(10, _dragStartW - dx);
                    newH = Math.Max(10, _dragStartH - dy);
                    break;
                case HandleType.ResizeNE:
                    newTop = _dragStartTop + dy;
                    newW = Math.Max(10, _dragStartW + dx);
                    newH = Math.Max(10, _dragStartH - dy);
                    break;
                case HandleType.ResizeSW:
                    newLeft = _dragStartLeft + dx;
                    newW = Math.Max(10, _dragStartW - dx);
                    newH = Math.Max(10, _dragStartH + dy);
                    break;
                case HandleType.ResizeSE:
                    newW = Math.Max(10, _dragStartW + dx);
                    newH = Math.Max(10, _dragStartH + dy);
                    break;
            }

            _activeBox.Left = newLeft;
            _activeBox.Top = newTop;
            _activeBox.Width = newW;
            _activeBox.Height = newH;
            RefreshBoxOnCanvas(_activeBox);
            return;
        }

        // 悬浮鼠标样式
        var hover = HitTestBox(pos);
        if (hover != null)
        {
            AnnotationCanvas.Cursor = GetHandleType(hover, pos) switch
            {
                HandleType.Move => new Cursor(StandardCursorType.SizeAll),
                HandleType.ResizeN => new Cursor(StandardCursorType.SizeNorthSouth),
                HandleType.ResizeS => new Cursor(StandardCursorType.SizeNorthSouth),
                HandleType.ResizeW => new Cursor(StandardCursorType.SizeWestEast),
                HandleType.ResizeE => new Cursor(StandardCursorType.SizeWestEast),
                HandleType.ResizeNW => new Cursor(StandardCursorType.TopLeftCorner),
                HandleType.ResizeSE => new Cursor(StandardCursorType.BottomRightCorner),
                HandleType.ResizeNE => new Cursor(StandardCursorType.TopRightCorner),
                HandleType.ResizeSW => new Cursor(StandardCursorType.BottomLeftCorner),
                _ => Cursor.Default
            };
        }
        else
        {
            AnnotationCanvas.Cursor = Cursor.Default;
        }
    }

    // ============================================================
    // Canvas 鼠标释放
    // ============================================================
    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_activeBox != null && _dragHandle != HandleType.None)
            SaveAllAnnotationsToJson();

        _activeBox = null;
        _dragHandle = HandleType.None;
        e.Pointer.Capture(null);
    }

    // ============================================================
    // 右键菜单
    // ============================================================
    private void ShowContextMenu(AnnotationBox box)
    {
        var menu = new ContextMenu();

        var itemCopy = new MenuItem { Header = "📋 复制此框 (或 Ctrl+左键)" };
        itemCopy.Click += (_, _) =>
        {
            var copy = new AnnotationBox
            {
                Label = box.Label,
                Left = box.Left + 10,
                Top = box.Top + 10,
                Width = box.Width,
                Height = box.Height
            };
            AddBoxToCanvas(copy);
            _boxes.Add(copy);
            SelectBox(copy);
            SaveAllAnnotationsToJson();
        };

        var itemRename = new MenuItem { Header = "✏️ 修改标签" };
        itemRename.Click += async (_, _) =>
        {
            var newLabel = await ShowLabelInputDialog(box.Label);
            if (!string.IsNullOrEmpty(newLabel))
            {
                box.Label = newLabel;
                if (box.LabelBlock != null) box.LabelBlock.Text = newLabel;
                if (!_labelHistory.Contains(newLabel)) _labelHistory.Add(newLabel);
                SaveAllAnnotationsToJson();
            }
        };

        var itemDelete = new MenuItem { Header = "🗑️ 删除此框" };
        itemDelete.Click += (_, _) =>
        {
            RemoveBoxFromCanvas(box);
            _boxes.Remove(box);
            if (_activeBox == box) _activeBox = null;
            SaveAllAnnotationsToJson();
        };

        menu.Items.Add(itemCopy);
        menu.Items.Add(itemRename);
        menu.Items.Add(new Separator());
        menu.Items.Add(itemDelete);
        menu.Open(AnnotationCanvas);
    }

    // ============================================================
    // 命中检测
    // ============================================================
    private AnnotationBox? HitTestBox(Point pos)
    {
        // 先检测已选中框（优先响应句柄）
        if (_activeBox != null && _activeBox.IsSelected &&
            GetHandleType(_activeBox, pos) != HandleType.None)
            return _activeBox;

        foreach (var box in Enumerable.Reverse(_boxes))
        {
            if (pos.X >= box.Left - EdgeHitZone && pos.X <= box.Right + EdgeHitZone &&
                pos.Y >= box.Top - EdgeHitZone && pos.Y <= box.Bottom + EdgeHitZone)
                return box;
        }
        return null;
    }

    // ============================================================
    // 判断鼠标在框的哪个部位
    // ============================================================
    private HandleType GetHandleType(AnnotationBox box, Point pos)
    {
        double l = box.Left, t = box.Top, r = box.Right, b = box.Bottom;
        double hz = EdgeHitZone;

        bool onLeft = pos.X >= l - hz && pos.X <= l + hz;
        bool onRight = pos.X >= r - hz && pos.X <= r + hz;
        bool onTop = pos.Y >= t - hz && pos.Y <= t + hz;
        bool onBottom = pos.Y >= b - hz && pos.Y <= b + hz;
        bool inside = pos.X > l + hz && pos.X < r - hz &&
                        pos.Y > t + hz && pos.Y < b - hz;

        if (onLeft && onTop) return HandleType.ResizeNW;
        if (onRight && onTop) return HandleType.ResizeNE;
        if (onLeft && onBottom) return HandleType.ResizeSW;
        if (onRight && onBottom) return HandleType.ResizeSE;
        if (onLeft) return HandleType.ResizeW;
        if (onRight) return HandleType.ResizeE;
        if (onTop) return HandleType.ResizeN;
        if (onBottom) return HandleType.ResizeS;
        if (inside) return HandleType.Move;
        return HandleType.None;
    }

    // ============================================================
    // 选中 / 取消选中
    // ============================================================
    private void SelectBox(AnnotationBox box)
    {
        DeselectAll();
        box.IsSelected = true;
        _activeBox = box;
        RefreshBoxOnCanvas(box);
    }

    private void DeselectAll()
    {
        foreach (var b in _boxes)
        {
            if (b.IsSelected)
            {
                b.IsSelected = false;
                RefreshBoxOnCanvas(b);
            }
        }
        _activeBox = null;
    }

    // ============================================================
    // Canvas 控件同步
    // ============================================================
    private void AddBoxToCanvas(AnnotationBox box)
    {
        var rect = new global::Avalonia.Controls.Shapes.Rectangle
        {
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(30, 0, 191, 255))
        };
        var label = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            Padding = new Thickness(3, 1),
            Text = box.Label
        };

        box.RectShape = rect;
        box.LabelBlock = label;

        AnnotationCanvas.Children.Add(rect);
        AnnotationCanvas.Children.Add(label);

        // 8 个句柄
        for (int i = 0; i < 8; i++)
        {
            var handle = new global::Avalonia.Controls.Shapes.Ellipse
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = Brushes.White,
                Stroke = Brushes.DeepSkyBlue,
                StrokeThickness = 1.5,
                IsVisible = false
            };
            box.Handles.Add(handle);
            AnnotationCanvas.Children.Add(handle);
        }

        RefreshBoxOnCanvas(box);
    }

    private void RemoveBoxFromCanvas(AnnotationBox box)
    {
        if (box.RectShape != null) AnnotationCanvas.Children.Remove(box.RectShape);
        if (box.LabelBlock != null) AnnotationCanvas.Children.Remove(box.LabelBlock);
        foreach (var h in box.Handles) AnnotationCanvas.Children.Remove(h);
    }

    private void RefreshBoxOnCanvas(AnnotationBox box)
    {
        if (box.RectShape == null) return;

        double l = box.Left, t = box.Top, w = box.Width, h = box.Height;

        box.RectShape.Stroke = box.IsSelected ? Brushes.Orange : Brushes.DeepSkyBlue;
        box.RectShape.Width = w;
        box.RectShape.Height = h;
        box.RectShape[Canvas.LeftProperty] = l;
        box.RectShape[Canvas.TopProperty] = t;

        if (box.LabelBlock != null)
        {
            box.LabelBlock.Foreground = box.IsSelected ? Brushes.Orange : Brushes.DeepSkyBlue;
            box.LabelBlock[Canvas.LeftProperty] = l + 2;
            box.LabelBlock[Canvas.TopProperty] = t + 2;
        }

        // 句柄位置：NW NE SW SE N S W E
        double cx = l + w / 2, cy = t + h / 2, hs = HandleSize / 2;
        Point[] hp = {
            new(l,     t    ),   // 0 NW
            new(l + w, t    ),   // 1 NE
            new(l,     t + h),   // 2 SW
            new(l + w, t + h),   // 3 SE
            new(cx,    t    ),   // 4 N
            new(cx,    t + h),   // 5 S
            new(l,     cy   ),   // 6 W
            new(l + w, cy   ),   // 7 E
        };
        for (int i = 0; i < box.Handles.Count; i++)
        {
            box.Handles[i].IsVisible = box.IsSelected;
            box.Handles[i][Canvas.LeftProperty] = hp[i].X - hs;
            box.Handles[i][Canvas.TopProperty] = hp[i].Y - hs;
        }
    }

    // ============================================================
    // 弹窗输入标签
    // ============================================================
    private async Task<string?> ShowLabelInputDialog(string? defaultText = null)
    {
        var window = this.VisualRoot as Window;
        if (window == null) return null;

        var dialog = new Window
        {
            Title = "输入标签",
            Width = 300,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            SystemDecorations = SystemDecorations.BorderOnly
        };

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        var textBox = new TextBox
        {
            Watermark = "请输入标签名称",
            Text = defaultText ?? (_labelHistory.Count > 0 ? _labelHistory.Last() : "")
        };

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        var btnOk = new Button { Content = "确定", Classes = { "Primary" } };
        var btnCancel = new Button { Content = "取消" };

        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnOk);
        panel.Children.Add(new TextBlock { Text = "Label Name:" });
        panel.Children.Add(textBox);
        panel.Children.Add(btnPanel);
        dialog.Content = panel;

        string? result = null;
        btnOk.Click += (_, _) => { result = textBox.Text; dialog.Close(); };
        btnCancel.Click += (_, _) => { result = null; dialog.Close(); };

        await dialog.ShowDialog(window);
        return result;
    }

    // ============================================================
    // 全量保存 JSON（所有框）
    // ============================================================
    private void SaveAllAnnotationsToJson()
    {
        var currentItem = ImageListBox.SelectedItem as ImageFileItem;
        if (currentItem == null) return;
        if (MarkImage.Source is not Bitmap bitmap) return;

        string jsonPath = System.IO.Path.ChangeExtension(currentItem.FullPath, ".json");
        double originalW = bitmap.PixelSize.Width;
        double originalH = bitmap.PixelSize.Height;

        // 与 DrawPendingAnnotations 保持完全相同的坐标系
        if (!GetImageTransform(originalW, originalH, out double scale, out double offsetX, out double offsetY))
            return;

        var data = new AnnotationFile
        {
            ImagePath = currentItem.FileName,
            ImageHeight = (int)originalH,
            ImageWidth = (int)originalW
        };

        foreach (var box in _boxes)
        {
            double ix1 = Math.Clamp((box.Left - offsetX) / scale, 0, originalW);
            double iy1 = Math.Clamp((box.Top - offsetY) / scale, 0, originalH);
            double ix2 = Math.Clamp((box.Right - offsetX) / scale, 0, originalW);
            double iy2 = Math.Clamp((box.Bottom - offsetY) / scale, 0, originalH);

            // 保存为4点格式（与 labelme 兼容）
            data.Shapes.Add(new SimpleShape
            {
                Label = box.Label,
                Points = new List<List<double>>
                {
                    new() { ix1, iy1 },
                    new() { ix2, iy1 },
                    new() { ix2, iy2 },
                    new() { ix1, iy2 }
                }
            });
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(data, options));
    }

    // ============================================================
    // 清空画布
    // ============================================================
    private void ResetCanvas()
    {
        AnnotationCanvas.Children.Clear();
        _boxes.Clear();
        _activeBox = null;
        _startPoint = null;
        _dragHandle = HandleType.None;
    }

    // ============================================================
    // 基础浏览功能
    // ============================================================
    private void ButtonClick_PageUp(object? sender, RoutedEventArgs e)
    {
        if (_imageList.Count == 0) return;
        int idx = ImageListBox.SelectedIndex - 1;
        if (idx >= 0) { ImageListBox.SelectedIndex = idx; ImageListBox.ScrollIntoView(ImageListBox.SelectedItem); }
        else _manager?.Show(new Notification("提示", "已经是第一张了", NotificationType.Information));
    }

    private void ButtonClick_PageDown(object? sender, RoutedEventArgs e)
    {
        if (_imageList.Count == 0) return;
        int idx = ImageListBox.SelectedIndex + 1;
        if (idx < _imageList.Count) { ImageListBox.SelectedIndex = idx; ImageListBox.ScrollIntoView(ImageListBox.SelectedItem); }
        else _manager?.Show(new Notification("提示", "已经是最后一张了", NotificationType.Information));
    }

    private async void ButtonClick_File(object? sender, RoutedEventArgs e)
    {
        var window = this.VisualRoot as Window;
        if (window == null) return;
        var folderDialog = new OpenFolderDialog { Title = "Select Annotation Folder" };
        var selectedPath = await folderDialog.ShowAsync(window);
        if (!string.IsNullOrEmpty(selectedPath))
        {
            LoadImagesFromFolder(selectedPath);
            _manager?.Show(new Notification("Success", $"Loaded {_imageList.Count} images", NotificationType.Success));
        }
    }

    private void LoadImagesFromFolder(string folderPath)
    {
        _imageList.Clear();
        var dirInfo = new DirectoryInfo(folderPath);
        if (!dirInfo.Exists) return;

        var files = dirInfo.GetFiles().Where(f =>
            f.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            f.Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            f.Extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
            _imageList.Add(new ImageFileItem { FileName = file.Name, FullPath = file.FullName });

        if (_imageList.Count > 0) ImageListBox.SelectedIndex = 0;
    }

    private void ImageListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var listBox = sender as ListBox;
        if (listBox?.SelectedItem is ImageFileItem selectedItem)
        {
            try
            {
                ResetCanvas();
                _isAnnotationMode = false;
                AnnotationCanvas.Cursor = Cursor.Default;

                var bitmap = new Bitmap(selectedItem.FullPath);
                MarkImage.Source = bitmap;

                // 提前设为可见，确保布局引擎计算 Canvas 尺寸，坐标转换才正确
                AnnotationCanvas.IsVisible = true;

                string jsonPath = System.IO.Path.ChangeExtension(selectedItem.FullPath, ".json");
                if (File.Exists(jsonPath))
                    LoadAnnotationsFromJson(jsonPath, bitmap);
            }
            catch (Exception ex)
            {
                _manager?.Show(new Notification("Error", $"Error loading image: {ex.Message}", NotificationType.Error));
                MarkImage.Source = null;
            }
        }
    }

    // ============================================================
    // 从 JSON 加载标注
    // ============================================================
    private void LoadAnnotationsFromJson(string jsonPath, Bitmap bitmap)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            };
            var data = JsonSerializer.Deserialize<AnnotationFile>(File.ReadAllText(jsonPath), options);
            if (data == null || data.Shapes.Count == 0) return;

            _pendingAnnotations = data;
            _pendingBitmap = bitmap;
            DrawPendingAnnotations();
        }
        catch (Exception ex)
        {
            _manager?.Show(new Notification("警告", $"读取标注文件失败: {ex.Message}", NotificationType.Warning));
        }
    }

    // ============================================================
    // 核心坐标转换：精确计算图片内容区在 Canvas 坐标系中的 scale 和 offset
    // 使用 PixelSize（真实像素）而非 Bounds 来避免 DPI / Margin 误差
    // ============================================================
    private bool GetImageTransform(double originalW, double originalH,
                                   out double scale, out double offsetX, out double offsetY)
    {
        scale = 1; offsetX = 0; offsetY = 0;

        // Canvas 尺寸（与鼠标坐标系一致）
        double canvasW = AnnotationCanvas.Bounds.Width;
        double canvasH = AnnotationCanvas.Bounds.Height;
        if (canvasW <= 0 || canvasH <= 0) return false;

        // 用 TranslatePoint 精确获取 MarkImage 内容区左上角在 Canvas 坐标系中的实际位置
        // 这样彻底消除 Margin / Padding / 布局偏差，无需硬编码任何数值
        var originInCanvas = MarkImage.TranslatePoint(new Point(0, 0), AnnotationCanvas);
        if (originInCanvas == null) return false;

        // MarkImage 的实际渲染尺寸（去掉 Margin 后的内容区）
        double imageCtrlW = MarkImage.Bounds.Width;
        double imageCtrlH = MarkImage.Bounds.Height;
        if (imageCtrlW <= 0 || imageCtrlH <= 0) return false;

        // Stretch="Uniform"：图片以等比缩放居中绘制在 MarkImage 内容区内
        scale = Math.Min(imageCtrlW / originalW, imageCtrlH / originalH);

        // 图片像素(0,0) 在 Canvas 坐标系中的位置
        // = MarkImage左上角坐标 + Stretch居中留白
        offsetX = originInCanvas.Value.X + (imageCtrlW - originalW * scale) / 2.0;
        offsetY = originInCanvas.Value.Y + (imageCtrlH - originalH * scale) / 2.0;
        return true;
    }

    private void DrawPendingAnnotations()
    {
        if (_pendingAnnotations == null || _pendingBitmap == null) return;

        double originalW = _pendingBitmap.PixelSize.Width;
        double originalH = _pendingBitmap.PixelSize.Height;

        // GetImageTransform 内部会检测布局是否完成（Canvas/Image 尺寸是否为0）
        if (!GetImageTransform(originalW, originalH, out double scale, out double offsetX, out double offsetY))
        {
            // 布局未完成，等待 LayoutUpdated
            if (!_layoutHandlerAttached)
            {
                _layoutHandlerAttached = true;
                MarkImage.LayoutUpdated += OnMarkImageLayoutUpdated;
            }
            return;
        }

        foreach (var shape in _pendingAnnotations.Shapes)
        {
            if (shape.Points == null || shape.Points.Count < 2) continue;

            double imgX1 = shape.Points.Min(p => p[0]);
            double imgY1 = shape.Points.Min(p => p[1]);
            double imgX2 = shape.Points.Max(p => p[0]);
            double imgY2 = shape.Points.Max(p => p[1]);

            var box = new AnnotationBox
            {
                Label = shape.Label,
                Left = imgX1 * scale + offsetX,
                Top = imgY1 * scale + offsetY,
                Width = (imgX2 - imgX1) * scale,
                Height = (imgY2 - imgY1) * scale
            };

            AddBoxToCanvas(box);
            _boxes.Add(box);

            if (!string.IsNullOrEmpty(shape.Label) && !_labelHistory.Contains(shape.Label))
                _labelHistory.Add(shape.Label);
        }

        _manager?.Show(new Notification("已恢复标注", $"读取到 {_boxes.Count} 个历史标注框，可直接编辑", NotificationType.Success));

        _pendingAnnotations = null;
        _pendingBitmap = null;
    }

    private void OnMarkImageLayoutUpdated(object? sender, EventArgs e)
    {
        if (_pendingAnnotations != null) DrawPendingAnnotations();
        if (_pendingAnnotations == null)
        {
            MarkImage.LayoutUpdated -= OnMarkImageLayoutUpdated;
            _layoutHandlerAttached = false;
        }
    }

    // ============================================================
    // 拖拽文件夹支持
    // ============================================================
    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFileNames();
        if (files == null || !files.Any()) return;
        var path = files.First();

        if (Directory.Exists(path))
        {
            LoadImagesFromFolder(path);
            _manager?.Show(new Notification("Success", $"Folder Dropped! Loaded {_imageList.Count} images", NotificationType.Success));
        }
        else if (File.Exists(path))
        {
            var folder = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                LoadImagesFromFolder(folder);
                _manager?.Show(new Notification("Success", "File Dropped! Loaded from folder", NotificationType.Success));
            }
        }
    }
}

// ============================================================
// 数据模型
// ============================================================
public class ImageFileItem
{
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
}

public class AnnotationFile
{
    [JsonPropertyName("version")] public string Version { get; set; } = "3.1.1";
    [JsonPropertyName("flags")] public Dictionary<string, object> Flags { get; set; } = new();
    [JsonPropertyName("shapes")] public List<SimpleShape> Shapes { get; set; } = new();
    [JsonPropertyName("imagePath")] public string ImagePath { get; set; } = string.Empty;
    [JsonPropertyName("imageData")] public string? ImageData { get; set; } = null;
    [JsonPropertyName("imageHeight")] public int ImageHeight { get; set; }
    [JsonPropertyName("imageWidth")] public int ImageWidth { get; set; }
}

public class SimpleShape
{
    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
    [JsonPropertyName("points")] public List<List<double>> Points { get; set; } = new();
    [JsonPropertyName("shape_type")] public string ShapeType { get; set; } = "rectangle";
    [JsonPropertyName("difficult")] public bool Difficult { get; set; } = false;
}
